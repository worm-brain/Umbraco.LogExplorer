using System.Text.Json;
using Umbraco.Community.LogExplorer.Core.Filtering;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Records;
using Umbraco.Community.LogExplorer.Core.SimpleSyntax;
using Umbraco.Community.LogExplorer.Features.Sources.Files;
using Umbraco.Community.LogExplorer.Files.Tests.TestSupport;

namespace Umbraco.Community.LogExplorer.Files.Tests.Features.Sources.Files;

/// <summary>
/// The pager's fast path (ADR 0017) tests the filter against a record mapped with only the parts
/// <see cref="FilterRecordParts.For"/> names. For every filter it must select exactly what
/// <see cref="LogRecordFilter"/> selects over the fully mapped record, across the dialect fixtures
/// (every level, typed and nested properties, exceptions, trace ids).
/// </summary>
public class FilterRecordPartsTests
{
    private static readonly LogFile File = new(
        "UmbracoTraceLog.WORM.20261001.json",
        "UmbracoTraceLog.WORM.20261001.json",
        "WORM",
        new DateOnly(2026, 10, 1),
        0
    );

    // What a user types in the search box; the chips AND together.
    public static TheoryData<string> SearchBoxInputs =>
        [
            "timeout",
            "\"background job\"",
            "unhandled timeout",
            "level:warn",
            "path:/api*",
            "-SourceContext:Umbraco.Cms*",
            "source:Umbraco.Cms.Core*",
            "Duration>1000",
            "has:ContentId",
            "-has:ContentId",
            "StatusCode:5*",
            "Cart.Total>100",
            "trace:" + DialectFixtures.TraceId,
            "msg:*published*",
            "template:\"Boot failed\"",
            "ex:System.InvalidOperationException",
            "level:info path:/api* -StatusCode:500",
        ];

    // Every portable field, resource paths, case variants and the unknown "@" name, which
    // LogFields reads as an attribute.
    public static IEnumerable<TheoryDataRow<FilterNode>> HandBuiltFilters =>
        [
            new(Condition("@body", FilterOperator.Contains, "\"basket\"")) { Label = "@body" },
            new(Condition("@BODY", FilterOperator.Contains, "\"basket\""))
            {
                Label = "@BODY upper case",
            },
            new(Condition("@template", FilterOperator.StartsWith, "\"HTTP\""))
            {
                Label = "@template",
            },
            new(Condition("@exception.type", FilterOperator.Exists, "null"))
            {
                Label = "@exception.type",
            },
            new(Condition("@exception.message", FilterOperator.Contains, "\"timeout\""))
            {
                Label = "@exception.message",
            },
            new(Condition("@resource.host.name", FilterOperator.Equals, "\"worm\""))
            {
                Label = "@resource.host.name",
            },
            new(Condition("@severity", FilterOperator.In, "[\"trace\", \"fatal\"]"))
            {
                Label = "@severity",
            },
            new(Condition("@scope", FilterOperator.EndsWith, "\"Service\"")) { Label = "@scope" },
            new(Condition("@traceId", FilterOperator.Exists, "null")) { Label = "@traceId" },
            new(Condition("@spanId", FilterOperator.NotExists, "null")) { Label = "@spanId" },
            new(Condition("@timestamp", FilterOperator.GreaterOrEqual, "\"2026-10-01T12:00:04Z\""))
            {
                Label = "@timestamp",
            },
            new(Condition("@unknown", FilterOperator.NotExists, "null"))
            {
                Label = "unknown @ name",
            },
            new(Condition("Tags[]", FilterOperator.Equals, "\"SALE\"")) { Label = "array element" },
            new(
                new OrNode([
                    new TextNode("boot"),
                    new NotNode(Condition("@template", FilterOperator.Contains, "\"{\"")),
                ])
            )
            {
                Label = "or of text and negated template",
            },
        ];

    [Theory]
    [MemberData(nameof(SearchBoxInputs))]
    public void For_SearchBoxQuery_PartialRecordsMatchLikeFullRecords(string input)
    {
        // Arrange
        ParseResult parsed = SimpleSyntaxParser.Parse(input);
        FilterNode? filter = parsed.Chips.Count switch
        {
            0 => null,
            1 => parsed.Chips[0],
            _ => new AndNode(parsed.Chips),
        };

        // Act
        bool[] partial = SelectWithPartialRecords(filter, parsed.Levels);

        // Assert
        Assert.Equal(SelectWithFullRecords(filter, parsed.Levels), partial);
    }

    [Theory]
    [MemberData(nameof(HandBuiltFilters))]
    public void For_HandBuiltFilter_PartialRecordsMatchLikeFullRecords(FilterNode filter)
    {
        // Act
        bool[] partial = SelectWithPartialRecords(filter, levels: null);

        // Assert
        Assert.Equal(SelectWithFullRecords(filter, levels: null), partial);
    }

    [Fact]
    public void For_TextSearch_NeedsOnlyTheBodyAndException()
    {
        // Act
        RecordParts parts = FilterRecordParts.For(new TextNode("timeout"));

        // Assert
        Assert.Equal(RecordParts.Body | RecordParts.Exception, parts);
    }

    [Fact]
    public void For_NoFilter_NeedsNoCostlyParts()
    {
        // Act
        RecordParts parts = FilterRecordParts.For(null);

        // Assert
        Assert.Equal(RecordParts.None, parts);
    }

    [Fact]
    public void For_UnknownNodeType_NeedsTheWholeRecord()
    {
        // Act
        RecordParts parts = FilterRecordParts.For(new UnknownNode());

        // Assert
        Assert.Equal(RecordParts.All, parts);
    }

    private static bool[] SelectWithFullRecords(FilterNode? filter, IReadOnlySet<string>? levels) =>
        [
            .. DialectFixtures.Events.Select(item =>
                LogRecordFilter.Matches(item.Record, filter, levels)
            ),
        ];

    private static bool[] SelectWithPartialRecords(FilterNode? filter, IReadOnlySet<string>? levels)
    {
        RecordParts parts = FilterRecordParts.For(filter);
        return
        [
            .. DialectFixtures.Events.Select(
                (item, index) =>
                {
                    LogRecord probe = CompactLogEventMapper.Map(item.Event, File, index, parts);
                    return LogRecordFilter.Matches(probe, filter, levels);
                }
            ),
        ];
    }

    private static ConditionNode Condition(string field, FilterOperator op, string json) =>
        new(field, op, JsonSerializer.Deserialize<JsonElement>(json));

    private sealed record UnknownNode : FilterNode;
}
