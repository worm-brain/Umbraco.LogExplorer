# Writing a provider

> **Outline.** The full walkthrough, with the Seq provider as the worked example, arrives with the
> Seq provider. Until then, this page lists the pieces.

A provider lets the explorer read another log store. You write it against the
`Umbraco.Community.LogExplorer.Core` package, which has no Umbraco dependency.

## The pieces

1. **A source class.** Derive from `LogSourceBase`, declare your `Capabilities` (features, filter
   operators, native language, longest range, largest page), and override the protected
   `...CoreAsync` members for the features you declare. The base class refuses calls to features
   you did not declare.
2. **Mapping.** Turn each stored entry into a `LogRecord`: timestamp, OpenTelemetry severity number,
   message and template, trace ids, scope, exception, typed attributes and resource attributes (see
   [fields and levels](../reference/fields.md)).
3. **Filtering.** Evaluate the `FilterNode` tree (and, or, not, condition, text) yourself, or
   compile it to your store's query language. Return what you cannot express in
   `CompileResult.Unsupported`, so the UI can show it disabled rather than drop it.
4. **Paging.** Return an opaque cursor that resumes exactly where the page ended, newest first and
   oldest first.
5. **A factory.** Implement `ILogSourceFactory` with a unique `Type` name, creating a source from
   its configuration entry (`LogSourceDefinition`, including its `Settings`).
6. **Registration.** Register the factory from an `IServiceCollection` extension and an Umbraco
   composer, so installing the package is enough.

## Test it with the contract suite

The repository's contract test suite is an abstract test class every provider runs against its own
fixture. It checks paging (no gaps or duplicates across cursors), sort order, time-range edges,
every declared operator, declared versus undeclared features, and cancellation. The `Fake` and
`UmbracoFiles` sources both pass it.

## Related

- [The provider model](../explanation/provider-model.md)
- [Configuration: sources](../reference/configuration.md#sources)
