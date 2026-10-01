# Sample sites

Two Umbraco sites for developing and testing the package. Both reference the package as a
project, install themselves unattended on first run (SQLite), and are never packed.

| Site | Umbraco | Build with | URL |
| --- | --- | --- | --- |
| `LogExplorer.Site17` | 17.7.0 | default (`UmbracoMajor=17`) | https://localhost:44370/umbraco |
| `LogExplorer.Site18` | 18.2.0 | `-p:UmbracoMajor=18` | https://localhost:44380/umbraco |

The package is built once per Umbraco major (ADR 0010), selected by the `UmbracoMajor` MSBuild
property; versions per major live in `Directory.Packages.props`. Each site refuses to start on the
wrong major, so its database is never upgraded or downgraded by accident.

Backoffice login on both: `admin@example.com` / `1234567890` (development only, set in
`appsettings.Development.json`).

## Running

```sh
# Build the backoffice client once (its output is gitignored), then run a site.
cd src/Umbraco.Community.LogExplorer/Client && bun install && bun run build
dotnet run --project samples/LogExplorer.Site17
dotnet run --project samples/LogExplorer.Site18 -p:UmbracoMajor=18
```

For client work, keep `bun run watch` running in `src/Umbraco.Community.LogExplorer/Client` and
refresh the backoffice after each rebuild. Static web asset folders are discovered when the site
starts, so restart the site once after the very first client build on a fresh clone.

## Sample sources

`appsettings.Development.json` configures two `Fake` sources (in-memory sample data, UI brief §12)
next to whatever else is set up:

| Alias | Data | Use |
| --- | --- | --- |
| `sample` | The prototype's sample hour (238 entries), ending now | Everyday UI work |
| `sample-48h` | The same hour repeated 48 times (11,424 entries); `Settings: { "SampleHours": "48" }` | Volume checks such as scrolling 10,000 rows: open `...?src=sample-48h&range=7d` |

## Log generator

Both sites compile in `samples/Shared/LogGenerator` (BRIEF Appendix C), which writes realistic
Umbraco-style events through `ILogger` into the site's real log files. It is off by default; turn
it on in `appsettings.Development.json` or with environment variables:

| Setting | Default | Effect |
| --- | --- | --- |
| `LogGenerator:Enabled` | `false` | Live events while the site runs: requests (2% slow, nested `Cart` and `Tags[]` on `/api/basket`), a noisy job every 5 s, content publishes (some failing) every 30 s, a surface-controller `NullReferenceException` every 45 s, a SQL timeout every 60 s and a 404 storm on `/wp-login.php` every 120 s. Request events share a trace id and `RequestId`. |
| `LogGenerator:RequestsPerSecond` | `2` | Average simulated request rate. |
| `LogGenerator:WriteFileScenarios` | `false` | Once per day at start-up: about 1.5 MB of events under a second machine name (`LOGEXPLORER-NODE2`), so `UmbracoTraceLog.LOGEXPLORER-NODE2.{date}.json` and its `_001` roll both exist, with a truncated last line. |
| `LogGenerator:SecondMachineName` | `LOGEXPLORER-NODE2` | Machine name for the scenario files. |

```sh
LogGenerator__Enabled=true LogGenerator__WriteFileScenarios=true dotnet run --project samples/LogExplorer.Site17
dotnet run --project samples/LogExplorer.Site18 -p:UmbracoMajor=18
```

### Bulk mode and the first-page benchmark

For the performance check (BRIEF §17, #36) the generator also writes about 2 GB of logs covering
7 days, ending now, in seconds: lines go straight to disk as Umbraco's compact JSON (not through
`ILogger`) under two machines, `BULK-NODE1` and `BULK-NODE2`, named and rolled like Umbraco's own
files (`UmbracoTraceLog.BULK-NODE1.20261001.json`, then `_001`, ...). The mix is mostly requests
(2% slow), a noisy job, content publishes, surface-controller and SQL timeout exceptions, rare 404
storms and one fatal boot failure an hour in.

| Setting | Default | Effect |
| --- | --- | --- |
| `LogGenerator:BulkGigabytes` | `0` (off) | Size of the set, written once at start-up in the background; skipped if `BULK-NODE1` files already exist in the directory. |
| `LogGenerator:BulkDirectory` | the site's log directory | Where to write it. |
| `LogGenerator:BulkDays` | `7` | Simulated days, ending now. |
| `LogGenerator:BulkRollMegabytes` | `100` | Size at which a day's file rolls to `_001` and on. |

```sh
LogGenerator__BulkGigabytes=2 dotnet run --project samples/LogExplorer.Site17
```

The benchmark needs no site: it writes the set itself into
`%TEMP%/LogExplorer.Benchmarks/bulk` (reused on later runs), then times the first page of the
files provider over the last 7 days. It is a console app, built by CI but never run there.

```sh
dotnet run -c Release --project tests/Umbraco.Community.LogExplorer.Benchmarks
dotnet run -c Release --project tests/Umbraco.Community.LogExplorer.Benchmarks -- --delete   # remove the 2 GB set
```

Other arguments: `--data <dir>`, `--gigabytes <n>`, `--runs <n>`, `--regenerate`; set
`LOG_EXPLORER_BENCHMARK_VERBOSE=1` to print every run's time. It exits 1 if a budgeted case's
median is 500 ms or more. ADR 0017 has the last recorded numbers.

Runtime state (`umbraco/` with the SQLite database and logs, `wwwroot/media/`, generated schema
files) is gitignored. Delete a site's `umbraco/` folder to reinstall it from scratch.
