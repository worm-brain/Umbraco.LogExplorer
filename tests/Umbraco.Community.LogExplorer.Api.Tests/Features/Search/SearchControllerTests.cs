using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Umbraco.Community.LogExplorer.Core.Fake;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Core.Sources;
using Umbraco.Community.LogExplorer.Features.Search;
using Umbraco.Community.LogExplorer.Infrastructure.Api;

namespace Umbraco.Community.LogExplorer.Api.Tests.Features.Search;

/// <summary>
/// <c>POST /sources/{alias}/search</c> runs the query on the user's source and fails with the
/// exceptions the problem filter maps to BRIEF §11.1 codes (#38). The mapping itself is covered by
/// <c>LogExplorerProblemFilterTests</c>.
/// </summary>
public class SearchControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly UserContext Editor = new(
        Guid.NewGuid(),
        new HashSet<string>(["editor"], StringComparer.OrdinalIgnoreCase)
    );

    private static readonly LogQuery LastHour = new()
    {
        Range = new TimeRange(null, null, "1h"),
        Take = 60,
    };

    [Fact]
    public async Task Search_VisibleSource_ReturnsTheFirstPageNewestFirst()
    {
        // Arrange
        var source = new FakeLogSource(
            new FakeLogSourceOptions { Alias = "sample", FixedNow = Now }
        );
        SearchController controller = CreateController(source);

        // Act
        LogPage page = await controller.Search("sample", LastHour, CancellationToken.None);

        // Assert
        Assert.Equal(
            (60, source.Records[^1].Id, 238L),
            (page.Records.Count, page.Records[0].Id, page.TotalCount)
        );
    }

    [Fact]
    public async Task Search_NextCursor_ReturnsTheFollowingPage()
    {
        // Arrange
        var source = new FakeLogSource(
            new FakeLogSourceOptions { Alias = "sample", FixedNow = Now }
        );
        SearchController controller = CreateController(source);
        LogPage first = await controller.Search("sample", LastHour, CancellationToken.None);

        // Act
        LogPage second = await controller.Search(
            "sample",
            LastHour with
            {
                Cursor = first.NextCursor,
            },
            CancellationToken.None
        );

        // Assert
        Assert.Equal(source.Records[^61].Id, second.Records[0].Id);
    }

    [Fact]
    public async Task Search_UnknownAlias_ThrowsKeyNotFound()
    {
        // Arrange
        ILogSourceRegistry registry = Substitute.For<ILogSourceRegistry>();
        registry.GetForUser("missing", Editor).Throws(new KeyNotFoundException("missing"));
        SearchController controller = CreateController(registry);

        // Act
        Exception? thrown = await Record.ExceptionAsync(() =>
            controller.Search("missing", LastHour, CancellationToken.None)
        );

        // Assert
        Assert.IsType<KeyNotFoundException>(thrown);
    }

    [Fact]
    public async Task Search_HiddenSource_ThrowsForbiddenSource()
    {
        // Arrange
        ILogSourceRegistry registry = Substitute.For<ILogSourceRegistry>();
        registry.GetForUser("prod", Editor).Throws(new ForbiddenSourceException("prod"));
        SearchController controller = CreateController(registry);

        // Act
        Exception? thrown = await Record.ExceptionAsync(() =>
            controller.Search("prod", LastHour, CancellationToken.None)
        );

        // Assert
        Assert.IsType<ForbiddenSourceException>(thrown);
    }

    [Fact]
    public async Task Search_RangeLongerThanTheSourceAllows_ThrowsRangeTooLarge()
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
        SearchController controller = CreateController(source);

        // Act
        Exception? thrown = await Record.ExceptionAsync(() =>
            controller.Search(
                "sample",
                LastHour with
                {
                    Range = new TimeRange(null, null, "24h"),
                },
                CancellationToken.None
            )
        );

        // Assert
        Assert.IsType<RangeTooLargeException>(thrown);
    }

    [Fact]
    public async Task Search_UnknownRelativeRange_ThrowsArgumentException()
    {
        // Arrange
        var source = new FakeLogSource(
            new FakeLogSourceOptions { Alias = "sample", FixedNow = Now }
        );
        SearchController controller = CreateController(source);

        // Act
        Exception? thrown = await Record.ExceptionAsync(() =>
            controller.Search(
                "sample",
                LastHour with
                {
                    Range = new TimeRange(null, null, "3w"),
                },
                CancellationToken.None
            )
        );

        // Assert
        Assert.IsAssignableFrom<ArgumentException>(thrown);
    }

    [Fact]
    public async Task Search_OperatorTheSourceDoesNotDeclare_ThrowsNotSupported()
    {
        // Arrange
        var source = new FakeLogSource(
            new FakeLogSourceOptions
            {
                Alias = "sample",
                FixedNow = Now,
                Operators = new HashSet<FilterOperator>([FilterOperator.Equals]),
            }
        );
        SearchController controller = CreateController(source);

        // Act
        Exception? thrown = await Record.ExceptionAsync(() =>
            controller.Search(
                "sample",
                LastHour with
                {
                    Filter = new ConditionNode("RequestPath", FilterOperator.StartsWith, null),
                },
                CancellationToken.None
            )
        );

        // Assert
        Assert.IsType<NotSupportedException>(thrown);
    }

    [Fact]
    public async Task Search_CancelledRequest_ThrowsOperationCanceled()
    {
        // Arrange
        var source = new FakeLogSource(
            new FakeLogSourceOptions { Alias = "sample", FixedNow = Now }
        );
        SearchController controller = CreateController(source);

        // Act
        Exception? thrown = await Record.ExceptionAsync(() =>
            controller.Search("sample", LastHour, new CancellationToken(canceled: true))
        );

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(thrown);
    }

    private static SearchController CreateController(FakeLogSource source)
    {
        ILogSourceRegistry registry = Substitute.For<ILogSourceRegistry>();
        registry.GetForUser(source.Alias, Editor).Returns(source);
        return CreateController(registry);
    }

    private static SearchController CreateController(ILogSourceRegistry registry)
    {
        IUserContextAccessor users = Substitute.For<IUserContextAccessor>();
        users.GetCurrent().Returns(Editor);
        return new SearchController(registry, users, new FakeTimeProvider(Now));
    }
}
