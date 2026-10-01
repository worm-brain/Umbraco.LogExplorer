using System.Text.Json;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Core.SimpleSyntax;
using Umbraco.Community.LogExplorer.Features.Sources.Files;

namespace Umbraco.Community.LogExplorer.Files.Tests.Features.Sources.Files;

/// <summary>
/// Golden output of "Show query" for the files source: the core Log Viewer's dialect (#34,
/// ADR 0013). The first theory is BRIEF §10.5's Files column, typed into the search box.
/// </summary>
public class FileQueryCompilerTests
{
    private static readonly TimeRange LastHour = new(null, null, "1h");

    [Theory]
    [InlineData("timeout", "(@Message like '%timeout%' ci or Contains(@Exception, 'timeout') ci)")]
    [InlineData("level:warn", "(@Level = 'Warning' or @Level = 'Error' or @Level = 'Fatal')")]
    [InlineData("RequestPath:/api*", "StartsWith(RequestPath, '/api') ci")]
    [InlineData(
        "-SourceContext:Umbraco.Cms.Core.Sync*",
        "not (StartsWith(SourceContext, 'Umbraco.Cms.Core.Sync') ci)"
    )]
    [InlineData("Duration>1000", "Duration > 1000")]
    [InlineData("has:ContentId", "ContentId is not null")]
    [InlineData("trace:abc123", "@tr = 'abc123' ci")]
    public void Compile_BriefGoldenExample_EmitsTheCoreViewerDialect(string input, string expected)
    {
        // Arrange
        LogQuery query = FromSearchBox(input);

        // Act
        CompileResult result = FileQueryCompiler.Compile(query);

        // Assert
        Assert.Equal(expected, result.Native);
        Assert.Empty(result.Unsupported);
    }

    [Theory]
    [InlineData(FilterOperator.Equals, "\"GET\"", "RequestMethod = 'GET' ci")]
    [InlineData(FilterOperator.NotEquals, "\"GET\"", "not (RequestMethod = 'GET' ci)")]
    [InlineData(FilterOperator.Contains, "\"ET\"", "Contains(RequestMethod, 'ET') ci")]
    [InlineData(FilterOperator.StartsWith, "\"GE\"", "StartsWith(RequestMethod, 'GE') ci")]
    [InlineData(FilterOperator.EndsWith, "\"ET\"", "EndsWith(RequestMethod, 'ET') ci")]
    [InlineData(FilterOperator.GreaterThan, "10", "RequestMethod > 10")]
    [InlineData(FilterOperator.GreaterOrEqual, "10", "RequestMethod >= 10")]
    [InlineData(FilterOperator.LessThan, "10.5", "RequestMethod < 10.5")]
    [InlineData(FilterOperator.LessOrEqual, "\"10\"", "RequestMethod <= 10")]
    [InlineData(FilterOperator.In, "[\"GET\", \"POST\"]", "RequestMethod in ['GET', 'POST'] ci")]
    [InlineData(FilterOperator.Exists, "null", "RequestMethod is not null")]
    [InlineData(FilterOperator.NotExists, "null", "RequestMethod is null")]
    [InlineData(FilterOperator.Matches, "\"^G.T$\"", "IsMatch(RequestMethod, '^G.T$') ci")]
    public void Compile_EachOperator_EmitsItsDialectForm(
        FilterOperator op,
        string valueJson,
        string expected
    )
    {
        // Arrange
        LogQuery query = WithFilter(Condition("RequestMethod", op, valueJson));

        // Act
        string? native = FileQueryCompiler.Compile(query).Native;

        // Assert
        Assert.Equal(expected, native);
    }

    [Theory]
    [InlineData("\"200\"", "StatusCode in ['200', 200] ci")]
    [InlineData("200", "StatusCode in [200, '200'] ci")]
    [InlineData("\"true\"", "StatusCode in ['true', true] ci")]
    [InlineData("false", "StatusCode in [false, 'false'] ci")]
    public void Compile_EqualityWithANumberOrBoolean_OffersEveryTypedForm(
        string valueJson,
        string expected
    )
    {
        // Arrange
        LogQuery query = WithFilter(Condition("StatusCode", FilterOperator.Equals, valueJson));

        // Act
        string? native = FileQueryCompiler.Compile(query).Native;

        // Assert
        Assert.Equal(expected, native);
    }

    [Fact]
    public void Compile_TextOperatorWithANumericValue_ConvertsTheFieldToText()
    {
        // Arrange
        LogQuery query = WithFilter(Condition("StatusCode", FilterOperator.StartsWith, "\"5\""));

        // Act
        string? native = FileQueryCompiler.Compile(query).Native;

        // Assert
        Assert.Equal("StartsWith(ToString(StatusCode), '5') ci", native);
    }

    [Fact]
    public void Compile_CaseSensitiveCondition_HasNoCiModifier()
    {
        // Arrange
        LogQuery query = WithFilter(
            new ConditionNode("RequestPath", FilterOperator.Equals, Json("\"/api\""), false)
        );

        // Act
        string? native = FileQueryCompiler.Compile(query).Native;

        // Assert
        Assert.Equal("RequestPath = '/api'", native);
    }

    [Theory]
    [InlineData("@body", "@Message = 'x' ci")]
    [InlineData("@template", "@MessageTemplate = 'x' ci")]
    [InlineData("@scope", "SourceContext = 'x' ci")]
    [InlineData("@traceId", "@tr = 'x' ci")]
    [InlineData("@spanId", "@sp = 'x' ci")]
    [InlineData("Cart.Total", "Cart.Total = 'x' ci")]
    [InlineData("Tags[]", "Tags[?] = 'x' ci")]
    [InlineData("Lines[].Sku", "Lines[?].Sku = 'x' ci")]
    [InlineData("Not An Identifier", "@Properties['Not An Identifier'] = 'x' ci")]
    [InlineData("Order.end", "Order['end'] = 'x' ci")]
    public void Compile_Field_IsNamedTheWayTheDialectReadsIt(string field, string expected)
    {
        // Arrange
        LogQuery query = WithFilter(Condition(field, FilterOperator.Equals, "\"x\""));

        // Act
        string? native = FileQueryCompiler.Compile(query).Native;

        // Assert
        Assert.Equal(expected, native);
    }

    [Fact]
    public void Compile_NotExistsOnAnArrayPath_AsksThatNoElementIsSet()
    {
        // Arrange
        LogQuery query = WithFilter(Condition("Tags[]", FilterOperator.NotExists, "null"));

        // Act
        string? native = FileQueryCompiler.Compile(query).Native;

        // Assert
        Assert.Equal("not (Tags[?] is not null)", native);
    }

    [Theory]
    [InlineData(FilterOperator.Equals, "\"warn\"", "@Level = 'Warning'")]
    [InlineData(FilterOperator.NotEquals, "\"debug\"", "@Level <> 'Debug'")]
    [InlineData(FilterOperator.In, "[\"trace\", \"fatal\"]", "@Level in ['Verbose', 'Fatal']")]
    public void Compile_SeverityCondition_UsesSerilogLevelNames(
        FilterOperator op,
        string valueJson,
        string expected
    )
    {
        // Arrange
        LogQuery query = WithFilter(Condition("@severity", op, valueJson));

        // Act
        string? native = FileQueryCompiler.Compile(query).Native;

        // Assert
        Assert.Equal(expected, native);
    }

    [Fact]
    public void Compile_SeveralChipsAndLevels_PutsOneClausePerLineWithAndAtTheStart()
    {
        // Arrange
        LogQuery query = FromSearchBox("level:error path:/api* -StatusCode:200");

        // Act
        string? native = FileQueryCompiler.Compile(query).Native;

        // Assert
        Assert.Equal(
            "(@Level = 'Error' or @Level = 'Fatal')\n"
                + "and StartsWith(RequestPath, '/api') ci\n"
                + "and not (StatusCode in ['200', 200] ci)",
            native
        );
    }

    [Fact]
    public void Compile_OrInsideTheFilter_KeepsItsParentheses()
    {
        // Arrange
        LogQuery query = WithFilter(
            new AndNode([
                new OrNode([
                    Condition("RequestPath", FilterOperator.Equals, "\"/\""),
                    Condition("RequestPath", FilterOperator.Equals, "\"/api\""),
                ]),
                Condition("Duration", FilterOperator.GreaterThan, "100"),
            ])
        );

        // Act
        string? native = FileQueryCompiler.Compile(query).Native;

        // Assert
        Assert.Equal(
            "(RequestPath = '/' ci or RequestPath = '/api' ci)\nand Duration > 100",
            native
        );
    }

    [Fact]
    public void Compile_SeveralWords_RequiresEachOne()
    {
        // Arrange
        LogQuery query = WithFilter(new TextNode("sql timeout"));

        // Act
        string? native = FileQueryCompiler.Compile(query).Native;

        // Assert
        Assert.Equal(
            "((@Message like '%sql%' ci or Contains(@Exception, 'sql') ci)"
                + " and (@Message like '%timeout%' ci or Contains(@Exception, 'timeout') ci))",
            native
        );
    }

    [Fact]
    public void Compile_TextWithQuotesAndWildcards_EscapesThem()
    {
        // Arrange
        LogQuery query = WithFilter(new TextNode("it's 100%_done", Phrase: true));

        // Act
        string? native = FileQueryCompiler.Compile(query).Native;

        // Assert
        Assert.Equal(
            "(@Message like '%it''s 100%%__done%' ci or Contains(@Exception, 'it''s 100%_done') ci)",
            native
        );
    }

    [Fact]
    public void Compile_ValueWithAQuote_DoublesIt()
    {
        // Arrange
        LogQuery query = WithFilter(Condition("Name", FilterOperator.Equals, "\"O'Brien\""));

        // Act
        string? native = FileQueryCompiler.Compile(query).Native;

        // Assert
        Assert.Equal("Name = 'O''Brien' ci", native);
    }

    [Fact]
    public void Compile_NativeQueryWithChips_AppendsItAsAClause()
    {
        // Arrange
        LogQuery query = WithFilter(Condition("Duration", FilterOperator.GreaterThan, "100")) with
        {
            NativeQuery = "Has(RequestId) or Has(TraceId)",
        };

        // Act
        string? native = FileQueryCompiler.Compile(query).Native;

        // Assert
        Assert.Equal("Duration > 100\nand (Has(RequestId) or Has(TraceId))", native);
    }

    [Fact]
    public void Compile_PlainTextNativeQuery_AppendsTheMessageSearchItStandsFor()
    {
        // Arrange
        LogQuery query = new() { Range = LastHour, NativeQuery = "timeout" };

        // Act
        string? native = FileQueryCompiler.Compile(query).Native;

        // Assert
        Assert.Equal("(@Message like '%timeout%')", native);
    }

    [Fact]
    public void Compile_NoFilter_HasNoNativeQuery()
    {
        // Arrange
        LogQuery query = new() { Range = LastHour };

        // Act
        CompileResult result = FileQueryCompiler.Compile(query);

        // Assert
        Assert.Null(result.Native);
        Assert.Empty(result.Unsupported);
    }

    [Fact]
    public void Compile_AllSixLevels_HasNoLevelClause()
    {
        // Arrange
        LogQuery query = new()
        {
            Range = LastHour,
            Levels = new HashSet<string> { "trace", "debug", "info", "warn", "error", "fatal" },
        };

        // Act
        string? native = FileQueryCompiler.Compile(query).Native;

        // Assert
        Assert.Null(native);
    }

    [Fact]
    public void Compile_OneLevel_HasNoParentheses()
    {
        // Arrange
        LogQuery query = new()
        {
            Range = LastHour,
            Levels = new HashSet<string> { "info" },
        };

        // Act
        string? native = FileQueryCompiler.Compile(query).Native;

        // Assert
        Assert.Equal("@Level = 'Information'", native);
    }

    public static IEnumerable<TheoryDataRow<FilterNode>> UnsupportedNodes =>
        [
            new(Condition("@timestamp", FilterOperator.GreaterThan, "\"2026-10-01\""))
            {
                Label = "timestamp",
            },
            new(Condition("@exception.type", FilterOperator.Equals, "\"System.X\""))
            {
                Label = "exception type",
            },
            new(Condition("@exception.message", FilterOperator.Contains, "\"x\""))
            {
                Label = "exception message",
            },
            new(Condition("@resource.host.name", FilterOperator.Equals, "\"WORM\""))
            {
                Label = "resource",
            },
            new(Condition("When", FilterOperator.LessThan, "\"2026-10-01\""))
            {
                Label = "date ordering",
            },
            new(Condition("Name", FilterOperator.GreaterThan, "\"m\"")) { Label = "text ordering" },
            new(Condition("Name", FilterOperator.Equals, "null")) { Label = "null value" },
            new(Condition("Cart", FilterOperator.Equals, "{\"Total\":1}"))
            {
                Label = "object value",
            },
            new(Condition("Name", FilterOperator.In, "[]")) { Label = "empty in" },
            new(Condition("@severity", FilterOperator.GreaterThan, "\"warn\""))
            {
                Label = "severity ordering",
            },
            new(Condition("@severity", FilterOperator.Equals, "\"loud\""))
            {
                Label = "unknown level",
            },
            new(Condition("Cart..Total", FilterOperator.Exists, "null"))
            {
                Label = "empty path segment",
            },
        ];

    [Theory]
    [MemberData(nameof(UnsupportedNodes))]
    public void Compile_NodeTheDialectCannotExpress_IsReportedAndLeftOut(FilterNode node)
    {
        // Arrange
        LogQuery query = WithFilter(
            new AndNode([node, Condition("Duration", FilterOperator.GreaterThan, "1")])
        );

        // Act
        CompileResult result = FileQueryCompiler.Compile(query);

        // Assert
        Assert.Equal("Duration > 1", result.Native);
        Assert.Equal([node], result.Unsupported);
    }

    [Fact]
    public void Compile_OrWithAnUnsupportedBranch_KeepsTheOtherBranch()
    {
        // Arrange
        FilterNode unsupported = Condition("@exception.type", FilterOperator.Equals, "\"X\"");
        LogQuery query = WithFilter(
            new OrNode([unsupported, Condition("Duration", FilterOperator.GreaterThan, "1")])
        );

        // Act
        CompileResult result = FileQueryCompiler.Compile(query);

        // Assert
        Assert.Equal("Duration > 1", result.Native);
        Assert.Equal([unsupported], result.Unsupported);
    }

    private static LogQuery FromSearchBox(string input)
    {
        ParseResult parsed = SimpleSyntaxParser.Parse(input);
        return new LogQuery
        {
            Range = LastHour,
            Levels = parsed.Levels,
            Filter = parsed.Chips.Count switch
            {
                0 => null,
                1 => parsed.Chips[0],
                _ => new AndNode(parsed.Chips),
            },
        };
    }

    private static LogQuery WithFilter(FilterNode filter) =>
        new() { Range = LastHour, Filter = filter };

    private static ConditionNode Condition(string field, FilterOperator op, string valueJson) =>
        new(field, op, valueJson == "null" ? null : Json(valueJson));

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
