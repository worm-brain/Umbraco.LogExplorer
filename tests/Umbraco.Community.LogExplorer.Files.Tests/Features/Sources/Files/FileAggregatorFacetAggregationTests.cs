using System.Globalization;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Features.Sources.Files;
using Umbraco.Community.LogExplorer.Files.Tests.TestSupport;

namespace Umbraco.Community.LogExplorer.Files.Tests.Features.Sources.Files;

/// <summary>
/// Facets report each field's presence among the matching entries and its top values with
/// counts, at most ten (BRIEF §6.6, #32).
/// </summary>
public class FileAggregatorFacetAggregationTests
{
    [Fact]
    public void GetFacets_FieldMissingFromSomeEntries_ReportsItsShareOfMatches()
    {
        // Arrange
        using TempDirectory directory = WritePaths("/a", "/a", "/b", null);
        FileAggregator aggregator = Aggregators.Create(directory.Path);

        // Act
        Facet facet = aggregator
            .GetFacets(Aggregators.NoonHour(), ["RequestPath"], 10, Token)
            .Result.Facets.Single();

        // Assert
        Assert.Equal(0.75, facet.PresenceRatio);
    }

    [Fact]
    public void GetFacets_RepeatedValues_ListsThemHighestCountFirst()
    {
        // Arrange
        using TempDirectory directory = WritePaths("/b", "/a", "/b", "/c", "/b", "/a");
        FileAggregator aggregator = Aggregators.Create(directory.Path);

        // Act
        Facet facet = aggregator
            .GetFacets(Aggregators.NoonHour(), ["RequestPath"], 10, Token)
            .Result.Facets.Single();

        // Assert
        Assert.Equal(
            new (string?, long)[] { ("/b", 3), ("/a", 2), ("/c", 1) },
            facet.TopValues.Select(value => (value.Value.GetString(), value.Count))
        );
    }

    [Fact]
    public void GetFacets_ValuesDifferingOnlyInCase_CountsThemSeparately()
    {
        // Arrange
        using TempDirectory directory = WritePaths("/a", "/A");
        FileAggregator aggregator = Aggregators.Create(directory.Path);

        // Act
        Facet facet = aggregator
            .GetFacets(Aggregators.NoonHour(), ["RequestPath"], 10, Token)
            .Result.Facets.Single();

        // Assert
        Assert.Equal(2, facet.TopValues.Count);
    }

    [Fact]
    public void GetFacets_TopAboveTen_ReturnsAtMostTenValues()
    {
        // Arrange
        string[] paths =
        [
            .. Enumerable.Range(1, 12).Select(i => "/" + i.ToString(CultureInfo.InvariantCulture)),
        ];
        using TempDirectory directory = WritePaths(paths);
        FileAggregator aggregator = Aggregators.Create(directory.Path);

        // Act
        Facet facet = aggregator
            .GetFacets(Aggregators.NoonHour(), ["RequestPath"], 50, Token)
            .Result.Facets.Single();

        // Assert
        Assert.Equal(10, facet.TopValues.Count);
    }

    [Fact]
    public void GetFacets_QueryWithLevelSet_CountsOnlyEntriesAtThoseLevels()
    {
        // Arrange
        using var directory = new TempDirectory();
        directory.Write(
            Aggregators.DayFile,
            LogLines.File(
                LogLines.Event(
                    Aggregators.Noon.AddMinutes(1),
                    "Hit",
                    null,
                    "\"RequestPath\":\"/a\""
                ),
                LogLines.Event(
                    Aggregators.Noon.AddMinutes(2),
                    "Hit",
                    "Error",
                    "\"RequestPath\":\"/b\""
                )
            )
        );
        FileAggregator aggregator = Aggregators.Create(directory.Path);
        var query = Aggregators.NoonHour() with { Levels = new HashSet<string> { "error" } };

        // Act
        Facet facet = aggregator
            .GetFacets(query, ["RequestPath"], 10, Token)
            .Result.Facets.Single();

        // Assert
        Assert.Equal(["/b"], facet.TopValues.Select(value => value.Value.GetString()));
    }

    [Fact]
    public void GetFacets_SeveralFields_ReturnsOneFacetPerFieldInRequestOrder()
    {
        // Arrange
        using TempDirectory directory = WritePaths("/a");
        FileAggregator aggregator = Aggregators.Create(directory.Path);

        // Act
        FacetResult result = aggregator
            .GetFacets(Aggregators.NoonHour(), ["StatusCode", "RequestPath"], 10, Token)
            .Result;

        // Assert
        Assert.Equal(["StatusCode", "RequestPath"], result.Facets.Select(facet => facet.Field));
    }

    [Fact]
    public void GetFacets_TopBelowOne_Throws()
    {
        // Arrange
        using TempDirectory directory = WritePaths("/a");
        FileAggregator aggregator = Aggregators.Create(directory.Path);

        // Act
        void Facets() => aggregator.GetFacets(Aggregators.NoonHour(), ["RequestPath"], 0, Token);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(Facets);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // One entry per path a minute apart from noon; a null path writes an entry without the field.
    private static TempDirectory WritePaths(params string?[] paths)
    {
        var directory = new TempDirectory();
        directory.Write(
            Aggregators.DayFile,
            LogLines.File([
                .. paths.Select(
                    (path, i) =>
                        LogLines.Event(
                            Aggregators.Noon.AddMinutes(i),
                            "Hit",
                            extraProperties: path is null ? null : $"\"RequestPath\":\"{path}\""
                        )
                ),
            ])
        );
        return directory;
    }
}
