# Architecture decision records

One file per decision, numbered in order, never renumbered. A superseded ADR stays in place with
`Status: Superseded by NNNN`.

Write a new ADR when a decision changes a contract, a dependency, a supported version, a §2 choice
in `docs/BRIEF.md`, or when a **Verify** item in the briefs turns out differently from what the
brief assumed. Update the brief in the same change.

| ADR | Decision | Status |
| --- | --- | --- |
| [0001](0001-one-source-for-umbraco-17-and-18.md) | One source tree serves Umbraco 17 and 18 | Accepted |
| [0002](0002-organise-by-feature.md) | Organise code by feature (vertical slices) | Accepted |
| [0003](0003-read-stores-directly.md) | Read log stores directly, never query through `ILogViewerService` | Accepted |
| [0004](0004-level-filter-is-a-set.md) | The level filter is a set of levels, not a minimum | Accepted |
| [0005](0005-one-simple-syntax-parser-on-the-server.md) | One simple-syntax parser, in Core, called over the API | Accepted |
| [0006](0006-tooling.md) | Tooling: Bun, Vitest, Prettier, xUnit v3, NSubstitute, CSharpier | Accepted |
| [0007](0007-hide-core-log-viewer-by-default.md) | Hide the core Log Viewer by default | Accepted |

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
