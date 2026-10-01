using Microsoft.Extensions.Time.Testing;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Features.Sources.Files;
using Umbraco.Community.LogExplorer.Files.Tests.TestSupport;

namespace Umbraco.Community.LogExplorer.Files.Tests.Features.Sources.Files;

/// <summary>
/// Relative ranges end at "now" rounded up to the bucket width, so a repeated "last 24 h"
/// aggregation within one bucket hits the 60-second cache.
/// </summary>
public class FileAggregatorRelativeRangeAggregationTests
{
    private static readonly DateTimeOffset TenPast = new(2026, 10, 1, 16, 10, 24, TimeSpan.Zero);

    [Fact]
    public void ResolveForAggregation_AbsoluteRange_IsUnchanged()
    {
        // Arrange
        var range = new TimeRange(TenPast.AddHours(-1), TenPast, null);

        // Act
        ResolvedRange resolved = FileAggregator.ResolveForAggregation(
            range,
            60,
            new FakeTimeProvider(TenPast)
        );

        // Assert
        Assert.Equal(new ResolvedRange(TenPast.AddHours(-1), TenPast), resolved);
    }

    [Fact]
    public void ResolveForAggregation_RelativeHour_EndsAtNowRoundedUpToTheMinute()
    {
        // Arrange: one hour over 60 buckets gives one-minute buckets.
        var range = new TimeRange(null, null, "1h");
        DateTimeOffset elevenPast = new(2026, 10, 1, 16, 11, 0, TimeSpan.Zero);

        // Act
        ResolvedRange resolved = FileAggregator.ResolveForAggregation(
            range,
            60,
            new FakeTimeProvider(TenPast)
        );

        // Assert
        Assert.Equal(new ResolvedRange(elevenPast.AddHours(-1), elevenPast), resolved);
    }

    [Fact]
    public void ResolveForAggregation_NowOnABucketBoundary_IsNotMoved()
    {
        // Arrange
        var range = new TimeRange(null, null, "1h");
        DateTimeOffset onTheMinute = new(2026, 10, 1, 16, 10, 0, TimeSpan.Zero);

        // Act
        ResolvedRange resolved = FileAggregator.ResolveForAggregation(
            range,
            60,
            new FakeTimeProvider(onTheMinute)
        );

        // Assert
        Assert.Equal(new ResolvedRange(onTheMinute.AddHours(-1), onTheMinute), resolved);
    }

    [Fact]
    public void ResolveForAggregation_UnknownRelativeRange_Throws()
    {
        // Arrange
        var range = new TimeRange(null, null, "2y");

        // Act
        void Act() =>
            FileAggregator.ResolveForAggregation(range, 60, new FakeTimeProvider(TenPast));

        // Assert
        Assert.Throws<ArgumentException>(Act);
    }

    [Fact]
    public void GetPatterns_SameRelativeQueryLaterInTheSameBucket_ServesFromTheCache()
    {
        // Arrange: 24 h over the sparkline's 30 buckets gives one-hour buckets, so 00:10 and
        // 00:30 both round up to 01:00.
        using TempDirectory directory = WriteNoonFile();
        var clock = new FakeTimeProvider(Aggregators.Now.AddMinutes(10));
        FileAggregator aggregator = Aggregators.Create(directory.Path, clock: clock);
        aggregator.GetPatterns(LastDay(), 10, Token);
        clock.Advance(TimeSpan.FromMinutes(20));

        // Act
        FileAggregate<PatternResult> second = aggregator.GetPatterns(LastDay(), 10, Token);

        // Assert
        Assert.Equal(0L, second.BytesRead);
    }

    [Fact]
    public void GetPatterns_SameRelativeQueryInTheNextBucket_ReadsTheFilesAgain()
    {
        // Arrange: 00:10 rounds up to 01:00, 01:05 to 02:00.
        using TempDirectory directory = WriteNoonFile();
        var clock = new FakeTimeProvider(Aggregators.Now.AddMinutes(10));
        FileAggregator aggregator = Aggregators.Create(directory.Path, clock: clock);
        aggregator.GetPatterns(LastDay(), 10, Token);
        clock.Advance(TimeSpan.FromMinutes(55));

        // Act
        FileAggregate<PatternResult> second = aggregator.GetPatterns(LastDay(), 10, Token);

        // Assert
        Assert.True(second.BytesRead > 0);
    }

    private static LogQuery LastDay() => new() { Range = new TimeRange(null, null, "24h") };

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static TempDirectory WriteNoonFile()
    {
        var directory = new TempDirectory();
        directory.Write(
            Aggregators.DayFile,
            LogLines.File(LogLines.Event(Aggregators.Noon.AddHours(11), "Late"))
        );
        return directory;
    }
}
