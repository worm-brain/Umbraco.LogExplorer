# ADR 0008: Core contract refinements from Phase 0

- Status: Accepted
- Date: 2026-10-01
- Issues: #19-#23

## Context

BRIEF §8 gave starting shapes for the Core contracts and allowed refining names during Phase 0.
Implementing them with the contract suite surfaced a few gaps. This records what changed, so the
contracts can be treated as frozen for the Phase 1 lanes (BRIEF §15).

## Decision

1. **`LogSourceBase` gates features itself.** The public `ILogSource` members on the base class are
   not virtual. Each checks that `Capabilities` declares its feature (else `NotSupportedException`),
   validates arguments, checks the cancellation token, then calls a protected `...CoreAsync` member
   (`TailCore`, `CompileCore`, `ValidateNativeCore` for the synchronous/streaming ones).
   `QueryCoreAsync` is abstract; every other `...Core` member is optional. Declared features and
   behaviour therefore cannot drift apart, even with configurable capabilities.
2. **Feature mapping:** `Compile` and `ValidateNative` are gated on `NativeQuery`. `Export` and
   `TraceCorrelation` gate features in the API/UI, not a source member.
3. **Unknown ids** throw `KeyNotFoundException`: an unknown record id in `GetContextAsync`, an
   unknown alias in `ILogSourceRegistry.GetForUser` (which throws `ForbiddenSourceException`, now
   carrying `SourceAlias`, when the alias exists but is hidden).
4. **Wire conventions:** level short names are lower case (`trace` ... `fatal`); histogram and
   pattern level-count dictionaries always carry all six keys; `CompileResult.Native` is null when
   there is no filter; `@severity` resolves to its short name and `@timestamp` to an ISO 8601 string
   when filtering.
5. **JSON:** `LogJson.Options` is the single serializer configuration (camelCase, string enums,
   `kind` discriminator accepted anywhere in the object, a converter for `IReadOnlySet<string>`
   that builds a case-insensitive set).
6. **Additional Core types:** `SeverityMap`, `SeverityBand`, `LogFields` (portable field names),
   `LogRecordFilter` (the FilterNode + level-set predicate, ADR 0004), `TemplateHash` (MD5, §9.3),
   `RelativeRange`, `LogSourceCapabilities.Supports`, `FakeLogSourceOptions`.
7. **`LogSourceDefinition`** is a record with init properties and defaults so configuration binding
   can construct it; `Settings` is `IReadOnlyDictionary<string, string>`.

Analyzer suppressions keep the brief's names, each with a justification in code: CA1716 (`Alias`),
CA1711 (`LogException`), CA5351 (MD5 is a hash for grouping, not security).

## Consequences

- BRIEF §8.4 points here for the base-class shape.
- Providers override `...Core` members only; the contract suite's declared-vs-undeclared tests hold
  for every provider without per-provider effort.
- `FakeLogSource` generates its data once at construction; a long-running host needs a new instance
  to keep data inside "last 1 hour" (the sample sites register it per request where that matters).
