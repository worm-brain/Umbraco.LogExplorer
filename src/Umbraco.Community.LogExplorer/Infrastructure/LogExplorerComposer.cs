using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Community.LogExplorer.Core.Sources;
using Umbraco.Community.LogExplorer.Features.Sources;
using Umbraco.Community.LogExplorer.Features.Sources.Fake;
using Umbraco.Community.LogExplorer.Features.Sources.Files;

namespace Umbraco.Community.LogExplorer.Infrastructure;

/// <summary>
/// Registers Log Explorer with Umbraco: options, the source registry and the built-in provider
/// types. Runs automatically when the package is installed.
/// </summary>
public sealed class LogExplorerComposer : IComposer
{
    /// <summary>Adds the package's services.</summary>
    /// <param name="builder">The Umbraco builder.</param>
    public void Compose(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.Configure<LogExplorerOptions>(
            builder.Config.GetSection(LogExplorerOptions.SectionName)
        );
        builder.Services.AddSingleton<
            IPostConfigureOptions<LogExplorerOptions>,
            LogExplorerOptionsDefaults
        >();

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ILogSourceRegistry, LogSourceRegistry>();

        // Built-in provider types; provider packages add their own factories the same way.
        builder.Services.AddSingleton<ILogSourceFactory, FakeLogSourceFactory>();
        builder.Services.AddSingleton<ILogSourceFactory, UmbracoFilesLogSourceFactory>();

        // The files provider's readers. The locator takes the log directory and file name format
        // from Umbraco's ILoggingConfiguration; the aggregator's IMemoryCache is already
        // registered by Umbraco (see FileAggregator).
        builder.Services.AddSingleton<UmbracoLogFileLocator>();
        builder.Services.AddSingleton<LogFilePager>();
        builder.Services.AddSingleton<FileAggregator>();
        builder.Services.AddSingleton<FileContextReader>();
    }
}
