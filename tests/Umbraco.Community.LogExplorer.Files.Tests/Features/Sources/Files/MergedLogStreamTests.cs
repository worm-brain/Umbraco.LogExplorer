using System.Text;
using NSubstitute;
using Umbraco.Cms.Core.Logging;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Features.Sources.Files;
using Umbraco.Community.LogExplorer.Files.Tests.TestSupport;

namespace Umbraco.Community.LogExplorer.Files.Tests.Features.Sources.Files;

/// <summary>
/// The merged stream reads every machine's files as one timestamp order and exposes where each
/// machine got to, which the pager, aggregations and context build on. Each test writes two
/// machines' files for one day: WORM at 10:00 and 10:02, NODE2 at 10:01 and 10:03.
/// </summary>
public sealed class MergedLogStreamTests : IDisposable
{
    private const string WormFile = "UmbracoTraceLog.WORM.20261001.json";
    private const string Node2File = "UmbracoTraceLog.NODE2.20261001.json";

    private static readonly DateTimeOffset TenAm = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);

    private static readonly ResolvedRange Range = new(TenAm.AddHours(-1), TenAm.AddHours(1));

    private static readonly string WormFirst = LogLines.Event(TenAm, "W0");
    private static readonly string WormSecond = LogLines.Event(TenAm.AddMinutes(2), "W2");
    private static readonly string NodeFirst = LogLines.Event(TenAm.AddMinutes(1), "N1");
    private static readonly string NodeSecond = LogLines.Event(TenAm.AddMinutes(3), "N3");

    private readonly TempDirectory _directory = new();
    private readonly UmbracoLogFileLocator _locator;

    public MergedLogStreamTests()
    {
        _directory.Write(WormFile, LogLines.File(WormFirst, WormSecond));
        _directory.Write(Node2File, LogLines.File(NodeFirst, NodeSecond));
        _locator = CreateLocator(_directory.Path);
    }

    public void Dispose() => _directory.Dispose();

    [Theory]
    [InlineData(SortDirection.Ascending, new[] { "W0", "N1", "W2", "N3" })]
    [InlineData(SortDirection.Descending, new[] { "N3", "W2", "N1", "W0" })]
    public void TryRead_TwoMachines_MergesTheirEventsInTimestampOrder(
        SortDirection direction,
        string[] expected
    )
    {
        // Arrange
        using MergedLogStream stream = MergedLogStream.Open(_locator, Range, direction, Token);

        // Act
        List<string> templates = ReadTemplates(stream, int.MaxValue);

        // Assert
        Assert.Equal(expected, templates);
    }

    [Fact]
    public void TryRead_EachEvent_ComesWithTheFileItWasReadFrom()
    {
        // Arrange
        using MergedLogStream stream = MergedLogStream.Open(
            _locator,
            Range,
            SortDirection.Ascending,
            Token
        );

        // Act
        stream.TryRead(out (LogFile File, LogFileEvent Event) first);
        stream.TryRead(out (LogFile File, LogFileEvent Event) second);

        // Assert
        Assert.Equal((WormFile, Node2File), (first.File.FileName, second.File.FileName));
    }

    [Fact]
    public void NextPositions_AfterPartialReadOldestFirst_HoldsTheStartOfEachMachinesNextEvent()
    {
        // Arrange
        using MergedLogStream stream = MergedLogStream.Open(
            _locator,
            Range,
            SortDirection.Ascending,
            Token
        );

        // Act: W0, N1 and W2 are read, so WORM is exhausted and NODE2 is at its second line.
        ReadTemplates(stream, 3);

        // Assert
        Assert.Equal([new FilePosition(Node2File, LineLength(NodeFirst))], stream.NextPositions);
    }

    [Fact]
    public void NextPositions_AfterPartialReadNewestFirst_HoldsTheEndOfEachMachinesNextEvent()
    {
        // Arrange
        using MergedLogStream stream = MergedLogStream.Open(
            _locator,
            Range,
            SortDirection.Descending,
            Token
        );

        // Act: N3 is read, so NODE2's next is N1 and WORM's next is still W2. Streams are in
        // machine-name order.
        ReadTemplates(stream, 1);

        // Assert
        Assert.Equal(
            [
                new FilePosition(Node2File, LineLength(NodeFirst)),
                new FilePosition(WormFile, LineLength(WormFirst) + LineLength(WormSecond)),
            ],
            stream.NextPositions
        );
    }

    [Fact]
    public void NextPositions_EveryEventRead_IsEmpty()
    {
        // Arrange
        using MergedLogStream stream = MergedLogStream.Open(
            _locator,
            Range,
            SortDirection.Descending,
            Token
        );

        // Act
        ReadTemplates(stream, int.MaxValue);

        // Assert
        Assert.Empty(stream.NextPositions);
    }

    [Theory]
    [InlineData(SortDirection.Ascending, new[] { "W2", "N3" })]
    [InlineData(SortDirection.Descending, new[] { "N1", "W0" })]
    public void Resume_FromNextPositions_ContinuesWithTheUnreadEvents(
        SortDirection direction,
        string[] expected
    )
    {
        // Arrange
        IReadOnlyList<FilePosition> positions;
        using (MergedLogStream first = MergedLogStream.Open(_locator, Range, direction, Token))
        {
            ReadTemplates(first, 2);
            positions = first.NextPositions;
        }

        // Act
        using MergedLogStream resumed = MergedLogStream.Resume(
            _locator,
            Range,
            direction,
            positions,
            Token
        );

        // Assert
        Assert.Equal(expected, ReadTemplates(resumed, int.MaxValue));
    }

    [Fact]
    public void Resume_MachineWithNoPosition_IsNotRead()
    {
        // Arrange: only WORM has a position, at its first line.
        FilePosition[] positions = [new(WormFile, 0)];

        // Act
        using MergedLogStream stream = MergedLogStream.Resume(
            _locator,
            Range,
            SortDirection.Ascending,
            positions,
            Token
        );

        // Assert
        Assert.Equal(["W0", "W2"], ReadTemplates(stream, int.MaxValue));
    }

    [Fact]
    public void OpenAt_StartForOneMachine_ReadsOtherMachinesFromTheNearEdge()
    {
        // Arrange: WORM starts after its first line, as context reads after an anchor.
        FilePosition[] starts = [new(WormFile, LineLength(WormFirst))];

        // Act
        using MergedLogStream stream = MergedLogStream.OpenAt(
            _locator,
            Range,
            SortDirection.Ascending,
            starts,
            Token
        );

        // Assert
        Assert.Equal(["N1", "W2", "N3"], ReadTemplates(stream, int.MaxValue));
    }

    [Fact]
    public void Resume_PositionThatIsNotALogFileName_ThrowsArgumentException()
    {
        // Arrange
        FilePosition[] positions = [new("..\\web.config", 0)];

        // Act
        void Resume() =>
            MergedLogStream.Resume(_locator, Range, SortDirection.Ascending, positions, Token);

        // Assert
        Assert.Throws<ArgumentException>(Resume);
    }

    [Fact]
    public void BytesRead_WhileFilesAreStillOpen_CountsWhatHasBeenRead()
    {
        // Arrange: opening reads ahead one event per machine, so both files are open and read.
        using MergedLogStream stream = MergedLogStream.Open(
            _locator,
            Range,
            SortDirection.Ascending,
            Token
        );

        // Act
        long bytesRead = stream.BytesRead;

        // Assert: each file is smaller than one read buffer, so each is read whole.
        Assert.Equal(
            FileLength(WormFirst, WormSecond) + FileLength(NodeFirst, NodeSecond),
            bytesRead
        );
    }

    [Fact]
    public void Warnings_FileWithAMalformedLine_NamesTheFile()
    {
        // Arrange
        _directory.Write(WormFile, LogLines.File(WormFirst, "{not json", WormSecond));
        using MergedLogStream stream = MergedLogStream.Open(
            _locator,
            Range,
            SortDirection.Ascending,
            Token
        );
        ReadTemplates(stream, int.MaxValue);

        // Act
        IReadOnlyList<string> warnings = stream.Warnings;

        // Assert
        Assert.Equal([$"1 malformed line skipped in {WormFile}"], warnings);
    }

    [Fact]
    public void TryRead_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        MergedLogStream stream = MergedLogStream.Open(
            _locator,
            Range,
            SortDirection.Ascending,
            Token
        );
        stream.Dispose();

        // Act
        void Read() => stream.TryRead(out _);

        // Assert
        Assert.Throws<ObjectDisposedException>(Read);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // A terminated line's length in bytes, which is the offset of the line after it.
    private static long LineLength(string line) => Encoding.UTF8.GetByteCount(line) + 1;

    private static long FileLength(params string[] lines) => lines.Sum(LineLength);

    private static List<string> ReadTemplates(MergedLogStream stream, int count)
    {
        var templates = new List<string>();
        while (
            templates.Count < count && stream.TryRead(out (LogFile File, LogFileEvent Event) next)
        )
        {
            templates.Add(next.Event.Event.MessageTemplate.Text);
        }

        return templates;
    }

    private static UmbracoLogFileLocator CreateLocator(string directory)
    {
        var configuration = Substitute.For<ILoggingConfiguration>();
        configuration.LogDirectory.Returns(directory);
        configuration.LogFileNameFormat.Returns("UmbracoTraceLog.{0}..json");
        configuration.GetLogFileNameFormatArguments().Returns(["WORM"]);
        return new UmbracoLogFileLocator(configuration, currentMachineName: "WORM");
    }
}
