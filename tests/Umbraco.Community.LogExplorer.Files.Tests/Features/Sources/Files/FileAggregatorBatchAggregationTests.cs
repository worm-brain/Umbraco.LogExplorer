using System.Globalization;
using System.Text;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Features.Sources.Files;
using Umbraco.Community.LogExplorer.Files.Tests.TestSupport;

namespace Umbraco.Community.LogExplorer.Files.Tests.Features.Sources.Files;

/// <summary>
/// Scans evaluate batches of events across cores (ADR 0026); results over many batches must be a
/// one-by-one scan's: every match counted once, newest-first choices kept, and the budget cut at
/// the same byte. Each test runs with and without parallel scanning.
/// </summary>
public class FileAggregatorBatchAggregationTests
{
    // Several of the aggregator's 1,024-event batches.
    private const int Entries = 5_000;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetFacets_FilterOverManyBatches_CountsEveryMatchOnce(bool parallelScan)
    {
        // Arrange: "Even" entries carry N = i % 7.
        using TempDirectory directory = WriteEntries();
        FileAggregator aggregator = Aggregators.Create(directory.Path, parallelScan: parallelScan);
        LogQuery query = Query() with { Filter = new TextNode("Even") };
        long expected = Enumerable.Range(0, Entries).Count(i => i % 2 == 0 && i % 7 == 0);

        // Act
        FacetValue zero = aggregator
            .GetFacets(query, ["N"], 10, Token)
            .Result.Facets[0]
            .TopValues.Single(value => value.Value.GetInt32() == 0);

        // Assert
        Assert.Equal(expected, zero.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetHistogram_FilterOverManyBatches_CountsEveryMatch(bool parallelScan)
    {
        // Arrange
        using TempDirectory directory = WriteEntries();
        FileAggregator aggregator = Aggregators.Create(directory.Path, parallelScan: parallelScan);
        LogQuery query = Query() with { Filter = new TextNode("Even") };

        // Act
        long total = aggregator
            .GetHistogram(query, 60, Token)
            .Result.Buckets.Sum(bucket => bucket.CountsBySeverityShortName.Values.Sum());

        // Assert
        Assert.Equal(Entries / 2, total);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetFields_KindDiffersInTheNewestEntry_KeepsTheNewestKind(bool parallelScan)
    {
        // Arrange: X is a string everywhere except in the newest entry, where it is a number.
        using TempDirectory directory = WriteEntries(newestX: "1");
        FileAggregator aggregator = Aggregators.Create(directory.Path, parallelScan: parallelScan);

        // Act
        FieldInfo x = aggregator
            .GetFields(Query(), Token)
            .Result.Single(field => field.Path == "X");

        // Assert
        Assert.Equal(("number", 1.0), (x.Kind, x.PresenceRatio));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetPatterns_FilterOverManyBatches_SamplesTheNewestMatch(bool parallelScan)
    {
        // Arrange
        using TempDirectory directory = WriteEntries();
        FileAggregator aggregator = Aggregators.Create(directory.Path, parallelScan: parallelScan);
        LogQuery query = Query() with { Filter = new TextNode("Odd") };

        // Act
        Pattern odd = Assert.Single(aggregator.GetPatterns(query, 10, Token).Result.Patterns);

        // Assert: the last odd entry written is the newest.
        Assert.Equal(Aggregators.Noon.AddSeconds(Entries - 1), odd.Sample.Timestamp);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetFacets_RegexFilterThatDoesNotParse_ThrowsArgumentExceptionUnwrapped(
        bool parallelScan
    )
    {
        // Arrange
        using TempDirectory directory = WriteEntries();
        FileAggregator aggregator = Aggregators.Create(directory.Path, parallelScan: parallelScan);
        LogQuery query = Query() with
        {
            Filter = new ConditionNode(
                "X",
                FilterOperator.Matches,
                System.Text.Json.JsonSerializer.SerializeToElement("(")
            ),
        };

        // Act
        void Facets() => aggregator.GetFacets(query, ["N"], 10, Token);

        // Assert: the type a one-by-one scan throws, which the API maps to invalid_query.
        Assert.ThrowsAny<ArgumentException>(Facets);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetFacets_BudgetHitWithAFilter_StopsAtTheDefaultBlockBoundary(bool parallelScan)
    {
        // Arrange: about 1.7 MB of entries against the smallest budget, 1 MB.
        using TempDirectory directory = WriteEntries(count: 12_000);
        FileAggregator aggregator = Aggregators.Create(
            directory.Path,
            scanBudgetMegabytes: 1,
            parallelScan: parallelScan
        );
        LogQuery query = Query(12_000) with { Filter = new TextNode("Even") };

        // Act
        long bytesRead = aggregator.GetFacets(query, ["N"], 10, Token).BytesRead;

        // Assert: sixteen 64 KB blocks, as a one-by-one reader reads them.
        Assert.Equal(1024L * 1024, bytesRead);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static LogQuery Query(int count = Entries) =>
        new() { Range = new TimeRange(Aggregators.Noon, Aggregators.Noon.AddSeconds(count), null) };

    // One entry a second from noon, alternating "Even {N}" and "Odd {N}", each with X as a string
    // unless newestX replaces the newest entry's X.
    private static TempDirectory WriteEntries(int count = Entries, string? newestX = null)
    {
        var directory = new TempDirectory();
        var content = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            string x = i == count - 1 && newestX is not null ? newestX : "\"text\"";
            content
                .Append(
                    LogLines.Event(
                        Aggregators.Noon.AddSeconds(i),
                        i % 2 == 0
                            ? "Even {N} padded to make the line about a hundred bytes long"
                            : "Odd {N} padded to make the line about a hundred bytes long",
                        extraProperties: string.Create(
                            CultureInfo.InvariantCulture,
                            $"\"N\":{i % 7},\"X\":{x}"
                        )
                    )
                )
                .Append('\n');
        }

        directory.Write(Aggregators.DayFile, Encoding.UTF8.GetBytes(content.ToString()));
        return directory;
    }
}
