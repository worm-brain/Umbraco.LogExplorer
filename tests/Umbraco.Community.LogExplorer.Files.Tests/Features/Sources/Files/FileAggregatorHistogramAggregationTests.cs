using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Features.Sources.Files;
using Umbraco.Community.LogExplorer.Files.Tests.TestSupport;

namespace Umbraco.Community.LogExplorer.Files.Tests.Features.Sources.Files;

/// <summary>
/// The histogram picks a round bucket width near the target, aligns buckets to it, lists every
/// bucket in the range, and stacks counts by level while ignoring the level set (ADR 0004, #32).
/// </summary>
public class FileAggregatorHistogramAggregationTests
{
    [Theory]
    [InlineData("15m", 45, 30)]
    [InlineData("1h", 60, 60)]
    [InlineData("24h", 60, 30 * 60)]
    [InlineData("7d", 60, 3 * 60 * 60)]
    [InlineData("30d", 60, 12 * 60 * 60)]
    public void GetHistogram_RelativeRange_UsesTheSmallestRoundWidthWithinTheTarget(
        string relative,
        int targetBuckets,
        int expectedSeconds
    )
    {
        // Arrange
        using var directory = new TempDirectory();
        FileAggregator aggregator = Aggregators.Create(directory.Path);
        var query = new LogQuery { Range = new TimeRange(null, null, relative) };

        // Act
        HistogramResult histogram = aggregator.GetHistogram(query, targetBuckets, Token).Result;

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), histogram.BucketSize);
    }

    [Fact]
    public void GetHistogram_RangeOnBucketBoundaries_HasOneBucketPerWidthIncludingEmptyOnes()
    {
        // Arrange
        using var directory = new TempDirectory();
        FileAggregator aggregator = Aggregators.Create(directory.Path);

        // Act
        HistogramResult histogram = aggregator
            .GetHistogram(Aggregators.NoonHour(), 60, Token)
            .Result;

        // Assert
        Assert.Equal(
            Enumerable.Range(0, 60).Select(minute => Aggregators.Noon.AddMinutes(minute)),
            histogram.Buckets.Select(bucket => bucket.Start)
        );
    }

    [Fact]
    public void GetHistogram_RangeOffBucketBoundaries_AlignsBucketsAndCoversBothEdges()
    {
        // Arrange
        using var directory = new TempDirectory();
        FileAggregator aggregator = Aggregators.Create(directory.Path);
        DateTimeOffset from = Aggregators.Noon.AddSeconds(30);
        var query = new LogQuery { Range = new TimeRange(from, from.AddHours(1), null) };

        // Act
        HistogramResult histogram = aggregator.GetHistogram(query, 60, Token).Result;

        // Assert
        Assert.Equal(
            (Aggregators.Noon, Aggregators.Noon.AddMinutes(60), 61),
            (histogram.Buckets[0].Start, histogram.Buckets[^1].Start, histogram.Buckets.Count)
        );
    }

    [Fact]
    public void GetHistogram_EventsOfSeveralLevels_StacksCountsPerBucketWithEveryLevelPresent()
    {
        // Arrange
        using var directory = new TempDirectory();
        directory.Write(
            Aggregators.DayFile,
            LogLines.File(
                LogLines.Event(Aggregators.Noon.AddSeconds(10), "One"),
                LogLines.Event(Aggregators.Noon.AddSeconds(20), "Two", "Warning"),
                LogLines.Event(Aggregators.Noon.AddSeconds(30), "Three", "Error"),
                LogLines.Event(Aggregators.Noon.AddSeconds(65), "Four")
            )
        );
        FileAggregator aggregator = Aggregators.Create(directory.Path);

        // Act
        HistogramResult histogram = aggregator
            .GetHistogram(Aggregators.NoonHour(), 60, Token)
            .Result;

        // Assert
        Assert.Equal(
            new IReadOnlyDictionary<string, long>[]
            {
                Levels(info: 1, warn: 1, error: 1),
                Levels(info: 1),
            },
            histogram.Buckets.Take(2).Select(bucket => bucket.CountsBySeverityShortName)
        );
    }

    [Fact]
    public void GetHistogram_QueryWithLevelSet_CountsEveryLevel()
    {
        // Arrange
        using var directory = new TempDirectory();
        directory.Write(
            Aggregators.DayFile,
            LogLines.File(
                LogLines.Event(Aggregators.Noon.AddSeconds(10), "One"),
                LogLines.Event(Aggregators.Noon.AddSeconds(20), "Two", "Error")
            )
        );
        FileAggregator aggregator = Aggregators.Create(directory.Path);
        LogQuery query = Aggregators.NoonHour() with { Levels = new HashSet<string> { "error" } };

        // Act
        HistogramResult histogram = aggregator.GetHistogram(query, 60, Token).Result;

        // Assert
        Assert.Equal(Levels(info: 1, error: 1), histogram.Buckets[0].CountsBySeverityShortName);
    }

    [Fact]
    public void GetHistogram_QueryWithFilter_CountsOnlyMatchingEntries()
    {
        // Arrange
        using var directory = new TempDirectory();
        directory.Write(
            Aggregators.DayFile,
            LogLines.File(
                LogLines.Event(Aggregators.Noon.AddSeconds(10), "Request timeout"),
                LogLines.Event(Aggregators.Noon.AddSeconds(20), "Request done", "Error")
            )
        );
        FileAggregator aggregator = Aggregators.Create(directory.Path);
        LogQuery query = Aggregators.NoonHour() with { Filter = new TextNode("timeout") };

        // Act
        HistogramResult histogram = aggregator.GetHistogram(query, 60, Token).Result;

        // Assert
        Assert.Equal(Levels(info: 1), histogram.Buckets[0].CountsBySeverityShortName);
    }

    [Fact]
    public void GetHistogram_TargetBelowOne_Throws()
    {
        // Arrange
        using var directory = new TempDirectory();
        FileAggregator aggregator = Aggregators.Create(directory.Path);

        // Act
        void Histogram() => aggregator.GetHistogram(Aggregators.NoonHour(), 0, Token);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(Histogram);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Dictionary<string, long> Levels(long info = 0, long warn = 0, long error = 0) =>
        new()
        {
            ["trace"] = 0,
            ["debug"] = 0,
            ["info"] = info,
            ["warn"] = warn,
            ["error"] = error,
            ["fatal"] = 0,
        };
}
