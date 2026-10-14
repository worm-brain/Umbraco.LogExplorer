/**
 * Stamps the package version into the built `umbraco-package.json`, so the NuGet package and the back-office
 * manifest agree. `dotnet pack` passes the version in; a plain `bun run build` falls back to `<Version>` in
 * `Umbraco.Community.LogExplorer.csproj` when it is a literal version, and to `0.0.0-dev` otherwise.
 */
import { readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import type { Plugin } from "vite";

/** The version stamped when the csproj `<Version>` is computed by MSBuild rather than written out. */
const devVersion = "0.0.0-dev";

/** A version written out in full: SemVer, or NuGet's four-part form, with optional pre-release and build parts. */
const literalVersion = /^\d+(\.\d+){2,3}(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$/;

/**
 * Picks the version to stamp.
 *
 * @param envVersion - `LOG_EXPLORER_VERSION`, which `dotnet pack` sets from `$(PackageVersion)`, so a
 *   `-p:Version=...` override reaches the manifest too. Empty or unset during day-to-day `bun run build`/`watch`.
 * @param csproj - The package `.csproj` source, whose `<Version>` is the fallback when it is a literal version.
 * @returns The environment version; else the csproj `<Version>` when it is literal; else `0.0.0-dev`, because
 *   a `<Version>` built from MSBuild properties (`$(UmbracoMajor).0.0-dev`) means nothing outside MSBuild.
 * @throws When the csproj has no `<Version>` and the environment none either, so a broken csproj is noticed.
 */
export function resolvePackageVersion(envVersion: string | undefined, csproj: string): string {
  if (envVersion?.trim()) {
    return envVersion.trim();
  }

  const version = /<Version>\s*([^<\s]+)\s*<\/Version>/.exec(csproj)?.[1];
  if (!version) {
    throw new Error("No <Version> in the package .csproj and LOG_EXPLORER_VERSION is not set.");
  }
  return literalVersion.test(version) ? version : devVersion;
}

/**
 * Returns the `umbraco-package.json` source with its `version` replaced; every other field is kept as-is.
 */
export function withVersion(manifestJson: string, version: string): string {
  const manifest = JSON.parse(manifestJson) as Record<string, unknown>;
  return `${JSON.stringify({ ...manifest, version }, null, 2)}\n`;
}

/**
 * Vite plugin that rewrites the `umbraco-package.json` copied from `public/` into the output directory.
 * It runs in `writeBundle`, after Vite has copied `public/`, so the file is there to rewrite.
 *
 * @param csprojPath - Path to the package `.csproj` that defines the version.
 */
export function umbracoPackageVersion(csprojPath: string): Plugin {
  return {
    name: "umbraco-package-version",
    apply: "build",
    writeBundle(outputOptions) {
      const version = resolvePackageVersion(process.env.LOG_EXPLORER_VERSION, readFileSync(csprojPath, "utf8"));
      const manifestPath = join(outputOptions.dir!, "umbraco-package.json");
      writeFileSync(manifestPath, withVersion(readFileSync(manifestPath, "utf8"), version));
    },
  };
}
