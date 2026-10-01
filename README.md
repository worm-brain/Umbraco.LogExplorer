<img src="https://raw.githubusercontent.com/worm-brain/Umbraco.LogExplorer/main/assets/icon/umbraco-logexplorer.svg" alt="" width="96" height="96">

# Umbraco Log Explorer

A click-to-filter log explorer for the Umbraco backoffice. Find the log entries you need without
knowing a query language: pick a time range, click values, facets, histogram bars and patterns, and
the filters build themselves. The generated Serilog expression is always one click away.

> **Alpha.** The `17.0.0-alpha` and `18.0.0-alpha` packages read the site's own Umbraco log files.
> Application Insights and Seq providers, saved views and export are planned for 1.0.

_Screenshot to follow._

## Install

Pick the package line that matches your Umbraco major:

| Umbraco | Package version |
| ------- | --------------- |
| 17.x    | `17.*`          |
| 18.x    | `18.*`          |

```sh
dotnet add package Umbraco.Community.LogExplorer --prerelease
```

Run the site and open **Settings > Advanced > Log Explorer**. No configuration is needed: the
package registers a `files` source that reads the site's own `umbraco/Logs` files, and it hides the
core Log Viewer menu item (set `HideCoreLogViewer` to `false` to keep it).

## What it does

- **Search** with a forgiving search box: bare words search the message; `field:value`,
  `-field:value`, `field:val*`, `field>1000`, `has:field` and `level:warn` become filter chips.
- A **histogram** stacked by level, whose level toggles are the level filter; click or drag to zoom.
- A **fields panel** with the top values of common fields; click to include, the minus to exclude.
- An **entry drawer** with the typed properties and the exception, plus **Same request** (every entry
  from that request in one click) and **Around this** (the entries either side).
- **Patterns** (entries grouped by message template) and an **Overview** of the time range.
- **Show query** with the Serilog expression for the core Log Viewer, and a native mode that accepts
  it. **Copy link to this view** shares exactly what you see.

## Configure (optional)

Everything lives under `LogExplorer` in `appsettings.json`. The defaults:

```json
{
  "LogExplorer": {
    "HideCoreLogViewer": true,
    "DefaultSource": "files",
    "DefaultTimeRange": "1h",
    "CorrelationFields": ["@traceId", "RequestId", "HttpRequestId"],
    "PinnedFacets": ["SourceContext", "RequestPath", "StatusCode", "MachineName", "@exception.type"],
    "Files": { "ScanBudgetMegabytes": 256 },
    "Sources": [{ "Alias": "files", "Type": "UmbracoFiles", "DisplayName": "This server's log files" }]
  }
}
```

- `DefaultTimeRange`: `15m`, `1h`, `4h`, `24h`, `7d` or `30d`.
- `CorrelationFields`: the fields **Same request** tries, in order.
- `Files:ScanBudgetMegabytes`: how much log the histogram, facets and patterns read before they
  report approximate counts over the most recent entries.
- `Sources`: leave it empty for the single `files` source. A source with `"AllowNativeQuery": false`
  hides native mode.

The explorer needs access to the Settings section.

## Licence

MIT. Source, issues and the changelog:
[github.com/worm-brain/Umbraco.LogExplorer](https://github.com/worm-brain/Umbraco.LogExplorer).
