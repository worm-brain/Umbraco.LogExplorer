# Management API

The explorer's backoffice UI calls its own Management API. Scripts and tools can call it too.

- **Base path:** `/umbraco/log-explorer/api/v1`
- **Authentication:** a backoffice access token, as for Umbraco's own Management API. Every
  endpoint requires access to the Settings section.
- **OpenAPI document:** `log-explorer`, at `/umbraco/swagger/log-explorer/swagger.json` on
  Umbraco 17 and `/umbraco/openapi/log-explorer.json` on Umbraco 18.
- **JSON:** camelCase. Filters are objects with a `kind` of `and`, `or`, `not`, `condition` or
  `text`.

## Endpoints

| Method | Path | Body or query | Returns |
| --- | --- | --- | --- |
| GET | `/settings` | | The client settings: `hideCoreLogViewer`, `defaultSource`, `defaultTimeRange`, `pinnedFacets`, `correlationFields` |
| GET | `/sources` | | The sources you can see, with their capabilities |
| POST | `/parse` | `{ "input": "..." }` | Filter chips, a level set and any plain-text fallback reason |
| POST | `/sources/{alias}/search` | a query | A page of entries, with a cursor for the next page |
| POST | `/sources/{alias}/histogram` | `{ "query", "targetBuckets" }` | Counts per time bucket and level |
| POST | `/sources/{alias}/facets` | `{ "query", "fields", "top" }` | The top values of each field |
| GET | `/sources/{alias}/fields` | `from`, `to` or `relative` | The fields entries carry in a range |
| POST | `/sources/{alias}/patterns` | `{ "query", "top" }` | Entries grouped by message template |
| GET | `/sources/{alias}/records/{id}/context` | `before`, `after` | The entries either side of one entry |
| POST | `/sources/{alias}/compile` | a query | The query in the source's own language |
| POST | `/sources/{alias}/validate` | `{ "native": "..." }` | Whether a native query is valid, and where it is not |
| GET | `/sources/{alias}/minimum-levels` | | The configured minimum level of each Serilog sink (log files source) |

## A query

```json
{
  "range": { "relative": "1h" },
  "levels": ["error", "fatal"],
  "filter": {
    "kind": "condition",
    "field": "RequestPath",
    "op": "startsWith",
    "value": "/umbraco"
  },
  "take": 100,
  "sort": "descending"
}
```

- `range` is either `relative` (`15m`, `1h`, `4h`, `24h`, `7d`, `30d`) or `from` and `to`. Every
  response returns the absolute range that ran.
- `levels` uses lower-case level names; leave it out for every level.
- `op` is one of `equals`, `notEquals`, `contains`, `startsWith`, `endsWith`, `greaterThan`,
  `greaterOrEqual`, `lessThan`, `lessOrEqual`, `in`, `exists`, `notExists` or `matches`.
- `nativeQuery` adds a query in the source's own language.
- `cursor` is the `nextCursor` of the previous page. Record ids and cursors are opaque.

## Errors

Errors are ProblemDetails with a `code`:

| Code | Meaning |
| --- | --- |
| `source_not_found` | No source has that alias. |
| `forbidden_source` | The source exists but you may not use it. |
| `unsupported_feature` | The source cannot do that, for example native queries when `AllowNativeQuery` is `false`. |
| `range_too_large` | The range is longer than the source allows. |
| `invalid_query` | A malformed range, cursor, page size or regular expression. |
| `invalid_native_query` | The native query is invalid; `position` gives the zero-based offset of the error when known. |
| `record_not_found` | No entry has that id (context endpoint). |
