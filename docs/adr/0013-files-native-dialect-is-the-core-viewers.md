# ADR 0013: The files source's native dialect is the core Log Viewer's own Serilog.Expressions setup

- Status: Accepted
- Date: 2026-10-01
- Issue: #34

## Context

BRIEF §10.1 asked which library the core Log Viewer filters with (`Serilog.Filters.Expressions` or
`Serilog.Expressions`), so native mode runs imported core saved searches unchanged and "Show query"
pastes into the core viewer. §10.5's Files column was a starting point to verify.

Verified by decompiling `Umbraco.Infrastructure` 17.7.0 and 18.2.0 and `Serilog.Expressions` 5.0.0:

1. `Umbraco.Cms.Infrastructure` 17.0.0, 17.7.0 and 18.2.0 all depend on **Serilog.Expressions
   5.0.0**. `Serilog.Filters.Expressions` is not referenced.
2. The core viewer's filter is `Umbraco.Cms.Core.Logging.Viewer.ExpressionFilter` (internal,
   identical in 17.7 and 18.2): `SerilogExpression.TryCompile(expression, formatProvider: null,
   new SerilogLegacyNameResolver(typeof(SerilogExpressionsFunctions)))`, evaluated with
   `ExpressionResult.IsTrue`. Both resolver types are public.
   - `SerilogLegacyNameResolver` maps `@Exception`, `@Level`, `@Message`, `@MessageTemplate`,
     `@Properties`, `@Timestamp` to `@x`, `@l`, `@m`, `@mt`, `@p`, `@t`.
     `SerilogExpressionsFunctions` adds `Has(x)` (same as `IsDefined`). There is no long alias for
     the trace and span ids: they are `@tr` and `@sp`, compared as lower-case hex text.
   - An input with no space and none of `()+=*<>%-` is searched as
     `@Message like '%input%'` (case-sensitive). An input that does not parse also falls back to
     that message search, silently. An unknown function throws out of the constructor.
   - `formatProvider: null` means the current culture when numbers are formatted.
3. Serilog.Expressions 5.0.0 behaviour that shapes the compiler:
   - `like`, `=`, `<>`, `in`, `StartsWith`, `EndsWith`, `Contains` and `IsMatch` are
     case-sensitive unless followed by `ci`.
   - `like` only matches string values; `Contains`/`StartsWith`/`EndsWith` also accept an
     exception (its full `ToString()` text), enums and trace ids. So `@Exception like '%x%'` never
     matches, while `Contains(@Exception, 'x')` does.
   - `<`, `>`, `<=`, `>=` compare numbers only; strings (dates included) are undefined.
   - `=` compares numbers numerically and strings textually, but a number never equals a string
     (`StatusCode = '200'` is false when the log holds the number 200).
   - `not` of an undefined value is true; `and`/`or` treat undefined as false; `x is null` is true
     for a missing property and a logged null.
   - `@m` renders string properties without quotes; the record body (`LogEvent.RenderMessage`)
     quotes them, as the core viewer's display does.
   - Wildcards `[?]` (any) work inside comparisons and the text functions.
4. Errors: `TryCompile` returns false only for parse errors, with a message
   `Syntax error (line L, column C): unexpected ...` (1-based line and column); a parse error at
   the end of the input has no location. Binding errors (unknown function, invalid regex in
   `IsMatch`) throw `ArgumentException` with no location.

## Decision

1. Native queries on the files source compile through `NativeFilter` with exactly the core
   viewer's settings (point 2), including the plain-text shortcut, by referencing Umbraco's public
   resolver types. The package references Serilog.Expressions 5.0.0 directly.
2. **Deliberate difference:** an expression that does not compile throws
   `InvalidNativeQueryException` (Core, for every provider; the API maps it to
   `invalid_native_query` when the endpoints land in #35) instead of silently becoming a message
   search, so native mode can show the error. `Position` is the zero-based offset derived from the
   message's line and column; it is null for end-of-input and binding errors.
   `ValidateNative` returns the same message and position.
3. The pager and the aggregator compile the native query once per call, evaluate it on the raw
   `LogEvent` before mapping, and AND it with the chips and levels. The aggregation cache key
   includes the native query.
4. `FileQueryCompiler` emits the dialect so it selects what `LogRecordFilter` selects:
   - string tests get `ci` when the chip is case-insensitive (the default);
   - equality with a number, numeric string or boolean offers every typed form
     (`StatusCode in ['200', 200] ci`), and text tests with a numeric value wrap the field in
     `ToString()`, so search-box values match the typed values in the file;
   - text search is `(@Message like '%w%' ci or Contains(@Exception, 'w') ci)` per word;
   - `has:` is `x is not null` and `-has:` is `x is null` (a logged null counts as absent);
   - `NotEquals` is `not (x = v)`, true for a missing field, as in C#;
   - `@body`, `@template`, `@scope`, `@traceId`, `@spanId` are `@Message`, `@MessageTemplate`,
     `SourceContext`, `@tr`, `@sp`; `a[]` is `a[?]`; a name that is not an identifier, or is a
     keyword, is an indexer (`@Properties['x']`, `a['end']`);
   - one clause per line, `and` at the line start; a native query is appended as its own clause.
5. Left out and returned in `Unsupported`: `@timestamp`, `@exception.*`, `@resource.*`, ordering
   against anything but a number, null, object, array and empty-`in` values, and severity
   operators other than `=`, `<>` and `in`. Exception type is not expressible exactly: the dialect
   only sees the whole exception text.

## Consequences

- BRIEF §10.1 and the §10.5 Files column are updated to these outputs; `FileQueryCompilerTests`
  locks them, `FileQueryCompileRoundTripTests` checks compiled output selects the same entries as
  the C# filter, and `NativeQueryCompileTests` compares native mode with Umbraco's own
  `ExpressionFilter` (by reflection) over the same events.
- Known residual differences between "Show query" in the core viewer and the files source:
  property names are case-sensitive in the dialect (C# falls back to ignoring case); a text
  phrase spanning a string property differs because `@m` does not quote it; text search also
  matches the exception's type and stack trace; ordering and regex tests do not see numbers stored
  as strings; `IsMatch` uses `ExplicitCapture`; numbers format in the server's culture in `@m` and
  `ToString()`; a dotted attribute key such as `host.name` is read as a path.
- Importing core saved searches (Phase 2): a saved search that only worked through the core
  viewer's silent fallback (it does not parse) fails validation here; the importer should detect
  that with `ValidateNative` and import it as a text search instead.
- When Umbraco moves to another Serilog.Expressions version, re-check points 2 to 4 and bump the
  package version with it.
