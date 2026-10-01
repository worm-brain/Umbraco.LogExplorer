using System.Text.Json;
using Umbraco.Community.LogExplorer.Core.Json;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.SimpleSyntax;

namespace Umbraco.Community.LogExplorer.Core.Tests.SimpleSyntax;

public class SimpleSyntaxParserTests
{
    // Named cases so a failure reads as the rule that broke. Chips are compared as their wire
    // JSON, because JsonElement values do not compare by value.
    private static readonly Dictionary<string, (string Input, FilterNode[] Chips)> ChipCases = new()
    {
        // Text
        ["bare word"] = ("timeout", [Text("timeout")]),
        ["bare words share one chip"] = ("connection timeout", [Text("connection timeout")]),
        ["extra whitespace collapses"] = ("  a \t  b  ", [Text("a b")]),
        ["phrase"] = ("\"connection refused\"", [Phrase("connection refused")]),
        ["each phrase is its own chip"] = ("\"a b\" \"c d\"", [Phrase("a b"), Phrase("c d")]),
        ["empty phrase is dropped"] = ("\"\" x", [Text("x")]),
        ["escaped quote in phrase"] = ("\"say \\\"hi\\\"\"", [Phrase("say \"hi\"")]),
        ["escaped backslash in phrase"] = ("\"C:\\\\temp\"", [Phrase("C:\\temp")]),
        ["other backslash in phrase is literal"] = ("\"a\\nb\"", [Phrase("a\\nb")]),
        ["unicode word"] = ("café 日本", [Text("café 日本")]),
        ["text chip sits at its first word"] = (
            "Code:500 slow path:/api crash",
            [
                Cond("Code", FilterOperator.Equals, "\"500\""),
                Text("slow crash"),
                Cond("RequestPath", FilterOperator.Equals, "\"/api\""),
            ]
        ),
        // Equals
        ["field equals"] = (
            "StatusCode:500",
            [Cond("StatusCode", FilterOperator.Equals, "\"500\"")]
        ),
        ["field equals quoted value"] = (
            "SourceContext:\"My App\"",
            [Cond("SourceContext", FilterOperator.Equals, "\"My App\"")]
        ),
        ["quoted value keeps stars literal"] = (
            "RequestPath:\"/api*\"",
            [Cond("RequestPath", FilterOperator.Equals, "\"/api*\"")]
        ),
        ["unicode field and value"] = (
            "Città:\"São Paulo\"",
            [Cond("Città", FilterOperator.Equals, "\"São Paulo\"")]
        ),
        ["nested attribute path kept verbatim"] = (
            "Cart.Total:12",
            [Cond("Cart.Total", FilterOperator.Equals, "\"12\"")]
        ),
        ["value may contain a colon"] = (
            "Url:http://x",
            [Cond("Url", FilterOperator.Equals, "\"http://x\"")]
        ),
        // Negation
        ["exclude"] = (
            "-StatusCode:200",
            [Not(Cond("StatusCode", FilterOperator.Equals, "\"200\""))]
        ),
        ["exclude wildcard"] = (
            "-SourceContext:Umbraco.Cms.Core.Sync*",
            [Not(Cond("SourceContext", FilterOperator.StartsWith, "\"Umbraco.Cms.Core.Sync\""))]
        ),
        ["exclude comparison"] = (
            "-Duration>1000",
            [Not(Cond("Duration", FilterOperator.GreaterThan, "1000"))]
        ),
        ["dash without a filter is a word"] = ("-foo well-known", [Text("-foo well-known")]),
        ["double dash is a word"] = ("--x:y", [Text("--x:y")]),
        // Wildcards
        ["starts with"] = (
            "path:/api*",
            [Cond("RequestPath", FilterOperator.StartsWith, "\"/api\"")]
        ),
        ["ends with"] = ("path:*.php", [Cond("RequestPath", FilterOperator.EndsWith, "\".php\"")]),
        ["contains"] = (
            "path:*login*",
            [Cond("RequestPath", FilterOperator.Contains, "\"login\"")]
        ),
        ["star in the middle is literal"] = (
            "path:a*b",
            [Cond("RequestPath", FilterOperator.Equals, "\"a*b\"")]
        ),
        ["only stars means present"] = (
            "path:*",
            [Cond("RequestPath", FilterOperator.Exists, null)]
        ),
        // Comparisons
        ["greater than integer"] = (
            "Duration>1000",
            [Cond("Duration", FilterOperator.GreaterThan, "1000")]
        ),
        ["greater or equal decimal"] = (
            "Cart.Total>=12.5",
            [Cond("Cart.Total", FilterOperator.GreaterOrEqual, "12.5")]
        ),
        ["less than negative"] = ("Delta<-3", [Cond("Delta", FilterOperator.LessThan, "-3")]),
        ["less or equal date stays a string"] = (
            "When<=2026-09-02T10:00:00Z",
            [Cond("When", FilterOperator.LessOrEqual, "\"2026-09-02T10:00:00Z\"")]
        ),
        ["comma decimal is not a number"] = (
            "Total>1,5",
            [Cond("Total", FilterOperator.GreaterThan, "\"1,5\"")]
        ),
        ["NaN is not a number"] = (
            "Total>NaN",
            [Cond("Total", FilterOperator.GreaterThan, "\"NaN\"")]
        ),
        // Exists
        ["has"] = ("has:ContentId", [Cond("ContentId", FilterOperator.Exists, null)]),
        ["not has"] = ("-has:ContentId", [Cond("ContentId", FilterOperator.NotExists, null)]),
        ["has resolves aliases"] = ("HAS:path", [Cond("RequestPath", FilterOperator.Exists, null)]),
        // Aliases
        ["alias msg"] = ("msg:hi", [Cond("@body", FilterOperator.Equals, "\"hi\"")]),
        ["alias message"] = ("message:hi", [Cond("@body", FilterOperator.Equals, "\"hi\"")]),
        ["alias template"] = ("template:x", [Cond("@template", FilterOperator.Equals, "\"x\"")]),
        ["alias source"] = ("source:x", [Cond("@scope", FilterOperator.Equals, "\"x\"")]),
        ["alias scope"] = ("scope:x", [Cond("@scope", FilterOperator.Equals, "\"x\"")]),
        ["alias trace"] = ("trace:abc123", [Cond("@traceId", FilterOperator.Equals, "\"abc123\"")]),
        ["alias ex"] = (
            "ex:*Timeout*",
            [Cond("@exception.type", FilterOperator.Contains, "\"Timeout\"")]
        ),
        ["alias exception"] = (
            "exception:X",
            [Cond("@exception.type", FilterOperator.Equals, "\"X\"")]
        ),
        ["alias path"] = ("path:/", [Cond("RequestPath", FilterOperator.Equals, "\"/\"")]),
        ["alias ignores case"] = ("PATH:/", [Cond("RequestPath", FilterOperator.Equals, "\"/\"")]),
        ["alias status"] = (
            "-status:200",
            [Not(Cond("StatusCode", FilterOperator.Equals, "\"200\""))]
        ),
        ["alias status comparison"] = (
            "status>=500",
            [Cond("StatusCode", FilterOperator.GreaterOrEqual, "500")]
        ),
        ["alias machine"] = (
            "machine:WORM",
            [Cond("MachineName", FilterOperator.Equals, "\"WORM\"")]
        ),
        // Not filters, so kept as words
        ["field without value is a word"] = ("Status:", [Text("Status:")]),
        ["equals sign on a field is a word"] = ("Status=500", [Text("Status=500")]),
        ["unknown level is a word"] = ("level:loud", [Text("level:loud")]),
        ["negated level is a word"] = ("-level:warn", [Text("-level:warn")]),
        ["level comparison is a word"] = ("level>warn", [Text("level>warn")]),
        ["level tokens make no chip"] = ("level:warn timeout", [Text("timeout")]),
    };

    private static readonly Dictionary<string, (string Input, string[]? Levels)> LevelCases = new()
    {
        ["no level token"] = ("timeout", null),
        ["minimum level"] = ("level:warn", ["error", "fatal", "warn"]),
        ["severity alias"] = ("severity:error", ["error", "fatal"]),
        ["lowest minimum is everything"] = (
            "level:trace",
            ["debug", "error", "fatal", "info", "trace", "warn"]
        ),
        ["exact level"] = ("level=error", ["error"]),
        ["level name ignores case"] = ("LEVEL:WARN", ["error", "fatal", "warn"]),
        ["quoted level name"] = ("level:\"info\"", ["error", "fatal", "info", "warn"]),
        ["level tokens union"] = ("level=debug level=error", ["debug", "error"]),
        ["minimum and exact union"] = ("level=trace level:error", ["error", "fatal", "trace"]),
        ["unknown level leaves levels unset"] = ("level:loud", null),
    };

    public static TheoryData<string> ChipCaseNames => [.. ChipCases.Keys];

    public static TheoryData<string> LevelCaseNames => [.. LevelCases.Keys];

    [Theory]
    [MemberData(nameof(ChipCaseNames))]
    public void Parse_ChipCase_ReturnsExpectedChips(string caseName)
    {
        // Arrange
        (string input, FilterNode[] expected) = ChipCases[caseName];

        // Act
        ParseResult result = SimpleSyntaxParser.Parse(input);

        // Assert
        Assert.Equal(ToJson(expected), ToJson(result.Chips));
    }

    [Theory]
    [MemberData(nameof(LevelCaseNames))]
    public void Parse_LevelCase_ReturnsExpectedLevels(string caseName)
    {
        // Arrange
        (string input, string[]? expected) = LevelCases[caseName];

        // Act
        ParseResult result = SimpleSyntaxParser.Parse(input);

        // Assert
        Assert.Equal(expected, result.Levels?.Order(StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [InlineData("level:warn")]
    [InlineData("level=error")]
    public void Parse_LevelOnly_ProducesNoChips(string input)
    {
        // Act
        ParseResult result = SimpleSyntaxParser.Parse(input);

        // Assert
        Assert.Empty(result.Chips);
    }

    [Fact]
    public void Parse_ValidInput_HasNoFallback()
    {
        // Act
        ParseResult result = SimpleSyntaxParser.Parse("level:error path:/api* \"a b\"");

        // Assert
        Assert.Null(result.Fallback);
    }

    [Fact]
    public void Parse_UnbalancedQuote_SearchesWholeInputAsText()
    {
        // Act
        ParseResult result = SimpleSyntaxParser.Parse("level:error \"connection refused");

        // Assert
        Assert.Equal(ToJson([Text("level:error connection refused")]), ToJson(result.Chips));
    }

    [Fact]
    public void Parse_UnbalancedQuote_ExplainsTheFallback()
    {
        // Act
        ParseResult result = SimpleSyntaxParser.Parse("path:\"/api");

        // Assert
        Assert.Equal(
            new ParseFallback(
                "unbalanced_quote",
                "Unbalanced quote, so this was searched as plain text"
            ),
            result.Fallback
        );
    }

    [Fact]
    public void Parse_UnbalancedQuote_IgnoresLevelTokens()
    {
        // Act
        ParseResult result = SimpleSyntaxParser.Parse("level:error \"x");

        // Assert
        Assert.Null(result.Levels);
    }

    [Fact]
    public void Parse_EscapedQuoteOnly_IsUnbalanced()
    {
        // Act
        ParseResult result = SimpleSyntaxParser.Parse("\"a \\\"");

        // Assert
        Assert.NotNull(result.Fallback);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_EmptyInput_ReturnsNothing(string input)
    {
        // Act
        ParseResult result = SimpleSyntaxParser.Parse(input);

        // Assert
        Assert.Multiple(
            () => Assert.Empty(result.Chips),
            () => Assert.Null(result.Levels),
            () => Assert.Null(result.Fallback)
        );
    }

    [Fact]
    public void Parse_NullInput_ThrowsArgumentNullException()
    {
        // Act
        static void Act() => SimpleSyntaxParser.Parse(null!);

        // Assert
        Assert.Throws<ArgumentNullException>(Act);
    }

    [Fact]
    public void ParseResult_Serialise_UsesTheWireShape()
    {
        // Arrange
        ParseResult result = SimpleSyntaxParser.Parse("level=error path:/api*");

        // Act
        string json = JsonSerializer.Serialize(result, LogJson.Options);

        // Assert
        Assert.Equal(
            """{"chips":[{"kind":"condition","field":"RequestPath","op":"startsWith","value":"/api","caseInsensitive":true}],"levels":["error"],"fallback":null}""",
            json
        );
    }

    [Theory]
    [InlineData("level:warn -has:x \"a b\" Duration>1000")]
    [InlineData("unbalanced \"quote")]
    public void ParseResult_RoundTrip_ReproducesTheSameJson(string input)
    {
        // Arrange
        string json = JsonSerializer.Serialize(SimpleSyntaxParser.Parse(input), LogJson.Options);

        // Act
        ParseResult parsed = JsonSerializer.Deserialize<ParseResult>(json, LogJson.Options)!;

        // Assert
        Assert.Equal(json, JsonSerializer.Serialize(parsed, LogJson.Options));
    }

    private static TextNode Text(string text) => new(text);

    private static TextNode Phrase(string text) => new(text, Phrase: true);

    private static NotNode Not(FilterNode child) => new(child);

    private static ConditionNode Cond(string field, FilterOperator op, string? valueJson) =>
        new(field, op, valueJson is null ? null : JsonDocument.Parse(valueJson).RootElement);

    private static string ToJson(IReadOnlyList<FilterNode> chips) =>
        JsonSerializer.Serialize(chips, LogJson.Options);
}
