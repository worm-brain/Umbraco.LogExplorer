# Umbraco Log Explorer

A rich log explorer for the Umbraco backoffice and a drop-in replacement for the built-in Log
Viewer. Install the package and it takes the Log Viewer's place under Settings, reading the same
log files with no configuration. You get far more on top: click values, fields, histogram bars
and patterns to build filters without knowing a query language, follow a request in one click,
and see the query it runs whenever you want it.

> **Alpha.** This release reads the site's own Umbraco log files. Application Insights and Seq
> sources, saved views and export are planned for 1.0.

## Install

One package line per Umbraco major: `17.x` for Umbraco 17, `18.x` for Umbraco 18.

```sh
# Umbraco 17
dotnet add package Umbraco.Community.LogExplorer --version 17.0.0-alpha.1

# Umbraco 18
dotnet add package Umbraco.Community.LogExplorer --version 18.0.0-alpha.1
```

Use the newest prerelease of your line. `--prerelease` on its own picks the `18.x` line.

## Quick start

1. Run the site and sign in as a user with access to the Settings section.
2. Open **Settings > Advanced > Log Explorer**.
3. Click a histogram bar to zoom in, open an entry, and choose **Same request** to see everything
   that request logged.

No configuration is needed. The core Log Viewer menu item is hidden by default; set
`LogExplorer:HideCoreLogViewer` to `false` to keep it.

## Features

- A forgiving search box: `field:value`, `-field:value`, wildcards, comparisons, `has:field` and
  `level:warn` become filter chips; anything else is a text search.
- A fields panel with the top values of each field: click to include, minus to exclude.
- A histogram stacked by level, whose toggles are the level filter; click or drag to zoom.
- An entry drawer with typed properties and the exception, plus Same request, Around this, Same
  pattern and Copy as JSON.
- Patterns (entries grouped by message template) and an Overview of the time range.
- Show generated query in Serilog Expressions, and a native mode that accepts it.
- The whole view in the URL, a copy-link button and keyboard shortcuts.
- Reads every machine's log files, rolled files included, newest first, so large logs open quickly.

## Documentation

- [README with screenshots](https://github.com/worm-brain/Umbraco.LogExplorer#readme)
- [Documentation](https://github.com/worm-brain/Umbraco.LogExplorer/blob/main/docs/index.md)
- [Configuration reference](https://github.com/worm-brain/Umbraco.LogExplorer/blob/main/docs/reference/configuration.md)
- [Changelog](https://github.com/worm-brain/Umbraco.LogExplorer/blob/main/CHANGELOG.md)

Writing a provider for another log store? Reference `Umbraco.Community.LogExplorer.Core` and see
[Writing a provider](https://github.com/worm-brain/Umbraco.LogExplorer/blob/main/docs/extend/writing-a-provider.md).

## Support and licence

Questions and bugs: [GitHub issues](https://github.com/worm-brain/Umbraco.LogExplorer/issues).
MIT licence.
