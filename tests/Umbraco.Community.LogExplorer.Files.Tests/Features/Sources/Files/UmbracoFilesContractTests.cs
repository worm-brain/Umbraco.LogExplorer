using System.Globalization;
using System.Text;
using Serilog.Events;
using Umbraco.Community.LogExplorer.ContractTests;
using Umbraco.Community.LogExplorer.Core.Records;
using Umbraco.Community.LogExplorer.Core.Sources;
using Umbraco.Community.LogExplorer.Features.Sources.Files;
using Umbraco.Community.LogExplorer.Files.Tests.TestSupport;

namespace Umbraco.Community.LogExplorer.Files.Tests.Features.Sources.Files;

/// <summary>The files provider must pass the whole provider contract suite (BRIEF §16, #35).</summary>
public sealed class UmbracoFilesContractTests(UmbracoFilesContractFixture fixture)
    : LogSourceContractTests<UmbracoFilesContractFixture>(fixture);

/// <summary>
/// Writes a generated data set as Umbraco compact JSON files in a temp directory, laid out like a
/// load-balanced site's: two machines (WORM and NODE2) over two days, with WORM's second day
/// rolled into a <c>_001</c> file, and some events on both machines at the same instant so the
/// merge's tie-breaking is exercised. Every level appears, and <c>RequestPath</c> (strings) and
/// <c>StatusCode</c> (numbers) are each missing from some events.
/// </summary>
/// <remarks>
/// <see cref="Records"/> are built from the written bytes with the provider's own line parser and
/// mapper, because a record's id is its file name and byte offset and its attributes are the
/// mapper's; the mapping itself is pinned by <c>CompactLogEventMapperTests</c>. The suite then
/// checks paging, ordering, ranges and filtering against those records.
/// </remarks>
public sealed class UmbracoFilesContractFixture : ILogSourceContractFixture, IDisposable
{
    private const string WormDay1 = "UmbracoTraceLog.WORM.20260930.json";
    private const string WormDay2 = "UmbracoTraceLog.WORM.20261001.json";
    private const string WormDay2Rolled = "UmbracoTraceLog.WORM.20261001_001.json";
    private const string Node2Day1 = "UmbracoTraceLog.NODE2.20260930.json";
    private const string Node2Day2 = "UmbracoTraceLog.NODE2.20261001.json";

    private static readonly DateTimeOffset Start = new(2026, 9, 30, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RollAt = new(2026, 10, 1, 3, 0, 0, TimeSpan.Zero);

    // Information is null: compact JSON omits @l for it, as Serilog does.
    private static readonly string?[] Levels =
    [
        "Verbose",
        "Debug",
        null,
        "Warning",
        "Error",
        "Fatal",
    ];

    private static readonly string[] Paths =
    [
        "/api/basket",
        "/api/basket/items",
        "/umbraco/backoffice",
        "/healtz",
        "/api/basket",
    ];

    private static readonly int[] StatusCodes = [200, 404, 500, 301, 200];

    private readonly TempDirectory _directory = new();

    /// <summary>Writes the files and creates the source over them.</summary>
    public UmbracoFilesContractFixture()
    {
        var files = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
        for (int i = 0; i < 96; i++)
        {
            DateTimeOffset timestamp = Start.AddMinutes(i * 17).AddMilliseconds(i * 7);
            bool onNode2 = i % 3 == 0;
            Append(files, FileFor(onNode2, timestamp), Event(i, timestamp));

            // A twin on the other machine at exactly the same instant.
            if (i % 10 == 5)
            {
                Append(files, FileFor(!onNode2, timestamp), Event(i + 1000, timestamp));
            }
        }

        var records = new List<LogRecord>();
        foreach ((string fileName, StringBuilder content) in files)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(content.ToString());
            string path = _directory.Write(fileName, bytes);
            records.AddRange(ReadBack(path, bytes));
        }

        Records = records;
        Source = FileSources.Create(_directory.Path);
    }

    /// <inheritdoc />
    public ILogSource Source { get; }

    /// <inheritdoc />
    public IReadOnlyList<LogRecord> Records { get; }

    /// <inheritdoc />
    public string StringField => "RequestPath";

    /// <inheritdoc />
    public string NumberField => "StatusCode";

    /// <inheritdoc />
    public void Dispose() => _directory.Dispose();

    private static string FileFor(bool onNode2, DateTimeOffset timestamp)
    {
        bool firstDay = timestamp.UtcDateTime.Date == Start.UtcDateTime.Date;
        if (onNode2)
        {
            return firstDay ? Node2Day1 : Node2Day2;
        }

        return firstDay ? WormDay1
            : timestamp < RollAt ? WormDay2
            : WormDay2Rolled;
    }

    private static string Event(int i, DateTimeOffset timestamp)
    {
        var properties = new List<string> { "\"SourceContext\":\"Contract.Generator\"" };
        string template = "Event {Index}";
        properties.Add(string.Create(CultureInfo.InvariantCulture, $"\"Index\":{i}"));
        if (i % 4 != 3)
        {
            template += " for {RequestPath}";
            properties.Add($"\"RequestPath\":\"{Paths[i % Paths.Length]}\"");
        }

        if (i % 5 != 4)
        {
            template += " returned {StatusCode}";
            properties.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"\"StatusCode\":{StatusCodes[i % StatusCodes.Length]}"
                )
            );
        }

        return LogLines.Event(
            timestamp,
            template,
            Levels[i / 2 % Levels.Length],
            string.Join(',', properties)
        );
    }

    private static void Append(Dictionary<string, StringBuilder> files, string file, string line)
    {
        if (!files.TryGetValue(file, out StringBuilder? content))
        {
            files[file] = content = new StringBuilder();
        }

        content.Append(line).Append('\n');
    }

    private static IEnumerable<LogRecord> ReadBack(string path, byte[] bytes)
    {
        LogFile file = new(
            path,
            Path.GetFileName(path),
            Path.GetFileName(path).Split('.')[1],
            DateOnly.ParseExact(
                Path.GetFileNameWithoutExtension(path).Split('.')[2][..8],
                "yyyyMMdd",
                CultureInfo.InvariantCulture
            ),
            path.Contains("_001", StringComparison.Ordinal) ? 1 : 0
        );

        int offset = 0;
        while (offset < bytes.Length)
        {
            int end = Array.IndexOf(bytes, (byte)'\n', offset);
            LogEvent logEvent = LogLineParser.Parse(
                bytes.AsSpan(offset, end - offset),
                offset,
                out _
            )!;
            yield return CompactLogEventMapper.Map(logEvent, file, offset);
            offset = end + 1;
        }
    }
}
