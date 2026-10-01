using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.LogExplorer.Core.Severity;
using Umbraco.Community.LogExplorer.Core.Sources;
using Umbraco.Community.LogExplorer.Features.Sources.Files;
using Umbraco.Community.LogExplorer.Infrastructure.Api;
using UmbracoLogLevel = Umbraco.Cms.Core.Logging.LogLevel;

namespace Umbraco.Community.LogExplorer.Features.MinimumLevels;

/// <summary>The minimum level one Serilog sink writes, as configured.</summary>
/// <param name="Name">
/// The sink's name as Umbraco reports it: <c>Global</c> for Serilog's global minimum and
/// <c>UmbracoFile</c> for the file sink the files source reads.
/// </param>
/// <param name="Level">The level as a lower-case OTel short name (<c>info</c>, <c>warn</c>; ADR 0008).</param>
public sealed record SinkMinimumLevel(string Name, string Level);

/// <summary>The body of <c>GET /sources/{alias}/minimum-levels</c> (ADR 0019).</summary>
/// <param name="Sinks">One entry per sink, in the order Umbraco returns them.</param>
public sealed record MinimumLevelsResult(IReadOnlyList<SinkMinimumLevel> Sinks);

/// <summary>
/// Reports the configured minimum level per Serilog sink for the Overview view (BRIEF §6.10,
/// UI brief §4.13), so a user can tell "no DEBUG entries" from "DEBUG is not written".
/// </summary>
/// <remarks>
/// This is the one query-time use of <see cref="ILogViewerService"/> that ADR 0003 allows: its
/// <see cref="ILogViewerService.GetLogLevelsFromSinks"/> reads the site's Serilog configuration,
/// which no other API exposes. Never use the service to read entries. The member has the same
/// synchronous signature from 17.0.0 to 18.2.0, so no V17/V18 pair is needed.
/// </remarks>
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "MinimumLevels")]
public sealed class GetMinimumLevelsController(
    ILogSourceRegistry registry,
    IUserContextAccessor userContextAccessor,
    ILogViewerService logViewerService
) : LogExplorerApiControllerBase
{
    /// <summary>
    /// Returns the minimum level of each sink in this site's Serilog configuration. Only sources
    /// of type <c>UmbracoFiles</c> answer: the configuration describes the files this site writes,
    /// not what an external store holds.
    /// </summary>
    /// <param name="alias">The source alias.</param>
    /// <returns>The sinks and their minimum levels.</returns>
    /// <exception cref="NotSupportedException">
    /// The source is not an <c>UmbracoFiles</c> source (400 <c>unsupported_feature</c>).
    /// </exception>
    /// <response code="400"><c>unsupported_feature</c>: the source is not a files source.</response>
    /// <response code="403"><c>forbidden_source</c>: the source exists but is hidden from the user.</response>
    /// <response code="404"><c>source_not_found</c>.</response>
    [HttpGet("sources/{alias}/minimum-levels")]
    [ProducesResponseType<MinimumLevelsResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public MinimumLevelsResult GetMinimumLevels(string alias)
    {
        ILogSource source = registry.GetForUser(alias, userContextAccessor.GetCurrent());
        if (!string.Equals(source.Type, UmbracoFilesLogSource.SourceType, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"Source '{alias}' is a {source.Type} source; minimum levels are only known for {UmbracoFilesLogSource.SourceType} sources."
            );
        }

        return new MinimumLevelsResult(
            logViewerService
                .GetLogLevelsFromSinks()
                .Select(sink => new SinkMinimumLevel(sink.Key, ToShortName(sink.Value)))
                .ToArray()
        );
    }

    // Umbraco's LogLevel members carry Serilog's names (Verbose ... Fatal), which SeverityMap maps.
    private static string ToShortName(UmbracoLogLevel level) =>
        SeverityMap.ToShortName(SeverityMap.FromSerilog(level.ToString()));
}
