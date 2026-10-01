# ADR 0010: Build one package per Umbraco major from one source tree

- Status: Accepted
- Date: 2026-10-01
- Supersedes: ADR 0001 (its fallback is now the plan)
- Issues: #27, #28

## Context

ADR 0001 shipped one package compiled against 17.0.0 with an `[17.0.0, 19.0.0)` range, with a
planned fallback if Umbraco 18 broke something we use. It did: Umbraco 18 replaced Swashbuckle with
Microsoft.AspNetCore.OpenApi, and the types a package uses to register its API document on 17
(`BackOfficeSecurityRequirementsOperationFilterBase`, `OperationIdHandler`, `SwaggerGenOptions`) no
longer exist. Site18 failed at start-up with `TypeLoadException`. 18 offers
`builder.AddBackOfficeOpenApiDocument(...)` instead (present from 18.0.0), which 17 does not have.

Everything else the package touches so far (controller attributes, auth policies, backoffice
manifests and aliases) is identical between 17 and 18.

Jack chose this option over a single package that switches at runtime (reflection on 18) or one
that skips the API document on 18.

## Decision

- **One source tree, one package per major.** The global MSBuild property `UmbracoMajor` (default
  `17`) selects the target. `dotnet build` builds for 17; `dotnet build -p:UmbracoMajor=18` builds
  for 18. It must be a global property so it reaches project references and restore.
- `Directory.Build.props` defines `UMBRACO_17` or `UMBRACO_18` and gives non-17 builds their own
  `bin/u18` and `obj/u18` folders, so the two never share restore assets.
- `Directory.Packages.props` sets, per major, `UmbracoPackageRange` (the compile floor and declared
  range: `[17.0.0, 18.0.0)` or `[18.0.0, 19.0.0)`) and `UmbracoDevVersion` (what sample sites and tests
  run: 17.7.0 or 18.2.0).
- **Major-specific code lives in small paired files** (`*.V17.cs` / `*.V18.cs`, each wrapped in
  `#if UMBRACO_17` / `#if UMBRACO_18`), never scattered `#if` blocks. The first pair is
  `LogExplorerApiDocument`.
- **Versions:** the package and Core version lines follow the major: `17.x.y` for Umbraco 17,
  `18.x.y` for 18; local builds are `{major}.0.0-dev`. A release tag `v17.0.0-alpha.1` packs the 17
  build at that version; `v18.0.0-alpha.1` packs the 18 build. The tag's first number selects the major.
- **Sample sites** each belong to one major: Site17 runs only on a 17 build and Site18 only on an 18
  build (`-p:UmbracoMajor=18`). Each checks the Umbraco major at start-up and refuses to boot on the
  wrong one, so its database is never upgraded or downgraded by accident.
- **CI** builds, tests and packs both majors (matrix `[17, 18]`), and typechecks the client against
  each major's lowest `@umbraco-cms/backoffice` (17.0.0, 18.0.0). The client's JS output does not
  depend on the major (`@umbraco-cms/*` is external), so it is built once per pack.

## Consequences

- Every future 17/18 API difference fails a build instead of a site.
- Two packages to release per version, and roughly twice the CI time.
- BRIEF §2 and the release instructions use the per-major scheme.
- Both majors publish the `log-explorer` document, at different URLs: `/umbraco/swagger/log-explorer/swagger.json`
  on 17 (Swashbuckle) and `/umbraco/openapi/log-explorer.json` on 18. Paths and operation ids match
  (verified for `GET /sources`), so the TypeScript client is generated from the 17 document.
