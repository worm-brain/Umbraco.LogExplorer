# Changelog

All notable user-visible changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- Log Explorer item under Settings > Advanced with an empty Search, Patterns and Overview workspace.
- Simple search syntax parser in Core: `field:value`, `-field:value`, wildcards, comparisons, `has:field`, field aliases and `level:`/`level=` turn search box input into filter chips and a level set, with a plain-text fallback for an unbalanced quote.
- English localization dictionary for every Log Explorer string; other backoffice languages fall back to it.
- `HideCoreLogViewer` (default `true`) now hides the core Settings > Log Viewer item; set it to `false` to show both.
- `GET /umbraco/log-explorer/api/v1/settings` returns the client settings (`hideCoreLogViewer`, `defaultSource`, `defaultTimeRange`).
- Time range picker on the Search view (last 15 minutes to 30 days, or a custom range with the time zone shown). The explorer's view state lives in the URL, so reloading, sharing a link or pressing Back restores the same view; the default range is `DefaultTimeRange`.
