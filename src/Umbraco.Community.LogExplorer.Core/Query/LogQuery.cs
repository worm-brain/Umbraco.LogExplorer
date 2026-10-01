namespace Umbraco.Community.LogExplorer.Core.Query;

/// <summary>
/// A provider-neutral query: time range, level set, filter tree and paging (BRIEF §8.2).
/// </summary>
public sealed record LogQuery
{
    /// <summary>The time range to search; relative ranges resolve on the server at execution.</summary>
    public required TimeRange Range { get; init; }

    /// <summary>
    /// OpenTelemetry short level names to include (<c>trace</c>, <c>debug</c>, <c>info</c>,
    /// <c>warn</c>, <c>error</c>, <c>fatal</c>), compared case-insensitively. Null or all six means
    /// no level filter. Any subset is allowed, including non-contiguous ones (ADR 0004).
    /// </summary>
    public IReadOnlySet<string>? Levels { get; init; }

    /// <summary>Filter built from chips and parsed simple syntax; null means no filter.</summary>
    public FilterNode? Filter { get; init; }

    /// <summary>
    /// Escape hatch in the source's own query language, combined with <see cref="Filter"/> with
    /// AND where the source supports it.
    /// </summary>
    public string? NativeQuery { get; init; }

    /// <summary>Page size; sources clamp it to their <c>MaxPageSize</c>.</summary>
    public int Take { get; init; } = 100;

    /// <summary>
    /// Opaque, provider-specific position returned as <c>NextCursor</c> by the previous page; null
    /// for the first page.
    /// </summary>
    public string? Cursor { get; init; }

    /// <summary>Timestamp order of the results.</summary>
    public SortDirection Sort { get; init; } = SortDirection.Descending;
}
