# ADR 0019: The Overview reuses the query endpoints; sink minimum levels come from GET /minimum-levels

- Status: Accepted
- Date: 2026-10-01
- Issue: #48

## Context

The Overview view (BRIEF §6.10, UI brief §4.13) shows counts by level for the range, the most
frequent templates, the exception types and, for the files source, the configured minimum level
per sink. BRIEF §11.1 had no endpoint for the summary or for the sink levels. Things to settle:

- whether the summary needs its own aggregate endpoint;
- how the level counts treat the query's level set (ADR 0004);
- the shape and gating of the sink levels, the one query-time use of `ILogViewerService` that
  ADR 0003 allows.

Verified: `ILogViewerService.GetLogLevelsFromSinks()` returns
`ReadOnlyDictionary<string, Umbraco.Cms.Core.Logging.LogLevel>` synchronously, with the same
signature in Umbraco.Cms.Core 17.0.0, 17.7.0, 18.0.0 and 18.2.0 (reflection over the NuGet
assemblies). Its keys are the sink names, `Global` and `UmbracoFile`; `LogLevel` has Serilog's
member names (`Verbose` ... `Fatal`).

## Decision

1. **No aggregate endpoint.** Each panel calls an endpoint that already exists, with the query
   bar's `queryState`:
   - entries by level: `POST /histogram` with 30 buckets, summed per level;
   - most frequent messages: `POST /patterns` with `top: 6`;
   - exception types: `POST /facets` on `@exception.type` with `top: 10`. Both the files and the
     Fake source facet that portable field through `LogFields.Resolve`.
   A panel whose source lacks the feature (`histogram`, `patterns`, `facets`) says so in its box
   instead of querying.
2. **Level counts ignore the level set**, as the histogram's level toggles do (ADR 0004), so the
   panel always shows all six levels. A level the set hides keeps its count and is drawn muted
   with its count struck through. The other two panels honour the set, like every other query.
3. **`GET /sources/{alias}/minimum-levels`** returns `{ sinks: [{ name, level }] }`, `name` as
   Umbraco reports it and `level` as a lower-case OTel short name (ADR 0008). It answers only for
   sources of type `UmbracoFiles`; any other source gets 400 `unsupported_feature`. The client asks
   only for `UmbracoFiles` sources, once per source, and leaves the section out otherwise. The
   levels are the site's own Serilog configuration, so every files source on the site reports the
   same ones.
4. BRIEF §6.10's "top 10 templates" and "ERROR+ histogram" give way to the UI brief's three panels
   with the top six templates (the UI brief wins on layout).

## Consequences

- BRIEF §11.1 lists `/minimum-levels`; §6.10 points here.
- No V17/V18 pair is needed for the sink levels while the signature stays the same.
- The Overview sends three queries (four on a files source) per query change; each is aborted
  when the next change arrives.
