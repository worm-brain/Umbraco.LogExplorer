# ADR 0022: Smoke-test the packed nupkgs on spawned sites before publishing

- Status: Accepted
- Date: 2026-10-01
- Issue: #52

## Context

Every test so far runs the package as a project reference. The sample sites and the e2e suite
(ADR 0020) reference the project and run only the dev versions (17.7.0, 18.2.0), and the floor
checks for 17.0.0 and 18.0.0 (ADR 0010) are client typechecks. Nothing exercises the `.nupkg`
itself at runtime: the client build shipped as static web assets under `App_Plugins`, the
composer registering itself in a site that only has a `PackageReference`, the zero-configuration
`files` source, or the declared lowest Umbraco versions (`[17.0.0, 18.0.0)`, `[18.0.0, 19.0.0)`).

`worm-brain/umbraco-spawn-harness` (public, Python, standard library only) spawns a throwaway site
of any exact Umbraco version: `dotnet new umbraco` from that `Umbraco.Templates` version, SQLite, an
unattended install, and an admin Management API session.

## Decision

1. **`tests/package-smoke/smoke_nupkg.py`** is a PEP 723 script pinning a harness tag (`v0.1.0`),
   run with `uv run`. It takes a folder of packed nupkgs and the major, and for each Umbraco version:
   scaffolds a clean site, adds `Umbraco.Community.LogExplorer` from the folder, builds and starts it,
   signs in as the admin and checks, with no `LogExplorer` configuration:
   - the site runs the requested Umbraco version (so a floor run cannot resolve a newer one);
   - `GET /umbraco/log-explorer/api/v1/sources` lists `files` (`UmbracoFiles`);
   - `GET /settings` answers with `defaultSource: files`;
   - the backoffice lists the package manifest, and `/App_Plugins/UmbracoCommunityLogExplorer/`
     serves `umbraco-package.json` stamped with the package version and the bundle it names;
   - `POST /sources/files/search` over the last hour returns 200 with entries from the site's own logs.

   The site is removed afterwards, also on failure. Any failure exits 1 with the failing check.
2. **Versions:** the floor of the major's `UmbracoPackageRange` in `Directory.Packages.props` (17.0.0,
   18.0.0) and the newest stable release of the major (resolved on nuget.org at run time).
   `--umbraco` overrides the list.
3. **Local package source:** the harness's `add_package(source=...)` applies only to the
   `dotnet add package` step, not to the restore `build` runs, so the script writes a `nuget.config`
   into the site folder with nuget.org and the nupkg folder, and maps `Umbraco.Community.LogExplorer`
   and `Umbraco.Community.LogExplorer.*` to the folder only. It also deletes those exact versions from
   the NuGet global-packages folder first, because local repacks reuse one version
   (`17.0.0-alpha.0`) and restore would otherwise take the stale copy.
4. **Release workflow:** a `smoke` job after `pack` downloads the `packages` artifact, sets up .NET
   and uv, creates the dev certificate and runs the script for the tag's major. `publish` needs
   `smoke`, so nothing reaches nuget.org without passing it.
5. **This is test tooling only.** ADR 0006 sets the product's tooling (Bun, Vitest, Prettier,
   xUnit v3, NSubstitute, CSharpier); nothing in the package, its build or its tests depends on
   Python. The script is not part of `dotnet test` or `bun run test`. The harness's admin
   (`admin@example.com` / `Password1234!`) is its own and separate from the samples' login. Its
   sites take ports from 44800 up, clear of the sample and e2e ports.

## Consequences

- A packaging defect (missing client assets, composer not running in a consumer site, a floor that
  does not actually boot) fails the release before publishing.
- Running it needs the .NET 10 SDK, uv and network access to nuget.org; one major takes a few minutes
  locally, mostly restore.
- The harness is pinned by tag; moving to a newer harness is a one-line change to the script's
  inline metadata.
- The floor is read from `Directory.Packages.props`, so raising the floor moves the smoke test with it.
