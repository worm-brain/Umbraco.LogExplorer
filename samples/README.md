# Sample sites

Two Umbraco sites for developing and testing the package. Both reference the package as a
project, install themselves unattended on first run (SQLite), and are never packed.

| Site | Umbraco | URL |
| --- | --- | --- |
| `LogExplorer.Site17` | 17.7.0 (pinned in `Directory.Packages.props`) | https://localhost:44370/umbraco |
| `LogExplorer.Site18` | 18.2.0 (`VersionOverride` in its csproj) | https://localhost:44380/umbraco |

Backoffice login on both: `admin@example.com` / `1234567890` (development only, set in
`appsettings.Development.json`).

## Running

```sh
# Build the backoffice client once (its output is gitignored), then run a site.
cd src/Umbraco.Community.LogExplorer/Client && bun install && bun run build
dotnet run --project samples/LogExplorer.Site17
```

For client work, keep `bun run watch` running in `src/Umbraco.Community.LogExplorer/Client` and
refresh the backoffice after each rebuild. Static web asset folders are discovered when the site
starts, so restart the site once after the very first client build on a fresh clone.

Runtime state (`umbraco/` with the SQLite database and logs, `wwwroot/media/`, generated schema
files) is gitignored. Delete a site's `umbraco/` folder to reinstall it from scratch.
