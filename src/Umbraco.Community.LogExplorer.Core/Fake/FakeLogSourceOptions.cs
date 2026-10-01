using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Sources;

namespace Umbraco.Community.LogExplorer.Core.Fake;

/// <summary>
/// Identity, clock and capabilities of a <see cref="FakeLogSource"/>. The defaults declare
/// everything except Tail; narrow <see cref="Features"/> or <see cref="Operators"/> to model a less
/// capable provider.
/// </summary>
public sealed record FakeLogSourceOptions
{
    /// <summary>Every feature the fake implements: all of them except <see cref="LogSourceFeatures.Tail"/>.</summary>
    public const LogSourceFeatures AllFeaturesExceptTail =
        LogSourceFeatures.Facets
        | LogSourceFeatures.Histogram
        | LogSourceFeatures.Patterns
        | LogSourceFeatures.Context
        | LogSourceFeatures.NativeQuery
        | LogSourceFeatures.TraceCorrelation
        | LogSourceFeatures.Export
        | LogSourceFeatures.FieldDiscovery;

    /// <summary>The source alias.</summary>
    public string Alias { get; init; } = "fake";

    /// <summary>The name shown in the source picker.</summary>
    public string DisplayName { get; init; } = "Sample data";

    /// <summary>Whether the source is flagged sensitive.</summary>
    public bool Sensitive { get; init; }

    /// <summary>
    /// Declared features. <see cref="LogSourceFeatures.Tail"/> is not implemented, so declaring it
    /// makes <c>TailAsync</c> throw.
    /// </summary>
    public LogSourceFeatures Features { get; init; } = AllFeaturesExceptTail;

    /// <summary>
    /// Declared operators; a query using any other operator throws
    /// <see cref="NotSupportedException"/>, and <c>Compile</c> reports it as unsupported.
    /// </summary>
    public IReadOnlySet<FilterOperator> Operators { get; init; } =
        new HashSet<FilterOperator>(Enum.GetValues<FilterOperator>());

    /// <summary>Name of the fake's readable pseudo-language shown with "Show query".</summary>
    public string NativeLanguage { get; init; } = "Sample";

    /// <summary>Longest accepted time range; null means unlimited.</summary>
    public TimeSpan? MaxRange { get; init; }

    /// <summary>Largest page size; larger <c>Take</c> values are clamped.</summary>
    public int MaxPageSize { get; init; } = 1000;

    /// <summary>
    /// Freezes the clock at this instant: the sample hour ends here and relative ranges resolve
    /// against it, for tests and stable screenshots. Null uses the injected clock.
    /// </summary>
    public DateTimeOffset? FixedNow { get; init; }
}
