using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Serilog.Events;
using Serilog.Formatting.Compact.Reader;
using Umbraco.Cms.Core.Logging;
using Umbraco.Community.LogExplorer.Core.Filtering;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Features.Sources;
using Umbraco.Community.LogExplorer.Features.Sources.Files;

namespace Umbraco.Community.LogExplorer.Benchmarks;

/// <summary>
/// Times the files source's aggregations (histogram, facets, fields, patterns) over any folder of
/// Umbraco compact JSON log files, against BRIEF §17's 3 s budget.
/// </summary>
/// <remarks>
/// <para>
/// Run as <c>aggregations --logs &lt;dir&gt;</c>, or set <c>LOG_EXPLORER_BENCHMARK_LOGS</c>; the
/// folder is never part of the repo. <c>--anchor</c> (default now) fixes "now" so relative ranges
/// cover the files; <c>--ranges</c> (default <c>7d,30d</c>), <c>--runs</c> (default 5).
/// </para>
/// <para>
/// Each run uses a new <see cref="FileAggregator"/> with an empty cache, so "warm" means a warm JIT
/// and OS file cache but no result cache. <c>--cold</c> starts one process per operation and
/// range and times its single first call (the JIT cost a request after a site restart pays).
/// <c>--search-view</c> times one Search view load as the UI makes it: histogram and fields at
/// once, then facets on the discovered fields. <c>--profile</c> splits one budgeted scan into
/// stages (reading, decoding, parsing, mapping).
/// </para>
/// </remarks>
internal static class AggregationBenchmark
{
    private const string LogsVariable = "LOG_EXPLORER_BENCHMARK_LOGS";
    private const string MachineName = "BENCH";

    private static readonly string[] Pinned =
    [
        "SourceContext",
        "RequestPath",
        "StatusCode",
        "MachineName",
        "@exception.type",
    ];

    private static readonly string[] AllOperations = ["histogram", "facets", "fields", "patterns"];

    private static readonly System.Text.Json.JsonSerializerOptions DumpOptions = new(
        Core.Json.LogJson.Options
    )
    {
        WriteIndented = true,
    };

    /// <summary>Runs the aggregation benchmark.</summary>
    /// <param name="args">Command-line arguments after <c>aggregations</c>.</param>
    /// <returns>0 on success, 2 when no log folder was given.</returns>
    public static int Run(string[] args)
    {
        string? directory =
            Argument(args, "--logs") ?? Environment.GetEnvironmentVariable(LogsVariable);
        if (directory is null || !Directory.Exists(directory))
        {
            Console.Error.WriteLine($"Pass --logs <dir> or set {LogsVariable}.");
            return 2;
        }

        DateTimeOffset anchor = Argument(args, "--anchor") is { } text
            ? DateTimeOffset.Parse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal
            )
            : DateTimeOffset.UtcNow;
        string[] ranges = (Argument(args, "--ranges") ?? "7d,30d").Split(',');
        string[] operations = (Argument(args, "--ops") ?? string.Join(',', AllOperations)).Split(
            ','
        );
        int runs = int.Parse(Argument(args, "--runs") ?? "5", CultureInfo.InvariantCulture);
        int budget = int.Parse(Argument(args, "--budget") ?? "256", CultureInfo.InvariantCulture);
        var context = new BenchContext(directory, anchor, budget, Argument(args, "--fields"));

        if (args.Contains("--single"))
        {
            // A child process of --cold: one call, print its time only.
            double ms = Time(context, operations[0], ranges[0], out _);
            Console.WriteLine(ms.ToString("0", CultureInfo.InvariantCulture));
            return 0;
        }

        if (Argument(args, "--dump") is { } dumpPath)
        {
            Dump(context, ranges, dumpPath);
            return 0;
        }

        if (args.Contains("--profile"))
        {
            foreach (string range in ranges)
            {
                Profile(context, range);
            }

            return 0;
        }

        if (args.Contains("--search-view"))
        {
            foreach (string range in ranges)
            {
                SearchView(context, range, runs);
            }

            return 0;
        }

        Console.WriteLine(
            $"Logs: {directory}; now = {anchor:u}; budget {budget} MB; {(System.Runtime.GCSettings.IsServerGC ? "server" : "workstation")} GC; parallel scan {context.NewAggregator(out _).ParallelScan}"
        );
        Console.WriteLine(
            args.Contains("--cold")
                ? "| Operation | Range | Cold ms (new process, first call) | MB read | Approximate |"
                : "| Operation | Range | First ms | Median ms | Min ms | Max ms | MB read | Approximate |"
        );
        Console.WriteLine(
            args.Contains("--cold")
                ? "| --- | --- | ---: | ---: | --- |"
                : "| --- | --- | ---: | ---: | ---: | ---: | ---: | --- |"
        );
        foreach (string range in ranges)
        {
            foreach (string operation in operations)
            {
                if (args.Contains("--cold"))
                {
                    double cold = ColdRun(context, args, operation, range);
                    Time(context, operation, range, out Outcome outcome);
                    Console.WriteLine(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"| {operation} | {range} | {cold:0} | {outcome.BytesRead / 1048576.0:0} | {outcome.Approximate} |"
                        )
                    );
                    continue;
                }

                double first = Time(context, operation, range, out Outcome last);
                double[] timings = new double[runs];
                for (int i = 0; i < runs; i++)
                {
                    timings[i] = Time(context, operation, range, out last);
                }

                Array.Sort(timings);
                Console.WriteLine(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"| {operation} | {range} | {first:0} | {timings[runs / 2]:0} | {timings[0]:0} | {timings[^1]:0} | {last.BytesRead / 1048576.0:0} | {last.Approximate} |"
                    )
                );
            }
        }

        return 0;
    }

    // Writes every aggregation's full result, with the bytes read and the scanned range, as JSON,
    // so two builds can be diffed to show a change kept the results identical.
    private static void Dump(BenchContext context, string[] ranges, string path)
    {
        var results = new Dictionary<string, object>();
        foreach (string range in ranges)
        {
            LogQuery query = new() { Range = new TimeRange(null, null, range) };
            LogQuery errors = query with
            {
                Levels = new HashSet<string> { "error", "fatal" },
            };
            LogQuery text = query with { Filter = new TextNode("exception") };
            string[] fields = context.FacetFields(range);
            results[$"{range} histogram"] = context
                .NewAggregator(out _)
                .GetHistogram(query, 120, CancellationToken.None);
            results[$"{range} histogram text"] = context
                .NewAggregator(out _)
                .GetHistogram(text, 120, CancellationToken.None);
            results[$"{range} facets"] = context
                .NewAggregator(out _)
                .GetFacets(query, fields, 5, CancellationToken.None);
            results[$"{range} facets errors"] = context
                .NewAggregator(out _)
                .GetFacets(errors, fields, 10, CancellationToken.None);
            results[$"{range} fields"] = context
                .NewAggregator(out _)
                .GetFields(query, CancellationToken.None);
            results[$"{range} patterns"] = context
                .NewAggregator(out _)
                .GetPatterns(query, 100, CancellationToken.None);
            results[$"{range} patterns text"] = context
                .NewAggregator(out _)
                .GetPatterns(text, 100, CancellationToken.None);
        }

        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(results, DumpOptions));
        Console.WriteLine($"Wrote {path}");
    }

    // Spawns this program again for a single call, so nothing is JIT-compiled or cached.
    private static double ColdRun(
        BenchContext context,
        string[] args,
        string operation,
        string range
    )
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("aggregations");
        start.ArgumentList.Add("--single");
        foreach (string name in new[] { "--logs", "--anchor", "--budget" })
        {
            if (Argument(args, name) is { } value)
            {
                start.ArgumentList.Add(name);
                start.ArgumentList.Add(value);
            }
        }

        // The facet field list is worked out here, so the child's only call is the timed one.
        start.ArgumentList.Add("--fields");
        start.ArgumentList.Add(string.Join(',', context.FacetFields(range)));
        start.ArgumentList.Add("--ops");
        start.ArgumentList.Add(operation);
        start.ArgumentList.Add("--ranges");
        start.ArgumentList.Add(range);
        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return double.Parse(output.Trim(), CultureInfo.InvariantCulture);
    }

    private static double Time(
        BenchContext context,
        string operation,
        string range,
        out Outcome outcome
    )
    {
        // A new aggregator and cache per call: no result is ever served from the 60 s cache.
        FileAggregator aggregator = context.NewAggregator(out _);
        LogQuery query = new() { Range = new TimeRange(null, null, range) };
        // Worked out before the clock starts: it is a fields scan of its own the first time.
        string[] facetFields = operation == "facets" ? context.FacetFields(range) : [];
        long start = Stopwatch.GetTimestamp();
        outcome = operation switch
        {
            "histogram" => Outcome.Of(
                aggregator.GetHistogram(query, 120, CancellationToken.None),
                r => r.Approximate
            ),
            "facets" => Outcome.Of(
                aggregator.GetFacets(query, facetFields, 5, CancellationToken.None),
                r => r.Approximate
            ),
            "fields" => Outcome.Of(aggregator.GetFields(query, CancellationToken.None), _ => null),
            "patterns" => Outcome.Of(
                aggregator.GetPatterns(query, 100, CancellationToken.None),
                r => r.Approximate
            ),
            _ => throw new ArgumentException($"Unknown operation {operation}."),
        };
        return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    // One Search view load as the client makes it: histogram and fields start together on one
    // aggregator (one site, one cache), then facets follows fields with the discovered fields.
    private static void SearchView(BenchContext context, string range, int runs)
    {
        for (int run = 0; run <= runs; run++)
        {
            FileAggregator aggregator = context.NewAggregator(out _);
            LogQuery query = new() { Range = new TimeRange(null, null, range) };
            long start = Stopwatch.GetTimestamp();
            Task<double> histogram = Task.Run(() =>
            {
                aggregator.GetHistogram(query, 120, CancellationToken.None);
                return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            });
            Task<(double Fields, double Facets)> facets = Task.Run(() =>
            {
                IReadOnlyList<FieldInfo> fields = aggregator
                    .GetFields(query, CancellationToken.None)
                    .Result;
                double fieldsMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                aggregator.GetFacets(
                    query,
                    [.. Pinned, .. Discovered(fields)],
                    5,
                    CancellationToken.None
                );
                return (fieldsMs, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            });
            Task.WaitAll(histogram, facets);
            Console.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"search view {range} run {run}{(run == 0 ? " (first)" : "")}: histogram {histogram.Result:0} ms, fields {facets.Result.Fields:0} ms, facets done {facets.Result.Facets:0} ms"
                )
            );
        }
    }

    // Splits one budgeted newest-first scan into stages, each timed over the same lines.
    private static void Profile(BenchContext context, string range)
    {
        ResolvedRange resolved = RelativeRange.Resolve(
            new TimeRange(null, null, range),
            context.Clock
        );
        long budget = context.BudgetMegabytes * 1048576L;
        List<LogFile> files = [.. MergedLogStream.GetCandidateFiles(context.Locator, resolved)];
        files.Sort((a, b) => b.Date.CompareTo(a.Date));

        // Stage 1: read whole files, newest day first, until the budget is spent, and split lines.
        long start = Stopwatch.GetTimestamp();
        var lines = new List<(byte[] Bytes, int Start, int Length, LogFile File)>();
        long bytes = 0;
        foreach (LogFile file in files)
        {
            if (bytes >= budget)
            {
                break;
            }

            byte[] content = ReadShared(file.Path);
            // Lines are kept from the end of the file, as the reverse reader meets them, until
            // the budget is spent, so the stages see the same amount of data a scan does.
            int offset = content.Length;
            while (offset > 0 && bytes < budget)
            {
                int stop = content[offset - 1] == '\n' ? offset - 1 : offset;
                int newline = content.AsSpan(0, stop).LastIndexOf((byte)'\n');
                int lineStart = newline + 1;
                if (stop > lineStart)
                {
                    lines.Add((content, lineStart, stop - lineStart, file));
                }

                bytes += offset - lineStart;
                offset = lineStart;
            }
        }

        double readMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        // Each later stage repeats the earlier ones and keeps nothing, so the garbage collector
        // sees what a real scan gives it; a stage's own cost is its time minus the previous one.
        string[] facetFields = context.FacetFields(range);
        var serializer = Newtonsoft.Json.JsonSerializer.Create(
            new Newtonsoft.Json.JsonSerializerSettings
            {
                DateParseHandling = Newtonsoft.Json.DateParseHandling.None,
                Culture = CultureInfo.InvariantCulture,
            }
        );
        LogFile anyFile = files[0];
        (string Name, Action<string> Work)[] stages =
        [
            ("UTF-8 decode to string", _ => { }),
            ("+ LogEventReader.ReadFromString (Newtonsoft)", text => Parse(text, null)),
            ("+ same, one serializer reused (instead)", text => Parse(text, serializer)),
            (
                "+ map, cheap members only",
                text => MapOrSkip(Parse(text, null), anyFile, RecordParts.None)
            ),
            (
                "+ map Body (render)",
                text => MapOrSkip(Parse(text, null), anyFile, RecordParts.Body)
            ),
            (
                "+ map Template (MD5)",
                text => MapOrSkip(Parse(text, null), anyFile, RecordParts.Template)
            ),
            (
                "+ map Attributes (typed JSON)",
                text => MapOrSkip(Parse(text, null), anyFile, RecordParts.Attributes)
            ),
            ("+ map All parts", text => MapOrSkip(Parse(text, null), anyFile, RecordParts.All)),
            (
                $"+ map All, then resolve {facetFields.Length} facet fields",
                text =>
                {
                    if (MapOrSkip(Parse(text, null), anyFile, RecordParts.All) is { } record)
                    {
                        foreach (string field in facetFields)
                        {
                            foreach (
                                System.Text.Json.JsonElement value in LogFields.Resolve(
                                    record,
                                    field
                                )
                            )
                            {
                                _ = value.GetRawText();
                            }
                        }
                    }
                }
            ),
        ];

        var rows = new List<(string Name, double Ms)> { ("read files + split lines", readMs) };
        foreach ((string name, Action<string> work) in stages)
        {
            start = Stopwatch.GetTimestamp();
            foreach (var line in lines)
            {
                work(Encoding.UTF8.GetString(line.Bytes, line.Start, line.Length));
            }

            rows.Add((name, Stopwatch.GetElapsedTime(start).TotalMilliseconds));
        }

        // The stream itself, read the way the aggregator reads it, with no aggregation work.
        start = Stopwatch.GetTimestamp();
        long streamBytes;
        int streamEvents = 0;
        using (
            MergedLogStream stream = MergedLogStream.Open(
                context.Locator,
                resolved,
                SortDirection.Descending,
                CancellationToken.None
            )
        )
        {
            while (stream.TryRead(out _))
            {
                streamEvents++;
                if (stream.BytesRead >= budget && stream.NextPositions.Count > 0)
                {
                    break;
                }
            }

            streamBytes = stream.BytesRead;
        }

        rows.Add(
            (
                $"MergedLogStream to budget ({streamEvents:N0} events, {streamBytes / 1048576.0:0} MB)",
                Stopwatch.GetElapsedTime(start).TotalMilliseconds
            )
        );

        Console.WriteLine(
            $"Profile {range}: {bytes / 1048576.0:0} MB, {lines.Count:N0} lines, newest first from {files.Count} candidate files. Times are cumulative."
        );
        Console.WriteLine("| Stage | ms | us/event |");
        Console.WriteLine("| --- | ---: | ---: |");
        foreach ((string name, double ms) in rows)
        {
            Console.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"| {name} | {ms:0} | {ms * 1000 / lines.Count:0.00} |"
                )
            );
        }

        Console.WriteLine();
    }

    private static LogEvent? Parse(string text, Newtonsoft.Json.JsonSerializer? serializer)
    {
        try
        {
            return LogEventReader.ReadFromString(text, serializer);
        }
        catch (Exception exception)
            when (exception is InvalidDataException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    private static Core.Records.LogRecord? MapOrSkip(
        LogEvent? logEvent,
        LogFile file,
        RecordParts parts
    ) => logEvent is null ? null : CompactLogEventMapper.Map(logEvent, file, 0, parts);

    private static byte[] ReadShared(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete
        );
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    // The client's selectDiscoveredFields, simplified: the most present non-object attributes.
    private static IEnumerable<string> Discovered(IReadOnlyList<FieldInfo> fields) =>
        fields
            .Where(field =>
                field.PresenceRatio > 0
                && field.Kind != "object"
                && !field.Path.StartsWith('@')
                && !Pinned.Contains(field.Path, StringComparer.OrdinalIgnoreCase)
                && !string.Equals(field.Path, "ExceptionType", StringComparison.OrdinalIgnoreCase)
            )
            .OrderByDescending(field => field.PresenceRatio)
            .ThenBy(field => field.Path, StringComparer.Ordinal)
            .Take(10)
            .Select(field => field.Path);

    private static string? Argument(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    /// <summary>What one call returned, for the table.</summary>
    private sealed record Outcome(long BytesRead, bool? Approximate)
    {
        public static Outcome Of<T>(FileAggregate<T> aggregate, Func<T, bool?> approximate) =>
            new(aggregate.BytesRead, approximate(aggregate.Result));
    }

    /// <summary>The folder, clock and budget every aggregator in a run shares.</summary>
    private sealed class BenchContext(
        string directory,
        DateTimeOffset now,
        int budgetMegabytes,
        string? fixedFacetFields
    )
    {
        private readonly Dictionary<string, string[]> _facetFields = [];

        public int BudgetMegabytes => budgetMegabytes;

        public TimeProvider Clock { get; } = new FixedClock(now);

        public UmbracoLogFileLocator Locator { get; } =
            new(new FolderLoggingConfiguration(directory), MachineName);

        public FileAggregator NewAggregator(out IMemoryCache cache)
        {
            cache = new MemoryCache(new MemoryCacheOptions());
            var options = new LogExplorerOptions();
            options.Files.ScanBudgetMegabytes = budgetMegabytes;
            return new FileAggregator(Locator, Clock, cache, Options.Create(options));
        }

        // Pinned facets plus up to 10 discovered ones, worked out once per range (untimed), so a
        // facets run asks for what the UI asks for (about 15 fields).
        public string[] FacetFields(string range)
        {
            if (fixedFacetFields is not null)
            {
                return fixedFacetFields.Split(',');
            }

            if (!_facetFields.TryGetValue(range, out string[]? fields))
            {
                IReadOnlyList<FieldInfo> discovered = NewAggregator(out _)
                    .GetFields(
                        new LogQuery { Range = new TimeRange(null, null, range) },
                        CancellationToken.None
                    )
                    .Result;
                fields = [.. Pinned, .. Discovered(discovered)];
                _facetFields[range] = fields;
            }

            return fields;
        }
    }

    /// <summary>A folder of log files in Umbraco's default name format, any machine.</summary>
    private sealed class FolderLoggingConfiguration(string directory) : ILoggingConfiguration
    {
        public string LogDirectory => directory;

        public string LogFileNameFormat => "UmbracoTraceLog.{0}..json";

        public string[] GetLogFileNameFormatArguments() => [MachineName];
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
