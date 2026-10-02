using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Umbraco.Cms.Core.Logging;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Features.Sources;
using Umbraco.Community.LogExplorer.Features.Sources.Files;

namespace Umbraco.Community.LogExplorer.Files.Tests.TestSupport;

/// <summary>Builds a <see cref="FileAggregator"/> over a directory, with a real, private memory cache.</summary>
internal static class Aggregators
{
    /// <summary>"Now" for tests that resolve a relative range.</summary>
    public static readonly DateTimeOffset Now = new(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Noon on the day the generated files cover, 2026-10-01 (file <see cref="DayFile"/>).</summary>
    public static readonly DateTimeOffset Noon = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A file name for WORM on 2026-10-01.</summary>
    public const string DayFile = "UmbracoTraceLog.WORM.20261001.json";

    /// <summary>Creates an aggregator over WORM's log files in a test directory.</summary>
    /// <param name="directory">The log directory.</param>
    /// <param name="scanBudgetMegabytes">The <c>Files:ScanBudgetMegabytes</c> option.</param>
    /// <param name="cache">The cache to use; a fresh one when null.</param>
    /// <param name="clock">The clock relative ranges resolve against; fixed at <see cref="Now"/> when null.</param>
    /// <param name="parallelScan">
    /// Whether scans run across cores (ADR 0026). True by default, whatever the test host's
    /// garbage collector, so every aggregation test covers the parallel path.
    /// </param>
    /// <returns>The aggregator.</returns>
    public static FileAggregator Create(
        string directory,
        int scanBudgetMegabytes = 256,
        IMemoryCache? cache = null,
        TimeProvider? clock = null,
        bool parallelScan = true
    )
    {
        var configuration = Substitute.For<ILoggingConfiguration>();
        configuration.LogDirectory.Returns(directory);
        configuration.LogFileNameFormat.Returns("UmbracoTraceLog.{0}..json");
        configuration.GetLogFileNameFormatArguments().Returns(["WORM"]);
        var options = new LogExplorerOptions();
        options.Files.ScanBudgetMegabytes = scanBudgetMegabytes;
        return new FileAggregator(
            new UmbracoLogFileLocator(configuration, currentMachineName: "WORM"),
            clock ?? new FakeTimeProvider(Now),
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            Options.Create(options)
        )
        {
            ParallelScan = parallelScan,
        };
    }

    /// <summary>The hour from <see cref="Noon"/>.</summary>
    public static LogQuery NoonHour() =>
        new() { Range = new TimeRange(Noon, Noon.AddHours(1), null) };
}
