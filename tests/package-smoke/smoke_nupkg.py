# /// script
# requires-python = ">=3.9"
# dependencies = ["umbraco-spawn-harness @ git+https://github.com/worm-brain/umbraco-spawn-harness@v0.1.0"]
# ///
"""Pre-publish smoke test of the packed Log Explorer nupkgs (ADR 0022).

For each Umbraco version of one major, spawns a clean site with umbraco-spawn-harness, installs
Umbraco.Community.LogExplorer from a local folder of packed .nupkg files with zero configuration,
builds and starts it (the first start runs the unattended install), signs in as the admin and checks
that the package works as installed from NuGet:

- GET  /umbraco/log-explorer/api/v1/sources lists the zero-configuration `files` source
- GET  /umbraco/log-explorer/api/v1/settings answers with `defaultSource: files`
- the backoffice lists the package manifest, and its umbraco-package.json and client bundle are
  served from App_Plugins (static web assets from the nupkg), stamped with the package version
- POST /umbraco/log-explorer/api/v1/sources/files/search over the last hour returns 200 with entries

The site is removed afterwards, also on failure (unless --keep).

    dotnet pack Umbraco.Community.LogExplorer.slnx -o <pkgs> -p:UmbracoMajor=17 -p:Version=17.0.0-alpha.0
    uv run tests/package-smoke/smoke_nupkg.py --packages <pkgs> --major 17

By default the versions tried are the floor of the package's declared Umbraco range (from
Directory.Packages.props) and the newest stable release of the major; --umbraco overrides that.
Exits 1 when any version fails.
"""
import argparse
import json
import re
import shutil
import sys
import tempfile
import time
import zipfile
from pathlib import Path
from xml.etree import ElementTree

from umbraco_spawn_harness import (SpawnError, heading, nuget_global_packages, remove_tree, resolve_umbraco_version,
                                   say, scaffold, utf8_stdio, warn)

PACKAGE_ID = "Umbraco.Community.LogExplorer"
API = "/umbraco/log-explorer/api/v1"
# Where the client build lands inside the package's static web assets (the RCL's ClientOutputDirectory).
APP_PLUGIN = "/App_Plugins/UmbracoCommunityLogExplorer"
REPO_ROOT = Path(__file__).resolve().parents[2]


class SmokeFailure(Exception):
    """A check against the running site failed."""


# --------------------------------------------------------------- inputs --
def read_packages(folder):
    """Read the id and version of every .nupkg in a folder from its .nuspec.

    The file name alone is ambiguous (`Umbraco.Community.LogExplorer.Core.17.0.0.nupkg`), so the
    nuspec inside each package is the source of truth.

    :param folder: the folder `dotnet pack -o` wrote to.
    :returns: a dict of package id to version.
    :raises SystemExit: when the folder holds no packages.
    """
    packages = {}
    for nupkg in sorted(Path(folder).glob("*.nupkg")):
        with zipfile.ZipFile(nupkg) as archive:
            nuspec = next(n for n in archive.namelist() if n.endswith(".nuspec") and "/" not in n)
            root = ElementTree.fromstring(archive.read(nuspec))
        # The nuspec namespace varies with the NuGet version, so match on local names.
        meta = next(el for el in root if el.tag.endswith("metadata"))
        fields = {el.tag.split("}")[-1]: el.text for el in meta}
        packages[fields["id"]] = fields["version"]
    if PACKAGE_ID not in packages:
        sys.exit(f"error: no {PACKAGE_ID} .nupkg in {folder}")
    return packages


def declared_floor(major):
    """The lowest Umbraco version the package declares for a major (UmbracoPackageRange, ADR 0010).

    :param major: 17 or 18.
    :returns: the floor version, e.g. `17.0.0`.
    :raises SystemExit: when Directory.Packages.props has no range for the major.
    """
    props = (REPO_ROOT / "Directory.Packages.props").read_text(encoding="utf-8")
    match = re.search(rf"'\$\(UmbracoMajor\)' == '{major}'\">\s*<UmbracoPackageRange>\[\s*([^,\]\s]+)", props)
    if not match:
        sys.exit(f"error: no UmbracoPackageRange for Umbraco {major} in Directory.Packages.props")
    return match.group(1)


def versions_to_try(major):
    """The declared floor of the major and its newest stable release, without duplicates.

    :param major: 17 or 18.
    :returns: the exact Umbraco versions.
    """
    latest = resolve_umbraco_version(str(major)) or sys.exit(f"error: no stable Umbraco {major} on nuget.org")
    return list(dict.fromkeys([declared_floor(major), latest]))


# ----------------------------------------------------------------- site --
def evict_from_cache(packages):
    """Delete the packages' exact versions from the NuGet global-packages folder.

    A local rebuild keeps the same version (`17.0.0-alpha.0`), and restore would otherwise reuse the
    first copy it extracted rather than the freshly packed one.

    :param packages: a dict of package id to version.
    """
    cache = nuget_global_packages()
    for package, version in packages.items():
        remove_tree(cache / package.lower() / version.lower())


def write_nuget_config(site, folder):
    """Give the site a nuget.config with the local package folder next to nuget.org.

    The harness's `add_package(source=...)` only applies to the `dotnet add package` step, not to the
    restore `build` runs, so the source has to live in configuration. Source mapping sends the Log
    Explorer packages to the local folder only, so nothing of the same id and version on nuget.org
    can stand in for what was just packed.

    :param site: the scaffolded harness `Site`.
    :param folder: the folder of .nupkg files.
    """
    (site.dir / "nuget.config").write_text(f"""<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="log-explorer-local" value="{Path(folder).resolve()}" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="log-explorer-local">
      <package pattern="{PACKAGE_ID}" />
      <package pattern="{PACKAGE_ID}.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
""", encoding="utf-8")


# --------------------------------------------------------------- checks --
def call(session, method, url, body=None):
    """Send an authenticated request to the site (any path, not only the Management API).

    :param session: the harness `ManagementApi` from `admin_session()`.
    :param method: the HTTP method.
    :param url: the path from the site root, e.g. `/umbraco/log-explorer/api/v1/sources`.
    :param body: an object to send as JSON.
    :returns: the parsed JSON body.
    :raises SmokeFailure: when the status is not 200 or the body is not JSON.
    """
    status, _, text = session.http.request(method, session.host + url, json_body=body,
                                           headers={"Authorization": f"Bearer {session.token}"})
    if status != 200:
        raise SmokeFailure(f"{method} {url} returned HTTP {status}: {text[:300]}")
    try:
        return json.loads(text)
    except ValueError:
        raise SmokeFailure(f"{method} {url} did not return JSON: {text[:300]}") from None


def check(condition, message):
    """Raise a SmokeFailure with the message when the condition is false."""
    if not condition:
        raise SmokeFailure(message)


def run_checks(site, package_version):
    """Run every check against a started site.

    :param site: the running harness `Site`.
    :param package_version: the version of the Log Explorer package under test.
    :raises SmokeFailure: on the first failing check.
    :raises SpawnError: when the admin cannot sign in.
    """
    session = site.admin_session()

    # The version that actually runs, so a floor test cannot silently resolve a newer Umbraco.
    running = call(session, "GET", "/umbraco/management/api/v1/server/information").get("version", "")
    check(running.split("+")[0] == site.umbraco_version,
          f"The site runs Umbraco {running}, expected {site.umbraco_version}")
    say(f"Signed in; the site runs Umbraco {running}")

    sources = call(session, "GET", f"{API}/sources")
    files = next((s for s in sources if s.get("alias") == "files"), None)
    check(files is not None, f"GET /sources has no `files` source: {sources}")
    check(files.get("type") == "UmbracoFiles", f"`files` source has type {files.get('type')!r}")
    say("GET /sources lists the zero-configuration `files` source")

    settings = call(session, "GET", f"{API}/settings")
    check(settings.get("defaultSource") == "files", f"GET /settings: unexpected body {settings}")
    say("GET /settings answers")

    # The backoffice discovers umbraco-package.json from App_Plugins; its private manifest list proves it did.
    manifests = call(session, "GET", "/umbraco/management/api/v1/manifest/manifest/private")
    check(any(m.get("id") == PACKAGE_ID or m.get("name") == "Log Explorer" for m in manifests),
          f"The backoffice does not list the package manifest (got {[m.get('name') for m in manifests]})")
    manifest = call(session, "GET", f"{APP_PLUGIN}/umbraco-package.json")
    check(manifest.get("version") == package_version,
          f"umbraco-package.json has version {manifest.get('version')!r}, expected {package_version!r}")
    bundle = next(e["js"] for e in manifest["extensions"] if e.get("type") == "bundle")
    status, _, text = session.http.request("GET", session.host + bundle)
    check(status == 200 and text.strip(), f"GET {bundle} returned HTTP {status}")
    # The bundle imports hashed chunks next to it; each must be in the site's static asset manifest too.
    for chunk in sorted(set(re.findall(r"\./([\w.-]+\.js)", text))):
        url = f"{bundle.rsplit('/', 1)[0]}/{chunk}"
        status, _, _ = session.http.request("GET", session.host + url)
        check(status == 200, f"GET {url} (imported by the bundle) returned HTTP {status}")
    say(f"The backoffice lists the package; {APP_PLUGIN} serves umbraco-package.json {package_version} and the bundle")

    page = call(session, "POST", f"{API}/sources/files/search", {"range": {"relative": "1h"}, "take": 10})
    records = page.get("records")
    check(isinstance(records, list) and records, f"POST /sources/files/search returned no entries: {page}")
    say(f"POST /sources/files/search returned {len(records)} entries from the site's own log files")


def smoke(root, umbraco, folder, packages, keep):
    """Spawn one Umbraco version, install the package, run the checks and remove the site.

    :param root: the sites folder.
    :param umbraco: the exact Umbraco version.
    :param folder: the folder of .nupkg files.
    :param packages: a dict of package id to version.
    :param keep: leave the site running instead of removing it.
    :returns: None when every check passed, else the failure message.
    """
    site = None
    try:
        site = scaffold(root, f"logexplorer-{umbraco}-{time.strftime('%H%M%S')}", umbraco)
        write_nuget_config(site, folder)
        site.add_package(PACKAGE_ID, packages[PACKAGE_ID])
        print("Building", flush=True)
        site.build()
        site.start()
        run_checks(site, packages[PACKAGE_ID])
        return None
    except (SpawnError, SmokeFailure) as e:
        if site:
            print(site.tail_log(), file=sys.stderr)
        return str(e)
    finally:
        if site and not keep:
            site.remove()


def main():
    utf8_stdio()
    p = argparse.ArgumentParser(prog="smoke_nupkg.py", description=__doc__,
                                formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--packages", required=True, help="Folder of packed .nupkg files (dotnet pack -o)")
    p.add_argument("--major", required=True, type=int, choices=(17, 18), help="Umbraco major the packages target")
    p.add_argument("--umbraco", action="append", help="Exact Umbraco version to try (repeatable; default: floor and latest)")
    p.add_argument("--root", help="Folder for the spawned sites (default: a new temp folder)")
    p.add_argument("--keep", action="store_true", help="Leave the sites running")
    a = p.parse_args()

    packages = read_packages(a.packages)
    version = packages[PACKAGE_ID]
    if version.split(".")[0] != str(a.major):
        sys.exit(f"error: {PACKAGE_ID} {version} is not an Umbraco {a.major} build (ADR 0010)")
    for umbraco in a.umbraco or []:
        if umbraco.split(".")[0] != str(a.major):
            sys.exit(f"error: Umbraco {umbraco} is not Umbraco {a.major}")
    umbraco_versions = a.umbraco or versions_to_try(a.major)
    root = Path(a.root) if a.root else Path(tempfile.mkdtemp(prefix="logexplorer-smoke-"))
    evict_from_cache(packages)

    results = {}
    for umbraco in umbraco_versions:
        heading(f"{PACKAGE_ID} {version} on Umbraco {umbraco}")
        results[umbraco] = smoke(root, umbraco, a.packages, packages, a.keep)
        if results[umbraco]:
            warn(f"Umbraco {umbraco}: {results[umbraco]}")

    print()
    for umbraco, failure in results.items():
        print(f"  Umbraco {umbraco}: {'FAIL - ' + failure.splitlines()[0] if failure else 'pass'}")
    failed = [u for u, f in results.items() if f]
    print(f"{len(results) - len(failed)}/{len(results)} passed" + (f" (sites kept in {root})" if a.keep else ""))
    if not a.keep and not a.root:
        shutil.rmtree(root, ignore_errors=True)
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
