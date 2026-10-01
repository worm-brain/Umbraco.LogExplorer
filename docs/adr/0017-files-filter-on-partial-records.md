# ADR 0017: The files pager tests filters on partially mapped records, not on a LogEvent predicate

- Status: Accepted
- Date: 2026-10-01
- Issue: #36

## Context

BRIEF §10.1 said to compile `FilterNode` to a `Func<LogEvent, bool>`. The files provider was
built instead by mapping every scanned event to a `LogRecord` and testing it with Core's
`LogRecordFilter`, the single reference semantics the fake source and the contract suite also use.
#36 asked for the latency budget (BRIEF §17: latest 100 entries over 7 days and about 2 GB in
under 500 ms) to be measured, and for a switch to a `LogEvent` predicate if the `LogRecord`
predicate was the bottleneck.

The benchmark (`tests/Umbraco.Community.LogExplorer.Benchmarks`) runs over 2.02 GB, 3.6 million
events in 30 files from two machines, written by the sample generator's bulk mode. Profiling a
newest-first read of 200,000 events, warm:

| Work per event | Share |
| --- | --- |
| Reading the line and parsing it with `LogEventReader` | about 60% |
| Mapping the `LogEvent` to a `LogRecord` (attributes as JSON, rendering, MD5 of the template, exception parsing) | about 40% |
| `LogRecordFilter.Matches` | negligible |

So the predicate itself is not the bottleneck; the mapping it needs is a large share, and the
parser is the largest. A `LogEvent` predicate would remove the mapping cost, but it would be a
second implementation of every operator and field rule, to be kept in step with
`LogRecordFilter` forever.

## Decision

1. Keep `LogRecordFilter` as the only filter semantics. Do not compile `FilterNode` against
   `LogEvent`.
2. `FilterRecordParts.For` works out which costly parts of a record a filter reads (body,
   exception, template, attributes, resource), following `LogFields.Resolve`'s field mapping.
   `CompactLogEventMapper.Map(..., parts)` maps only those, plus the cheap members (id, timestamp,
   severity, trace and span ids, scope). The pager tests the filter and level set on that partial
   record and maps an event in full only when it matches.
3. A level set alone, or no filter, needs no costly part; a text search needs the body and the
   exception; an attribute path needs the attributes. A node type the analysis does not know maps
   everything, so behaviour for it is unchanged.
4. Tests prove the fast path selects exactly what full records select, for the search-box inputs
   and hand-built filters over every portable field, across the dialect fixtures.

## Consequences

Benchmark before and after (Ryzen 9 7950X, 64 GB, Samsung 990 PRO NVMe, warm OS file cache,
Release; "first" is the first call in a fresh process, the median is over the next 10):

| Case | Before: first / median ms | After: first / median ms |
| --- | ---: | ---: |
| (a) newest first, no filter | 87 / 4.8 | 80 / 4.9 |
| (b) newest first, text `timeout` | 996 / 257 | 752 / 221 (steady state about 120) |
| (c) newest first, levels warn+ | 38 / 38 | 25 / 23 |
| (d) newest first, rare phrase, full 2 GB scan | 34 s | 22 s |
| (e) oldest first, no filter | 4.9 / 3.5 | 4.5 / 3.6 |

- The budget holds for (a) to (c) once the process is warm. The first text search in a fresh
  process still takes about 0.75 s: the parse path is still tier-0 JIT code. That is the cost of
  `LogEventReader` (Newtonsoft.Json), the reader Umbraco itself ships (BRIEF §10.1); a faster parser would
  be a separate decision.
- Aggregations (`FileAggregator`) still map every event; they can adopt the same parts analysis
  if their budget (BRIEF §17, 3 s) needs it.
- BRIEF §10.1's "compile to `Func<LogEvent, bool>`" bullet now points here.
