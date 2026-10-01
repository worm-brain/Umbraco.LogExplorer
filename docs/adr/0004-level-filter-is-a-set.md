# ADR 0004: The level filter is a set of levels, not a minimum

- Status: Accepted
- Date: 2026-10-01

## Context

BRIEF §8.2 modelled the level filter as `int? MinSeverity`. The UI brief (§4.7) and the prototype
make the histogram's level toggles the level filter: each of the six OTel levels can be switched on
or off independently (for example hide INFO but keep DEBUG), and `level:warn` typed in the search
box sets the toggles rather than adding a chip. A minimum cannot express that.

## Decision

- `LogQuery.MinSeverity` is replaced by `LogQuery.Levels`: an optional set of OTel short names
  (`trace`, `debug`, `info`, `warn`, `error`, `fatal`). `null` or all six means no level filter.
- Each short name covers its OTel severity-number band (TRACE 1-4, DEBUG 5-8, INFO 9-12, WARN
  13-16, ERROR 17-20, FATAL 21-24). Severity 0 (unspecified) counts as INFO.
- `level:warn` in the simple syntax resolves to `{warn, error, fatal}`; `level=error` resolves to
  `{error}`. Neither produces a chip; both set the toggles.
- Histogram and Overview level counts are computed **without** the level filter, so users see what
  they are hiding (UI brief §4.7). Every other result honours it.
- Providers compile the set to their native form (`@Level in [...]`, `SeverityLevel in (...)`).

## Consequences

- BRIEF §8.2, §6.2 and §10.5 updated accordingly.
- The contract suite tests non-contiguous sets (for example `{debug, error}`).
