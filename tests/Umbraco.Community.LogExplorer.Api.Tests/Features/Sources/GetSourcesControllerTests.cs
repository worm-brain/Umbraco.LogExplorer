using NSubstitute;
using Umbraco.Community.LogExplorer.Core.Fake;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Sources;
using Umbraco.Community.LogExplorer.Features.Sources;
using Umbraco.Community.LogExplorer.Infrastructure.Api;

namespace Umbraco.Community.LogExplorer.Api.Tests.Features.Sources;

/// <summary>
/// <c>GET /sources</c> returns only the visible sources, with identity and capabilities in the
/// client-friendly shape (#27).
/// </summary>
public class GetSourcesControllerTests
{
    private static readonly UserContext Editor = new(
        Guid.NewGuid(),
        new HashSet<string>(["editor"], StringComparer.OrdinalIgnoreCase)
    );

    [Fact]
    public void GetSources_VisibleSource_ReturnsItsIdentity()
    {
        // Arrange
        GetSourcesController controller = CreateController(
            new FakeLogSource(
                new FakeLogSourceOptions { Alias = "sample", DisplayName = "Sample data" }
            )
        );

        // Act
        SourceResponseModel source = Assert.Single(controller.GetSources());

        // Assert
        Assert.Equal(
            ("sample", "Sample data", "Fake", false),
            (source.Alias, source.DisplayName, source.Type, source.Sensitive)
        );
    }

    [Fact]
    public void GetSources_SourceDeclaringNativeQuery_AllowsNativeQueries()
    {
        // Arrange
        GetSourcesController controller = CreateController(
            new FakeLogSource(new FakeLogSourceOptions { Alias = "sample" })
        );

        // Act
        SourceResponseModel source = Assert.Single(controller.GetSources());

        // Assert
        Assert.True(source.AllowNativeQuery);
    }

    [Fact]
    public void GetSources_SourceConfiguredWithoutNativeQueries_DoesNotAllowThem()
    {
        // Arrange
        GetSourcesController controller = CreateController(
            new NativeQueryDisabledSource(
                new FakeLogSource(new FakeLogSourceOptions { Alias = "sample" })
            )
        );

        // Act
        SourceResponseModel source = Assert.Single(controller.GetSources());

        // Assert
        Assert.False(source.AllowNativeQuery);
    }

    [Fact]
    public void GetSources_SourceWithoutTheNativeQueryFeature_DoesNotAllowThem()
    {
        // Arrange
        GetSourcesController controller = CreateController(
            new FakeLogSource(
                new FakeLogSourceOptions
                {
                    Alias = "sample",
                    Features = LogSourceFeatures.Histogram,
                }
            )
        );

        // Act
        SourceResponseModel source = Assert.Single(controller.GetSources());

        // Assert
        Assert.False(source.AllowNativeQuery);
    }

    [Fact]
    public void GetSources_NoVisibleSources_ReturnsAnEmptyList()
    {
        // Arrange
        GetSourcesController controller = CreateController();

        // Act
        IReadOnlyList<SourceResponseModel> sources = controller.GetSources();

        // Assert
        Assert.Empty(sources);
    }

    [Fact]
    public void From_Capabilities_ListsDeclaredFeaturesAsCamelCaseNames()
    {
        // Arrange
        var capabilities = new LogSourceCapabilities(
            LogSourceFeatures.Histogram | LogSourceFeatures.FieldDiscovery,
            new HashSet<FilterOperator>([FilterOperator.StartsWith, FilterOperator.Equals]),
            "Sample",
            TimeSpan.FromDays(30),
            500
        );

        // Act
        SourceCapabilitiesResponseModel model = SourceCapabilitiesResponseModel.From(capabilities);

        // Assert
        Assert.Equal(["histogram", "fieldDiscovery"], model.Features);
    }

    [Fact]
    public void From_Capabilities_ListsOperatorsAsCamelCaseNamesInEnumOrder()
    {
        // Arrange
        var capabilities = new LogSourceCapabilities(
            LogSourceFeatures.None,
            new HashSet<FilterOperator>([FilterOperator.StartsWith, FilterOperator.Equals]),
            null,
            null,
            100
        );

        // Act
        SourceCapabilitiesResponseModel model = SourceCapabilitiesResponseModel.From(capabilities);

        // Assert
        Assert.Equal(["equals", "startsWith"], model.Operators);
    }

    [Fact]
    public void From_MaxRange_ConvertsToSeconds()
    {
        // Arrange
        var capabilities = new LogSourceCapabilities(
            LogSourceFeatures.None,
            new HashSet<FilterOperator>(),
            null,
            TimeSpan.FromDays(30),
            100
        );

        // Act
        SourceCapabilitiesResponseModel model = SourceCapabilitiesResponseModel.From(capabilities);

        // Assert
        Assert.Equal(2_592_000, model.MaxRangeSeconds);
    }

    private static GetSourcesController CreateController(params ILogSource[] visible)
    {
        ILogSourceRegistry registry = Substitute.For<ILogSourceRegistry>();
        registry.GetVisibleSources(Editor).Returns(visible);
        IUserContextAccessor users = Substitute.For<IUserContextAccessor>();
        users.GetCurrent().Returns(Editor);
        return new GetSourcesController(registry, users);
    }
}
