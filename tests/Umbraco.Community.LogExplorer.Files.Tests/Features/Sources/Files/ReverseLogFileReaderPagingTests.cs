using System.Text;
using Serilog.Events;
using Umbraco.Community.LogExplorer.Features.Sources.Files;
using Umbraco.Community.LogExplorer.Files.Tests.TestSupport;

namespace Umbraco.Community.LogExplorer.Files.Tests.Features.Sources.Files;

/// <summary>
/// The reverse reader gives the forward reader's events and offsets in reverse, whatever the block
/// size, so newest-first pages can start from the end of a file (#31). Offsets are listed in
/// <see cref="LogFixtures"/>.
/// </summary>
public class ReverseLogFileReaderPagingTests
{
    [Theory]
    [InlineData(LogFixtures.CrlfFile, 1)]
    [InlineData(LogFixtures.CrlfFile, 7)]
    [InlineData(LogFixtures.CrlfFile, 64 * 1024)]
    [InlineData(LogFixtures.RolledFile, 1)]
    [InlineData(LogFixtures.RolledFile, 7)]
    [InlineData(LogFixtures.RolledFile, 64 * 1024)]
    public void ReadEvents_AnyBlockSize_ReturnsTheForwardReadersEventsInReverse(
        string fileName,
        int blockSize
    )
    {
        // Arrange
        string path = LogFixtures.PathOf(fileName);
        var expected = new LogFileReader(path)
            .ReadEvents(TestContext.Current.CancellationToken)
            .Select(Describe)
            .Reverse()
            .ToArray();
        var reader = new ReverseLogFileReader(path, blockSize);

        // Act
        var actual = reader
            .ReadEvents(TestContext.Current.CancellationToken)
            .Select(Describe)
            .ToArray();

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ReadEvents_CrlfFile_ReturnsOffsetsNewestFirst()
    {
        // Arrange
        var reader = new ReverseLogFileReader(LogFixtures.PathOf(LogFixtures.CrlfFile));

        // Act
        long[] offsets = reader
            .ReadEvents(TestContext.Current.CancellationToken)
            .Select(logEvent => logEvent.Offset)
            .ToArray();

        // Assert
        Assert.Equal([523L, 147L, 0L], offsets);
    }

    [Fact]
    public void ReadEvents_MultiByteCharacterSplitByEveryBlockBoundary_IsDecodedAsUtf8()
    {
        // Arrange: one-byte blocks split every multi-byte character.
        var reader = new ReverseLogFileReader(LogFixtures.PathOf(LogFixtures.CrlfFile), 1);

        // Act
        LogEvent first = reader.ReadEvents(TestContext.Current.CancellationToken).Last().Event;

        // Assert
        Assert.Equal(new ScalarValue("Zürich"), first.Properties["Name"]);
    }

    [Fact]
    public void ReadEvents_LineLongerThanABlock_IsReadWholeAndOffsetsStayExact()
    {
        // Arrange: a 100,000-character property makes the first line span two 64 KB blocks.
        using var directory = new TempDirectory();
        string longLine =
            "{\"@t\":\"2026-10-01T12:00:00Z\",\"@mt\":\"Long\",\"Payload\":\""
            + new string('x', 100_000)
            + "\"}";
        string path = directory.Write(
            "log.json",
            Encoding.UTF8.GetBytes(longLine + "\n" + Line("Next") + "\n")
        );
        var reader = new ReverseLogFileReader(path);

        // Act
        long[] offsets = reader
            .ReadEvents(TestContext.Current.CancellationToken)
            .Select(logEvent => logEvent.Offset)
            .ToArray();

        // Assert: 100,055 bytes of JSON plus the line break.
        Assert.Equal([100_056L, 0L], offsets);
    }

    [Fact]
    public void ReadEvents_MalformedMiddleLineAndTruncatedLastLine_ReturnsOnlyTheGoodLines()
    {
        // Arrange
        var reader = new ReverseLogFileReader(LogFixtures.PathOf(LogFixtures.RolledFile));

        // Act
        long[] offsets = reader
            .ReadEvents(TestContext.Current.CancellationToken)
            .Select(logEvent => logEvent.Offset)
            .ToArray();

        // Assert
        Assert.Equal([190L, 62L, 0L], offsets);
    }

    [Fact]
    public void ReadEvents_MalformedMiddleLineAndTruncatedLastLine_CountsOnlyTheMiddleLine()
    {
        // Arrange
        var reader = new ReverseLogFileReader(LogFixtures.PathOf(LogFixtures.RolledFile));

        // Act
        _ = reader.ReadEvents(TestContext.Current.CancellationToken).ToArray();

        // Assert
        Assert.Equal(1, reader.MalformedLineCount);
    }

    [Fact]
    public void ReadEvents_CompleteLastLineWithoutLineBreak_IsReturnedFirst()
    {
        // Arrange
        using var directory = new TempDirectory();
        string path = directory.Write(
            "log.json",
            Encoding.UTF8.GetBytes(Line("First") + "\n" + Line("Last"))
        );
        var reader = new ReverseLogFileReader(path);

        // Act
        string[] templates = reader
            .ReadEvents(TestContext.Current.CancellationToken)
            .Select(logEvent => logEvent.Event.MessageTemplate.Text)
            .ToArray();

        // Assert
        Assert.Equal(["Last", "First"], templates);
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
        var reader = new ReverseLogFileReader(path, 4);

        // Act
        LogFileEvent first = Assert.Single(
            reader.ReadEvents(TestContext.Current.CancellationToken)
        );

        // Assert
        Assert.Equal("First", first.Event.MessageTemplate.Text);
    }

    [Fact]
    public void ReadEvents_EndOffsetAtALine_ReturnsOnlyTheLinesBeforeIt()
    {
        // Arrange
        var reader = new ReverseLogFileReader(LogFixtures.PathOf(LogFixtures.CrlfFile));

        // Act
        long[] offsets = reader
            .ReadEvents(endOffset: 523, TestContext.Current.CancellationToken)
            .Select(logEvent => logEvent.Offset)
            .ToArray();

        // Assert
        Assert.Equal([147L, 0L], offsets);
    }

    [Fact]
    public void ReadEvents_EndOffsetAtAnEventsEnd_ReturnsThatEventFirst()
    {
        // Arrange
        string path = LogFixtures.PathOf(LogFixtures.CrlfFile);
        LogFileEvent middle = new LogFileReader(path)
            .ReadEvents(TestContext.Current.CancellationToken)
            .Single(logEvent => logEvent.Offset == 147);
        var reader = new ReverseLogFileReader(path);

        // Act
        LogFileEvent first = reader
            .ReadEvents(middle.End, TestContext.Current.CancellationToken)
            .First();

        // Assert
        Assert.Equal(147L, first.Offset);
    }

    [Fact]
    public void ReadEvents_CancelledToken_ThrowsOperationCanceled()
    {
        // Arrange
        var reader = new ReverseLogFileReader(LogFixtures.PathOf(LogFixtures.CrlfFile));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        void Read() => _ = reader.ReadEvents(cancellation.Token).ToArray();

        // Assert
        Assert.Throws<OperationCanceledException>(Read);
    }

    private static (long Offset, long End, string Template) Describe(LogFileEvent logEvent) =>
        (logEvent.Offset, logEvent.End, logEvent.Event.MessageTemplate.Text);

    private static string Line(string template) =>
        $$"""{"@t":"2026-10-01T12:00:00Z","@mt":"{{template}}"}""";
}
