using System.Text.Json;
using Umbraco.Community.LogExplorer.Core.Fake;
using Umbraco.Community.LogExplorer.Core.Json;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Records;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Core.Sources;
using Umbraco.Community.LogExplorer.Core.Tests.TestSupport;

namespace Umbraco.Community.LogExplorer.Core.Tests.Fake;

/// <summary>
/// Expected numbers are counted by hand from the prototype's <c>data()</c> generator
/// (UI brief §12): per minute one /healtz request, one anonymous-user warning and one background
/// job; MSAL pairs every 5 minutes; 404s when minute % 7 == 3; the SQL incident in minutes 41-44;
/// one publish failure in minute 43.
/// </summary>
public class FakeLogSourceTests
{
    private static readonly DateTimeOffset Now = FixedClock.Noon;
    private static readonly LogQuery LastHour = new() { Range = new TimeRange(null, null, "1h") };

    private static FakeLogSource CreateSource(FakeLogSourceOptions? options = null) =>
        new((options ?? new FakeLogSourceOptions()) with { FixedNow = Now });

    [Fact]
    public void Records_SampleHour_Has238Entries()
    {
        // Act
        int count = CreateSource().Records.Count;

        // Assert
        Assert.Equal(238, count);
    }

    [Fact]
    public void Records_SampleHour_AllFallInTheHourEndingNow()
    {
        // Act
        IReadOnlyList<LogRecord> records = CreateSource().Records;

        // Assert
        Assert.All(
            records,
            record => Assert.InRange(record.Timestamp, Now.AddHours(-1), Now.AddTicks(-1))
        );
    }

    [Fact]
    public void Records_SampleHour_HasPrototypeLevelMix()
    {
        // Act
        var levels = CreateSource()
            .Records.GroupBy(record => record.SeverityText)
            .ToDictionary(group => group.Key!, group => group.Count());

        // Assert
        Assert.Equal(
            new Dictionary<string, int>
            {
                ["Information"] = 161,
                ["Warning"] = 68,
                ["Error"] = 9,
            },
            levels
        );
    }

    [Fact]
    public void Records_FirstEntry_IsRenderedLikeSerilog()
    {
        // Act
        LogRecord first = CreateSource().Records[0];

        // Assert
        Assert.Equal(
            (
                Now.AddHours(-1).AddSeconds(9).AddMilliseconds(112),
                "HTTP \"GET\" \"/healtz\" responded 200 in 4 ms"
            ),
            (first.Timestamp, first.Body)
        );
    }

    [Fact]
    public void Records_SameClock_AreIdentical()
    {
        // Act
        string first = JsonSerializer.Serialize(CreateSource().Records, LogJson.Options);
        string second = JsonSerializer.Serialize(CreateSource().Records, LogJson.Options);

        // Assert
        Assert.Equal(first, second);
    }

    [Fact]
    public void Records_ThreeSampleHours_HasThreeHoursOfEntriesWithUniqueIds()
    {
        // Act
        IReadOnlyList<LogRecord> records = CreateSource(
            new FakeLogSourceOptions { SampleHours = 3 }
        ).Records;

        // Assert
        Assert.Equal(
            (238 * 3, 238 * 3),
            (records.Count, records.Select(record => record.Id).Distinct().Count())
        );
    }

    [Fact]
    public void Records_ThreeSampleHours_StartThreeHoursAgoInTimestampOrder()
    {
        // Act
        IReadOnlyList<LogRecord> records = CreateSource(
            new FakeLogSourceOptions { SampleHours = 3 }
        ).Records;

        // Assert
        Assert.Equal(
            (true, true),
            (
                records[0].Timestamp >= Now.AddHours(-3) && records[0].Timestamp < Now.AddHours(-2),
                records
                    .Zip(records.Skip(1))
                    .All(pair => pair.First.Timestamp <= pair.Second.Timestamp)
            )
        );
    }

    [Fact]
    public void Constructor_ZeroSampleHours_Throws()
    {
        // Act
        Exception? thrown = Record.Exception(() =>
            CreateSource(new FakeLogSourceOptions { SampleHours = 0 })
        );

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(thrown);
    }

    [Fact]
    public void Records_InjectedClock_EndsTheHourAtItsNow()
    {
        // Arrange
        DateTimeOffset now = Now.AddDays(3);

        // Act
        var source = new FakeLogSource(clock: new FixedClock(now));

        // Assert
        Assert.InRange(source.Records[^1].Timestamp, now.AddMinutes(-1), now);
    }

    [Fact]
    public async Task QueryAsync_LastFifteenMinutes_ReturnsOnlyThatWindow()
    {
        // Arrange
        LogQuery query = LastHour with
        {
            Range = new TimeRange(null, null, "15m"),
        };

        // Act
        LogPage page = await CreateSource().QueryAsync(query, CancellationToken.None);

        // Assert: minutes 45-59 hold 45 base entries, 3 MSAL pairs and 3 404s (45, 52, 59).
        Assert.Equal(54, page.TotalCount);
    }

    [Fact]
    public async Task QueryAsync_FirstPage_ReturnsNewestFirstWithACursor()
    {
        // Act
        LogPage page = await CreateSource()
            .QueryAsync(LastHour with { Take = 10 }, CancellationToken.None);

        // Assert
        Assert.Equal(
            (10, true, true),
            (
                page.Records.Count,
                page.NextCursor is not null,
                page.Records.SequenceEqual(page.Records.OrderByDescending(r => r.Timestamp))
            )
        );
    }

    [Fact]
    public async Task QueryAsync_LevelSet_ReturnsOnlyThoseLevels()
    {
        // Arrange
        LogQuery query = LastHour with
        {
            Levels = new HashSet<string> { "error" },
        };

        // Act
        LogPage page = await CreateSource().QueryAsync(query, CancellationToken.None);

        // Assert
        Assert.Equal(9, page.TotalCount);
    }

    [Fact]
    public async Task QueryAsync_SameRequestFilter_ReturnsTheWholeRequest()
    {
        // Arrange
        LogQuery query = LastHour with
        {
            Filter = new ConditionNode(
                "RequestId",
                FilterOperator.Equals,
                JsonSerializer.SerializeToElement("0HNFKQ41C4:00000003")
            ),
        };

        // Act
        LogPage page = await CreateSource().QueryAsync(query, CancellationToken.None);

        // Assert: request starting, the unhandled SQL exception, then the slow-request warning.
        Assert.Equal(
            ["Information", "Error", "Warning"],
            page.Records.Reverse().Select(record => record.SeverityText)
        );
    }

    [Fact]
    public async Task QueryAsync_UndeclaredOperator_ThrowsNotSupportedException()
    {
        // Arrange
        FakeLogSource source = CreateSource(
            new FakeLogSourceOptions
            {
                Operators = new HashSet<FilterOperator> { FilterOperator.Equals },
            }
        );
        LogQuery query = LastHour with
        {
            Filter = new ConditionNode(
                "RequestPath",
                FilterOperator.StartsWith,
                JsonSerializer.SerializeToElement("/")
            ),
        };

        // Act
        Task Act() => source.QueryAsync(query, CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<NotSupportedException>(Act);
    }

    [Fact]
    public async Task QueryAsync_MalformedCursor_ThrowsArgumentException()
    {
        // Act
        Task Act() =>
            CreateSource()
                .QueryAsync(LastHour with { Cursor = "not-a-cursor" }, CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(Act);
    }

    [Fact]
    public async Task GetHistogramAsync_LastHour_HasSixtyOneMinuteBuckets()
    {
        // Act
        HistogramResult histogram = await CreateSource()
            .GetHistogramAsync(LastHour, 60, CancellationToken.None);

        // Assert
        Assert.Equal(
            (60, TimeSpan.FromMinutes(1)),
            (histogram.Buckets.Count, histogram.BucketSize)
        );
    }

    [Fact]
    public async Task GetHistogramAsync_WithLevelSet_StillCountsEveryLevel()
    {
        // Arrange
        LogQuery query = LastHour with
        {
            Levels = new HashSet<string> { "error" },
        };

        // Act
        HistogramResult histogram = await CreateSource()
            .GetHistogramAsync(query, 60, CancellationToken.None);

        // Assert
        Assert.Equal(
            238,
            histogram.Buckets.Sum(bucket => bucket.CountsBySeverityShortName.Values.Sum())
        );
    }

    [Fact]
    public async Task GetFacetsAsync_RequestPath_ReturnsTopValuesAndPresence()
    {
        // Act
        FacetResult result = await CreateSource()
            .GetFacetsAsync(LastHour, ["RequestPath"], 2, CancellationToken.None);

        // Assert: 60 /healtz + 24 contact posts + 9 404s carry RequestPath, out of 238.
        Facet facet = Assert.Single(result.Facets);
        Assert.Equal(
            (93 / 238.0, "/healtz", 60L, "/umbraco/surface/contact/submit", 24L),
            (
                facet.PresenceRatio,
                facet.TopValues[0].Value.GetString(),
                facet.TopValues[0].Count,
                facet.TopValues[1].Value.GetString(),
                facet.TopValues[1].Count
            )
        );
    }

    [Fact]
    public async Task GetFacetsAsync_ExceptionType_CountsThePortableField()
    {
        // Act
        FacetResult result = await CreateSource()
            .GetFacetsAsync(LastHour, ["@exception.type"], 10, CancellationToken.None);

        // Assert
        Assert.Equal(
            [
                ("Microsoft.Data.SqlClient.SqlException", 8L),
                ("System.InvalidOperationException", 1L),
            ],
            result.Facets[0].TopValues.Select(value => (value.Value.GetString(), value.Count))
        );
    }

    [Fact]
    public async Task GetPatternsAsync_LastHour_PutsTheHttpTemplateFirst()
    {
        // Act
        PatternResult result = await CreateSource()
            .GetPatternsAsync(LastHour, 3, CancellationToken.None);

        // Assert: 60 /healtz requests plus 9 404s share the HTTP template.
        Pattern top = result.Patterns[0];
        Assert.Equal(
            (
                "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed} ms",
                69L,
                30,
                69L
            ),
            (top.Template, top.Count, top.Sparkline.Count, top.Sparkline.Sum())
        );
    }

    [Fact]
    public async Task GetContextAsync_MiddleRecord_ReturnsNeighboursInTimeOrder()
    {
        // Arrange
        FakeLogSource source = CreateSource();
        LogRecord anchor = source.Records[100];

        // Act
        ContextResult context = await source.GetContextAsync(
            anchor.Id,
            7,
            7,
            CancellationToken.None
        );

        // Assert
        Assert.Equal(
            source.Records.Skip(93).Take(15),
            [.. context.Before, context.Anchor, .. context.After]
        );
    }

    [Fact]
    public async Task GetContextAsync_UnknownId_ThrowsKeyNotFoundException()
    {
        // Act
        Task Act() => CreateSource().GetContextAsync("nope", 7, 7, CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(Act);
    }

    [Theory]
    [InlineData("@timestamp", "datetime", 1.0)]
    [InlineData("StatusCode", "number", 129 / 238.0)]
    [InlineData("SourceContext", "string", 1.0)]
    [InlineData("@exception.type", "string", 9 / 238.0)]
    public async Task GetFieldsAsync_LastHour_ReportsKindAndPresence(
        string path,
        string kind,
        double presence
    )
    {
        // Act
        IReadOnlyList<FieldInfo> fields = await CreateSource()
            .GetFieldsAsync(LastHour, CancellationToken.None);

        // Assert
        Assert.Contains(new FieldInfo(path, kind, presence), fields);
    }

    [Fact]
    public void Compile_LevelsAndFilter_RendersOneClausePerLine()
    {
        // Arrange
        LogQuery query = LastHour with
        {
            Levels = new HashSet<string> { "error", "warn" },
            Filter = new AndNode([
                new OrNode([
                    new ConditionNode(
                        "RequestPath",
                        FilterOperator.StartsWith,
                        JsonSerializer.SerializeToElement("/api")
                    ),
                    new ConditionNode(
                        "RequestPath",
                        FilterOperator.Equals,
                        JsonSerializer.SerializeToElement("/")
                    ),
                ]),
                new NotNode(new TextNode("timeout")),
            ]),
        };

        // Act
        CompileResult result = CreateSource().Compile(query);

        // Assert
        Assert.Equal(
            "@severity in [warn, error]\nand (RequestPath startswith \"/api\" or RequestPath = \"/\")\nand not text(\"timeout\")",
            result.Native
        );
    }

    [Fact]
    public void Compile_UndeclaredOperator_ReportsTheNodeAsUnsupported()
    {
        // Arrange
        FakeLogSource source = CreateSource(
            new FakeLogSourceOptions
            {
                Operators = new HashSet<FilterOperator> { FilterOperator.Equals },
            }
        );
        var regex = new ConditionNode(
            "RequestPath",
            FilterOperator.Matches,
            JsonSerializer.SerializeToElement("^/")
        );
        LogQuery query = LastHour with { Filter = regex };

        // Act
        CompileResult result = source.Compile(query);

        // Assert
        Assert.Equal((null, regex), (result.Native, Assert.Single(result.Unsupported)));
    }

    [Theory]
    [InlineData("anything at all", true)]
    [InlineData("   ", false)]
    public void ValidateNative_Text_IsValidUnlessBlank(string native, bool expected)
    {
        // Act
        ValidationResult result = CreateSource().ValidateNative(native);

        // Assert
        Assert.Equal(expected, result.Valid);
    }

    [Fact]
    public void TailAsync_DefaultOptions_ThrowsNotSupportedException()
    {
        // Act
        void Act() => CreateSource().TailAsync(LastHour, CancellationToken.None);

        // Assert
        Assert.Throws<NotSupportedException>(Act);
    }

    [Fact]
    public void GetHistogramAsync_FeatureNotDeclared_ThrowsNotSupportedException()
    {
        // Arrange
        FakeLogSource source = CreateSource(
            new FakeLogSourceOptions { Features = LogSourceFeatures.Facets }
        );

        // Act
        void Act() => source.GetHistogramAsync(LastHour, 60, CancellationToken.None);

        // Assert
        Assert.Throws<NotSupportedException>(Act);
    }
}
