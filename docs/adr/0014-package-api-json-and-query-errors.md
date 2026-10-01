# ADR 0014: The package API formats JSON with LogJson, and malformed queries return invalid_query

- Status: Accepted
- Date: 2026-10-01
- Issue: #38

## Context

`POST /sources/{alias}/search` is the first endpoint that takes a Core contract (`LogQuery`) as its
body. The controllers used the backoffice JSON options (ADR 0009), which cannot read it:

- `LogQuery.Levels` is an `IReadOnlySet<string>`; System.Text.Json cannot instantiate that
  interface without the `ReadOnlyStringSetConverter` that only `LogJson.Options` registers (400 on
  any request with levels).
- `FilterNode`'s `kind` discriminator must come first in the object; `LogJson.Options` allows it
  anywhere (ADR 0008), browser-built JSON does not guarantee the order.
- Enums are written in PascalCase (`Descending`, `StartsWith`), where `LogJson` and the client use
  camelCase.

The API documents also misdescribed the contracts. Umbraco 17's SwaggerGen setup names every
polymorphic discriminator `$type` (the runtime uses `kind`) and lists enum values in PascalCase;
the empty `FilterNode` base with `additionalProperties: false` made hey-api intersect every node
with `{ [key: string]: never }`. Umbraco 18's Microsoft.AspNetCore.OpenApi document takes its
schemas from the minimal-API `JsonOptions` of the configured name, not the MVC ones.

BRIEF §11.1's error codes have nothing for a request that is simply malformed: an unknown
relative range, `From` not before `To`, a cursor from another source, `take` below 1, an invalid
`matches` regex. Providers throw `ArgumentException` for all of these (ADR 0008 conventions).

## Decision

- **Named JSON options.** The package registers MVC `JsonOptions` named `log-explorer`
  (Umbraco's `AddJsonOptions(name, ...)`), configured by the new `LogJson.Apply`, which is also
  how `LogJson.Options` is built, so there is still one configuration. `LogExplorerApiControllerBase`
  uses `[JsonOptionsName("log-explorer")]` instead of the backoffice name. Existing endpoints are
  unaffected (their models have no enums or sets).
- **Documents describe the wire format.**
  - 17: a document filter, for the `log-explorer` document only, renames `$type` to `kind` on the
    `FilterNode` subtypes, drops their `allOf` link to the empty base (and the base schema),
    marks record parameters with defaults (`caseInsensitive`, `phrase`) optional, and writes Core
    enums as camelCase strings.
  - 18: the minimal-API `JsonOptions` named `log-explorer` get the same `LogJson.Apply`, with
    strict number handling so `take` is an integer rather than "integer or string". Its schema
    names differ from 17's (`FilterNodeConditionNode`); the client is generated from the 17
    document.
- **`invalid_query`.** `ArgumentException` maps to 400 with code `invalid_query`.
- **`range_too_large`** is checked by the API, not each provider: `QueryRangeGuard` resolves the
  range against `TimeProvider` and throws `RangeTooLargeException` (400 `range_too_large`) when it
  is longer than the source's `MaxRange`.

## Consequences

- BRIEF §11.1 lists `invalid_query` with the other codes.
- New endpoints taking Core contracts work without further JSON work; new Core enums are
  documented in camelCase automatically on 17.
- Every query endpoint should call `QueryRangeGuard.EnsureAllowed` before the source runs.
