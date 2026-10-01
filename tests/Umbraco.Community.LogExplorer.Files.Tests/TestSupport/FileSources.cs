using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Umbraco.Cms.Core.Logging;
using Umbraco.Cms.Infrastructure.Logging.Serilog;
using Umbraco.Community.LogExplorer.Core.Sources;
using Umbraco.Community.LogExplorer.Features.Sources;
using Umbraco.Community.LogExplorer.Features.Sources.Files;

namespace Umbraco.Community.LogExplorer.Files.Tests.TestSupport;

/// <summary>
/// Builds <c>UmbracoFiles</c> sources over a test directory the way the package wires them: the
/// factory with real readers, a fixed clock and a private memory cache.
/// </summary>
internal static class FileSources
{
    /// <summary>"Now" for relative ranges: the day after the generated data.</summary>
    public static readonly DateTimeOffset Now = new(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Creates the factory over WORM's (and any other machine's) files in a directory.</summary>
    /// <param name="directory">The log directory.</param>
    /// <param name="configuration">
    /// Site configuration for <see cref="UmbracoFileConfiguration"/>; empty (Umbraco's defaults)
    /// when null.
    /// </param>
    /// <param name="logger">Receives the factory's warnings; discarded when null.</param>
    /// <returns>The factory.</returns>
    public static UmbracoFilesLogSourceFactory Factory(
        string directory,
        IConfiguration? configuration = null,
        ILogger<UmbracoFilesLogSourceFactory>? logger = null
    )
    {
        var loggingConfiguration = Substitute.For<ILoggingConfiguration>();
        loggingConfiguration.LogDirectory.Returns(directory);
        loggingConfiguration.LogFileNameFormat.Returns("UmbracoTraceLog.{0}..json");
        loggingConfiguration.GetLogFileNameFormatArguments().Returns(["WORM"]);
        var locator = new UmbracoLogFileLocator(loggingConfiguration, currentMachineName: "WORM");
        var clock = new FakeTimeProvider(Now);

        return new UmbracoFilesLogSourceFactory(
            new LogFilePager(locator, clock),
            new FileAggregator(
                locator,
                clock,
                new MemoryCache(new MemoryCacheOptions()),
                Options.Create(new LogExplorerOptions())
            ),
            new FileContextReader(locator),
            new UmbracoFileConfiguration(configuration ?? new ConfigurationBuilder().Build()),
            logger ?? NullLogger<UmbracoFilesLogSourceFactory>.Instance
        );
    }

    /// <summary>Creates a source through the factory.</summary>
    /// <param name="directory">The log directory.</param>
    /// <param name="definition">The configuration entry; the zero-configuration <c>files</c> entry when null.</param>
    /// <returns>The source.</returns>
    public static ILogSource Create(string directory, LogSourceDefinition? definition = null) =>
        Factory(directory)
            .Create(
                definition ?? LogSourceRegistry.DefaultFilesSource,
                new ServiceCollection().BuildServiceProvider()
            );
}
