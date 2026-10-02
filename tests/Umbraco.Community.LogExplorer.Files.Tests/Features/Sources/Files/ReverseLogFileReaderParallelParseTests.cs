using System.Globalization;
using System.Text;
using Umbraco.Community.LogExplorer.Features.Sources.Files;
using Umbraco.Community.LogExplorer.Files.Tests.TestSupport;

namespace Umbraco.Community.LogExplorer.Files.Tests.Features.Sources.Files;

/// <summary>
/// Parallel parsing (ADR 0026) changes only when lines are parsed: the events, their order and
/// offsets, the malformed-line count and the bytes-read count match a one-by-one read.
/// </summary>
public class ReverseLogFileReaderParallelParseTests
{
    [Theory]
    [InlineData(LogFixtures.CrlfFile, 1)]
    [InlineData(LogFixtures.CrlfFile, 7)]
    [InlineData(LogFixtures.CrlfFile, ReverseLogFileReader.ParallelBlockSize)]
    [InlineData(LogFixtures.RolledFile, 1)]
    [InlineData(LogFixtures.RolledFile, 7)]
    [InlineData(LogFixtures.RolledFile, ReverseLogFileReader.ParallelBlockSize)]
    public void ReadEvents_ParallelParse_ReturnsTheSameEventsAsOneByOne(
        string fileName,
        int blockSize
    )
    {
        // Arrange
        string path = LogFixtures.PathOf(fileName);
        var expected = new ReverseLogFileReader(path).ReadEvents(Token).Select(Describe).ToArray();
        var reader = new ReverseLogFileReader(path, blockSize, parallelParse: true);

        // Act
        var actual = reader.ReadEvents(Token).Select(Describe).ToArray();

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ReadEvents_ParallelParseOverMalformedAndTruncatedLines_CountsOnlyTheMiddleLine()
    {
        // Arrange
        var reader = new ReverseLogFileReader(
            LogFixtures.PathOf(LogFixtures.RolledFile),
            ReverseLogFileReader.ParallelBlockSize,
            parallelParse: true
        );

        // Act
        _ = reader.ReadEvents(Token).ToArray();

        // Assert
        Assert.Equal(1, reader.MalformedLineCount);
    }

    [Fact]
    public void ReadEvents_ParallelParse_ReportsTheBytesReadADefaultReaderReportsAtEachEvent()
    {
        // Arrange: about 300 KB, so a default reader crosses several 64 KB blocks.
        using var directory = new TempDirectory();
        string path = directory.Write("log.json", ManyLines(3_000));
        var oneByOne = new ReverseLogFileReader(path);
        long[] expected = [.. oneByOne.ReadEvents(Token).Select(_ => oneByOne.BytesRead)];
        var parallel = new ReverseLogFileReader(
            path,
            ReverseLogFileReader.ParallelBlockSize,
            parallelParse: true
        );

        // Act
        long[] actual = [.. parallel.ReadEvents(Token).Select(_ => parallel.BytesRead)];

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ReadEvents_ParallelParseWithEndOffset_ReturnsOnlyTheLinesBeforeIt()
    {
        // Arrange
        var reader = new ReverseLogFileReader(
            LogFixtures.PathOf(LogFixtures.CrlfFile),
            ReverseLogFileReader.ParallelBlockSize,
            parallelParse: true
        );

        // Act
        long[] offsets = [.. reader.ReadEvents(endOffset: 523, Token).Select(e => e.Offset)];

        // Assert
        Assert.Equal([147L, 0L], offsets);
    }

    [Fact]
    public void ReadEvents_ParallelParseWithCancelledToken_ThrowsOperationCanceled()
    {
        // Arrange
        var reader = new ReverseLogFileReader(
            LogFixtures.PathOf(LogFixtures.CrlfFile),
            ReverseLogFileReader.ParallelBlockSize,
            parallelParse: true
        );
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        void Read() => _ = reader.ReadEvents(cancellation.Token).ToArray();

        // Assert
        Assert.Throws<OperationCanceledException>(Read);
    }

    [Fact]
    public void DefaultBlocksReadBefore_LineAfterABlockBoundary_CountsTheBlocksUpToItsLineBreak()
    {
        // Arrange: the \n before the line sits at the first byte of the second block from the end.
        long windowEnd = 3 * ReverseLogFileReader.DefaultBlockSize;
        long lineStart = ReverseLogFileReader.DefaultBlockSize * 2;

        // Act
        long read = ReverseLogFileReader.DefaultBlocksReadBefore(lineStart, windowEnd);

        // Assert
        Assert.Equal(2L * ReverseLogFileReader.DefaultBlockSize, read);
    }

    [Fact]
    public void DefaultBlocksReadBefore_FirstLineOfTheFile_CountsTheWholeWindow()
    {
        // Arrange: a window that is not a whole number of blocks.
        long windowEnd = ReverseLogFileReader.DefaultBlockSize + 10;

        // Act
        long read = ReverseLogFileReader.DefaultBlocksReadBefore(0, windowEnd);

        // Assert
        Assert.Equal(windowEnd, read);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static (long Offset, long End, string Template) Describe(LogFileEvent logEvent) =>
        (logEvent.Offset, logEvent.End, logEvent.Event.MessageTemplate.Text);

    private static byte[] ManyLines(int count)
    {
        var content = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            content
                .Append(
                    LogLines.Event(
                        Aggregators.Noon.AddSeconds(i),
                        "Line {N} with padding to make it about a hundred bytes long",
                        extraProperties: string.Create(CultureInfo.InvariantCulture, $"\"N\":{i}")
                    )
                )
                .Append('\n');
        }

        return Encoding.UTF8.GetBytes(content.ToString());
    }
}
