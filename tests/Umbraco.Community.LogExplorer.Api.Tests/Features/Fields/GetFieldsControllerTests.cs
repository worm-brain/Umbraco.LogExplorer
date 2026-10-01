using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Umbraco.Community.LogExplorer.Core.Fake;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Core.Sources;
using Umbraco.Community.LogExplorer.Features.Fields;
using Umbraco.Community.LogExplorer.Infrastructure.Api;

namespace Umbraco.Community.LogExplorer.Api.Tests.Features.Fields;

/// <summary>
/// <c>GET /sources/{alias}/fields</c> discovers the fields in a time range on the user's source
/// (#86). Source resolution failures are the registry's and are covered with the search endpoint.
/// </summary>
public class GetFieldsControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly UserContext Editor = new(
        Guid.NewGuid(),
        new HashSet<string>(["editor"], StringComparer.OrdinalIgnoreCase)
    );

    [Fact]
    public async Task GetFields_SampleSourceLastHour_IncludesAttributeFields()
    {
        // Arrange
        GetFieldsController controller = CreateController(CreateSource());

        // Act
        IReadOnlyList<FieldInfo> fields = await controller.GetFields(
            "sample",
            null,
            null,
            "1h",
            CancellationToken.None
        );

        // Assert
        Assert.Contains(fields, field => field.Path == "RequestPath");
    }

    [Fact]
    public async Task GetFields_AbsoluteRangeBeforeAnyEntry_ReturnsNoAttributeFields()
    {
        // Arrange
        GetFieldsController controller = CreateController(CreateSource());

        // Act
        IReadOnlyList<FieldInfo> fields = await controller.GetFields(
            "sample",
            Now.AddDays(-3),
            Now.AddDays(-2),
            null,
            CancellationToken.None
        );

        // Assert
        Assert.DoesNotContain(fields, field => field.Path == "RequestPath");
    }

    [Fact]
    public async Task GetFields_NoRange_ThrowsArgumentException()
    {
        // Arrange
        GetFieldsController controller = CreateController(CreateSource());

        // Act
        Exception? thrown = await Record.ExceptionAsync(() =>
            controller.GetFields("sample", null, null, null, CancellationToken.None)
        );

        // Assert
        Assert.IsAssignableFrom<ArgumentException>(thrown);
    }

    [Fact]
    public async Task GetFields_SourceWithoutFieldDiscovery_ThrowsNotSupported()
    {
        // Arrange
        GetFieldsController controller = CreateController(
            CreateSource(LogSourceFeatures.Facets | LogSourceFeatures.Histogram)
        );

        // Act
        Exception? thrown = await Record.ExceptionAsync(() =>
            controller.GetFields("sample", null, null, "1h", CancellationToken.None)
        );

        // Assert
        Assert.IsType<NotSupportedException>(thrown);
    }

    [Fact]
    public async Task GetFields_RangeLongerThanTheSourceAllows_ThrowsRangeTooLarge()
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
        GetFieldsController controller = CreateController(source);

        // Act
        Exception? thrown = await Record.ExceptionAsync(() =>
            controller.GetFields("sample", null, null, "24h", CancellationToken.None)
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

    private static GetFieldsController CreateController(FakeLogSource source)
    {
        ILogSourceRegistry registry = Substitute.For<ILogSourceRegistry>();
        registry.GetForUser(source.Alias, Editor).Returns(source);
        IUserContextAccessor users = Substitute.For<IUserContextAccessor>();
        users.GetCurrent().Returns(Editor);
        return new GetFieldsController(registry, users, new FakeTimeProvider(Now));
    }
}
