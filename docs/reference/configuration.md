# Configuration

Every setting is optional and lives under `LogExplorer` in `appsettings.json` (or any other
configuration source, such as environment variables: `LogExplorer__HideCoreLogViewer=false`).
With no `LogExplorer` section at all, the explorer reads the site's own log files.

## Settings

| Setting | Default | What it does |
| --- | --- | --- |
| `HideCoreLogViewer` | `true` | Hides the core **Settings > Log Viewer** menu item. Set `false` to show both tools. |
| `DefaultSource` | `"files"` | The alias of the source the explorer opens on. If it is not visible to the user, the first visible source is used. |
| `DefaultTimeRange` | `"1h"` | The range the explorer opens with: `15m`, `1h`, `4h`, `24h`, `7d` or `30d`. |
| `CorrelationFields` | `["@traceId", "RequestId", "HttpRequestId"]` | The fields **Same request** tries, in order; the first with a value on the entry is used. |
| `PinnedFacets` | `["SourceContext", "RequestPath", "StatusCode", "MachineName", "@exception.type"]` | The fields always shown first in the fields panel, in this order. |
| `Files:ScanBudgetMegabytes` | `256` | How much log the histogram, fields panel and patterns read before they stop and report **Approximate** counts over the newest entries. |
| `Sources` | one `files` source | The log sources the explorer can read. See [Sources](#sources). |

A full example with the defaults:

```json
{
  "LogExplorer": {
    "HideCoreLogViewer": true,
    "DefaultSource": "files",
    "DefaultTimeRange": "1h",
    "CorrelationFields": ["@traceId", "RequestId", "HttpRequestId"],
    "PinnedFacets": ["SourceContext", "RequestPath", "StatusCode", "MachineName", "@exception.type"],
    "Files": { "ScanBudgetMegabytes": 256 },
    "Sources": [
      { "Alias": "files", "Type": "UmbracoFiles", "DisplayName": "This server's log files" }
    ]
  }
}
```

## Sources

Each entry in `Sources` is one configured source. With `Sources` empty or missing, a single
`files` source of type `UmbracoFiles` is registered for you.

| Property | Default | What it does |
| --- | --- | --- |
| `Alias` | (required) | Unique id, used in URLs and API routes. |
| `Type` | (required) | The provider type. This release ships `UmbracoFiles` and `Fake`. |
| `DisplayName` | the alias | The name shown in the source picker. |
| `AllowNativeQuery` | `true` | Set `false` to hide native mode and refuse native queries for this source. **Show generated query** stays available. |
| `Sensitive` | `false` | Marks the source with a lock and, unless `AllowedUserGroups` is set, shows it only to the `admin` group. |
| `AllowedUserGroups` | `[]` | User group aliases that may see and query the source. Empty means everyone with Settings access (or `admin` only for a sensitive source). |
| `Settings` | `{}` | Provider-specific settings, as string key-value pairs. |

The explorer checks the sources when the site starts. An unknown `Type`, a duplicate alias or a
missing setting logs one clear error for that source, and the other sources keep working.

### `UmbracoFiles`

Reads the site's own Umbraco log files from the configured log directory (`umbraco/Logs` by
default), for every machine name in it. It has no `Settings`. Umbraco's file sink must roll daily,
which is Umbraco's default; a startup warning says so when it does not. Its native language is
Serilog Expressions. See [how log files are read](../explanation/reading-log-files.md).

### `Fake`

Generates a realistic sample hour of Umbraco-style entries, including a burst of SQL timeouts, in
memory. Use it to try the explorer or to demo it without real logs. Its `Settings`:

| Setting | Default | What it does |
| --- | --- | --- |
| `SampleHours` | `1` | How many copies of the sample hour to generate, back to back and ending now. |
| `Operators` | all | A comma-separated list of the filter operators the source accepts (for example `equals,notEquals`), to see how the explorer treats filters a source cannot run. |

```json
{ "Alias": "sample", "Type": "Fake", "DisplayName": "Sample data", "Settings": { "SampleHours": "48" } }
```

## Reserved for later releases

The configuration also binds `DeepLinks`, `Export:MaxRows`, `Masking` and `Files:TailPollSeconds`.
They belong to features planned for later releases (CMS links, export, masking and live tail)
and have no effect yet.

## Related

- [The provider model](../explanation/provider-model.md)
- [The security model](../explanation/security.md)
