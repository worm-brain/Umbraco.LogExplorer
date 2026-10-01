namespace Umbraco.Community.LogExplorer.Core.Sources;

/// <summary>
/// A provider type (BRIEF §7.3): creates configured <see cref="ILogSource"/> instances from the
/// <c>LogExplorer:Sources</c> entries whose <c>Type</c> matches.
/// </summary>
public interface ILogSourceFactory
{
    /// <summary>The provider type name, for example <c>UmbracoFiles</c>, <c>ApplicationInsights</c> or <c>Seq</c>.</summary>
    string Type { get; }

    /// <summary>Creates a source for one configuration entry.</summary>
    /// <param name="definition">The configuration entry; its <c>Type</c> equals <see cref="Type"/>.</param>
    /// <param name="services">Services for resolving provider dependencies such as a clock or HTTP client.</param>
    /// <returns>The configured source.</returns>
    /// <exception cref="ArgumentException">The definition's settings are missing or invalid.</exception>
    ILogSource Create(LogSourceDefinition definition, IServiceProvider services);
}
