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

    public static FileAggregator Create(
        string directory,
        int scanBudgetMegabytes = 256,
        IMemoryCache? cache = null
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
            new FakeTimeProvider(Now),
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            Options.Create(options)
        );
    }

    /// <summary>The hour from <see cref="Noon"/>.</summary>
    public static LogQuery NoonHour() =>
        new() { Range = new TimeRange(Noon, Noon.AddHours(1), null) };
}
