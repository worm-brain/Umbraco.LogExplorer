using System.Text.Json;
using Serilog.Events;
using Umbraco.Community.LogExplorer.Core.Filtering;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Core.SimpleSyntax;
using Umbraco.Community.LogExplorer.Features.Sources.Files;
using Umbraco.Community.LogExplorer.Files.Tests.TestSupport;

namespace Umbraco.Community.LogExplorer.Files.Tests.Features.Sources.Files;

/// <summary>
/// "Show query" pasted into the core Log Viewer returns the same entries the files source shows
/// (BRIEF Phase 1 acceptance criterion 6): for each query, the compiled expression run the way the
/// core viewer runs it selects exactly what the C# filter selects over the same events.
/// </summary>
public class FileQueryCompileRoundTripTests
{
    // What a user types in the search box; the chips AND together.
    public static TheoryData<string> SearchBoxInputs =>
        [
            "timeout",
            "\"background job\"",
            "unhandled timeout",
            "level:warn",
            "level=error",
            "path:/api*",
            "path:/umbraco*",
            "-SourceContext:Umbraco.Cms*",
            "source:Umbraco.Cms.Core*",
            "Duration>1000",
            "Duration<=65",
            "has:ContentId",
            "-has:ContentId",
            "StatusCode:200",
            "-StatusCode:200",
            "StatusCode:404",
            "StatusCode:5*",
            "Flag:true",
            "Name:\"o'brien\"",
            "Cart.Total>100",
            "trace:" + DialectFixtures.TraceId,
            "msg:*published*",
            "template:\"Boot failed\"",
            "level:info path:/api* -StatusCode:500",
        ];

    public static IEnumerable<TheoryDataRow<FilterNode>> HandBuiltFilters =>
        [
            new(
                new OrNode([
                    Condition("RequestPath", FilterOperator.Equals, "\"/api/basket\""),
                    Condition("StatusCode", FilterOperator.Equals, "500"),
                ])
            )
            {
                Label = "same-field style OR",
            },
            new(Condition("Tags[]", FilterOperator.Equals, "\"SALE\"")) { Label = "array element" },
            new(Condition("Tags[]", FilterOperator.NotExists, "null"))
            {
                Label = "no array element",
            },
            new(Condition("RequestMethod", FilterOperator.In, "[\"get\", \"put\"]"))
            {
                Label = "in",
            },
            new(Condition("RequestPath", FilterOperator.Matches, "\"^/UMBRACO/.+/submit$\""))
            {
                Label = "regex",
            },
            new(Condition("@severity", FilterOperator.In, "[\"trace\", \"fatal\"]"))
            {
                Label = "severity in",
            },
            new(
                new ConditionNode(
                    "RequestMethod",
                    FilterOperator.Equals,
                    JsonSerializer.SerializeToElement("get"),
                    CaseInsensitive: false
                )
            )
            {
                Label = "case-sensitive equals",
            },
            new(new NotNode(Condition("Duration", FilterOperator.GreaterThan, "100")))
            {
                Label = "not a comparison on a missing field",
            },
        ];

    [Theory]
    [MemberData(nameof(SearchBoxInputs))]
    public void Compile_SearchBoxQuery_SelectsTheSameEntriesAsTheFilesSource(string input)
    {
        // Arrange
        ParseResult parsed = SimpleSyntaxParser.Parse(input);
        LogQuery query = Query(
            parsed.Chips.Count == 1 ? parsed.Chips[0] : new AndNode(parsed.Chips),
            parsed.Levels
        );

        // Act
        int[] native = SelectWithCompiledQuery(query);

        // Assert
        Assert.Equal(SelectInCSharp(query), native);
    }

    [Theory]
    [MemberData(nameof(HandBuiltFilters))]
    public void Compile_Filter_SelectsTheSameEntriesAsTheFilesSource(FilterNode filter)
    {
        // Arrange
        LogQuery query = Query(filter, levels: null);

        // Act
        int[] native = SelectWithCompiledQuery(query);

        // Assert
        Assert.Equal(SelectInCSharp(query), native);
    }

    private static LogQuery Query(FilterNode filter, IReadOnlySet<string>? levels) =>
        new()
        {
            Range = new TimeRange(null, null, "1h"),
            Filter = filter,
            Levels = levels,
        };

    // Indexes of the fixture events the compiled expression selects, failing if anything had to
    // be left out of it (the comparison only holds where nothing is unsupported).
    private static int[] SelectWithCompiledQuery(LogQuery query)
    {
        CompileResult compiled = FileQueryCompiler.Compile(query);
        Assert.Empty(compiled.Unsupported);
        Func<LogEvent, bool> filter = NativeFilter.Compile(compiled.Native);
        return
        [
            .. DialectFixtures
                .Events.Select((pair, index) => (pair.Event, index))
                .Where(item => filter(item.Event))
                .Select(item => item.index),
        ];
    }

    private static int[] SelectInCSharp(LogQuery query) =>
        [
            .. DialectFixtures
                .Events.Select((pair, index) => (pair.Record, index))
                .Where(item => LogRecordFilter.Matches(item.Record, query.Filter, query.Levels))
                .Select(item => item.index),
        ];

    private static ConditionNode Condition(string field, FilterOperator op, string valueJson) =>
        new(
            field,
            op,
            valueJson == "null" ? null : JsonDocument.Parse(valueJson).RootElement.Clone()
        );
}
