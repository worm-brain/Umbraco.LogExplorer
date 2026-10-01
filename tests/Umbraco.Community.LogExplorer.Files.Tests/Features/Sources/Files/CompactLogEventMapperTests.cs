using System.Text.Json;
using Serilog.Formatting.Compact.Reader;
using Umbraco.Community.LogExplorer.Core.Records;
using Umbraco.Community.LogExplorer.Features.Sources.Files;

namespace Umbraco.Community.LogExplorer.Files.Tests.Features.Sources.Files;

/// <summary>
/// Compact JSON maps to <see cref="LogRecord"/> per the files column of BRIEF §9.1 (#30). Each
/// test reads one compact JSON line through the real Serilog reader, as the provider does.
/// </summary>
public class CompactLogEventMapperTests
{
    private static readonly LogFile File = new(
        "C:/logs/UmbracoTraceLog.NODE2.20261001.json",
        "UmbracoTraceLog.NODE2.20261001.json",
        "NODE2",
        new DateOnly(2026, 10, 1),
        0
    );

    [Fact]
    public void Map_Event_IdIsFileNameAndLineOffset()
    {
        // Arrange
        const string json = """{"@t":"2026-10-01T12:00:00Z","@mt":"Started"}""";

        // Act
        LogRecord record = Map(json, offset: 147);

        // Assert
        Assert.Equal("UmbracoTraceLog.NODE2.20261001.json:147", record.Id);
    }

    [Fact]
    public void Map_Timestamp_KeepsTheStoredOffset()
    {
        // Arrange
        const string json = """{"@t":"2026-10-01T12:00:00.1234567+01:00","@mt":"Started"}""";

        // Act
        LogRecord record = Map(json);

        // Assert
        Assert.Equal(
            new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.FromHours(1)).AddTicks(1_234_567),
            record.Timestamp
        );
    }

    [Fact]
    public void Map_NoLevel_IsInformation()
    {
        // Arrange
        const string json = """{"@t":"2026-10-01T12:00:00Z","@mt":"Started"}""";

        // Act
        LogRecord record = Map(json);

        // Assert
        Assert.Equal((9, "Information"), (record.SeverityNumber, record.SeverityText));
    }

    [Fact]
    public void Map_WarningLevel_IsWarnSeverity()
    {
        // Arrange
        const string json = """{"@t":"2026-10-01T12:00:00Z","@mt":"Slow","@l":"Warning"}""";

        // Act
        LogRecord record = Map(json);

        // Assert
        Assert.Equal((13, "Warning"), (record.SeverityNumber, record.SeverityText));
    }

    [Fact]
    public void Map_Template_BodyIsRenderedWithStringsQuoted()
    {
        // Arrange
        const string json = """
            {"@t":"2026-10-01T12:00:00Z","@mt":"HTTP {RequestMethod} {RequestPath} responded {StatusCode}","RequestMethod":"GET","RequestPath":"/","StatusCode":200}
            """;

        // Act
        LogRecord record = Map(json);

        // Assert
        Assert.Equal("HTTP \"GET\" \"/\" responded 200", record.Body);
    }

    [Fact]
    public void Map_StoredRenderings_BodyUsesThem()
    {
        // Arrange
        const string json = """
            {"@t":"2026-10-01T12:00:00Z","@mt":"Number {N:x8}","@r":["0000002a"],"N":42}
            """;

        // Act
        LogRecord record = Map(json);

        // Assert
        Assert.Equal("Number 0000002a", record.Body);
    }

    [Fact]
    public void Map_Template_IsTheMessageTemplate()
    {
        // Arrange
        const string json = """
            {"@t":"2026-10-01T12:00:00Z","@mt":"Basket {RequestId} priced","RequestId":"0HN5"}
            """;

        // Act
        LogRecord record = Map(json);

        // Assert
        Assert.Equal("Basket {RequestId} priced", record.MessageTemplate);
    }

    [Fact]
    public void Map_Template_TemplateHashIsLowerCaseMd5OfTheTemplate()
    {
        // Arrange
        const string json = """{"@t":"2026-10-01T12:00:00Z","@mt":"Acquiring MainDom."}""";

        // Act
        LogRecord record = Map(json);

        // Assert
        Assert.Equal("96ec8f0b70b9ffdd2252f614f064e1ed", record.TemplateHash);
    }

    [Fact]
    public void Map_TraceAndSpan_AreCopied()
    {
        // Arrange
        const string json = """
            {"@t":"2026-10-01T12:00:00Z","@mt":"In a request","@tr":"f73dfa962952f5780ed2108020fe2029","@sp":"f426677ff16a5408"}
            """;

        // Act
        LogRecord record = Map(json);

        // Assert
        Assert.Equal(
            ("f73dfa962952f5780ed2108020fe2029", "f426677ff16a5408"),
            (record.TraceId, record.SpanId)
        );
    }

    [Fact]
    public void Map_NoTrace_TraceAndSpanAreNull()
    {
        // Arrange
        const string json = """{"@t":"2026-10-01T12:00:00Z","@mt":"Background"}""";

        // Act
        LogRecord record = Map(json);

        // Assert
        Assert.Equal((null, null), (record.TraceId, record.SpanId));
    }

    [Fact]
    public void Map_SourceContext_IsTheScopeAndStaysAnAttribute()
    {
        // Arrange
        const string json = """
            {"@t":"2026-10-01T12:00:00Z","@mt":"Acquired","SourceContext":"Umbraco.Cms.Core.Runtime.MainDom"}
            """;

        // Act
        LogRecord record = Map(json);

        // Assert
        Assert.Equal(
            ("Umbraco.Cms.Core.Runtime.MainDom", "\"Umbraco.Cms.Core.Runtime.MainDom\""),
            (record.Scope, record.Attributes["SourceContext"].GetRawText())
        );
    }

    [Theory]
    [InlineData("200", JsonValueKind.Number)]
    [InlineData("130.78", JsonValueKind.Number)]
    [InlineData("123456789012345678901234567890", JsonValueKind.Number)]
    [InlineData("true", JsonValueKind.True)]
    [InlineData("null", JsonValueKind.Null)]
    [InlineData("\"2026-10-01T12:00:00Z\"", JsonValueKind.String)]
    public void Map_ScalarProperty_KeepsItsJsonKindAndValue(string value, JsonValueKind kind)
    {
        // Arrange
        string json = $$"""{"@t":"2026-10-01T12:00:00Z","@mt":"Scalar","Value":{{value}}}""";

        // Act
        LogRecord record = Map(json);

        // Assert
        JsonElement attribute = record.Attributes["Value"];
        Assert.Equal((kind, value), (attribute.ValueKind, attribute.GetRawText()));
    }

    [Fact]
    public void Map_NestedObjectProperty_KeepsItsStructure()
    {
        // Arrange
        const string json = """
            {"@t":"2026-10-01T12:00:00Z","@mt":"Priced {@Cart}","Cart":{"Total":130.78,"Items":5,"Lines":[{"Sku":"A1"}]}}
            """;

        // Act
        LogRecord record = Map(json);

        // Assert
        Assert.Equal(
            """{"Total":130.78,"Items":5,"Lines":[{"Sku":"A1"}]}""",
            record.Attributes["Cart"].GetRawText()
        );
    }

    [Fact]
    public void Map_ArrayProperty_IsAJsonArray()
    {
        // Arrange
        const string json = """
            {"@t":"2026-10-01T12:00:00Z","@mt":"Tagged {@Tags}","Tags":["new","sale",3]}
            """;

        // Act
        LogRecord record = Map(json);

        // Assert
        Assert.Equal("""["new","sale",3]""", record.Attributes["Tags"].GetRawText());
    }

    [Fact]
    public void Map_TypeTaggedObject_KeepsTheTagAsTypeMember()
    {
        // Arrange
        const string json = """
            {"@t":"2026-10-01T12:00:00Z","@mt":"Priced {@Cart}","Cart":{"$type":"Basket","Total":1}}
            """;

        // Act
        LogRecord record = Map(json);

        // Assert
        Assert.Equal("""{"$type":"Basket","Total":1}""", record.Attributes["Cart"].GetRawText());
    }

    [Fact]
    public void Map_MachineNameProperty_IsTheHostName()
    {
        // Arrange
        const string json = """
            {"@t":"2026-10-01T12:00:00Z","@mt":"Started","MachineName":"WORM"}
            """;

        // Act
        LogRecord record = Map(json);

        // Assert
        Assert.Equal("WORM", record.Resource["host.name"].GetString());
    }

    [Fact]
    public void Map_NoMachineNameProperty_HostNameComesFromTheFileName()
    {
        // Arrange
        const string json = """{"@t":"2026-10-01T12:00:00Z","@mt":"Started"}""";

        // Act
        LogRecord record = Map(json);

        // Assert
        Assert.Equal("NODE2", record.Resource["host.name"].GetString());
    }

    [Fact]
    public void Map_NoMachineNameAnywhere_HasNoHostName()
    {
        // Arrange
        const string json = """{"@t":"2026-10-01T12:00:00Z","@mt":"Started"}""";

        // Act
        LogRecord record = Map(json, File with { MachineName = null });

        // Assert
        Assert.Empty(record.Resource);
    }

    [Fact]
    public void Map_NoException_ExceptionIsNull()
    {
        // Arrange
        const string json = """{"@t":"2026-10-01T12:00:00Z","@mt":"Fine"}""";

        // Act
        LogRecord record = Map(json);

        // Assert
        Assert.Null(record.Exception);
    }

    [Fact]
    public void Map_ExceptionWithHResult_SplitsTypeMessageAndStackTrace()
    {
        // Arrange
        const string json = """
            {"@t":"2026-10-01T12:00:00Z","@mt":"Unavailable","@l":"Warning","@x":"Microsoft.Data.Sqlite.SqliteException (0x80004005): SQLite Error 14: 'unable to open database file'.\r\n   at Microsoft.Data.Sqlite.SqliteConnection.Open()\r\n   at Umbraco.Extensions.DbConnectionExtensions.IsAvailable(IDbConnection connection)"}
            """;

        // Act
        LogRecord record = Map(json);

        // Assert
        Assert.Equal(
            new LogException(
                "Microsoft.Data.Sqlite.SqliteException",
                "SQLite Error 14: 'unable to open database file'.",
                "   at Microsoft.Data.Sqlite.SqliteConnection.Open()\r\n   at Umbraco.Extensions.DbConnectionExtensions.IsAvailable(IDbConnection connection)"
            ),
            record.Exception
        );
    }

    [Fact]
    public void Map_MultiLineExceptionMessage_KeepsEveryMessageLine()
    {
        // Arrange
        const string json = """
            {"@t":"2026-10-01T12:00:00Z","@mt":"Failed","@l":"Error","@x":"System.InvalidOperationException: First line\nSecond line\n   at A.B()"}
            """;

        // Act
        LogRecord record = Map(json);

        // Assert
        Assert.Equal(
            new LogException(
                "System.InvalidOperationException",
                "First line\nSecond line",
                "   at A.B()"
            ),
            record.Exception
        );
    }

    [Fact]
    public void Map_WrappedException_KeepsTheOuterMessageAndPutsTheInnerExceptionInTheStackTrace()
    {
        // Arrange
        const string json = """
            {"@t":"2026-10-01T12:00:00Z","@mt":"Failed","@l":"Error","@x":"System.Exception: outer ---> System.IO.IOException: inner\r\n   at A.B()\r\n   --- End of inner exception stack trace ---\r\n   at C.D()"}
            """;

        // Act
        LogRecord record = Map(json);

        // Assert
        Assert.Equal(
            new LogException(
                "System.Exception",
                "outer",
                "---> System.IO.IOException: inner\r\n   at A.B()\r\n   --- End of inner exception stack trace ---\r\n   at C.D()"
            ),
            record.Exception
        );
    }

    [Fact]
    public void Map_ExceptionTextWithoutATypeName_IsTheMessage()
    {
        // Arrange
        const string json = """
            {"@t":"2026-10-01T12:00:00Z","@mt":"Failed","@l":"Error","@x":"Something: went wrong"}
            """;

        // Act
        LogRecord record = Map(json);

        // Assert
        Assert.Equal(new LogException(null, "Something: went wrong", null), record.Exception);
    }

    private static LogRecord Map(string json, LogFile? file = null, long offset = 0) =>
        CompactLogEventMapper.Map(LogEventReader.ReadFromString(json), file ?? File, offset);
}
