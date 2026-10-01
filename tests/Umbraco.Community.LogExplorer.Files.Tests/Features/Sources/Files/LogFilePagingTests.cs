using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Umbraco.Cms.Core.Logging;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Records;
using Umbraco.Community.LogExplorer.Features.Sources.Files;
using Umbraco.Community.LogExplorer.Files.Tests.TestSupport;

namespace Umbraco.Community.LogExplorer.Files.Tests.Features.Sources.Files;

/// <summary>
/// The pager merges every machine's files into one timestamp order and pages through it with
/// cursors in both directions, stopping as soon as a page is full (#31). The committed fixtures
/// hold nine good events across machines WORM and NODE2, including a rolled <c>_001</c> file with
/// a malformed line.
/// </summary>
public class LogFilePagingTests
{
    private const int MaxPageSize = 1000;

    private static readonly DateTimeOffset Now = new(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);

    // The fixtures' templates, oldest first.
    private static readonly string[] FixtureTemplates =
    [
        "Acquiring MainDom.",
        "Acquired MainDom.",
        "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Duration} ms",
        "First {N}",
        "Second {N}",
        "Third {N}",
        "Café {Name} opened",
        "Basket {RequestId} priced {@Cart} with tags {@Tags}",
        "Configured database is reporting as not being available.",
    ];

    [Fact]
    public void Query_WalkingEveryCursorOldestFirst_ReturnsEachRecordOnceInTimestampOrder()
    {
        // Arrange
        LogFilePager pager = CreatePager(LogFixtures.Directory);
        LogQuery query = FixtureQuery(SortDirection.Ascending) with { Take = 2 };

        // Act
        List<LogRecord> records = ReadAll(pager, query);

        // Assert
        Assert.Equal(FixtureTemplates, Templates(records));
    }

    [Fact]
    public void Query_WalkingEveryCursorNewestFirst_ReturnsEachRecordOnceInTimestampOrder()
    {
        // Arrange
        LogFilePager pager = CreatePager(LogFixtures.Directory);
        LogQuery query = FixtureQuery(SortDirection.Descending) with { Take = 2 };

        // Act
        List<LogRecord> records = ReadAll(pager, query);

        // Assert
        Assert.Equal(FixtureTemplates.Reverse(), Templates(records));
    }

    [Theory]
    [InlineData(SortDirection.Ascending)]
    [InlineData(SortDirection.Descending)]
    public void Query_WalkingEveryCursor_GivesEveryRecordADistinctId(SortDirection sort)
    {
        // Arrange
        LogFilePager pager = CreatePager(LogFixtures.Directory);
        LogQuery query = FixtureQuery(sort) with { Take = 1 };

        // Act
        List<LogRecord> records = ReadAll(pager, query);

        // Assert
        Assert.Equal(
            FixtureTemplates.Length,
            records.Select(record => record.Id).Distinct().Count()
        );
    }

    [Fact]
    public void Query_EveryRecordFitsOnOnePage_HasNoNextCursor()
    {
        // Arrange
        LogFilePager pager = CreatePager(LogFixtures.Directory);

        // Act
        FilePage page = pager.Query(FixtureQuery(SortDirection.Descending), MaxPageSize, Token);

        // Assert
        Assert.Null(page.Page.NextCursor);
    }

    [Theory]
    [InlineData(SortDirection.Ascending, new[] { "On NODE2", "On WORM" })]
    [InlineData(SortDirection.Descending, new[] { "On WORM", "On NODE2" })]
    public void Query_SameTimestampOnTwoMachines_OrdersByFileName(
        SortDirection sort,
        string[] expected
    )
    {
        // Arrange
        using var directory = new TempDirectory();
        DateTimeOffset noon = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        directory.Write(
            "UmbracoTraceLog.WORM.20261001.json",
            LogLines.File(LogLines.Event(noon, "On WORM"))
        );
        directory.Write(
            "UmbracoTraceLog.NODE2.20261001.json",
            LogLines.File(LogLines.Event(noon, "On NODE2"))
        );
        LogFilePager pager = CreatePager(directory.Path);

        // Act
        FilePage page = pager.Query(DayQuery(sort), MaxPageSize, Token);

        // Assert
        Assert.Equal(expected, Templates(page.Page.Records));
    }

    [Theory]
    [InlineData(SortDirection.Ascending, new[] { "At From", "Just before To" })]
    [InlineData(SortDirection.Descending, new[] { "Just before To", "At From" })]
    public void Query_EventsAtTheRangeEdges_IncludesFromAndExcludesTo(
        SortDirection sort,
        string[] expected
    )
    {
        // Arrange
        using var directory = new TempDirectory();
        DateTimeOffset from = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset to = from.AddHours(1);
        directory.Write(
            "UmbracoTraceLog.WORM.20261001.json",
            LogLines.File(
                LogLines.Event(from.AddTicks(-1), "Just before From"),
                LogLines.Event(from, "At From"),
                LogLines.Event(to.AddTicks(-1), "Just before To"),
                LogLines.Event(to, "At To")
            )
        );
        LogFilePager pager = CreatePager(directory.Path);
        var query = new LogQuery { Range = new TimeRange(from, to, null), Sort = sort };

        // Act
        FilePage page = pager.Query(query, MaxPageSize, Token);

        // Assert
        Assert.Equal(expected, Templates(page.Page.Records));
    }

    [Fact]
    public void Query_RelativeRange_ResolvesAgainstTheClock()
    {
        // Arrange: the clock reads midnight, so "1h" covers 23:00 to midnight.
        using var directory = new TempDirectory();
        directory.Write(
            "UmbracoTraceLog.WORM.20261001.json",
            LogLines.File(
                LogLines.Event(Now.AddMinutes(-90), "An hour and a half ago"),
                LogLines.Event(Now.AddMinutes(-30), "Half an hour ago")
            )
        );
        LogFilePager pager = CreatePager(directory.Path);
        var query = new LogQuery { Range = new TimeRange(null, null, "1h") };

        // Act
        FilePage page = pager.Query(query, MaxPageSize, Token);

        // Assert
        Assert.Equal(["Half an hour ago"], Templates(page.Page.Records));
    }

    [Fact]
    public void Query_FilterAndLevels_KeepsOnlyRecordsPassingBoth()
    {
        // Arrange: A fails the level set, B fails the filter, C passes both.
        using var directory = new TempDirectory();
        DateTimeOffset noon = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        directory.Write(
            "UmbracoTraceLog.WORM.20261001.json",
            LogLines.File(
                LogLines.Event(noon, "A", extraProperties: "\"StatusCode\":200"),
                LogLines.Event(noon.AddSeconds(1), "B", "Error", "\"StatusCode\":500"),
                LogLines.Event(noon.AddSeconds(2), "C", "Error", "\"StatusCode\":200")
            )
        );
        LogFilePager pager = CreatePager(directory.Path);
        LogQuery query = DayQuery(SortDirection.Ascending) with
        {
            Levels = new HashSet<string>(["error"]),
            Filter = new ConditionNode(
                "StatusCode",
                FilterOperator.Equals,
                JsonSerializer.SerializeToElement(200)
            ),
        };

        // Act
        FilePage page = pager.Query(query, MaxPageSize, Token);

        // Assert
        Assert.Equal(["C"], Templates(page.Page.Records));
    }

    [Theory]
    [InlineData(SortDirection.Ascending, "Event 0")]
    [InlineData(SortDirection.Descending, "Event 19999")]
    public void Query_TakeOneFromALargeFile_ReadsOnlyOneBlock(SortDirection sort, string expected)
    {
        // Arrange: 20,000 events, about 1.4 MB.
        using var directory = new TempDirectory();
        DateTimeOffset start = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        directory.Write(
            "UmbracoTraceLog.WORM.20261001.json",
            LogLines.File([
                .. Enumerable
                    .Range(0, 20_000)
                    .Select(i => LogLines.Event(start.AddSeconds(i), $"Event {i}")),
            ])
        );
        LogFilePager pager = CreatePager(directory.Path);
        LogQuery query = DayQuery(sort) with { Take = 1 };

        // Act
        FilePage page = pager.Query(query, MaxPageSize, Token);

        // Assert
        Assert.Equal(
            (expected, true),
            (Assert.Single(page.Page.Records).MessageTemplate, page.BytesRead <= 64 * 1024)
        );
    }

    [Fact]
    public void Query_TakeAboveTheMaximumPageSize_ReturnsTheMaximum()
    {
        // Arrange
        LogFilePager pager = CreatePager(LogFixtures.Directory);
        LogQuery query = FixtureQuery(SortDirection.Descending) with { Take = 100 };

        // Act
        FilePage page = pager.Query(query, maxPageSize: 2, Token);

        // Assert
        Assert.Equal(2, page.Page.Records.Count);
    }

    [Theory]
    [InlineData(SortDirection.Ascending, "UmbracoTraceLog.WORM.20261001.json", "Day 2 first")]
    [InlineData(SortDirection.Descending, "UmbracoTraceLog.WORM.20261002.json", "Day 1 second")]
    public void Query_CursorsFileDeletedBetweenPages_ResumesAtTheNextFile(
        SortDirection sort,
        string firstPageFile,
        string expected
    )
    {
        // Arrange
        using var directory = new TempDirectory();
        DateTimeOffset day1 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        directory.Write(
            "UmbracoTraceLog.WORM.20261001.json",
            LogLines.File(
                LogLines.Event(day1, "Day 1 first"),
                LogLines.Event(day1.AddMinutes(1), "Day 1 second")
            )
        );
        directory.Write(
            "UmbracoTraceLog.WORM.20261002.json",
            LogLines.File(
                LogLines.Event(day1.AddDays(1), "Day 2 first"),
                LogLines.Event(day1.AddDays(1).AddMinutes(1), "Day 2 second")
            )
        );
        LogFilePager pager = CreatePager(directory.Path);
        var query = new LogQuery
        {
            Range = new TimeRange(day1.AddHours(-12), day1.AddDays(1).AddHours(12), null),
            Sort = sort,
            Take = 1,
        };
        string? cursor = pager.Query(query, MaxPageSize, Token).Page.NextCursor;
        File.Delete(Path.Combine(directory.Path, firstPageFile));

        // Act
        FilePage page = pager.Query(query with { Cursor = cursor }, MaxPageSize, Token);

        // Assert
        Assert.Equal([expected], Templates(page.Page.Records));
    }

    [Fact]
    public void Query_MalformedLineRead_WarnsOncePerFile()
    {
        // Arrange
        LogFilePager pager = CreatePager(LogFixtures.Directory);

        // Act
        FilePage page = pager.Query(FixtureQuery(SortDirection.Descending), MaxPageSize, Token);

        // Assert
        Assert.Equal(
            ["1 malformed line skipped in UmbracoTraceLog.NODE2.20260930_001.json"],
            page.Page.Warnings
        );
    }

    [Fact]
    public void Query_EmptyLogDirectory_ReturnsAnEmptyLastPage()
    {
        // Arrange
        using var directory = new TempDirectory();
        LogFilePager pager = CreatePager(directory.Path);

        // Act
        FilePage page = pager.Query(DayQuery(SortDirection.Descending), MaxPageSize, Token);

        // Assert
        Assert.Equal((0, null), (page.Page.Records.Count, page.Page.NextCursor));
    }

    [Fact]
    public void Query_InvalidCursor_ThrowsArgumentException()
    {
        // Arrange
        LogFilePager pager = CreatePager(LogFixtures.Directory);
        LogQuery query = FixtureQuery(SortDirection.Descending) with { Cursor = "not a cursor" };

        // Act
        void Query() => pager.Query(query, MaxPageSize, Token);

        // Assert
        Assert.Throws<ArgumentException>(Query);
    }

    [Fact]
    public void Query_CursorFromTheOtherDirection_ThrowsArgumentException()
    {
        // Arrange
        LogFilePager pager = CreatePager(LogFixtures.Directory);
        LogQuery newestFirst = FixtureQuery(SortDirection.Descending) with { Take = 1 };
        string? cursor = pager.Query(newestFirst, MaxPageSize, Token).Page.NextCursor;
        LogQuery oldestFirst = FixtureQuery(SortDirection.Ascending) with { Cursor = cursor };

        // Act
        void Query() => pager.Query(oldestFirst, MaxPageSize, Token);

        // Assert
        Assert.Throws<ArgumentException>(Query);
    }

    [Fact]
    public void Query_CursorNamingAFileOutsideTheFormat_ThrowsArgumentException()
    {
        // Arrange
        LogFilePager pager = CreatePager(LogFixtures.Directory);
        string cursor = new FileCursor(
            SortDirection.Descending,
            [new FilePosition("..\\web.config", 0)]
        ).Encode();
        LogQuery query = FixtureQuery(SortDirection.Descending) with { Cursor = cursor };

        // Act
        void Query() => pager.Query(query, MaxPageSize, Token);

        // Assert
        Assert.Throws<ArgumentException>(Query);
    }

    [Fact]
    public void Query_NativeQuery_ThrowsNotSupported()
    {
        // Arrange
        LogFilePager pager = CreatePager(LogFixtures.Directory);
        LogQuery query = FixtureQuery(SortDirection.Descending) with { NativeQuery = "Has(N)" };

        // Act
        void Query() => pager.Query(query, MaxPageSize, Token);

        // Assert
        Assert.Throws<NotSupportedException>(Query);
    }

    [Fact]
    public void Query_CancelledToken_ThrowsOperationCanceled()
    {
        // Arrange
        LogFilePager pager = CreatePager(LogFixtures.Directory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        void Query() =>
            pager.Query(FixtureQuery(SortDirection.Descending), MaxPageSize, cancellation.Token);

        // Assert
        Assert.Throws<OperationCanceledException>(Query);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static LogQuery FixtureQuery(SortDirection sort) =>
        new()
        {
            Range = new TimeRange(
                new DateTimeOffset(2026, 9, 29, 0, 0, 0, TimeSpan.Zero),
                Now,
                null
            ),
            Sort = sort,
        };

    private static LogQuery DayQuery(SortDirection sort) =>
        new() { Range = new TimeRange(Now.AddDays(-1), Now, null), Sort = sort };

    private static LogFilePager CreatePager(string directory)
    {
        var configuration = Substitute.For<ILoggingConfiguration>();
        configuration.LogDirectory.Returns(directory);
        configuration.LogFileNameFormat.Returns("UmbracoTraceLog.{0}..json");
        configuration.GetLogFileNameFormatArguments().Returns(["WORM"]);
        return new LogFilePager(
            new UmbracoLogFileLocator(configuration, currentMachineName: "WORM"),
            new FakeTimeProvider(Now)
        );
    }

    private static List<LogRecord> ReadAll(LogFilePager pager, LogQuery query)
    {
        var records = new List<LogRecord>();
        string? cursor = null;
        do
        {
            FilePage page = pager.Query(query with { Cursor = cursor }, MaxPageSize, Token);
            records.AddRange(page.Page.Records);
            cursor = page.Page.NextCursor;
        } while (cursor is not null);

        return records;
    }

    private static string?[] Templates(IEnumerable<LogRecord> records) =>
        [.. records.Select(record => record.MessageTemplate)];
}
