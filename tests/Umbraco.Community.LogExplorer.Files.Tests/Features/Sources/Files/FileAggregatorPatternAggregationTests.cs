using Umbraco.Community.LogExplorer.Core.Records;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Features.Sources.Files;
using Umbraco.Community.LogExplorer.Files.Tests.TestSupport;

namespace Umbraco.Community.LogExplorer.Files.Tests.Features.Sources.Files;

/// <summary>
/// Patterns group the matching entries by message template, highest count first, each with its
/// newest entry as the sample and a 30-bucket sparkline across the range (BRIEF §6.9, #32).
/// </summary>
public class FileAggregatorPatternAggregationTests
{
    private const string Slow = "Slow {Path}";
    private const string Started = "Started";

    [Fact]
    public void GetPatterns_TwoTemplates_GroupsAndCountsThemHighestFirst()
    {
        // Arrange
        using TempDirectory directory = WriteMixedTemplates();
        FileAggregator aggregator = Aggregators.Create(directory.Path);

        // Act
        PatternResult result = aggregator.GetPatterns(Aggregators.NoonHour(), 10, Token).Result;

        // Assert
        Assert.Equal(
            new[]
            {
                (TemplateHash.Compute(Slow), Slow, 3L),
                (TemplateHash.Compute(Started), Started, 1L),
            },
            result.Patterns.Select(pattern =>
                (pattern.TemplateHash, pattern.Template, pattern.Count)
            )
        );
    }

    [Fact]
    public void GetPatterns_SeveralEntriesInAPattern_UsesTheNewestAsTheSample()
    {
        // Arrange
        using TempDirectory directory = WriteMixedTemplates();
        FileAggregator aggregator = Aggregators.Create(directory.Path);

        // Act
        Pattern slow = aggregator.GetPatterns(Aggregators.NoonHour(), 10, Token).Result.Patterns[0];

        // Assert
        Assert.Equal("Slow \"/c\"", slow.Sample.Body);
    }

    [Fact]
    public void GetPatterns_EntriesAcrossTheRange_SpreadsThemOverThirtySparklineBuckets()
    {
        // Arrange
        using TempDirectory directory = WriteMixedTemplates();
        FileAggregator aggregator = Aggregators.Create(directory.Path);

        // Act
        Pattern slow = aggregator.GetPatterns(Aggregators.NoonHour(), 10, Token).Result.Patterns[0];

        // Assert: two-minute buckets; entries at minutes 1, 3 and 59.
        long[] expected = new long[30];
        expected[0] = 1;
        expected[1] = 1;
        expected[29] = 1;
        Assert.Equal(expected, slow.Sparkline);
    }

    [Fact]
    public void GetPatterns_MixedLevels_CountsEachLevel()
    {
        // Arrange
        using TempDirectory directory = WriteMixedTemplates();
        FileAggregator aggregator = Aggregators.Create(directory.Path);

        // Act
        Pattern slow = aggregator.GetPatterns(Aggregators.NoonHour(), 10, Token).Result.Patterns[0];

        // Assert
        Assert.Equal(
            new Dictionary<string, long>
            {
                ["trace"] = 0,
                ["debug"] = 0,
                ["info"] = 0,
                ["warn"] = 2,
                ["error"] = 1,
                ["fatal"] = 0,
            },
            slow.CountsBySeverityShortName
        );
    }

    [Fact]
    public void GetPatterns_TopOne_ReturnsOnlyTheMostFrequent()
    {
        // Arrange
        using TempDirectory directory = WriteMixedTemplates();
        FileAggregator aggregator = Aggregators.Create(directory.Path);

        // Act
        PatternResult result = aggregator.GetPatterns(Aggregators.NoonHour(), 1, Token).Result;

        // Assert
        Assert.Equal([Slow], result.Patterns.Select(pattern => pattern.Template));
    }

    [Fact]
    public void GetPatterns_QueryWithLevelSet_LeavesOutEntriesAtOtherLevels()
    {
        // Arrange
        using TempDirectory directory = WriteMixedTemplates();
        FileAggregator aggregator = Aggregators.Create(directory.Path);
        var query = Aggregators.NoonHour() with { Levels = new HashSet<string> { "info" } };

        // Act
        PatternResult result = aggregator.GetPatterns(query, 10, Token).Result;

        // Assert
        Assert.Equal([Started], result.Patterns.Select(pattern => pattern.Template));
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static TempDirectory WriteMixedTemplates()
    {
        var directory = new TempDirectory();
        directory.Write(
            Aggregators.DayFile,
            LogLines.File(
                LogLines.Event(Aggregators.Noon.AddMinutes(1), Slow, "Warning", "\"Path\":\"/a\""),
                LogLines.Event(Aggregators.Noon.AddMinutes(2), Started),
                LogLines.Event(Aggregators.Noon.AddMinutes(3), Slow, "Error", "\"Path\":\"/b\""),
                LogLines.Event(Aggregators.Noon.AddMinutes(59), Slow, "Warning", "\"Path\":\"/c\"")
            )
        );
        return directory;
    }
}
