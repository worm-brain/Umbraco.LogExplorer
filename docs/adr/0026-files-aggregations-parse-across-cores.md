# ADR 0026: Files aggregations parse and evaluate entries across cores, aggregating in stream order

- Status: Accepted
- Date: 2026-10-02
- Issue: none (lane-a/aggregation-perf)

## Context

On a client's real logs (about 1 GB, 107 files, 15 machines), the files source's aggregations
missed BRIEF §17's 3 s budget on a freshly started site: histogram 4.9 s, facets 4.3 s, fields
6.9 s, all stopping at the 256 MB scan budget. The fields panel waits for fields, then facets,
so it filled in after about 11 s.

`tests/Umbraco.Community.LogExplorer.Benchmarks` gained an `aggregations` mode that runs the
aggregator over any log folder (warm, cold in a new process, as one Search view load, a per-stage
profile, and a JSON dump to diff two builds). Profile of one budgeted scan (256 MB, about 196,000
events, warm, Ryzen 9 7950X with 32 logical cores; times per event):

| Stage | us/event | Share of a 3.0 s fields scan |
| --- | ---: | ---: |
| Read, split lines, decode to a string | about 0.4 | 3% |
| `LogEventReader.ReadFromString` (Newtonsoft.Json) | about 7.3 | 48% |
| Map to `LogRecord`: cheap members, body, template, resource | about 0.6 | 4% |
| Map attributes to typed JSON | about 2.2 | 15% |
| Collect attribute paths and kinds, tally | about 4.7 | 31% |
| (Facets instead: resolve 15 fields, then tally) | about 2.3 + 3 | |

Reusing one Newtonsoft serializer instead of the one `ReadFromString` creates per call changed
nothing. Most of the remaining gap between a warm scan and the first one after a restart is tier-0
JIT code in Newtonsoft and the Serilog reader: with `DOTNET_TieredPGO=0` the first histogram took
1.0 s instead of 1.8 s. Each Search view load scans the same bytes three times (histogram and
fields at once, then facets on the discovered fields); the 60 s cache only helps a repeat.

## Decision

1. Aggregation scans parse each file's lines across cores. `ReverseLogFileReader` collects the
   lines each block completes and hands them out in order; with `parallelParse` it reads 1 MB
   blocks and parses a block's lines with `Parallel.For` first, otherwise it parses each line as it
   is handed out, so the pager and context reader still parse only what they use.
2. With parallel parsing, the reader's `BytesRead` follows the lines handed out in 64 KB steps,
   exactly as the default reader counts them, so the scan budget stops at the same event and the
   scanned range and `BytesRead` are unchanged.
3. The aggregator takes events in batches of 256. When the filter, the native query or the
   aggregation reads records (facets, fields), each batch is matched, mapped and prepared (facet
   values resolved, field paths collected) across cores, then the matches are added in stream
   order and the budget is checked after each event. Results are a one-by-one scan's: the
   real-logs dump of every aggregation, 7 and 30 days, filtered and not, is byte-identical before
   and after.
4. Exceptions from parallel work are rethrown unwrapped (`ParallelWork.For`), so a regex filter
   that does not parse is still an `ArgumentException` (`invalid_query`).
5. Parallel scanning runs only under the server garbage collector, on more than one core
   (`FileAggregator.ParallelScan`). Parsing allocates heavily; under the workstation collector the
   parallel scan took about twice as long as the sequential one. ASP.NET Core sites, Umbraco's
   included, use the server collector by default. Without parallel scanning the aggregator takes
   one event at a time, as before.

## Consequences

Before and after, real logs, anchored at 2026-10-02, server GC, Release, warm OS file cache; warm
is the median of 5 calls on a fresh aggregator (no result cache), cold is the first call in a new
process:

| Endpoint | 7d warm | 7d cold | 30d warm | 30d cold |
| --- | ---: | ---: | ---: | ---: |
| Histogram | 1651 -> 497 ms | 2912 -> 1813 ms | 1699 -> 449 ms | 3029 -> 1782 ms |
| Facets (15 fields) | 3080 -> 1049 ms | 5566 -> 2912 ms | 3131 -> 1049 ms | 5576 -> 3118 ms |
| Fields | 3014 -> 933 ms | 5670 -> 3106 ms | 3047 -> 933 ms | 5689 -> 2882 ms |
| Patterns | 1617 -> 460 ms | 2828 -> 1830 ms | 1639 -> 472 ms | 3243 -> 1786 ms |

One Search view load (histogram and fields together, then facets), 7 days: facets done after
about 2.2 s instead of 6.2 s warm, and 5.5 s instead of 9.4 s as the first load in a new process.

- A scan uses every core for a fraction of a second; concurrent scans share them through the
  thread pool. Peak memory per scan grows by one 1 MB block of lines per machine and one batch of
  parsed events.
- The first scans after a restart still pay for tier-0 JIT of the Newtonsoft-based reader, so a
  cold fields or facets call is about 3 s. Left for a later decision: a startup warm-up scan, a
  host-level `TieredPGO` setting, a faster parser than `LogEventReader` (ADR 0017), sharing one
  scan between the endpoints of a view, or the `IFileLogIndex` seam (BRIEF §10.1).
- The pager and context reader keep their sequential behaviour.
