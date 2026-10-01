namespace Umbraco.Community.LogExplorer.Core.Results;

/// <summary>
/// Entry volume over time, stacked by level. Level counts ignore the query's level filter so the
/// UI can show what the level toggles hide (ADR 0004).
/// </summary>
/// <param name="Range">The absolute range that ran.</param>
/// <param name="BucketSize">Width of every bucket.</param>
/// <param name="Buckets">Buckets in ascending time order, covering the range without gaps.</param>
/// <param name="Approximate">Whether the source sampled or stopped at a scan budget.</param>
public sealed record HistogramResult(
    ResolvedRange Range,
    TimeSpan BucketSize,
    IReadOnlyList<HistogramBucket> Buckets,
    bool Approximate
);

/// <summary>One histogram bucket.</summary>
/// <param name="Start">Inclusive start; the bucket ends at <c>Start + BucketSize</c>.</param>
/// <param name="CountsBySeverityShortName">Entry count per lower-case OTel short name.</param>
public sealed record HistogramBucket(
    DateTimeOffset Start,
    IReadOnlyDictionary<string, long> CountsBySeverityShortName
);
