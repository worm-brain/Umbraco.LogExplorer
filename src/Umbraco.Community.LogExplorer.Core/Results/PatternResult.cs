using Umbraco.Community.LogExplorer.Core.Records;

namespace Umbraco.Community.LogExplorer.Core.Results;

/// <summary>Entries grouped by message template (the Patterns view, BRIEF §6.9).</summary>
/// <param name="Patterns">Patterns, highest count first.</param>
/// <param name="Approximate">Whether the source sampled or stopped at a scan budget.</param>
public sealed record PatternResult(IReadOnlyList<Pattern> Patterns, bool Approximate);

/// <summary>One message template and its occurrences.</summary>
/// <param name="TemplateHash">Stable pattern id (BRIEF §9.3).</param>
/// <param name="Template">The template text.</param>
/// <param name="Count">Number of matching entries.</param>
/// <param name="CountsBySeverityShortName">Count per lower-case OTel short name.</param>
/// <param name="Sample">The newest matching entry, for its rendered message.</param>
/// <param name="Sparkline">Counts in equal-width buckets across the range, oldest first.</param>
public sealed record Pattern(
    string TemplateHash,
    string Template,
    long Count,
    IReadOnlyDictionary<string, long> CountsBySeverityShortName,
    LogRecord Sample,
    IReadOnlyList<long> Sparkline
);
