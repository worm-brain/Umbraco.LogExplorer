# The provider model

The explorer is not tied to one log store. Everything it shows comes from a **source**, and each
source is created by a **provider type**.

## Provider types and sources

- A **provider type** is code: `UmbracoFiles` reads Umbraco's log files, `Fake` generates sample
  data, and the planned Application Insights and Seq packages add their own.
- A **source** is one configured instance of a type, with its own alias and display name, set under
  `LogExplorer:Sources` (see the [configuration reference](../reference/configuration.md#sources)).
  One site can have several sources of the same type, such as staging and production.

The browser only ever sees a source's alias, display name, type, sensitive flag and capabilities.
Connection details and secrets stay on the server.

## One record shape

Every source maps its entries to one record shape based on the OpenTelemetry log data model: a
timestamp, a level, a message and its template, trace ids, a scope, an exception, typed properties
and resource attributes. The UI is written once against that shape. See
[fields and levels](../reference/fields.md).

## One filter language

Chips and the simple search syntax become a small, portable filter tree (and, or, not, field
conditions and text). Each source either evaluates that tree itself (the files source does so in
.NET) or compiles it to its own language, such as Serilog Expressions, KQL or Seq's syntax. **Show
generated query** shows that compiled form.

## Capabilities

Each source declares what it can do: which features (histogram, facets, patterns, context, native
query and so on), which filter operators, its native language, its longest range and its largest
page. The UI follows those declarations:

- a feature a source does not have is hidden or disabled;
- a filter a source cannot run is shown struck through with "Not supported by {source}", and left
  out of the search rather than silently dropped.

## Why not the core Log Viewer's service?

Umbraco's `ILogViewerService` re-scans every file in the period for each request, caps out at 1 GB,
flattens properties to strings, and is chosen at compile time. Building on it would inherit all of
that, so the files source reads the files itself. The explorer uses the service for one thing
only: reading the configured minimum level of each log sink for the Overview view.

## Related

- [How log files are read](reading-log-files.md)
- [Writing a provider](../extend/writing-a-provider.md)
