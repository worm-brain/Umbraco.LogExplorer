using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using LogExplorer.Samples.LogGenerator;
using Umbraco.Cms.Core.Logging;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Features.Sources.Files;

namespace Umbraco.Community.LogExplorer.Benchmarks;

/// <summary>
/// Times the files provider's first page over about 2 GB of logs covering 7 days (BRIEF §14 Phase 1
/// acceptance 4, §17: under 500 ms on a laptop SSD; #36).
/// </summary>
/// <remarks>
/// <para>
/// A Stopwatch harness rather than BenchmarkDotNet: the budget is the wall-clock latency of one
/// call that reads files, measured in hundreds of milliseconds, where BenchmarkDotNet's
/// nanosecond-grade machinery (pilot and overhead stages, a generated project built per run) adds
/// a package and minutes of runtime without changing the answer. Its out-of-process build also
/// does not know the repo's per-major <c>bin/u18</c> layout (ADR 0010).
/// </para>
/// <para>
/// The data set is written once with the sample generator's bulk mode into a cache directory and
/// reused while its manifest matches; the clock is fixed a minute after the set's last event, so "last 7 days"
/// always covers it. After the first run the files sit in the OS file cache, so the numbers are
/// warm-cache numbers; a cold read after a reboot is slower.
/// </para>
/// <para>
/// Arguments: <c>--data &lt;dir&gt;</c> (default <c>%TEMP%/LogExplorer.Benchmarks/bulk</c>),
/// <c>--gigabytes &lt;n&gt;</c> (default 2), <c>--runs &lt;n&gt;</c> (default 10),
/// <c>--regenerate</c>, and <c>--delete</c> to remove the data set and exit.
/// </para>
/// </remarks>
internal static class Program
{
    private const string ManifestName = "bulk-manifest.json";
    private const int Take = 100;
    private const int BudgetMilliseconds = 500;

    private static int Main(string[] args)
    {
        // The aggregation benchmark has its own arguments and data (AggregationBenchmark).
        if (args.Length > 0 && args[0] == "aggregations")
        {
            return AggregationBenchmark.Run(args[1..]);
        }

        string directory = Argument(args, "--data") ?? DefaultDirectory();
        if (args.Contains("--delete"))
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }

            Console.WriteLine($"Deleted {directory}");
            return 0;
        }

        double gigabytes = double.Parse(
            Argument(args, "--gigabytes") ?? "2",
            CultureInfo.InvariantCulture
        );
        int runs = int.Parse(Argument(args, "--runs") ?? "10", CultureInfo.InvariantCulture);

        Manifest manifest = EnsureDataSet(
            directory,
            (long)(gigabytes * 1024 * 1024 * 1024),
            args.Contains("--regenerate")
        );
        Console.WriteLine(
            $"Data: {directory}: {manifest.Bytes / (1024.0 * 1024 * 1024):0.00} GB, {manifest.Events:N0} events, {manifest.Files} files, 7 days to {manifest.End:u}"
        );
        Console.WriteLine(
            $"Runs per case: a first run (\"First\"), then {runs} for the median, min and max; the full scan gets at most 3 and no first run."
        );
        Console.WriteLine();

        var pager = new LogFilePager(
            new UmbracoLogFileLocator(new BulkLoggingConfiguration(directory), MachineName),
            new FixedClock(manifest.End.AddMinutes(1))
        );

        Case[] cases =
        [
            new("(a) newest first, no filter", Query(SortDirection.Descending), InBudget: true),
            new(
                "(b) newest first, text \"timeout\"",
                Query(SortDirection.Descending) with
                {
                    Filter = new TextNode("timeout"),
                },
                InBudget: true
            ),
            new(
                "(c) newest first, levels warn+",
                Query(SortDirection.Descending) with
                {
                    Levels = new HashSet<string> { "warn", "error", "fatal" },
                },
                InBudget: true
            ),
            new(
                "(d) newest first, rare \"boot failed\" (full scan)",
                Query(SortDirection.Descending) with
                {
                    Filter = new TextNode("boot failed", Phrase: true),
                },
                InBudget: false,
                FullScan: true
            ),
            new("(e) oldest first, no filter", Query(SortDirection.Ascending), InBudget: false),
        ];

        Console.WriteLine(
            "| Case | First ms | Median ms | Min ms | Max ms | Records | MB read | Budget |"
        );
        Console.WriteLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |");
        bool allWithinBudget = true;
        foreach (Case benchmark in cases)
        {
            Result result = Measure(pager, benchmark, runs);
            string verdict =
                !benchmark.InBudget ? "reference"
                : result.MedianMilliseconds < BudgetMilliseconds ? "pass"
                : "FAIL";
            allWithinBudget &= verdict != "FAIL";
            Console.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"| {benchmark.Name} | {(double.IsNaN(result.FirstMilliseconds) ? "-" : result.FirstMilliseconds.ToString("0.0", CultureInfo.InvariantCulture))} | {result.MedianMilliseconds:0.0} | {result.MinMilliseconds:0.0} | {result.MaxMilliseconds:0.0} | {result.Records} | {result.BytesRead / (1024.0 * 1024):0.0} | {verdict} |"
                )
            );
        }

        return allWithinBudget ? 0 : 1;
    }

    private const string MachineName = "BULK-NODE1";

    private static LogQuery Query(SortDirection sort) =>
        new()
        {
            Range = new TimeRange(null, null, "7d"),
            Take = Take,
            Sort = sort,
        };

    // A full scan takes seconds, so it gets at most three runs and no warm-up (JIT is noise there).
    private static Result Measure(LogFilePager pager, Case benchmark, int runs)
    {
        if (benchmark.FullScan)
        {
            runs = Math.Min(runs, 3);
        }

        double first = double.NaN;
        FilePage? page = null;
        if (!benchmark.FullScan)
        {
            first = Time(pager, benchmark.Query, out page);
        }

        var timings = new double[runs];
        for (int i = 0; i < runs; i++)
        {
            timings[i] = Time(pager, benchmark.Query, out page);
        }

        if (Environment.GetEnvironmentVariable("LOG_EXPLORER_BENCHMARK_VERBOSE") == "1")
        {
            Console.WriteLine(
                $"  {benchmark.Name}: {string.Join(", ", timings.Select(t => t.ToString("0.0", CultureInfo.InvariantCulture)))}"
            );
        }

        Array.Sort(timings);
        return new Result(
            first,
            timings[runs / 2],
            timings[0],
            timings[^1],
            page!.Page.Records.Count,
            page.BytesRead
        );
    }

    private static double Time(LogFilePager pager, LogQuery query, out FilePage page)
    {
        long start = Stopwatch.GetTimestamp();
        page = pager.Query(query, Take, CancellationToken.None);
        return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    private static Manifest EnsureDataSet(string directory, long targetBytes, bool regenerate)
    {
        string manifestPath = Path.Combine(directory, ManifestName);
        if (!regenerate && File.Exists(manifestPath))
        {
            Manifest? existing = JsonSerializer.Deserialize<Manifest>(
                File.ReadAllText(manifestPath)
            );
            if (existing is not null && existing.TargetBytes == targetBytes)
            {
                return existing;
            }
        }

        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        Console.WriteLine(
            $"Writing {targetBytes / (1024.0 * 1024 * 1024):0.00} GB to {directory}..."
        );
        long start = Stopwatch.GetTimestamp();
        // Whole seconds, so the manifest's round trip through JSON gives back the exact instant.
        DateTimeOffset end = DateTimeOffset.FromUnixTimeSeconds(
            DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        );
        BulkLogResult result = BulkLogWriter.Write(
            new BulkLogSettings
            {
                Directory = directory,
                TargetBytes = targetBytes,
                End = end,
            }
        );
        Console.WriteLine($"Wrote in {Stopwatch.GetElapsedTime(start).TotalSeconds:0.0} s.");

        var manifest = new Manifest(
            targetBytes,
            end,
            result.Bytes,
            result.Events,
            result.Files.Count
        );
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));
        return manifest;
    }

    private static string DefaultDirectory() =>
        Path.Combine(Path.GetTempPath(), "LogExplorer.Benchmarks", "bulk");

    private static string? Argument(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    /// <summary>One query to time.</summary>
    /// <param name="Name">Row label.</param>
    /// <param name="Query">The query.</param>
    /// <param name="InBudget">Whether the 500 ms budget applies; the others are for reference.</param>
    /// <param name="FullScan">Whether the query reads the whole set, so it gets fewer runs.</param>
    private sealed record Case(string Name, LogQuery Query, bool InBudget, bool FullScan = false);

    private sealed record Result(
        double FirstMilliseconds,
        double MedianMilliseconds,
        double MinMilliseconds,
        double MaxMilliseconds,
        int Records,
        long BytesRead
    );

    /// <summary>The data set's identity, so a later run reuses it only when it was written for the same size.</summary>
    private sealed record Manifest(
        long TargetBytes,
        DateTimeOffset End,
        long Bytes,
        long Events,
        int Files
    );

    /// <summary>Umbraco's default file name format, with the bulk set's first machine as "this" machine.</summary>
    private sealed class BulkLoggingConfiguration(string directory) : ILoggingConfiguration
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
