using System.Text.Json;
using Umbraco.Community.LogExplorer.Core.Json;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Records;

namespace Umbraco.Community.LogExplorer.Core.Tests.Json;

public class LogJsonTests
{
    // Each case is the canonical wire form; a round-trip must reproduce it byte for byte.
    public static TheoryData<string> FilterNodeJson =>
        new()
        {
            """{"kind":"condition","field":"RequestPath","op":"startsWith","value":"/api","caseInsensitive":true}""",
            """{"kind":"condition","field":"ContentId","op":"exists","value":null,"caseInsensitive":true}""",
            """{"kind":"text","text":"connection refused","phrase":true}""",
            """{"kind":"not","child":{"kind":"text","text":"timeout","phrase":false}}""",
            """{"kind":"and","children":[{"kind":"or","children":[{"kind":"condition","field":"StatusCode","op":"equals","value":500,"caseInsensitive":true},{"kind":"condition","field":"StatusCode","op":"in","value":[502,503],"caseInsensitive":false}]},{"kind":"not","child":{"kind":"condition","field":"SourceContext","op":"startsWith","value":"Umbraco.Cms.Core.Sync","caseInsensitive":true}}]}""",
        };

    [Theory]
    [MemberData(nameof(FilterNodeJson))]
    public void FilterNode_RoundTrip_ReproducesTheSameJson(string json)
    {
        // Arrange
        FilterNode node = JsonSerializer.Deserialize<FilterNode>(json, LogJson.Options)!;

        // Act
        string reserialised = JsonSerializer.Serialize(node, LogJson.Options);

        // Assert
        Assert.Equal(json, reserialised);
    }

    [Fact]
    public void FilterNode_Deserialise_BuildsTheTypedTree()
    {
        // Arrange
        const string json = """
            {"kind":"and","children":[{"kind":"not","child":{"kind":"text","text":"x"}}]}
            """;

        // Act
        FilterNode node = JsonSerializer.Deserialize<FilterNode>(json, LogJson.Options)!;

        // Assert
        AndNode and = Assert.IsType<AndNode>(node);
        Assert.IsType<TextNode>(Assert.IsType<NotNode>(Assert.Single(and.Children)).Child);
    }

    [Fact]
    public void FilterNode_KindNotFirst_StillDeserialises()
    {
        // Arrange
        const string json = """{"text":"timeout","kind":"text"}""";

        // Act
        FilterNode node = JsonSerializer.Deserialize<FilterNode>(json, LogJson.Options)!;

        // Assert
        Assert.Equal(new TextNode("timeout"), node);
    }

    [Theory]
    [InlineData("1234.5", JsonValueKind.Number)]
    [InlineData("true", JsonValueKind.True)]
    [InlineData("false", JsonValueKind.False)]
    [InlineData("\"text\"", JsonValueKind.String)]
    [InlineData("""{"total":12.5,"items":[1,2]}""", JsonValueKind.Object)]
    [InlineData("""["a","b"]""", JsonValueKind.Array)]
    [InlineData("null", JsonValueKind.Null)]
    public void LogRecordAttribute_RoundTrip_KeepsItsJsonKindAndValue(
        string valueJson,
        JsonValueKind expectedKind
    )
    {
        // Arrange
        var record = new LogRecord
        {
            Id = "1",
            Timestamp = DateTimeOffset.UnixEpoch,
            Attributes = new Dictionary<string, JsonElement>
            {
                ["Value"] = JsonDocument.Parse(valueJson).RootElement,
            },
        };

        // Act
        LogRecord roundTripped = JsonSerializer.Deserialize<LogRecord>(
            JsonSerializer.Serialize(record, LogJson.Options),
            LogJson.Options
        )!;

        // Assert
        JsonElement value = roundTripped.Attributes["Value"];
        Assert.Equal((expectedKind, valueJson), (value.ValueKind, value.GetRawText()));
    }

    [Fact]
    public void LogQuery_Serialise_UsesCamelCaseAndStringEnums()
    {
        // Arrange
        var query = new LogQuery
        {
            Range = new TimeRange(null, null, "1h"),
            Levels = new HashSet<string> { "warn" },
            Sort = SortDirection.Ascending,
        };

        // Act
        string json = JsonSerializer.Serialize(query, LogJson.Options);

        // Assert
        Assert.Equal(
            """{"range":{"from":null,"to":null,"relative":"1h"},"levels":["warn"],"filter":null,"nativeQuery":null,"take":100,"cursor":null,"sort":"ascending"}""",
            json
        );
    }

    [Fact]
    public void LogQuery_DeserialiseLevels_ReadsTheSet()
    {
        // Arrange
        const string json = """{"range":{"relative":"1h"},"levels":["debug","error"]}""";

        // Act
        LogQuery query = JsonSerializer.Deserialize<LogQuery>(json, LogJson.Options)!;

        // Assert
        Assert.Equal(["debug", "error"], query.Levels!.Order());
    }

    [Fact]
    public void Apply_ToOtherOptions_ReadsAQueryWithLevelsAndKindAfterTheFields()
    {
        // Arrange
        var options = new JsonSerializerOptions();
        LogJson.Apply(options);
        const string json = """
            {"range":{"relative":"1h"},"levels":["WARN"],"sort":"ascending",
             "filter":{"field":"RequestPath","op":"startsWith","value":"/api","kind":"condition"}}
            """;

        // Act
        LogQuery query = JsonSerializer.Deserialize<LogQuery>(json, options)!;

        // Assert
        Assert.Equal(
            (true, SortDirection.Ascending, FilterOperator.StartsWith),
            (query.Levels!.Contains("warn"), query.Sort, ((ConditionNode)query.Filter!).Op)
        );
    }

    [Fact]
    public void Apply_ReadOnlyOptions_Throws()
    {
        // Act
        Exception? thrown = Record.Exception(() => LogJson.Apply(LogJson.Options));

        // Assert
        Assert.IsType<InvalidOperationException>(thrown);
    }
}
