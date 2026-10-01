using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Umbraco.Cms.Core.Logging;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Features.Sources;
using Umbraco.Community.LogExplorer.Features.Sources.Files;
using Umbraco.Community.LogExplorer.Files.Tests.TestSupport;

namespace Umbraco.Community.LogExplorer.Files.Tests.Features.Sources.Files;

/// <summary>
/// What every aggregation shares: the scan budget, the 60-second cache keyed on the files' state,
/// cancellation and the native-query guard (#32).
/// </summary>
public class FileAggregatorScanAggregationTests
{
    // About 3 MB: one entry a second for 30,000 seconds from noon.
    private const int LargeFileEntries = 30_000;

    [Fact]
    public void GetFacets_FilesWithinBudget_IsExactOverTheWholeRange()
    {
        // Arrange
        using TempDirectory directory = WriteLargeFile();
        FileAggregator aggregator = Aggregators.Create(directory.Path);

        // Act
        FacetResult result = aggregator.GetFacets(LargeQuery(), ["N"], 10, Token).Result;

        // Assert
        Assert.Equal((false, LargeRange), (result.Approximate, result.ScannedRange));
    }

    [Fact]
    public void GetFacets_BudgetHit_IsApproximate()
    {
        // Arrange
        using TempDirectory directory = WriteLargeFile();
        FileAggregator aggregator = Aggregators.Create(directory.Path, scanBudgetMegabytes: 1);

        // Act
        FacetResult result = aggregator.GetFacets(LargeQuery(), ["N"], 10, Token).Result;

        // Assert
        Assert.True(result.Approximate);
    }

    [Fact]
    public void GetFacets_BudgetHit_ReportsTheNewestPartOfTheRangeAsScanned()
    {
        // Arrange
        using TempDirectory directory = WriteLargeFile();
        FileAggregator aggregator = Aggregators.Create(directory.Path, scanBudgetMegabytes: 1);

        // Act
        ResolvedRange scanned = aggregator
            .GetFacets(LargeQuery(), ["N"], 10, Token)
            .Result.ScannedRange;

        // Assert: about a third of the 3 MB file, newest first.
        Assert.Multiple(
            () => Assert.Equal(LargeRange.To, scanned.To),
            () =>
                Assert.InRange(
                    scanned.From,
                    LargeRange.From.AddHours(5),
                    LargeRange.To.AddHours(-1)
                )
        );
    }

    [Fact]
    public void GetHistogram_BudgetHit_KeepsTheWholeRangeAndSaysWhatWasScanned()
    {
        // Arrange
        using TempDirectory directory = WriteLargeFile();
        FileAggregator aggregator = Aggregators.Create(directory.Path, scanBudgetMegabytes: 1);

        // Act
        FileAggregate<HistogramResult> histogram = aggregator.GetHistogram(LargeQuery(), 60, Token);

        // Assert
        Assert.Equal(
            (true, LargeRange, true),
            (
                histogram.Result.Approximate,
                histogram.Result.Range,
                histogram.ScannedRange.From > LargeRange.From
            )
        );
    }

    [Fact]
    public void GetPatterns_SameQueryTwice_ServesTheSecondFromTheCacheWithoutReading()
    {
        // Arrange
        using TempDirectory directory = WriteSmallFile();
        FileAggregator aggregator = Aggregators.Create(directory.Path);
        PatternResult first = aggregator.GetPatterns(Aggregators.NoonHour(), 10, Token).Result;

        // Act
        FileAggregate<PatternResult> second = aggregator.GetPatterns(
            Aggregators.NoonHour(),
            10,
            Token
        );

        // Assert
        Assert.Equal((0L, first), (second.BytesRead, second.Result));
    }

    [Fact]
    public void GetPatterns_FileAppendedAfterTheFirstCall_ReadsTheFilesAgain()
    {
        // Arrange
        using TempDirectory directory = WriteSmallFile();
        FileAggregator aggregator = Aggregators.Create(directory.Path);
        aggregator.GetPatterns(Aggregators.NoonHour(), 10, Token);
        File.AppendAllText(
            Path.Combine(directory.Path, Aggregators.DayFile),
            LogLines.Event(Aggregators.Noon.AddMinutes(30), "Appended") + "\n"
        );

        // Act
        PatternResult result = aggregator.GetPatterns(Aggregators.NoonHour(), 10, Token).Result;

        // Assert
        Assert.Contains("Appended", result.Patterns.Select(pattern => pattern.Template));
    }

    [Fact]
    public void GetPatterns_SameQueryWithAnotherTop_ReadsTheFilesAgain()
    {
        // Arrange
        using TempDirectory directory = WriteSmallFile();
        FileAggregator aggregator = Aggregators.Create(directory.Path);
        aggregator.GetPatterns(Aggregators.NoonHour(), 10, Token);

        // Act
        FileAggregate<PatternResult> other = aggregator.GetPatterns(
            Aggregators.NoonHour(),
            5,
            Token
        );

        // Assert
        Assert.NotEqual(0, other.BytesRead);
    }

    [Fact]
    public void GetFields_CancelledToken_ThrowsOperationCanceled()
    {
        // Arrange
        using TempDirectory directory = WriteSmallFile();
        FileAggregator aggregator = Aggregators.Create(directory.Path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        void Fields() => aggregator.GetFields(Aggregators.NoonHour(), cancellation.Token);

        // Assert
        Assert.Throws<OperationCanceledException>(Fields);
    }

    [Fact]
    public void GetHistogram_NativeQuery_ThrowsNotSupported()
    {
        // Arrange
        using TempDirectory directory = WriteSmallFile();
        FileAggregator aggregator = Aggregators.Create(directory.Path);
        LogQuery query = Aggregators.NoonHour() with { NativeQuery = "Has(Duration)" };

        // Act
        void Histogram() => aggregator.GetHistogram(query, 60, Token);

        // Assert
        Assert.Throws<NotSupportedException>(Histogram);
    }

    /// <summary>
    /// BRIEF §17: histogram, facets or patterns over the sample site's logs in under 3 s, or
    /// Approximate within budget. Explicit because it needs the sample site's generated logs, which
    /// are not committed: run the site with the generator on first, then run this test with
    /// <c>--explicit on</c>.
    /// </summary>
    [Fact(Explicit = true)]
    public void Aggregations_SampleSiteLogs_FinishInThreeSecondsOrAreApproximate()
    {
        // Arrange
        string? logs = FindSampleLogs();
        Assert.SkipWhen(
            logs is null,
            "The sample site has no logs; run it with the generator on first."
        );
        FileAggregator aggregator = CreateForSample(logs!);
        var query = new LogQuery { Range = new TimeRange(null, null, "30d") };
        string[] pinned =
        [
            "SourceContext",
            "RequestPath",
            "StatusCode",
            "MachineName",
            "@exception.type",
        ];

        // Act
        (TimeSpan Elapsed, bool Approximate)[] runs =
        [
            Time(() => aggregator.GetHistogram(query, 60, Token).Result.Approximate),
            Time(() => aggregator.GetFacets(query, pinned, 10, Token).Result.Approximate),
            Time(() => aggregator.GetPatterns(query, 50, Token).Result.Approximate),
        ];

        // Assert
        Assert.All(
            runs,
            run =>
                Assert.True(
                    run.Elapsed < TimeSpan.FromSeconds(3) || run.Approximate,
                    $"Took {run.Elapsed}"
                )
        );
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static ResolvedRange LargeRange { get; } =
        new(Aggregators.Noon, Aggregators.Noon.AddSeconds(LargeFileEntries));

    private static LogQuery LargeQuery() =>
        new() { Range = new TimeRange(LargeRange.From, LargeRange.To, null) };

    private static TempDirectory WriteLargeFile()
    {
        var directory = new TempDirectory();
        var content = new StringBuilder();
        for (int i = 0; i < LargeFileEntries; i++)
        {
            content
                .Append(
                    LogLines.Event(
                        Aggregators.Noon.AddSeconds(i),
                        "Tick {N} with some padding to make the line about a hundred bytes",
                        extraProperties: string.Create(
                            CultureInfo.InvariantCulture,
                            $"\"N\":{i % 7}"
                        )
                    )
                )
                .Append('\n');
        }

        directory.Write(Aggregators.DayFile, Encoding.UTF8.GetBytes(content.ToString()));
        return directory;
    }

    private static TempDirectory WriteSmallFile()
    {
        var directory = new TempDirectory();
        directory.Write(
            Aggregators.DayFile,
            LogLines.File(
                LogLines.Event(Aggregators.Noon.AddMinutes(1), "Started"),
                LogLines.Event(
                    Aggregators.Noon.AddMinutes(2),
                    "Slow {Path}",
                    "Warning",
                    "\"Path\":\"/a\""
                )
            )
        );
        return directory;
    }

    // The sample site's log folder, found by walking up from the test binaries, so it resolves from
    // the main checkout and from a worktree under .claude/worktrees alike.
    private static string? FindSampleLogs()
    {
        for (
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent
        )
        {
            string candidate = Path.Combine(
                directory.FullName,
                "samples",
                "LogExplorer.Site17",
                "umbraco",
                "Logs"
            );
            if (Directory.Exists(candidate) && Directory.EnumerateFiles(candidate, "*.json").Any())
            {
                return candidate;
            }
        }

        return null;
    }

    // The generator dates the sample logs when it runs, so "now" is just after the newest write.
    private static FileAggregator CreateForSample(string logs)
    {
        DateTime newest = Directory.EnumerateFiles(logs).Max(File.GetLastWriteTimeUtc);
        var configuration = Substitute.For<ILoggingConfiguration>();
        configuration.LogDirectory.Returns(logs);
        configuration.LogFileNameFormat.Returns("UmbracoTraceLog.{0}..json");
        configuration.GetLogFileNameFormatArguments().Returns(["WORM"]);
        return new FileAggregator(
            new UmbracoLogFileLocator(configuration, currentMachineName: "WORM"),
            new FakeTimeProvider(new DateTimeOffset(newest, TimeSpan.Zero).AddMinutes(1)),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new LogExplorerOptions())
        );
    }

    private static (TimeSpan Elapsed, bool Approximate) Time(Func<bool> aggregation)
    {
        var stopwatch = Stopwatch.StartNew();
        bool approximate = aggregation();
        return (stopwatch.Elapsed, approximate);
    }
}
