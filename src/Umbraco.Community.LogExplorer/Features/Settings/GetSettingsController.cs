using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Umbraco.Community.LogExplorer.Features.Sources;
using Umbraco.Community.LogExplorer.Infrastructure.Api;

namespace Umbraco.Community.LogExplorer.Features.Settings;

/// <summary>
/// The <c>LogExplorer</c> options the backoffice client needs before it renders anything (ADR 0011).
/// Only UI behaviour flags and defaults are exposed; sources, masking rules and secrets never are.
/// </summary>
/// <param name="HideCoreLogViewer">Whether the client removes the core Log Viewer menu item (ADR 0007).</param>
/// <param name="DefaultSource">Alias of the source the explorer opens with. It may name a source the user cannot see.</param>
/// <param name="DefaultTimeRange">Relative range the explorer opens with, for example <c>1h</c> (BRIEF §6.5).</param>
public sealed record SettingsResponseModel(
    bool HideCoreLogViewer,
    string DefaultSource,
    string DefaultTimeRange
);

/// <summary>Returns the client-facing settings (BRIEF §11.1 <c>GET /settings</c>).</summary>
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "Settings")]
public sealed class GetSettingsController(IOptions<LogExplorerOptions> options)
    : LogExplorerApiControllerBase
{
    /// <summary>Gets the settings as configured, after defaults are applied.</summary>
    /// <returns>The settings.</returns>
    [HttpGet("settings")]
    [ProducesResponseType<SettingsResponseModel>(StatusCodes.Status200OK)]
    public SettingsResponseModel GetSettings()
    {
        LogExplorerOptions value = options.Value;
        return new SettingsResponseModel(
            value.HideCoreLogViewer,
            value.DefaultSource,
            value.DefaultTimeRange
        );
    }
}
