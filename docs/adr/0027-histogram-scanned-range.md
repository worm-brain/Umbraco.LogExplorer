# ADR 0027: The histogram reports its scanned range and shades the part it did not read

- Status: Accepted
- Date: 2026-10-02

## Context

The files source reads aggregations newest first and stops at `Files:ScanBudgetMegabytes`
(default 256 MB), then reports `Approximate` (BRIEF §10.1). BRIEF §10.1 also says to return the
scanned range "so the UI can say 'based on the most recent N hours'". `FacetResult` carries a
`ScannedRange`, but `HistogramResult` did not, so the histogram drew the whole requested range
and the buckets the scan never reached looked like a quiet period. On Jack's real logs (about
1 GB from 15 machines) "Last 7 days" showed bars only from 30 September, although the files
cover the whole week.

## Decision

- `HistogramResult` gets an optional `ScannedRange` (`null` when the whole range was read). It is
  additive: other sources and older clients are unaffected. The files source sets it only when
  the budget cut the scan short.
- The histogram hatches the part of the chart older than `scannedRange.from` (a dashed edge marks
  where reading stopped), the summary adds "counts from {time}", and bars in the unread part say
  "Not read: the scan limit was reached ...". The overlay ignores the pointer, so zoom and drag
  still work across it.

## Consequences

- Users can tell "no entries" from "not read" and are pointed at narrowing the range or adding a
  filter. Making the scan reach further is a separate concern (ADR 0026, `IFileLogIndex`).
