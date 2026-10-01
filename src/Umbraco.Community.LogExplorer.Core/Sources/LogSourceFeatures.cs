namespace Umbraco.Community.LogExplorer.Core.Sources;

/// <summary>
/// Optional capabilities a source declares. The UI hides or disables what a source does not
/// declare, and <see cref="LogSourceBase"/> refuses calls to undeclared members.
/// </summary>
[Flags]
public enum LogSourceFeatures
{
    /// <summary>Search only.</summary>
    None = 0,

    /// <summary><see cref="ILogSource.GetFacetsAsync"/>.</summary>
    Facets = 1,

    /// <summary><see cref="ILogSource.GetHistogramAsync"/>.</summary>
    Histogram = 2,

    /// <summary><see cref="ILogSource.GetPatternsAsync"/>.</summary>
    Patterns = 4,

    /// <summary><see cref="ILogSource.GetContextAsync"/>.</summary>
    Context = 8,

    /// <summary><see cref="ILogSource.TailAsync"/>.</summary>
    Tail = 16,

    /// <summary>
    /// <see cref="ILogSource.Compile"/>, <see cref="ILogSource.ValidateNative"/> and
    /// <c>LogQuery.NativeQuery</c>.
    /// </summary>
    NativeQuery = 32,

    /// <summary>Records carry trace ids, so "Same request" can use <c>@traceId</c>.</summary>
    TraceCorrelation = 64,

    /// <summary>The API may stream an export of the source's search results.</summary>
    Export = 128,

    /// <summary><see cref="ILogSource.GetFieldsAsync"/>.</summary>
    FieldDiscovery = 256,
}
