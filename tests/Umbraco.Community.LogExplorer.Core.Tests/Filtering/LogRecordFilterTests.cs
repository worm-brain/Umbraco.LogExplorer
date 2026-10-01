using System.Text.Json;
using Umbraco.Community.LogExplorer.Core.Filtering;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Records;

namespace Umbraco.Community.LogExplorer.Core.Tests.Filtering;

public class LogRecordFilterTests
{
    private static readonly LogRecord Record = new()
    {
        Id = "1",
        Timestamp = new DateTimeOffset(2026, 9, 2, 0, 41, 27, TimeSpan.Zero),
        SeverityNumber = 13,
        Body = "Request timed out after 30 s",
        Scope = "Client.Web.Diagnostics.SlowRequestLogger",
        Exception = new LogException("System.TimeoutException", "Connection refused", null),
        Attributes = Attributes(
            """
            {
              "StatusCode": 200,
              "RequestPath": "/api/users",
              "Elapsed": "1500",
              "Flag": true,
              "When": "2026-09-02T10:00:00Z",
              "Missing": null,
              "Tags": ["red", "Blue"],
              "Cart": { "Total": 12.5, "Items": [{ "Sku": "A1" }, { "Sku": "B2" }] },
              "host.name": "web-1"
            }
            """
        ),
    };

    // Named cases so a failure reads as the rule that broke.
    private static readonly Dictionary<string, (FilterNode Filter, bool Expected)> Cases = new()
    {
        ["equals number"] = (Cond("StatusCode", FilterOperator.Equals, "200"), true),
        ["equals number given as string"] = (
            Cond("StatusCode", FilterOperator.Equals, "\"200\""),
            true
        ),
        ["equals string ignoring case"] = (
            Cond("RequestPath", FilterOperator.Equals, "\"/API/USERS\""),
            true
        ),
        ["equals string case-sensitive"] = (
            Cond("RequestPath", FilterOperator.Equals, "\"/API/USERS\"", false),
            false
        ),
        ["equals bool"] = (Cond("Flag", FilterOperator.Equals, "true"), true),
        ["equals other number"] = (Cond("StatusCode", FilterOperator.Equals, "500"), false),
        ["not equals other value"] = (Cond("StatusCode", FilterOperator.NotEquals, "500"), true),
        ["not equals same value"] = (Cond("StatusCode", FilterOperator.NotEquals, "200"), false),
        ["not equals missing field"] = (Cond("Nope", FilterOperator.NotEquals, "1"), true),
        ["contains"] = (Cond("RequestPath", FilterOperator.Contains, "\"API\""), true),
        ["starts with"] = (Cond("RequestPath", FilterOperator.StartsWith, "\"/api\""), true),
        ["starts with other prefix"] = (
            Cond("RequestPath", FilterOperator.StartsWith, "\"/umbraco\""),
            false
        ),
        ["ends with"] = (Cond("RequestPath", FilterOperator.EndsWith, "\"users\""), true),
        ["greater than numeric string"] = (
            Cond("Elapsed", FilterOperator.GreaterThan, "1000"),
            true
        ),
        ["greater or equal at boundary"] = (
            Cond("StatusCode", FilterOperator.GreaterOrEqual, "200"),
            true
        ),
        ["less than at boundary"] = (Cond("StatusCode", FilterOperator.LessThan, "200"), false),
        ["less or equal at boundary"] = (
            Cond("StatusCode", FilterOperator.LessOrEqual, "200"),
            true
        ),
        ["greater than earlier date"] = (
            Cond("When", FilterOperator.GreaterThan, "\"2026-09-02T09:00:00+00:00\""),
            true
        ),
        ["less than earlier date"] = (
            Cond("When", FilterOperator.LessThan, "\"2026-09-01T00:00:00Z\""),
            false
        ),
        ["greater than on text compares text"] = (
            Cond("RequestPath", FilterOperator.GreaterThan, "\"/a\""),
            true
        ),
        ["greater than bool is not comparable"] = (
            Cond("Flag", FilterOperator.GreaterThan, "0"),
            false
        ),
        ["in list containing value"] = (Cond("StatusCode", FilterOperator.In, "[404, 200]"), true),
        ["in list without value"] = (Cond("StatusCode", FilterOperator.In, "[500]"), false),
        ["exists nested"] = (Cond("Cart.Total", FilterOperator.Exists, null), true),
        ["exists on null value"] = (Cond("Missing", FilterOperator.Exists, null), false),
        ["not exists on absent field"] = (Cond("Nope", FilterOperator.NotExists, null), true),
        ["matches regex ignoring case"] = (
            Cond("RequestPath", FilterOperator.Matches, "\"^/API/\\\\w+$\""),
            true
        ),
        ["matches regex case-sensitive"] = (
            Cond("RequestPath", FilterOperator.Matches, "\"^/API\"", false),
            false
        ),
        ["nested object path"] = (Cond("Cart.Total", FilterOperator.GreaterThan, "10"), true),
        ["any array element"] = (Cond("Tags[]", FilterOperator.Equals, "\"blue\""), true),
        ["any element of nested array"] = (
            Cond("Cart.Items[].Sku", FilterOperator.Equals, "\"B2\""),
            true
        ),
        ["array without [] is not expanded"] = (
            Cond("Tags", FilterOperator.Equals, "\"red\""),
            false
        ),
        ["dotted attribute key"] = (Cond("host.name", FilterOperator.Equals, "\"web-1\""), true),
        ["field name ignoring case"] = (Cond("statuscode", FilterOperator.Equals, "200"), true),
        ["portable severity"] = (Cond("@severity", FilterOperator.Equals, "\"warn\""), true),
        ["portable scope"] = (Cond("@scope", FilterOperator.StartsWith, "\"Client.Web\""), true),
        ["portable body"] = (Cond("@body", FilterOperator.Contains, "\"timed out\""), true),
        ["portable exception type"] = (
            Cond("@exception.type", FilterOperator.EndsWith, "\"TimeoutException\""),
            true
        ),
        ["portable timestamp"] = (
            Cond("@timestamp", FilterOperator.GreaterOrEqual, "\"2026-09-02T00:41:27Z\""),
            true
        ),
        ["absent portable field"] = (Cond("@traceId", FilterOperator.Exists, null), false),
        ["text words in any order"] = (new TextNode("out timed"), true),
        ["text phrase out of order"] = (new TextNode("out timed", Phrase: true), false),
        ["text ignoring case"] = (new TextNode("REQUEST"), true),
        ["text in exception message"] = (new TextNode("refused"), true),
        ["text absent"] = (new TextNode("deadlock"), false),
        ["empty and"] = (new AndNode([]), true),
        ["empty or"] = (new OrNode([]), false),
        ["and with a failing child"] = (
            new AndNode([new TextNode("timed"), new TextNode("deadlock")]),
            false
        ),
        ["or with a passing child"] = (
            new OrNode([new TextNode("deadlock"), new TextNode("timed")]),
            true
        ),
        ["not"] = (new NotNode(new TextNode("deadlock")), true),
    };

    public static TheoryData<string> CaseNames => [.. Cases.Keys];

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Matches_FilterCase_ReturnsExpectedOutcome(string caseName)
    {
        // Arrange
        (FilterNode filter, bool expected) = Cases[caseName];

        // Act
        bool matched = LogRecordFilter.Matches(Record, filter);

        // Assert
        Assert.Equal(expected, matched);
    }

    [Theory]
    [InlineData("warn", true)]
    [InlineData("error", false)]
    public void Matches_LevelSet_KeepsOnlySelectedLevels(string level, bool expected)
    {
        // Act
        bool matched = LogRecordFilter.Matches(Record, null, new HashSet<string> { level });

        // Assert
        Assert.Equal(expected, matched);
    }

    [Fact]
    public void Matches_InvalidRegex_ThrowsArgumentException()
    {
        // Arrange
        FilterNode filter = Cond("RequestPath", FilterOperator.Matches, "\"(\"");

        // Act
        void Act() => LogRecordFilter.Matches(Record, filter);

        // Assert
        Assert.ThrowsAny<ArgumentException>(Act);
    }

    private static ConditionNode Cond(
        string field,
        FilterOperator op,
        string? valueJson,
        bool caseInsensitive = true
    ) =>
        new(
            field,
            op,
            valueJson is null ? null : JsonDocument.Parse(valueJson).RootElement,
            caseInsensitive
        );

    private static Dictionary<string, JsonElement> Attributes(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
}
