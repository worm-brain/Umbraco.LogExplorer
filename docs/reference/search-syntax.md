# Search syntax

The search box reads one forgiving syntax for every source. Press Enter and the explorer turns the
forms below into chips; anything else is searched as text. The input is parsed on the server, so
every client reads it the same way, and it is limited to 2,000 characters.

## Forms

| Input | Meaning |
| --- | --- |
| `timeout` | Text search: the message (or the exception) contains the word, any case. Several words must all match. |
| `"connection refused"` | Exact phrase text search. |
| `field:value` | The field equals the value; strings compare case-insensitively. |
| `field:"value with spaces"` | The same, with a quoted value. |
| `-field:value` | Exclude: the field does not equal the value. |
| `field:val*` | Starts with. |
| `field:*val` | Ends with. |
| `field:*val*` | Contains. |
| `field>1000`, `field>=1000`, `field<1000`, `field<=1000` | Numeric or date comparison. |
| `has:field` | The entry has the field. |
| `-has:field` | The entry does not have the field. |
| `level:warn` | That level and above. Sets the level toggles; makes no chip. |
| `level=warn` | Exactly that level. Sets the level toggles; makes no chip. |

Level names are the OpenTelemetry short names: `trace`, `debug`, `info`, `warn`, `error` and
`fatal`.

The parser never rejects input. A token it cannot read as a filter, such as an unknown level,
`-level:`, `field=value` or `field:` with no value, is searched as a word. A double quote left open
makes the whole input a text search, and a notification says why.

## How chips combine

- Chips on **different** fields must all match.
- Include chips on the **same** field match either value: `path:/a path:/b` finds both paths.
- Text chips and native queries are combined with the chips.

## Field aliases

| Alias | Field |
| --- | --- |
| `level`, `severity` | `@severity` |
| `msg`, `message` | `@body` (the rendered message) |
| `template` | `@template` (the message template) |
| `source`, `scope` | `@scope` (`SourceContext`) |
| `trace` | `@traceId` |
| `ex`, `exception` | `@exception.type` |
| `path` | `RequestPath` |
| `status` | `StatusCode` |
| `machine` | `MachineName` |

Any other name is a property path: `RequestPath`, `Cart.Total` for a nested value, `Tags[]` for an
array. See [fields and levels](fields.md).

## Examples

| Input | Finds |
| --- | --- |
| `level:error path:/umbraco*` | errors on any path under `/umbraco` |
| `-status:200` | everything except successful requests |
| `Elapsed>1000` | entries with an `Elapsed` over 1,000 |
| `ex:*SqlException` | entries with any SQL exception |
| `has:ContentId` | entries that carry a content id |
| `source:Umbraco.Cms.Core.Sync*` | entries from one namespace |
