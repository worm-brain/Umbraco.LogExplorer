using System.Text;
using NSubstitute;
using Umbraco.Cms.Core.Logging;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Records;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Features.Sources.Files;
using Umbraco.Community.LogExplorer.Files.Tests.TestSupport;

namespace Umbraco.Community.LogExplorer.Files.Tests.Features.Sources.Files;

/// <summary>
/// "Around this" reads the entries either side of an anchor in the pager's merged order, across
/// files, days and machines, and rejects ids that do not point at an event (#33). Each test writes
/// its own files; templates are unique so they identify events.
/// </summary>
public sealed class FileContextReaderTests : IDisposable
{
    private const string WormFile = "UmbracoTraceLog.WORM.20261001.json";
    private const string WormRolledFile = "UmbracoTraceLog.WORM.20261001_001.json";
    private const string WormPreviousDayFile = "UmbracoTraceLog.WORM.20260930.json";
    private const string Node2File = "UmbracoTraceLog.NODE2.20261001.json";

    private static readonly DateTimeOffset TenAm = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);

    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void Read_WithinOneFile_ReturnsTheRequestedEntriesEitherSideOldestFirst()
    {
        // Arrange
        WriteMinutes(WormFile, "A", 0, 1, 2, 3, 4, 5, 6);
        FileContextReader reader = CreateReader();

        // Act
        ContextResult context = reader.Read(IdOf("A3"), before: 2, after: 2, Token);

        // Assert
        Assert.Equal("A1,A2 | A3 | A4,A5", Shape(context));
    }

    [Fact]
    public void Read_AnchorBeforeARoll_ContinuesIntoTheRolledFile()
    {
        // Arrange
        WriteMinutes(WormFile, "A", 0, 1);
        WriteMinutes(WormRolledFile, "R", 2, 3);
        FileContextReader reader = CreateReader();

        // Act
        ContextResult context = reader.Read(IdOf("A1"), before: 0, after: 2, Token);

        // Assert
        Assert.Equal("R2,R3", Join(context.After));
    }

    [Fact]
    public void Read_AnchorAfterARoll_ReachesBackIntoTheFirstFile()
    {
        // Arrange
        WriteMinutes(WormFile, "A", 0, 1);
        WriteMinutes(WormRolledFile, "R", 2, 3);
        FileContextReader reader = CreateReader();

        // Act
        ContextResult context = reader.Read(IdOf("R2"), before: 2, after: 0, Token);

        // Assert
        Assert.Equal("A0,A1", Join(context.Before));
    }

    [Fact]
    public void Read_AnchorJustAfterMidnight_ReachesBackIntoThePreviousDaysFile()
    {
        // Arrange
        DateTimeOffset midnight = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        _directory.Write(
            WormPreviousDayFile,
            LogLines.File(
                LogLines.Event(midnight.AddMinutes(-2), "Late"),
                LogLines.Event(midnight.AddMinutes(-1), "Later")
            )
        );
        _directory.Write(WormFile, LogLines.File(LogLines.Event(midnight, "Early")));
        FileContextReader reader = CreateReader();

        // Act
        ContextResult context = reader.Read(IdOf("Early"), before: 5, after: 0, Token);

        // Assert
        Assert.Equal("Late,Later", Join(context.Before));
    }

    [Fact]
    public void Read_TwoMachines_InterleavesThemInTimestampOrder()
    {
        // Arrange
        WriteMinutes(WormFile, "W", 0, 2, 4);
        WriteMinutes(Node2File, "N", 1, 3, 5);
        FileContextReader reader = CreateReader();

        // Act
        ContextResult context = reader.Read(IdOf("W2"), before: 2, after: 2, Token);

        // Assert
        Assert.Equal("W0,N1 | W2 | N3,W4", Shape(context));
    }

    [Theory]
    [InlineData("W2", "N1,N2 | W2 | W3")]
    [InlineData("N2", "N1 | N2 | W2,W3")]
    public void Read_OtherMachineAtTheAnchorsTimestamp_IsOnTheSideItsFileNameSortsTo(
        string anchor,
        string expected
    )
    {
        // Arrange: both machines log at 10:02; NODE2's file name sorts before WORM's, so the
        // merged order is N1, N2, W2, W3.
        WriteMinutes(WormFile, "W", 2, 3);
        WriteMinutes(Node2File, "N", 1, 2);
        FileContextReader reader = CreateReader();

        // Act
        ContextResult context = reader.Read(IdOf(anchor), before: 5, after: 5, Token);

        // Assert
        Assert.Equal(expected, Shape(context));
    }

    [Fact]
    public void Read_AroundEveryRecord_MatchesItsNeighboursInTheMergedOrder()
    {
        // Arrange: equal timestamps on both machines and within one file, across a roll.
        _directory.Write(
            WormFile,
            LogLines.File(LogLines.Event(TenAm, "W0a"), LogLines.Event(TenAm, "W0b"))
        );
        _directory.Write(WormRolledFile, LogLines.File(LogLines.Event(TenAm.AddMinutes(1), "W1")));
        _directory.Write(
            Node2File,
            LogLines.File(LogLines.Event(TenAm, "N0"), LogLines.Event(TenAm.AddMinutes(1), "N1"))
        );
        FileContextReader reader = CreateReader();
        List<LogRecord> merged = ReadMerged();

        // Act
        List<string> mismatches = [];
        for (int index = 0; index < merged.Count; index++)
        {
            string actual = Shape(reader.Read(merged[index].Id, before: 10, after: 10, Token));
            string expected = Shape(
                new ContextResult(merged[..index], merged[index], merged[(index + 1)..])
            );
            if (actual != expected)
            {
                mismatches.Add(
                    $"{merged[index].MessageTemplate}: expected {expected}, got {actual}"
                );
            }
        }

        // Assert
        Assert.Empty(mismatches);
    }

    [Fact]
    public void Read_FirstEntryInTheLogs_ReturnsNothingBefore()
    {
        // Arrange
        WriteMinutes(WormFile, "A", 0, 1, 2);
        FileContextReader reader = CreateReader();

        // Act
        ContextResult context = reader.Read(IdOf("A0"), before: 7, after: 7, Token);

        // Assert
        Assert.Equal(" | A0 | A1,A2", Shape(context));
    }

    [Fact]
    public void Read_LastEntryInTheLogs_ReturnsNothingAfter()
    {
        // Arrange
        WriteMinutes(WormFile, "A", 0, 1, 2);
        FileContextReader reader = CreateReader();

        // Act
        ContextResult context = reader.Read(IdOf("A2"), before: 7, after: 7, Token);

        // Assert
        Assert.Equal("A0,A1 | A2 | ", Shape(context));
    }

    [Fact]
    public void Read_ZeroEitherSide_ReturnsOnlyTheAnchorWithItsId()
    {
        // Arrange
        WriteMinutes(WormFile, "A", 0, 1, 2);
        FileContextReader reader = CreateReader();
        string id = IdOf("A1");

        // Act
        ContextResult context = reader.Read(id, before: 0, after: 0, Token);

        // Assert
        Assert.Equal((0, id, 0), (context.Before.Count, context.Anchor.Id, context.After.Count));
    }

    [Fact]
    public void Read_MoreThanTheMaximumRequested_ReturnsTheMaximum()
    {
        // Arrange
        int count = FileContextReader.MaxEntriesPerSide + 10;
        _directory.Write(
            WormFile,
            LogLines.File([
                .. Enumerable
                    .Range(0, count)
                    .Select(second => LogLines.Event(TenAm.AddSeconds(second), $"E{second}")),
            ])
        );
        FileContextReader reader = CreateReader();

        // Act
        ContextResult context = reader.Read(IdOf("E0"), before: 0, after: count, Token);

        // Assert
        Assert.Equal(FileContextReader.MaxEntriesPerSide, context.After.Count);
    }

    [Fact]
    public void Read_FileDeletedSinceTheIdWasIssued_ThrowsKeyNotFoundException()
    {
        // Arrange
        WriteMinutes(WormFile, "A", 0, 1);
        FileContextReader reader = CreateReader();
        string id = IdOf("A1");
        File.Delete(Path.Combine(_directory.Path, WormFile));

        // Act
        void Read() => reader.Read(id, before: 1, after: 1, Token);

        // Assert
        Assert.Throws<KeyNotFoundException>(Read);
    }

    public static TheoryData<string, string> InvalidIds()
    {
        string wormFirstLine = LogLines.Event(TenAm, "A0");
        long secondLine = Encoding.UTF8.GetByteCount(wormFirstLine) + 1;
        return new TheoryData<string, string>
        {
            {
                "Unknown file name",
                new FilePosition("UmbracoTraceLog.WORM.20991231.json", 0).ToRecordId()
            },
            { "Not a log file name", new FilePosition("notes.txt", 0).ToRecordId() },
            { "Offset inside a line", new FilePosition(WormFile, 5).ToRecordId() },
            { "Offset past the end", new FilePosition(WormFile, secondLine * 10).ToRecordId() },
            { "Offset of the blank line", new FilePosition(WormFile, secondLine * 2).ToRecordId() },
            {
                "Offset of a malformed line",
                new FilePosition(WormFile, secondLine * 2 + 1).ToRecordId()
            },
            { "Relative path in the name", new FilePosition(@"..\" + WormFile, 0).ToRecordId() },
            { "Not base64url", "not an id!" },
        };
    }

    [Theory]
    [MemberData(nameof(InvalidIds))]
    public void Read_IdThatDoesNotPointAtAnEvent_ThrowsKeyNotFoundException(
        string scenario,
        string id
    )
    {
        // Arrange: two events of equal length, a blank line, then a malformed line.
        _ = scenario;
        _directory.Write(
            WormFile,
            LogLines.File(
                LogLines.Event(TenAm, "A0"),
                LogLines.Event(TenAm.AddMinutes(1), "A1"),
                "",
                "{not json"
            )
        );
        FileContextReader reader = CreateReader();

        // Act
        void Read() => reader.Read(id, before: 1, after: 1, Token);

        // Assert
        Assert.Throws<KeyNotFoundException>(Read);
    }

    [Fact]
    public void Read_AbsolutePathToALogFileOutsideTheDirectory_ThrowsKeyNotFoundException()
    {
        // Arrange: a readable log file elsewhere, named by its full path.
        WriteMinutes(WormFile, "A", 0);
        using var elsewhere = new TempDirectory();
        string outside = elsewhere.Write(WormFile, LogLines.File(LogLines.Event(TenAm, "Secret")));
        FileContextReader reader = CreateReader();
        string id = new FilePosition(outside, 0).ToRecordId();

        // Act
        void Read() => reader.Read(id, before: 1, after: 1, Token);

        // Assert
        Assert.Throws<KeyNotFoundException>(Read);
    }

    [Fact]
    public void Read_NegativeCount_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        WriteMinutes(WormFile, "A", 0, 1);
        FileContextReader reader = CreateReader();
        string id = IdOf("A0");

        // Act
        void Read() => reader.Read(id, before: -1, after: 1, Token);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(Read);
    }

    [Fact]
    public void Read_CancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        WriteMinutes(WormFile, "A", 0, 1);
        FileContextReader reader = CreateReader();
        string id = IdOf("A0");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        void Read() => reader.Read(id, before: 1, after: 1, cancellation.Token);

        // Assert
        Assert.ThrowsAny<OperationCanceledException>(Read);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // "before | anchor | after" as templates, oldest first, so one assertion shows all three.
    private static string Shape(ContextResult context) =>
        $"{Join(context.Before)} | {context.Anchor.MessageTemplate} | {Join(context.After)}";

    private static string Join(IEnumerable<LogRecord> records) =>
        string.Join(",", records.Select(record => record.MessageTemplate));

    // One event per listed minute past 10:00, templated "{prefix}{minute}".
    private void WriteMinutes(string fileName, string prefix, params int[] minutes) =>
        _directory.Write(
            fileName,
            LogLines.File([
                .. minutes.Select(minute =>
                    LogLines.Event(TenAm.AddMinutes(minute), $"{prefix}{minute}")
                ),
            ])
        );

    // Every record in the merged ascending order, as the pager would return them.
    private List<LogRecord> ReadMerged()
    {
        var records = new List<LogRecord>();
        using MergedLogStream stream = MergedLogStream.Open(
            CreateLocator(),
            new ResolvedRange(TenAm.AddDays(-2), TenAm.AddDays(2)),
            SortDirection.Ascending,
            Token
        );
        while (stream.TryRead(out (LogFile File, LogFileEvent Event) next))
        {
            records.Add(CompactLogEventMapper.Map(next.Event.Event, next.File, next.Event.Offset));
        }

        return records;
    }

    private string IdOf(string template) =>
        ReadMerged().Single(record => record.MessageTemplate == template).Id;

    private FileContextReader CreateReader() => new(CreateLocator());

    private UmbracoLogFileLocator CreateLocator()
    {
        var configuration = Substitute.For<ILoggingConfiguration>();
        configuration.LogDirectory.Returns(_directory.Path);
        configuration.LogFileNameFormat.Returns("UmbracoTraceLog.{0}..json");
        configuration.GetLogFileNameFormatArguments().Returns(["WORM"]);
        return new UmbracoLogFileLocator(configuration, currentMachineName: "WORM");
    }
}
