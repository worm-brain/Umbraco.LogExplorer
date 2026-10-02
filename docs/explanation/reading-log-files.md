# How log files are read

The `UmbracoFiles` source reads Umbraco's own Serilog log files directly from disk. It does not
use the core Log Viewer's service, which re-reads every file in the period on every request and
refuses any period whose files add up to more than 1 GB.

## Which files

Umbraco writes one file per machine per day, named
`UmbracoTraceLog.{MachineName}.{yyyyMMdd}.json`, with `_001`, `_002` and so on when a day's file
rolls over its size limit. The source reads every machine's files in the log directory, including
rolled files, and merges them into one timeline. On a load-balanced site whose servers share log
storage, you see all servers at once; the machine name in each file's name becomes the entry's
`@resource.host.name`.

Only daily files are read. If Umbraco's file sink is configured to roll on another interval, a
warning is logged at startup.

## Newest first, from the end of the file

A search reads backwards from the end of the newest file in the range, in 64 KB blocks, and stops
as soon as it has a page of matching entries. A search for the latest entries over a week of busy
logs therefore reads only the last few kilobytes of each machine's newest file, however large the
files are. In the project's benchmark, the first page over 7 days and about 2 GB of logs loads in
5 to 221 ms once warm.

Each next page continues from a cursor that remembers a position in every machine's stream, so
paging never skips or repeats an entry. Filters are checked on a lightly read entry first, and the
entry is fully prepared only when it matches.

The file Serilog is writing to is opened for shared reading, so the site keeps logging while you
search. A last line still being written is skipped; any other unreadable line is counted and
reported as a warning.

## Counts, and what Approximate means

The histogram, the fields panel and Patterns need counts over the whole range, which means reading
every matching entry in it. For a long range on a busy site that could be gigabytes, so each count
reads newest-first and stops at a **scan budget** (`Files:ScanBudgetMegabytes`, 256 MB by
default).

When the budget stops a count early:

- the result covers the most recent part of the range only;
- it is marked **Approximate**;
- the histogram marks the part of the range it did not read, so a gap in the bars is never
  mistaken for a quiet period.

Counts are cached for 60 seconds, keyed by the query and by each file's size and last write, so
switching between views does not read the files again, and a file that grows is read afresh. The
scans parse lines on several cores.

## Native queries

Native mode on this source uses Serilog Expressions with the same resolver as the core Log Viewer,
so a query that works in one works in the other. The explorer evaluates your chips in .NET
directly rather than turning them into an expression first, which keeps numbers, nested objects
and arrays typed.

## Related

- [Configuration: `Files:ScanBudgetMegabytes`](../reference/configuration.md)
- [The provider model](provider-model.md)
