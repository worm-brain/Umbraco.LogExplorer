# ADR 0011: The client reads its settings from GET /settings

- Status: Accepted
- Date: 2026-10-01
- Issue: #49

## Context

Some `LogExplorer` options change what the backoffice client does before any query runs:
`HideCoreLogViewer` (ADR 0007) decides whether the entry point removes the core Log Viewer menu
item, and `DefaultSource` / `DefaultTimeRange` (BRIEF §13) are the view state the explorer starts
from (#37). The options live on the server; BRIEF §11.1 had no endpoint that returns them.

Alternatives considered:

- Writing the values into `umbraco-package.json` at build time. The package is built once and
  configured per site, so a build-time value cannot follow `appsettings.json`.
- A server-side manifest filter or a C# `IManifestFilter` to drop the core item. The core item is a
  client-side manifest registered by the backoffice bundle, so only the client extension registry
  can remove it.
- Returning the flags from `GET /sources`. That mixes source identity with UI settings and makes
  the source picker's response carry unrelated fields.

## Decision

- Add `GET /umbraco/log-explorer/api/v1/settings` (Phase 1), returning
  `{ hideCoreLogViewer, defaultSource, defaultTimeRange }` from `IOptions<LogExplorerOptions>`.
- It derives from `LogExplorerApiControllerBase`, so it has the same Settings-section policy and
  appears in the `log-explorer` OpenAPI document on both majors.
- Only UI flags and defaults are exposed. Sources, masking rules and secrets stay server-side
  (BRIEF §12).
- The backoffice entry point fetches it once at start-up and calls
  `extensionRegistry.exclude("Umb.MenuItem.LogViewer")` when `hideCoreLogViewer` is true. Any failure
  (network, 401/403 for users without Settings access) keeps the core item.

## Consequences

- BRIEF §11.1 gains a `/settings` row.
- New client-facing options are added to this response rather than to a new endpoint.
- Exclusion lives in the registry only, so uninstalling the package restores the core item with no
  clean-up (ADR 0007).
