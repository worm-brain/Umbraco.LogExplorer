using Umbraco.Community.LogExplorer.Core.Query;

namespace Umbraco.Community.LogExplorer.Core.Sources;

/// <summary>What a source can do; sent to the UI with the source list so it can adapt.</summary>
/// <param name="Features">Optional members the source implements.</param>
/// <param name="Operators">Filter operators the source can evaluate.</param>
/// <param name="NativeLanguage">
/// Display name of the native query language, for example <c>Serilog Expressions</c>, <c>KQL</c>
/// or <c>Seq</c>; null when the source has none.
/// </param>
/// <param name="MaxRange">Longest time range the source accepts; null means unlimited.</param>
/// <param name="MaxPageSize">Largest <c>LogQuery.Take</c> honoured; larger requests are clamped.</param>
public sealed record LogSourceCapabilities(
    LogSourceFeatures Features,
    IReadOnlySet<FilterOperator> Operators,
    string? NativeLanguage,
    TimeSpan? MaxRange,
    int MaxPageSize
)
{
    /// <summary>Whether every flag in <paramref name="feature"/> is declared.</summary>
    /// <param name="feature">One or more features.</param>
    /// <returns>True when all are in <see cref="Features"/>.</returns>
    public bool Supports(LogSourceFeatures feature) => (Features & feature) == feature;
}
