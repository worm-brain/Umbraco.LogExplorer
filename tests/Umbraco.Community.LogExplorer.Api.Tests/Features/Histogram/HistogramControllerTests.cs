using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Umbraco.Community.LogExplorer.Core.Fake;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Core.Sources;
using Umbraco.Community.LogExplorer.Features.Histogram;
using Umbraco.Community.LogExplorer.Infrastructure.Api;

namespace Umbraco.Community.LogExplorer.Api.Tests.Features.Histogram;

/// <summary>
/// <c>POST /sources/{alias}/histogram</c> counts the user's source over time and fails with the
/// exceptions the problem filter maps to BRIEF §11.1 codes (#40). Source resolution failures are
/// the registry's and are covered with the search endpoint.
/// </summary>
public class HistogramControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly UserContext Editor = new(
        Guid.NewGuid(),
        new HashSet<string>(["editor"], StringComparer.OrdinalIgnoreCase)
    );

    private static readonly LogQuery LastHour = new() { Range = new TimeRange(null, null, "1h") };

    [Fact]
    public async Task GetHistogram_SampleSourceLastHour_CountsEveryEntryInSixtyBuckets()
    {
        // Arrange
        HistogramController controller = CreateController(CreateSource());

        // Act
        HistogramResult histogram = await controller.GetHistogram(
            "sample",
            new HistogramRequest(LastHour, 60),
            CancellationToken.None
        );

        // Assert
        Assert.Equal(
            (60, 238L),
            (
                histogram.Buckets.Count,
                histogram.Buckets.Sum(bucket => bucket.CountsBySeverityShortName.Values.Sum())
            )
        );
    }

    [Fact]
    public async Task GetHistogram_WithLevelSet_StillCountsEveryLevel()
    {
        // Arrange
        HistogramController controller = CreateController(CreateSource());
        LogQuery errorsOnly = LastHour with { Levels = new HashSet<string>(["error"]) };

        // Act
        HistogramResult histogram = await controller.GetHistogram(
            "sample",
            new HistogramRequest(errorsOnly, 60),
            CancellationToken.None
        );

        // Assert
        Assert.Equal(
            238L,
            histogram.Buckets.Sum(bucket => bucket.CountsBySeverityShortName.Values.Sum())
        );
    }

    [Fact]
    public async Task GetHistogram_SourceWithoutHistogram_ThrowsNotSupported()
    {
        // Arrange
        HistogramController controller = CreateController(
            CreateSource(LogSourceFeatures.Facets | LogSourceFeatures.Patterns)
        );

        // Act
        Exception? thrown = await Record.ExceptionAsync(() =>
            controller.GetHistogram(
                "sample",
                new HistogramRequest(LastHour, 60),
                CancellationToken.None
            )
        );

        // Assert
        Assert.IsType<NotSupportedException>(thrown);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(HistogramController.MaxTargetBuckets + 1)]
    public async Task GetHistogram_TargetBucketsOutOfRange_ThrowsArgumentException(
        int targetBuckets
    )
    {
        // Arrange
        HistogramController controller = CreateController(CreateSource());

        // Act
        Exception? thrown = await Record.ExceptionAsync(() =>
            controller.GetHistogram(
                "sample",
                new HistogramRequest(LastHour, targetBuckets),
                CancellationToken.None
            )
        );

        // Assert
        Assert.IsAssignableFrom<ArgumentException>(thrown);
    }

    [Fact]
    public async Task GetHistogram_RangeLongerThanTheSourceAllows_ThrowsRangeTooLarge()
    {
        // Arrange
        var source = new FakeLogSource(
            new FakeLogSourceOptions
            {
                Alias = "sample",
                FixedNow = Now,
                MaxRange = TimeSpan.FromHours(4),
            }
        );
        HistogramController controller = CreateController(source);

        // Act
        Exception? thrown = await Record.ExceptionAsync(() =>
            controller.GetHistogram(
                "sample",
                new HistogramRequest(
                    LastHour with
                    {
                        Range = new TimeRange(null, null, "24h"),
                    },
                    60
                ),
                CancellationToken.None
            )
        );

        // Assert
        Assert.IsType<RangeTooLargeException>(thrown);
    }

    private static FakeLogSource CreateSource(
        LogSourceFeatures features = FakeLogSourceOptions.AllFeaturesExceptTail
    ) =>
        new(
            new FakeLogSourceOptions
            {
                Alias = "sample",
                FixedNow = Now,
                Features = features,
            }
        );

    private static HistogramController CreateController(FakeLogSource source)
    {
        ILogSourceRegistry registry = Substitute.For<ILogSourceRegistry>();
        registry.GetForUser(source.Alias, Editor).Returns(source);
        IUserContextAccessor users = Substitute.For<IUserContextAccessor>();
        users.GetCurrent().Returns(Editor);
        return new HistogramController(registry, users, new FakeTimeProvider(Now));
    }
}
