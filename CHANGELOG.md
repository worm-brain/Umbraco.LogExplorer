# Changelog

All notable user-visible changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- Search box on the Search view: type text or `field:value` filters and press Enter to turn them into chips (`level:` and `level=` set the level filter instead). Two chips on the same field match either value; chips on different fields must all match. Click a chip to change its operator or value, or exclude it; Backspace in the empty box removes the last chip and Escape clears the box. An unbalanced quote searches the input as plain text and says so. Chips are kept in the URL.
- `POST /umbraco/log-explorer/api/v1/parse` turns search box input (up to 2,000 characters; longer is `invalid_query`) into filter chips, a level set and any plain-text fallback reason.
- Results list on the Search view: the latest entries newest first (toggle with the Time header), each row showing time with milliseconds, a level badge, the message with its values highlighted and the short source. More entries load as you scroll, or with "Load 60 more"; scrolling stays smooth with thousands of rows loaded. Loading, empty and error states (with Retry) are shown in the panel.
- `POST /umbraco/log-explorer/api/v1/sources/{alias}/search` returns a page of entries for a query. Errors are ProblemDetails with a `code`: `source_not_found`, `forbidden_source`, `unsupported_feature`, `range_too_large`, or `invalid_query` for a malformed range, cursor, page size or regex.
- `Fake` sources accept a `SampleHours` setting to generate more than one hour of sample data.

- Log Explorer item under Settings > Advanced with an empty Search, Patterns and Overview workspace.
- Simple search syntax parser in Core: `field:value`, `-field:value`, wildcards, comparisons, `has:field`, field aliases and `level:`/`level=` turn search box input into filter chips and a level set, with a plain-text fallback for an unbalanced quote.
- English localization dictionary for every Log Explorer string; other backoffice languages fall back to it.
- `HideCoreLogViewer` (default `true`) now hides the core Settings > Log Viewer item; set it to `false` to show both.
- `GET /umbraco/log-explorer/api/v1/settings` returns the client settings (`hideCoreLogViewer`, `defaultSource`, `defaultTimeRange`).
- Time range picker on the Search view (last 15 minutes to 30 days, or a custom range with the time zone shown). The explorer's view state lives in the URL, so reloading, sharing a link or pressing Back restores the same view; the default range is `DefaultTimeRange`.
- Source picker in the workspace header: shows the active source with its query language and a lock for sensitive sources, and lists every source you can see. The chosen source is kept in the URL (`src`); without one the explorer opens on `DefaultSource`, or the first visible source if that is unavailable.
