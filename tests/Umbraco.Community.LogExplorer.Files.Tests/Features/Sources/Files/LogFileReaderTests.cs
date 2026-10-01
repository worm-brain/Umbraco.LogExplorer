using System.Text;
using Serilog.Events;
using Umbraco.Community.LogExplorer.Features.Sources.Files;
using Umbraco.Community.LogExplorer.Files.Tests.TestSupport;

namespace Umbraco.Community.LogExplorer.Files.Tests.Features.Sources.Files;

/// <summary>
/// The reader yields every event with its line's byte offset, skips a half-written last line
/// silently and counts other bad lines (#30). Offsets are listed in <see cref="LogFixtures"/>.
/// </summary>
public class LogFileReaderTests
{
    [Fact]
    public void ReadEvents_CrlfFileWithBlankLine_ReturnsTheByteOffsetOfEachEventsLine()
    {
        // Arrange
        var reader = new LogFileReader(LogFixtures.PathOf(LogFixtures.CrlfFile));

        // Act
        long[] offsets = reader
            .ReadEvents(TestContext.Current.CancellationToken)
            .Select(logEvent => logEvent.Offset)
            .ToArray();

        // Assert
        Assert.Equal([0L, 147L, 523L], offsets);
    }

    [Fact]
    public void ReadEvents_CrlfFileWithBlankLine_EachEventEndsWhereTheNextLineStarts()
    {
        // Arrange
        var reader = new LogFileReader(LogFixtures.PathOf(LogFixtures.CrlfFile));

        // Act
        long[] ends = reader
            .ReadEvents(TestContext.Current.CancellationToken)
            .Select(logEvent => logEvent.End)
            .ToArray();

        // Assert: the first event's line is followed by the blank line at 145; the file is 995 bytes.
        Assert.Equal([145L, 523L, 995L], ends);
    }

    [Fact]
    public void ReadEvents_StartOffsetAtALine_ReturnsThatLineAndTheRest()
    {
        // Arrange
        var reader = new LogFileReader(LogFixtures.PathOf(LogFixtures.CrlfFile));

        // Act
        long[] offsets = reader
            .ReadEvents(147, TestContext.Current.CancellationToken)
            .Select(logEvent => logEvent.Offset)
            .ToArray();

        // Assert
        Assert.Equal([147L, 523L], offsets);
    }

    [Fact]
    public void ReadEvents_NegativeStartOffset_ThrowsArgumentOutOfRange()
    {
        // Arrange
        var reader = new LogFileReader(LogFixtures.PathOf(LogFixtures.CrlfFile));

        // Act
        void Read() => _ = reader.ReadEvents(-1, TestContext.Current.CancellationToken);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(Read);
    }

    [Fact]
    public void ReadEvents_BlankLine_IsNotCountedAsMalformed()
    {
        // Arrange
        var reader = new LogFileReader(LogFixtures.PathOf(LogFixtures.CrlfFile));

        // Act
        _ = reader.ReadEvents(TestContext.Current.CancellationToken).ToArray();

        // Assert
        Assert.Equal(0, reader.MalformedLineCount);
    }

    [Fact]
    public void ReadEvents_MultiByteCharacters_AreDecodedAsUtf8()
    {
        // Arrange
        var reader = new LogFileReader(LogFixtures.PathOf(LogFixtures.CrlfFile));

        // Act
        LogEvent first = reader.ReadEvents(TestContext.Current.CancellationToken).First().Event;

        // Assert
        Assert.Equal(new ScalarValue("Zürich"), first.Properties["Name"]);
    }

    [Fact]
    public void ReadEvents_MalformedMiddleLineAndTruncatedLastLine_ReturnsOnlyTheGoodLines()
    {
        // Arrange
        var reader = new LogFileReader(LogFixtures.PathOf(LogFixtures.RolledFile));

        // Act
        long[] offsets = reader
            .ReadEvents(TestContext.Current.CancellationToken)
            .Select(logEvent => logEvent.Offset)
            .ToArray();

        // Assert
        Assert.Equal([0L, 62L, 190L], offsets);
    }

    [Fact]
    public void ReadEvents_MalformedMiddleLineAndTruncatedLastLine_CountsOnlyTheMiddleLine()
    {
        // Arrange
        var reader = new LogFileReader(LogFixtures.PathOf(LogFixtures.RolledFile));

        // Act
        _ = reader.ReadEvents(TestContext.Current.CancellationToken).ToArray();

        // Assert
        Assert.Equal(1, reader.MalformedLineCount);
    }

    [Fact]
    public void ReadEvents_CompleteLastLineWithoutLineBreak_IsReturned()
    {
        // Arrange
        using var directory = new TempDirectory();
        string path = directory.Write(
            "log.json",
            Encoding.UTF8.GetBytes(Line("First") + "\n" + Line("Last"))
        );
        var reader = new LogFileReader(path);

        // Act
        string[] templates = reader
            .ReadEvents(TestContext.Current.CancellationToken)
            .Select(logEvent => logEvent.Event.MessageTemplate.Text)
            .ToArray();

        // Assert
        Assert.Equal(["First", "Last"], templates);
    }

    [Fact]
    public void ReadEvents_LineLongerThanTheReadBuffer_IsReadWholeAndOffsetsStayExact()
    {
        // Arrange: a 100,000-character property makes the first line span two 64 KB reads.
        using var directory = new TempDirectory();
        string longLine =
            "{\"@t\":\"2026-10-01T12:00:00Z\",\"@mt\":\"Long\",\"Payload\":\""
            + new string('x', 100_000)
            + "\"}";
        string path = directory.Write(
            "log.json",
            Encoding.UTF8.GetBytes(longLine + "\n" + Line("Next") + "\n")
        );
        var reader = new LogFileReader(path);

        // Act
        long[] offsets = reader
            .ReadEvents(TestContext.Current.CancellationToken)
            .Select(logEvent => logEvent.Offset)
            .ToArray();

        // Assert: 100,055 bytes of JSON plus the line break.
        Assert.Equal([0L, 100_056L], offsets);
    }

    [Fact]
    public void ReadEvents_ByteOrderMark_FirstLineParses()
    {
        // Arrange
        using var directory = new TempDirectory();
        string path = directory.Write(
            "log.json",
            [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes(Line("First") + "\n")]
        );
        var reader = new LogFileReader(path);

        // Act
        LogFileEvent first = Assert.Single(
            reader.ReadEvents(TestContext.Current.CancellationToken)
        );

        // Assert
        Assert.Equal("First", first.Event.MessageTemplate.Text);
    }

    [Fact]
    public void ReadEvents_FileOpenForWritingBySerilog_CanStillBeRead()
    {
        // Arrange: Serilog's file sink holds the file open for writing and shares it for reading only.
        using var directory = new TempDirectory();
        string path = directory.Write("log.json", Encoding.UTF8.GetBytes(Line("First") + "\n"));
        using var writer = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        var reader = new LogFileReader(path);

        // Act
        LogFileEvent first = Assert.Single(
            reader.ReadEvents(TestContext.Current.CancellationToken)
        );

        // Assert
        Assert.Equal("First", first.Event.MessageTemplate.Text);
    }

    [Fact]
    public void ReadEvents_CancelledToken_ThrowsOperationCanceled()
    {
        // Arrange
        var reader = new LogFileReader(LogFixtures.PathOf(LogFixtures.CrlfFile));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        void Read() => _ = reader.ReadEvents(cancellation.Token).ToArray();

        // Assert
        Assert.Throws<OperationCanceledException>(Read);
    }

    [Fact]
    public void ReadEvents_MissingFile_ThrowsFileNotFound()
    {
        // Arrange
        var reader = new LogFileReader(LogFixtures.PathOf("UmbracoTraceLog.NOPE.20261001.json"));

        // Act
        void Read() => _ = reader.ReadEvents(TestContext.Current.CancellationToken).ToArray();

        // Assert
        Assert.Throws<FileNotFoundException>(Read);
    }

    private static string Line(string template) =>
        $$"""{"@t":"2026-10-01T12:00:00Z","@mt":"{{template}}"}""";
}
