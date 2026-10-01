# Architecture decision records

One file per decision, numbered in order, never renumbered. A superseded ADR stays in place with
`Status: Superseded by NNNN`.

Write a new ADR when a decision changes a contract, a dependency, a supported version, a §2 choice
in `docs/BRIEF.md`, or when a **Verify** item in the briefs turns out differently from what the
brief assumed. Update the brief in the same change.

| ADR | Decision | Status |
| --- | --- | --- |
| [0001](0001-one-source-for-umbraco-17-and-18.md) | One source tree serves Umbraco 17 and 18 | Superseded by 0010 |
| [0002](0002-organise-by-feature.md) | Organise code by feature (vertical slices) | Accepted |
| [0003](0003-read-stores-directly.md) | Read log stores directly, never query through `ILogViewerService` | Accepted |
| [0004](0004-level-filter-is-a-set.md) | The level filter is a set of levels, not a minimum | Accepted |
| [0005](0005-one-simple-syntax-parser-on-the-server.md) | One simple-syntax parser, in Core, called over the API | Accepted |
| [0006](0006-tooling.md) | Tooling: Bun, Vitest, Prettier, xUnit v3, NSubstitute, CSharpier | Accepted |
| [0007](0007-hide-core-log-viewer-by-default.md) | Hide the core Log Viewer by default | Accepted |
| [0008](0008-core-contract-refinements.md) | Core contract refinements from Phase 0 | Accepted |
| [0009](0009-package-api-route.md) | The package API lives at `/umbraco/log-explorer/api/v1` | Accepted |
| [0010](0010-one-package-per-umbraco-major.md) | Build one package per Umbraco major from one source tree | Accepted |
| [0011](0011-settings-endpoint.md) | The client reads its settings from `GET /settings` | Accepted |
| [0012](0012-file-cursor-per-machine-stream.md) | The files cursor holds one position per machine stream; record ids are base64url | Accepted |
| [0013](0013-files-native-dialect-is-the-core-viewers.md) | The files source's native dialect is the core Log Viewer's own Serilog.Expressions setup | Accepted |
| [0014](0014-package-api-json-and-query-errors.md) | The package API formats JSON with `LogJson`; malformed queries return `invalid_query` | Accepted |
| [0015](0015-results-list-virtual-window.md) | The results list virtualises with its own fixed-height row window | Accepted |

## Template

```markdown
# ADR NNNN: <decision as a sentence>

- Status: Proposed | Accepted | Superseded by NNNN
- Date: YYYY-MM-DD
- Issue: #n

## Context

## Decision

## Consequences
```
