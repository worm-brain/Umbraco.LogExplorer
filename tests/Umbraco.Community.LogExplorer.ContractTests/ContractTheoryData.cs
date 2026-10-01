using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Sources;

namespace Umbraco.Community.LogExplorer.ContractTests;

/// <summary>
/// Theory rows for <see cref="LogSourceContractTests{TFixture}"/>, kept outside the generic class
/// so each list exists once rather than once per fixture type.
/// </summary>
public static class ContractTheoryData
{
    /// <summary>Both sort directions.</summary>
    public static TheoryData<SortDirection> SortDirections =>
        [SortDirection.Descending, SortDirection.Ascending];

    /// <summary>Every filter operator.</summary>
    public static TheoryData<FilterOperator> Operators => [.. Enum.GetValues<FilterOperator>()];

    /// <summary>Level sets with gaps, as the histogram toggles allow (ADR 0004).</summary>
    public static TheoryData<string> NonContiguousLevelSets =>
        ["debug,error", "info,error", "trace,info,fatal"];

    /// <summary>Optional features that have an <see cref="ILogSource"/> member.</summary>
    public static TheoryData<LogSourceFeatures> OptionalFeatures =>
        [
            LogSourceFeatures.Histogram,
            LogSourceFeatures.Facets,
            LogSourceFeatures.Patterns,
            LogSourceFeatures.Context,
            LogSourceFeatures.FieldDiscovery,
            LogSourceFeatures.Tail,
            LogSourceFeatures.NativeQuery,
        ];

    /// <summary>Async features whose members take a cancellation token, plus plain search.</summary>
    public static TheoryData<LogSourceFeatures> CancellableFeatures =>
        [
            LogSourceFeatures.None,
            LogSourceFeatures.Histogram,
            LogSourceFeatures.Facets,
            LogSourceFeatures.Patterns,
            LogSourceFeatures.Context,
            LogSourceFeatures.FieldDiscovery,
        ];
}
