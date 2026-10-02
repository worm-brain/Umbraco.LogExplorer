# Fields and levels

Every source maps its entries to one record shape, based on the
[OpenTelemetry log data model](https://opentelemetry.io/docs/specs/otel/logs/data-model/). Fields
that start with `@` are portable: they mean the same thing on every source. Everything else is a
property of the entry.

## Portable fields

| Field | Meaning | From Umbraco's log files |
| --- | --- | --- |
| `@timestamp` | When the entry was logged | `@t` |
| `@severity` | The level (see [Levels](#levels)) | `@l`; absent means Information |
| `@body` | The rendered message | `@m`, or rendered from `@mt` and the properties |
| `@template` | The message template | `@mt` |
| `@traceId` / `@spanId` | Trace correlation ids | `@tr` / `@sp` |
| `@scope` | The logging category | `SourceContext` |
| `@exception.type` / `@exception.message` | The exception, when there is one | parsed from `@x` |
| `@resource.*` | Where it was logged, such as `@resource.host.name` | the machine name in the file name |

## Properties

Any other name is a property of the entry, such as `RequestPath`, `StatusCode` or `ContentId`.
Properties keep their types: numbers compare as numbers, and objects and arrays stay structured.

- A nested value is addressed with a dot: `Cart.Total`.
- An array's elements are addressed with brackets: `Tags[]`.

The fields panel and the entry drawer show the properties each entry carries. Umbraco adds some to
every entry, such as `MachineName`, `ProcessId` and `ThreadId`.

## Levels

The explorer uses the six OpenTelemetry levels and compares them by number:

| Level | Serilog | Microsoft.Extensions.Logging |
| --- | --- | --- |
| TRACE | Verbose | Trace |
| DEBUG | Debug | Debug |
| INFO | Information | Information |
| WARN | Warning | Warning |
| ERROR | Error | Error |
| FATAL | Fatal | Critical |

An entry with no level is treated as INFO when filtering.

## Related

- [Search syntax](search-syntax.md), including the field aliases.
- [How log files are read](../explanation/reading-log-files.md)
