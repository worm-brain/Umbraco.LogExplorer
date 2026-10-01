using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Community.LogExplorer.Core.Sources;
using Umbraco.Community.LogExplorer.Infrastructure.Api;

namespace Umbraco.Community.LogExplorer.Features.Sources;

/// <summary>
/// One source as the UI sees it (BRIEF §7.3, §12): identity, the sensitive flag and capabilities.
/// Connection settings and secrets are never included.
/// </summary>
/// <param name="Alias">The alias used in routes and URLs.</param>
/// <param name="DisplayName">The name shown in the source picker.</param>
/// <param name="Type">The provider type, for example <c>UmbracoFiles</c>.</param>
/// <param name="Sensitive">Whether the source is sensitive (shows a lock, queries are audited).</param>
/// <param name="Capabilities">What the source supports, so the UI can hide or disable the rest.</param>
/// <param name="AllowNativeQuery">
/// Whether users may run native queries: the source declares <c>nativeQuery</c> and its
/// configuration does not turn them off (<c>AllowNativeQuery</c>, BRIEF §12). When false the UI
/// hides native mode; "Show query" still works for any source declaring <c>nativeQuery</c>.
/// </param>
public sealed record SourceResponseModel(
    string Alias,
    string DisplayName,
    string Type,
    bool Sensitive,
    SourceCapabilitiesResponseModel Capabilities,
    bool AllowNativeQuery
);

/// <summary>
/// A source's capabilities in a client-friendly shape: features and operators as lists of camelCase
/// names rather than a flags number, and the maximum range in seconds.
/// </summary>
/// <param name="Features">Supported optional features, for example <c>histogram</c> or <c>patterns</c>.</param>
/// <param name="Operators">Supported filter operators, for example <c>equals</c> or <c>startsWith</c>.</param>
/// <param name="NativeLanguage">Name of the native query language, or null when there is none.</param>
/// <param name="MaxRangeSeconds">Longest accepted time range in seconds, or null when unlimited.</param>
/// <param name="MaxPageSize">Largest page size the source honours.</param>
public sealed record SourceCapabilitiesResponseModel(
    IReadOnlyList<string> Features,
    IReadOnlyList<string> Operators,
    string? NativeLanguage,
    long? MaxRangeSeconds,
    int MaxPageSize
)
{
    /// <summary>Maps Core capabilities to the response shape.</summary>
    /// <param name="capabilities">The source's capabilities.</param>
    /// <returns>The response model.</returns>
    public static SourceCapabilitiesResponseModel From(LogSourceCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        return new SourceCapabilitiesResponseModel(
            [
                .. Enum.GetValues<LogSourceFeatures>()
                    .Where(feature =>
                        feature != LogSourceFeatures.None && capabilities.Supports(feature)
                    )
                    .Select(feature => CamelCase(feature.ToString())),
            ],
            [.. capabilities.Operators.Order().Select(op => CamelCase(op.ToString()))],
            capabilities.NativeLanguage,
            capabilities.MaxRange is { } range ? (long)range.TotalSeconds : null,
            capabilities.MaxPageSize
        );
    }

    private static string CamelCase(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}

/// <summary>Lists the log sources the current user may see (BRIEF §11.1 <c>GET /sources</c>).</summary>
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "Sources")]
public sealed class GetSourcesController(
    ILogSourceRegistry registry,
    IUserContextAccessor userContextAccessor
) : LogExplorerApiControllerBase
{
    /// <summary>Gets the visible sources, in configuration order.</summary>
    /// <returns>The sources; hidden ones are left out entirely.</returns>
    [HttpGet("sources")]
    [ProducesResponseType<IReadOnlyList<SourceResponseModel>>(StatusCodes.Status200OK)]
    public IReadOnlyList<SourceResponseModel> GetSources() =>
        [
            .. registry
                .GetVisibleSources(userContextAccessor.GetCurrent())
                .Select(source => new SourceResponseModel(
                    source.Alias,
                    source.DisplayName,
                    source.Type,
                    source.Sensitive,
                    SourceCapabilitiesResponseModel.From(source.Capabilities),
                    source is not NativeQueryDisabledSource
                        && source.Capabilities.Supports(LogSourceFeatures.NativeQuery)
                )),
        ];
}
