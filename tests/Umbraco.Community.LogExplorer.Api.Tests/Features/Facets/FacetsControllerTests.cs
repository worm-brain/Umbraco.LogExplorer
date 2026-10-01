using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Umbraco.Community.LogExplorer.Core.Fake;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Core.Sources;
using Umbraco.Community.LogExplorer.Features.Facets;
using Umbraco.Community.LogExplorer.Infrastructure.Api;

namespace Umbraco.Community.LogExplorer.Api.Tests.Features.Facets;

/// <summary>
/// <c>POST /sources/{alias}/facets</c> counts field values on the user's source and fails with the
/// exceptions the problem filter maps to BRIEF §11.1 codes (#86). Source resolution failures are
/// the registry's and are covered with the search endpoint.
/// </summary>
public class FacetsControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly UserContext Editor = new(
        Guid.NewGuid(),
        new HashSet<string>(["editor"], StringComparer.OrdinalIgnoreCase)
    );

    private static readonly LogQuery LastHour = new() { Range = new TimeRange(null, null, "1h") };

    [Fact]
    public async Task GetFacets_SampleSourceLastHour_ReturnsFacetsInRequestedOrder()
    {
        // Arrange
        FacetsController controller = CreateController(CreateSource());

        // Act
        FacetResult result = await controller.GetFacets(
            "sample",
            new FacetsRequest(LastHour, ["MachineName", "SourceContext"], 5),
            CancellationToken.None
        );

        // Assert
        Assert.Equal(
            ["MachineName", "SourceContext"],
            result.Facets.Select(facet => facet.Field).ToArray()
        );
    }

    [Fact]
    public async Task GetFacets_WithExcludeFilter_DropsTheExcludedValue()
    {
        // Arrange
        FacetsController controller = CreateController(CreateSource());
        LogQuery withoutOneMachine = LastHour with
        {
            Filter = new NotNode(
                new ConditionNode(
                    "MachineName",
                    FilterOperator.Equals,
                    JsonSerializer.SerializeToElement("wn1xsdwk000EJK")
                )
            ),
        };

        // Act
        FacetResult result = await controller.GetFacets(
            "sample",
            new FacetsRequest(withoutOneMachine, ["MachineName"], 5),
            CancellationToken.None
        );

        // Assert
        Assert.DoesNotContain(
            result.Facets[0].TopValues,
            value => value.Value.GetString() == "wn1xsdwk000EJK"
        );
    }

    [Fact]
    public async Task GetFacets_SourceWithoutFacets_ThrowsNotSupported()
    {
        // Arrange
        FacetsController controller = CreateController(
            CreateSource(LogSourceFeatures.Histogram | LogSourceFeatures.FieldDiscovery)
        );

        // Act
        Exception? thrown = await Record.ExceptionAsync(() =>
            controller.GetFacets(
                "sample",
                new FacetsRequest(LastHour, ["MachineName"], 5),
                CancellationToken.None
            )
        );

        // Assert
        Assert.IsType<NotSupportedException>(thrown);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(FacetsController.MaxTop + 1)]
    public async Task GetFacets_TopOutOfRange_ThrowsArgumentException(int top)
    {
        // Arrange
        FacetsController controller = CreateController(CreateSource());

        // Act
        Exception? thrown = await Record.ExceptionAsync(() =>
            controller.GetFacets(
                "sample",
                new FacetsRequest(LastHour, ["MachineName"], top),
                CancellationToken.None
            )
        );

        // Assert
        Assert.IsAssignableFrom<ArgumentException>(thrown);
    }

    public static TheoryData<string, string[]> InvalidFieldLists() =>
        new()
        {
            {
                "too many fields",
                Enumerable.Range(0, FacetsController.MaxFields + 1).Select(i => $"F{i}").ToArray()
            },
            { "blank field", ["MachineName", " "] },
            { "over-long field", [new string('x', FacetsController.MaxFieldLength + 1)] },
        };

    [Theory]
    [MemberData(nameof(InvalidFieldLists))]
    public async Task GetFacets_InvalidFieldList_ThrowsArgumentException(
        string scenario,
        string[] fields
    )
    {
        // Arrange
        _ = scenario;
        FacetsController controller = CreateController(CreateSource());

        // Act
        Exception? thrown = await Record.ExceptionAsync(() =>
            controller.GetFacets(
                "sample",
                new FacetsRequest(LastHour, fields, 5),
                CancellationToken.None
            )
        );

        // Assert
        Assert.IsAssignableFrom<ArgumentException>(thrown);
    }

    [Fact]
    public async Task GetFacets_RangeLongerThanTheSourceAllows_ThrowsRangeTooLarge()
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
        FacetsController controller = CreateController(source);

        // Act
        Exception? thrown = await Record.ExceptionAsync(() =>
            controller.GetFacets(
                "sample",
                new FacetsRequest(
                    LastHour with
                    {
                        Range = new TimeRange(null, null, "24h"),
                    },
                    ["MachineName"],
                    5
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

    private static FacetsController CreateController(FakeLogSource source)
    {
        ILogSourceRegistry registry = Substitute.For<ILogSourceRegistry>();
        registry.GetForUser(source.Alias, Editor).Returns(source);
        IUserContextAccessor users = Substitute.For<IUserContextAccessor>();
        users.GetCurrent().Returns(Editor);
        return new FacetsController(registry, users, new FakeTimeProvider(Now));
    }
}
