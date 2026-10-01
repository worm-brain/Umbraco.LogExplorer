using NSubstitute;
using Umbraco.Community.LogExplorer.Core.Fake;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Core.Sources;
using Umbraco.Community.LogExplorer.Features.Context;
using Umbraco.Community.LogExplorer.Infrastructure.Api;

namespace Umbraco.Community.LogExplorer.Api.Tests.Features.Context;

/// <summary>
/// <c>GET /sources/{alias}/records/{id}/context</c> returns the entries either side of a record and
/// keeps an unknown record apart from an unknown source (#42).
/// </summary>
public class GetContextControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly UserContext Editor = new(
        Guid.NewGuid(),
        new HashSet<string>(["editor"], StringComparer.OrdinalIgnoreCase)
    );

    [Fact]
    public async Task GetContext_DefaultCounts_ReturnsSevenEntriesEitherSideOfTheAnchor()
    {
        // Arrange
        FakeLogSource source = CreateSource();
        GetContextController controller = CreateController(source);
        string anchorId = source.Records[20].Id;

        // Act
        ContextResult result = await controller.GetContext(
            "sample",
            anchorId,
            CancellationToken.None
        );

        // Assert
        Assert.Multiple(
            () =>
                Assert.Equal(
                    source.Records.Skip(13).Take(7).Select(record => record.Id),
                    result.Before.Select(record => record.Id)
                ),
            () => Assert.Equal(anchorId, result.Anchor.Id),
            () =>
                Assert.Equal(
                    source.Records.Skip(21).Take(7).Select(record => record.Id),
                    result.After.Select(record => record.Id)
                )
        );
    }

    [Fact]
    public async Task GetContext_ZeroCounts_ReturnsOnlyTheAnchor()
    {
        // Arrange
        FakeLogSource source = CreateSource();
        GetContextController controller = CreateController(source);

        // Act
        ContextResult result = await controller.GetContext(
            "sample",
            source.Records[20].Id,
            CancellationToken.None,
            before: 0,
            after: 0
        );

        // Assert
        Assert.Equal((0, 0), (result.Before.Count, result.After.Count));
    }

    [Fact]
    public async Task GetContext_UnknownRecord_ThrowsRecordNotFound()
    {
        // Arrange
        GetContextController controller = CreateController(CreateSource());

        // Act
        Exception? thrown = await Record.ExceptionAsync(() =>
            controller.GetContext("sample", "no-such-record", CancellationToken.None)
        );

        // Assert
        Assert.IsType<RecordNotFoundException>(thrown);
    }

    [Fact]
    public async Task GetContext_UnknownSource_KeepsTheRegistrysKeyNotFound()
    {
        // Arrange
        ILogSourceRegistry registry = Substitute.For<ILogSourceRegistry>();
        registry
            .GetForUser("missing", Editor)
            .Returns(_ => throw new KeyNotFoundException("No log source has the alias 'missing'."));
        var controller = new GetContextController(registry, Users());

        // Act
        Exception? thrown = await Record.ExceptionAsync(() =>
            controller.GetContext("missing", "e1", CancellationToken.None)
        );

        // Assert
        Assert.IsType<KeyNotFoundException>(thrown);
    }

    [Theory]
    [InlineData(-1, 7)]
    [InlineData(7, -1)]
    [InlineData(GetContextController.MaxCount + 1, 7)]
    [InlineData(7, GetContextController.MaxCount + 1)]
    public async Task GetContext_CountOutOfRange_ThrowsArgumentException(int before, int after)
    {
        // Arrange
        FakeLogSource source = CreateSource();
        GetContextController controller = CreateController(source);

        // Act
        Exception? thrown = await Record.ExceptionAsync(() =>
            controller.GetContext(
                "sample",
                source.Records[20].Id,
                CancellationToken.None,
                before,
                after
            )
        );

        // Assert
        Assert.IsAssignableFrom<ArgumentException>(thrown);
    }

    [Fact]
    public async Task GetContext_SourceWithoutContext_ThrowsNotSupported()
    {
        // Arrange
        FakeLogSource source = CreateSource(LogSourceFeatures.Facets);
        GetContextController controller = CreateController(source);

        // Act
        Exception? thrown = await Record.ExceptionAsync(() =>
            controller.GetContext("sample", source.Records[20].Id, CancellationToken.None)
        );

        // Assert
        Assert.IsType<NotSupportedException>(thrown);
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

    private static IUserContextAccessor Users()
    {
        IUserContextAccessor users = Substitute.For<IUserContextAccessor>();
        users.GetCurrent().Returns(Editor);
        return users;
    }

    private static GetContextController CreateController(FakeLogSource source)
    {
        ILogSourceRegistry registry = Substitute.For<ILogSourceRegistry>();
        registry.GetForUser(source.Alias, Editor).Returns(source);
        return new GetContextController(registry, Users());
    }
}
