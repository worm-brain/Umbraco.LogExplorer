using Umbraco.Community.LogExplorer.Core.Records;

namespace Umbraco.Community.LogExplorer.Core.Results;

/// <summary>
/// The entries either side of an anchor in time order, ignoring filters ("Around this",
/// BRIEF §6.8).
/// </summary>
/// <param name="Before">Entries before the anchor, oldest first.</param>
/// <param name="Anchor">The entry asked for.</param>
/// <param name="After">Entries after the anchor, oldest first.</param>
public sealed record ContextResult(
    IReadOnlyList<LogRecord> Before,
    LogRecord Anchor,
    IReadOnlyList<LogRecord> After
);
