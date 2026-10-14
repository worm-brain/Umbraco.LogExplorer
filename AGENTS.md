# AGENTS.md

Instructions for anyone, person or agent, changing the code in this repository. This is the
canonical instructions file; tool-specific files such as `CLAUDE.md` point here. Contributions
follow [CONTRIBUTING.md](CONTRIBUTING.md): ideas and issues are welcome from anyone, and pull
requests are issue-first and invitation-only.

## What this is

`Umbraco.Community.LogExplorer` is an Umbraco 17/18 backoffice log explorer and a drop-in
replacement for the built-in Log Viewer: click-to-filter search over Umbraco's own log files, then
Application Insights and Seq, through a provider model built on the OpenTelemetry log record. It
ships two NuGet packages per Umbraco major: the package, and `Umbraco.Community.LogExplorer.Core`
with the provider contracts.

Some code comments and ADRs cite "BRIEF §n" or "UI brief §n". Those are the maintainer's private
build and UI briefs (ADR 0030); every decision a contributor needs is recorded in `docs/adr/`.

## Working rules

- Verify Umbraco and SDK APIs against the installed version before use; never invent aliases,
  routes, element names or members. When docs and the installed package disagree, the package wins.
- Record decisions in `docs/adr/` (see its README): a change to a contract, a dependency, a
  supported version or something an earlier ADR decided gets a new ADR.
- Organise by feature folder, not technical layer (ADR 0002).
- The Core project must not reference Umbraco packages; a Core.Tests test enforces it.
- Never query through `ILogViewerService` (ADR 0003).
- Tooling (ADR 0006): Bun, Vite, Vitest and Prettier for the client; xUnit v3, NSubstitute and
  CSharpier for .NET.
- One package per Umbraco major (ADR 0010): `dotnet build` targets 17, `-p:UmbracoMajor=18`
  targets 18. Major-specific code goes in paired `*.V17.cs` / `*.V18.cs` files. Build and test both
  majors before committing anything that touches Umbraco APIs.
- Run the tests before every commit and read the build summary. Stop any running sample site first:
  a locked DLL shows up as build errors.
- C# members carry XML doc comments, exported TypeScript carries TSDoc, and new or changed code
  comes with tests (`MethodName_Scenario_ExpectedBehaviour` names in .NET).
- Conventional Commits, ASCII only.
- Every user-visible change adds a line under `## [Unreleased]` in `CHANGELOG.md` and updates the
  `docs/` page that describes it (CONTRIBUTING.md has the docs style guide).
- Branches: `main` plus one short-lived branch per pull request. Both majors build from `main`.

## Commands

```sh
# .NET: format check, build and test for Umbraco 17; add -p:UmbracoMajor=18 for 18
dotnet tool restore && dotnet csharpier check .
dotnet build -warnaserror
dotnet test --solution Umbraco.Community.LogExplorer.slnx

# Client, in src/Umbraco.Community.LogExplorer/Client
bun install
bun run format:check && bun run typecheck && bun run test && bun run build
bun run e2e        # Playwright against both sample sites
bun run generate   # regenerate src/api/ from the running Umbraco 17 sample site

# Sample sites; build the client first, because its output is gitignored
dotnet run --project samples/LogExplorer.Site17                      # https://localhost:44370/umbraco
dotnet run --project samples/LogExplorer.Site18 -p:UmbracoMajor=18   # https://localhost:44380/umbraco
```

The sample sites install themselves on first run (SQLite) and sign in with `admin@example.com` /
`1234567890` (development only, set in `appsettings.Development.json`). The same file sets a fixed,
development-only `Umbraco:CMS:Imaging:HMACSecretKey`, because without one the unattended install
writes a generated key into `appsettings.json`; never commit a key there. Each runs on one Umbraco
major: a solution build for the other major skips it, and building or running it on its own for
the other major fails with the switch to use. The e2e suite starts them on ports 44374 (17) and
44384 (18).

To release, move the `## [Unreleased]` entries under `## [17.x.y] and [18.x.y] - <date>` in the
commit you tag, then push the tags `v17.x.y` and `v18.x.y`. `release.yml` runs the security gate,
tests, packs, smoke-tests the packed packages (ADR 0022), publishes to nuget.org and creates the
GitHub Release from that CHANGELOG section. A suffix (`v17.1.0-beta.1`) makes a pre-release.

## Backoffice client facts

- Source in `src/Umbraco.Community.LogExplorer/Client/src/`, one folder per feature: `search`,
  `query`, `results`, `histogram`, `facets`, `entry-detail`, `around`, `patterns`, `overview`,
  `show-query`, `share`, `sources`, `time-range`, `settings` and `workspace`. Shared code is in
  `shared/`, entry points in `entrypoints/`, localisation in `lang/`.
- Vite builds it into `wwwroot/App_Plugins/UmbracoCommunityLogExplorer/`. The manifest
  `public/umbraco-package.json` has the id `Umbraco.Community.LogExplorer`; the build stamps its
  version from `LOG_EXPLORER_VERSION`, which `dotnet pack` sets.
- Shared query state lives in `LogExplorerQueryContext` (`query/query.context.ts`, token
  `LOG_EXPLORER_QUERY_CONTEXT`), not in chains of properties. Components talk through typed custom
  events with `bubbles: true` and `composed: true`.
- The Management API client in `src/api/` is generated by `@hey-api/openapi-ts` from the package's
  `log-explorer` OpenAPI document (`bun run generate`). Never hand-edit it.
- UUI and the backoffice's `umb-*` elements come first, with their variants (UI brief §4.0). Build a
  custom element only when nothing fits, and say in a comment which components were checked. Lit
  and UUI are imported through `@umbraco-cms/backoffice/external/*`.
- Colours and sizes come from UUI tokens (`--uui-color-*`, `--uui-size-*`). Use a plugin-scoped
  `--log-explorer-*` custom property only when no token fits, and define it once.
- Log text is rendered as text, never as HTML.
- Every control is keyboard-reachable and labelled; toggles expose `aria-pressed` and menus
  `aria-expanded`.
