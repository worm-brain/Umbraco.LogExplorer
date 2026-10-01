using LogExplorer.Samples.LogGenerator;
using Serilog.Events;
using Serilog.Formatting.Compact.Reader;

namespace LogExplorer.Samples.Tests.LogGenerator;

/// <summary>
/// The bulk mode's output on a tiny set: Umbraco's file names per machine and local day, size
/// rolls, the time span, the size target, and lines the compact JSON reader (the one the files
/// provider uses) accepts. Each test writes into its own temporary directory, in UTC so file dates
/// do not depend on the machine running the tests.
/// </summary>
public sealed class BulkLogWriterTests : IDisposable
{
    private static readonly DateTimeOffset End = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Directory
        .CreateTempSubdirectory("le-bulk-writer-")
        .FullName;

    /// <inheritdoc />
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Write_ThreeDays_WritesOneFilePerMachinePerDay()
    {
        // Arrange
        BulkLogSettings settings = Settings(targetBytes: 300_000, span: TimeSpan.FromDays(3));

        // Act
        BulkLogWriter.Write(settings);

        // Assert
        Assert.Equal(
            [
                "UmbracoTraceLog.BULK-NODE1.20260928.json",
                "UmbracoTraceLog.BULK-NODE1.20260929.json",
                "UmbracoTraceLog.BULK-NODE1.20260930.json",
                "UmbracoTraceLog.BULK-NODE1.20261001.json",
                "UmbracoTraceLog.BULK-NODE2.20260928.json",
                "UmbracoTraceLog.BULK-NODE2.20260929.json",
                "UmbracoTraceLog.BULK-NODE2.20260930.json",
                "UmbracoTraceLog.BULK-NODE2.20261001.json",
            ],
            FileNames()
        );
    }

    [Fact]
    public void Write_PastTheRollSize_RollsTheDayToNumberedFiles()
    {
        // Arrange: one machine, about 100 KB in one day, rolling every 40 KB.
        BulkLogSettings settings = Settings(targetBytes: 100_000, span: TimeSpan.FromHours(10)) with
        {
            MachineNames = ["NODE"],
            RollSizeBytes = 40_000,
        };

        // Act
        BulkLogWriter.Write(settings);

        // Assert
        Assert.Equal(
            [
                "UmbracoTraceLog.NODE.20261001.json",
                "UmbracoTraceLog.NODE.20261001_001.json",
                "UmbracoTraceLog.NODE.20261001_002.json",
            ],
            FileNames()
        );
    }

    [Fact]
    public void Write_TinySet_WritesAboutTheTargetSize()
    {
        // Arrange
        BulkLogSettings settings = Settings(targetBytes: 2_000_000, span: TimeSpan.FromDays(7));

        // Act
        BulkLogResult result = BulkLogWriter.Write(settings);

        // Assert
        Assert.InRange(result.Bytes, 1_800_000, 2_200_000);
    }

    [Fact]
    public void Write_TinySet_EveryLineParsesWithTheCompactReader()
    {
        // Arrange
        BulkLogResult result = BulkLogWriter.Write(
            Settings(targetBytes: 300_000, span: TimeSpan.FromDays(3))
        );

        // Act
        List<LogEvent> events = ReadAll(result);

        // Assert
        Assert.Equal(result.Events, events.Count);
    }

    [Fact]
    public void Write_TinySet_CoversTheSpanInTimeOrderWithinEachFile()
    {
        // Arrange
        BulkLogResult result = BulkLogWriter.Write(
            Settings(targetBytes: 300_000, span: TimeSpan.FromDays(3))
        );

        // Act
        bool everyFileInOrder = result
            .Files.Select(ReadFile)
            .All(file =>
                file.Zip(file.Skip(1)).All(pair => pair.First.Timestamp <= pair.Second.Timestamp)
            );
        List<LogEvent> events = ReadAll(result);

        // Assert
        Assert.Multiple(
            () => Assert.True(everyFileInOrder, "events within a file are in time order"),
            () =>
                Assert.InRange(
                    events.Min(e => e.Timestamp),
                    End.AddDays(-3),
                    End.AddDays(-3).AddHours(1)
                ),
            () => Assert.InRange(events.Max(e => e.Timestamp), End.AddHours(-1), End.AddSeconds(1))
        );
    }

    [Fact]
    public void Write_TinySet_WritesTheMixOfLevelsWithOneFatal()
    {
        // Arrange
        BulkLogResult result = BulkLogWriter.Write(
            Settings(targetBytes: 2_000_000, span: TimeSpan.FromDays(7))
        );

        // Act
        Dictionary<LogEventLevel, int> levels = ReadAll(result)
            .GroupBy(e => e.Level)
            .ToDictionary(group => group.Key, group => group.Count());

        // Assert
        Assert.Multiple(
            () => Assert.Equal(1, levels.GetValueOrDefault(LogEventLevel.Fatal)),
            () => Assert.True(levels.GetValueOrDefault(LogEventLevel.Error) > 0, "has errors"),
            () => Assert.True(levels.GetValueOrDefault(LogEventLevel.Warning) > 0, "has warnings"),
            () =>
                Assert.True(
                    levels[LogEventLevel.Information] > levels.Values.Sum() / 2,
                    "mostly information"
                )
        );
    }

    [Fact]
    public void Write_NoTargetSize_Throws()
    {
        // Arrange
        BulkLogSettings settings = Settings(targetBytes: 0, span: TimeSpan.FromDays(1));

        // Act
        void Write() => BulkLogWriter.Write(settings);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(Write);
    }

    private BulkLogSettings Settings(long targetBytes, TimeSpan span) =>
        new()
        {
            Directory = _directory,
            TargetBytes = targetBytes,
            End = End,
            Span = span,
            TimeZone = TimeZoneInfo.Utc,
        };

    private string[] FileNames() =>
        [
            .. Directory
                .GetFiles(_directory)
                .Select(path => Path.GetFileName(path))
                .Order(StringComparer.Ordinal),
        ];

    private static List<LogEvent> ReadAll(BulkLogResult result) =>
        [.. result.Files.SelectMany(ReadFile)];

    private static List<LogEvent> ReadFile(string path)
    {
        using var reader = new LogEventReader(File.OpenText(path));
        var events = new List<LogEvent>();
        while (reader.TryRead(out LogEvent? logEvent))
        {
            events.Add(logEvent);
        }

        return events;
    }
}
