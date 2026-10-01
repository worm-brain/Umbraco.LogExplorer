using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Umbraco.Community.LogExplorer.Core.Fake;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Core.Sources;
using Umbraco.Community.LogExplorer.Features.Patterns;
using Umbraco.Community.LogExplorer.Infrastructure.Api;

namespace Umbraco.Community.LogExplorer.Api.Tests.Features.Patterns;

/// <summary>
/// <c>POST /sources/{alias}/patterns</c> groups the user's source by template and fails with the
/// exceptions the problem filter maps to BRIEF §11.1 codes (#47). Source resolution failures are
/// the registry's and are covered with the search endpoint.
/// </summary>
public class PatternsControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly UserContext Editor = new(
        Guid.NewGuid(),
        new HashSet<string>(["editor"], StringComparer.OrdinalIgnoreCase)
    );

    private static readonly LogQuery LastHour = new() { Range = new TimeRange(null, null, "1h") };

    [Fact]
    public async Task GetPatterns_SampleSourceLastHour_GroupsEveryEntryHighestCountFirst()
    {
        // Arrange
        PatternsController controller = CreateController(CreateSource());

        // Act
        PatternResult result = await controller.GetPatterns(
            "sample",
            new PatternsRequest(LastHour, 100),
            CancellationToken.None
        );

        // Assert
        long[] counts = result.Patterns.Select(pattern => pattern.Count).ToArray();
        Assert.Multiple(
            () => Assert.Equal(238L, counts.Sum()),
            () => Assert.Equal(counts.OrderByDescending(count => count), counts)
        );
    }

    [Fact]
    public async Task GetPatterns_WithErrorLevelSet_ReturnsOnlyPatternsWithErrors()
    {
        // Arrange
        PatternsController controller = CreateController(CreateSource());
        LogQuery errorsOnly = LastHour with { Levels = new HashSet<string>(["error"]) };

        // Act
        PatternResult result = await controller.GetPatterns(
            "sample",
            new PatternsRequest(errorsOnly, 100),
            CancellationToken.None
        );

        // Assert
        Assert.All(
            result.Patterns,
            pattern => Assert.Equal(pattern.Count, pattern.CountsBySeverityShortName["error"])
        );
    }

    [Fact]
    public async Task GetPatterns_TopBelowPatternCount_ReturnsThatMany()
    {
        // Arrange
        PatternsController controller = CreateController(CreateSource());

        // Act
        PatternResult result = await controller.GetPatterns(
            "sample",
            new PatternsRequest(LastHour, 2),
            CancellationToken.None
        );

        // Assert
        Assert.Equal(2, result.Patterns.Count);
    }

    [Fact]
    public async Task GetPatterns_SourceWithoutPatterns_ThrowsNotSupported()
    {
        // Arrange
        PatternsController controller = CreateController(
            CreateSource(LogSourceFeatures.Facets | LogSourceFeatures.Histogram)
        );

        // Act
        Exception? thrown = await Record.ExceptionAsync(() =>
            controller.GetPatterns(
                "sample",
                new PatternsRequest(LastHour, 100),
                CancellationToken.None
            )
        );

        // Assert
        Assert.IsType<NotSupportedException>(thrown);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(PatternsController.MaxTop + 1)]
    public async Task GetPatterns_TopOutOfRange_ThrowsArgumentException(int top)
    {
        // Arrange
        PatternsController controller = CreateController(CreateSource());

        // Act
        Exception? thrown = await Record.ExceptionAsync(() =>
            controller.GetPatterns(
                "sample",
                new PatternsRequest(LastHour, top),
                CancellationToken.None
            )
        );

        // Assert
        Assert.IsAssignableFrom<ArgumentException>(thrown);
    }

    [Fact]
    public async Task GetPatterns_RangeLongerThanTheSourceAllows_ThrowsRangeTooLarge()
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
        PatternsController controller = CreateController(source);

        // Act
        Exception? thrown = await Record.ExceptionAsync(() =>
            controller.GetPatterns(
                "sample",
                new PatternsRequest(
                    LastHour with
                    {
                        Range = new TimeRange(null, null, "24h"),
                    },
                    100
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

    private static PatternsController CreateController(FakeLogSource source)
    {
        ILogSourceRegistry registry = Substitute.For<ILogSourceRegistry>();
        registry.GetForUser(source.Alias, Editor).Returns(source);
        IUserContextAccessor users = Substitute.For<IUserContextAccessor>();
        users.GetCurrent().Returns(Editor);
        return new PatternsController(registry, users, new FakeTimeProvider(Now));
    }
}
