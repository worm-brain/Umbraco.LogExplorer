using Microsoft.Extensions.Logging;
using Serilog;
using Umbraco.Cms.Infrastructure.Logging.Serilog;
using Umbraco.Community.LogExplorer.Core.Sources;

namespace Umbraco.Community.LogExplorer.Features.Sources.Files;

/// <summary>
/// The <c>UmbracoFiles</c> provider type, for example
/// <c>{ "Alias": "files", "Type": "UmbracoFiles", "DisplayName": "This server's log files" }</c>.
/// It is also the type of the zero-configuration <c>files</c> source. Sources of this type take no
/// <c>Settings</c>; any given are ignored, since the log directory and file name format come from
/// Umbraco's own logging configuration.
/// </summary>
/// <remarks>
/// The readers are shared by every source of this type: they hold no per-source state, and all of
/// them read the same directory.
/// </remarks>
internal sealed partial class UmbracoFilesLogSourceFactory : ILogSourceFactory
{
    private readonly LogFilePager _pager;
    private readonly FileAggregator _aggregator;
    private readonly FileContextReader _contextReader;
    private readonly UmbracoFileConfiguration _fileConfiguration;
    private readonly ILogger<UmbracoFilesLogSourceFactory> _logger;

    /// <summary>Creates the factory.</summary>
    /// <param name="pager">Reads search pages.</param>
    /// <param name="aggregator">Computes histogram, facets, patterns and fields.</param>
    /// <param name="contextReader">Reads "Around this".</param>
    /// <param name="fileConfiguration">
    /// Umbraco's file sink settings; only <see cref="UmbracoFileConfiguration.RollingInterval"/>
    /// is read, to warn when the files are named in a way the locator does not recognise.
    /// </param>
    /// <param name="logger">Receives that warning.</param>
    public UmbracoFilesLogSourceFactory(
        LogFilePager pager,
        FileAggregator aggregator,
        FileContextReader contextReader,
        UmbracoFileConfiguration fileConfiguration,
        ILogger<UmbracoFilesLogSourceFactory> logger
    )
    {
        _pager = pager;
        _aggregator = aggregator;
        _contextReader = contextReader;
        _fileConfiguration = fileConfiguration;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Type => UmbracoFilesLogSource.SourceType;

    /// <inheritdoc />
    /// <remarks>
    /// Logs a warning when Umbraco's file sink does not roll daily: the locator only recognises
    /// daily names (<c>yyyyMMdd</c> plus an optional <c>_NNN</c>), so hourly or monthly files
    /// would be silently ignored. The source is still created, because daily files written
    /// before the setting changed remain readable.
    /// </remarks>
    public ILogSource Create(LogSourceDefinition definition, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (_fileConfiguration.RollingInterval != RollingInterval.Day)
        {
            LogUnsupportedRollingInterval(definition.Alias, _fileConfiguration.RollingInterval);
        }

        return new UmbracoFilesLogSource(definition, _pager, _aggregator, _contextReader);
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Log Explorer source '{Alias}' only reads daily log files, but Umbraco's file sink rolls every {RollingInterval}. Files whose names do not end in a yyyyMMdd date (with an optional _NNN roll suffix) are ignored; set the UmbracoFile sink's RollingInterval to Day to read them."
    )]
    private partial void LogUnsupportedRollingInterval(
        string alias,
        RollingInterval rollingInterval
    );
}
