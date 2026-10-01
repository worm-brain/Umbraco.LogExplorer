using Umbraco.Community.LogExplorer.Core.Records;

namespace Umbraco.Community.LogExplorer.Core.Results;

/// <summary>One page of search results.</summary>
/// <param name="Records">The matching records, in the query's sort order.</param>
/// <param name="NextCursor">Pass back as the query's cursor for the next page; null on the last page.</param>
/// <param name="Range">The absolute range that ran.</param>
/// <param name="TotalCount">Number of matches across all pages; null when it is not cheap to compute.</param>
/// <param name="TotalIsLowerBound">Whether <paramref name="TotalCount"/> is "at least" rather than exact.</param>
/// <param name="Warnings">Human-readable notes, for example malformed lines skipped.</param>
public sealed record LogPage(
    IReadOnlyList<LogRecord> Records,
    string? NextCursor,
    ResolvedRange Range,
    long? TotalCount,
    bool TotalIsLowerBound,
    IReadOnlyList<string> Warnings
);
