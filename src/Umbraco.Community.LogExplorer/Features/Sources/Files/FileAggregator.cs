using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Serilog.Events;
using Umbraco.Community.LogExplorer.Core.Filtering;
using Umbraco.Community.LogExplorer.Core.Json;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Records;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Core.Severity;

namespace Umbraco.Community.LogExplorer.Features.Sources.Files;

/// <summary>An aggregation result and what it took to produce it.</summary>
/// <typeparam name="T">The aggregation's result type.</typeparam>
/// <param name="Result">The result.</param>
/// <param name="ScannedRange">
/// The range actually scanned: the resolved range, or, when the scan budget was hit, from the
/// oldest event read to the end of the range. Histogram, pattern and field results have no member
/// for it, so it travels here for the source to report.
/// </param>
/// <param name="BytesRead">Bytes of log files this call read; 0 when served from the cache.</param>
internal sealed record FileAggregate<T>(T Result, ResolvedRange ScannedRange, long BytesRead);

/// <summary>
/// Histogram, facets, patterns and fields over Umbraco's log files (BRIEF §10.1, Aggregations).
/// Each is one newest-first pass over <see cref="MergedLogStream"/>, so when the scan budget
/// (<c>Files:ScanBudgetMegabytes</c>) stops it early, the result covers the most recent part of
/// the range and is marked approximate.
/// </summary>
/// <remarks>
/// <para>
/// Every aggregation applies <see cref="LogQuery.Filter"/> and <see cref="LogQuery.Levels"/> with
/// <see cref="LogRecordFilter"/>, except that the histogram ignores the level set so the level
/// toggles can show what they hide (ADR 0004). Malformed-line warnings are not reported: the
/// result contracts have nowhere to put them, and the search page already shows them.
/// </para>
/// <para>
/// Results are cached in <see cref="IMemoryCache"/> for 60 seconds, keyed by a SHA-256 hash of
/// the operation, its parameters and the query with its range resolved, plus each candidate
/// file's name, length and last-write time, so appending to a file misses the cache. A relative
/// range resolves to the clock's "now", so the same relative query a moment later is a different
/// key; only an identical absolute range (a zoomed histogram, a shared link) hits.
/// </para>
/// <para>
/// <see cref="IMemoryCache"/> needs no registration of our own: Umbraco 17.7 and 18.2 register
/// HybridCache for the published cache, and <c>AddHybridCache</c> calls <c>AddMemoryCache</c>.
/// The work is synchronous file I/O; the source that calls it (#35) decides which thread it runs on.
/// </para>
/// </remarks>
internal sealed class FileAggregator
{
    /// <summary>How many buckets each pattern's sparkline has, the same as the sample source.</summary>
    public const int SparklineBuckets = 30;

    /// <summary>The most top values a facet returns (BRIEF §6.6); larger requests are clamped.</summary>
    public const int MaxFacetValues = 10;

    /// <summary>
    /// The most buckets a histogram asks for; larger targets are clamped, so a stray request cannot
    /// allocate millions of one-second buckets over a month.
    /// </summary>
    public const int MaxTargetBuckets = 1000;

    private const long BytesPerMegabyte = 1024 * 1024;

    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    // Bucket widths a histogram can use, smallest first: round numbers people read axes in.
    private static readonly TimeSpan[] BucketLadder =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(3),
        TimeSpan.FromHours(6),
        TimeSpan.FromHours(12),
        TimeSpan.FromDays(1),
    ];

    private static readonly Dictionary<string, int> BandIndexes = SeverityMap
        .ShortNames.Select((name, band) => (name, band))
        .ToDictionary(level => level.name, level => level.band, StringComparer.Ordinal);

    private readonly UmbracoLogFileLocator _locator;
    private readonly TimeProvider _clock;
    private readonly IMemoryCache _cache;
    private readonly IOptions<LogExplorerOptions> _options;

    /// <summary>Creates an aggregator over the files the locator finds.</summary>
    /// <param name="locator">Lists the log files.</param>
    /// <param name="clock">Resolves relative time ranges.</param>
    /// <param name="cache">Holds results for 60 seconds.</param>
    /// <param name="options">Supplies <c>Files:ScanBudgetMegabytes</c>, read on every call.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public FileAggregator(
        UmbracoLogFileLocator locator,
        TimeProvider clock,
        IMemoryCache cache,
        IOptions<LogExplorerOptions> options
    )
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(options);
        _locator = locator;
        _clock = clock;
        _cache = cache;
        _options = options;
    }

    /// <summary>
    /// Entry counts over time, stacked by level, ignoring the query's level set (ADR 0004).
    /// </summary>
    /// <param name="query">The query; its paging and sort are ignored.</param>
    /// <param name="targetBuckets">
    /// About how many buckets to return, at most <see cref="MaxTargetBuckets"/>. The width is the
    /// smallest of 1 s, 5 s, 10 s, 30 s, 1, 5, 10, 15 and 30 min, 1, 3, 6 and 12 h and 1 day that
    /// gives no more than this many buckets (whole days beyond that), so the count lands at or a
    /// little under the target, plus one when the range does not start on a bucket boundary.
    /// </param>
    /// <param name="cancellationToken">Checked between events.</param>
    /// <returns>
    /// Buckets aligned to multiples of the width since the Unix epoch in UTC, so the first may start
    /// before the range; every bucket that overlaps the range is present, with a count for each of
    /// the six levels, zeros included. <see cref="HistogramResult.Range"/> is the whole resolved
    /// range even when approximate; buckets older than the scanned range are then zero.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="targetBuckets"/> is less than 1.</exception>
    /// <exception cref="ArgumentException">The range is invalid, or a regex filter does not parse.</exception>
    /// <exception cref="NotSupportedException">The query has a native query.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public FileAggregate<HistogramResult> GetHistogram(
        LogQuery query,
        int targetBuckets,
        CancellationToken cancellationToken
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(targetBuckets, 1);
        int target = Math.Min(targetBuckets, MaxTargetBuckets);

        return Aggregate(
            "histogram",
            query,
            target,
            applyLevels: false,
            range =>
            {
                TimeSpan size = ChooseBucketSize(range.To - range.From, target);
                DateTimeOffset first = AlignDown(range.From, size);
                int count = (int)Math.Ceiling((range.To - first) / size);
                var counts = new long[count, SeverityMap.ShortNames.Count];

                return new Accumulator<HistogramResult>(
                    candidate =>
                    {
                        int bucket = (int)((candidate.Event.Timestamp - first) / size);
                        counts[bucket, BandOf(candidate.SeverityNumber)]++;
                    },
                    (approximate, _) =>
                        new HistogramResult(
                            range,
                            size,
                            [
                                .. Enumerable
                                    .Range(0, count)
                                    .Select(bucket => new HistogramBucket(
                                        first + (size * bucket),
                                        LevelCounts(band => counts[bucket, band])
                                    )),
                            ],
                            approximate
                        )
                );
            },
            cancellationToken
        );
    }

    /// <summary>The most frequent values of each requested field among the matching entries.</summary>
    /// <param name="query">The query; its paging and sort are ignored.</param>
    /// <param name="fields">Field paths as <see cref="LogFields.Resolve"/> reads them; one facet each, in this order.</param>
    /// <param name="top">How many values per field, clamped to <see cref="MaxFacetValues"/>.</param>
    /// <param name="cancellationToken">Checked between events.</param>
    /// <returns>
    /// Per field, the share of matching entries that have it and its top values, highest count
    /// first, ties in ordinal order of the value's JSON text. Values are equal when their JSON text
    /// is, so strings compare case-sensitively as stored, and an entry counts once per distinct
    /// value even when an array path repeats it.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> or <paramref name="fields"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="top"/> is less than 1.</exception>
    /// <exception cref="ArgumentException">The range is invalid, or a regex filter does not parse.</exception>
    /// <exception cref="NotSupportedException">The query has a native query.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public FileAggregate<FacetResult> GetFacets(
        LogQuery query,
        IReadOnlyList<string> fields,
        int top,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentOutOfRangeException.ThrowIfLessThan(top, 1);
        int take = Math.Min(top, MaxFacetValues);
        string[] requested = [.. fields];

        return Aggregate(
            "facets",
            query,
            new { fields = requested, top = take },
            applyLevels: true,
            _ =>
            {
                long matched = 0;
                long[] present = new long[requested.Length];
                var values = requested
                    .Select(_ => new Dictionary<string, (JsonElement Value, long Count)>(
                        StringComparer.Ordinal
                    ))
                    .ToArray();
                var seen = new HashSet<string>(StringComparer.Ordinal);

                return new Accumulator<FacetResult>(
                    candidate =>
                    {
                        matched++;
                        for (int i = 0; i < requested.Length; i++)
                        {
                            IReadOnlyList<JsonElement> found = LogFields.Resolve(
                                candidate.Record,
                                requested[i]
                            );
                            if (found.Count > 0)
                            {
                                present[i]++;
                            }

                            seen.Clear();
                            foreach (JsonElement value in found)
                            {
                                string key = value.GetRawText();
                                if (seen.Add(key))
                                {
                                    values[i][key] = values[i].TryGetValue(key, out var entry)
                                        ? (entry.Value, entry.Count + 1)
                                        : (value.Clone(), 1);
                                }
                            }
                        }
                    },
                    (approximate, scanned) =>
                        new FacetResult(
                            [
                                .. requested.Select(
                                    (field, i) =>
                                        new Facet(
                                            field,
                                            Ratio(present[i], matched),
                                            [
                                                .. values[i]
                                                    .OrderByDescending(pair => pair.Value.Count)
                                                    .ThenBy(
                                                        pair => pair.Key,
                                                        StringComparer.Ordinal
                                                    )
                                                    .Take(take)
                                                    .Select(pair => new FacetValue(
                                                        pair.Value.Value,
                                                        pair.Value.Count
                                                    )),
                                            ]
                                        )
                                ),
                            ],
                            approximate,
                            scanned
                        )
                );
            },
            cancellationToken
        );
    }

    /// <summary>Matching entries grouped by message template (the Patterns view, BRIEF §6.9).</summary>
    /// <param name="query">The query; its paging and sort are ignored.</param>
    /// <param name="top">How many patterns to return.</param>
    /// <param name="cancellationToken">Checked between events.</param>
    /// <returns>
    /// Patterns by <see cref="LogRecord.TemplateHash"/>, highest count first, ties in ordinal order
    /// of the template. Each has its level counts (all six levels), the newest matching entry as
    /// its sample, and a <see cref="SparklineBuckets"/>-bucket sparkline across the whole resolved
    /// range, so older buckets are zero when the scan stopped early.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="top"/> is less than 1.</exception>
    /// <exception cref="ArgumentException">The range is invalid, or a regex filter does not parse.</exception>
    /// <exception cref="NotSupportedException">The query has a native query.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public FileAggregate<PatternResult> GetPatterns(
        LogQuery query,
        int top,
        CancellationToken cancellationToken
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(top, 1);

        return Aggregate(
            "patterns",
            query,
            top,
            applyLevels: true,
            range =>
            {
                // At least one tick, so a range shorter than 30 ticks still divides.
                TimeSpan width = TimeSpan.FromTicks(
                    Math.Max(1, (range.To - range.From).Ticks / SparklineBuckets)
                );

                // Keyed by template text rather than hash, so the hash is computed once per
                // pattern instead of once per entry; the text determines the hash.
                var groups = new Dictionary<string, PatternTally>(StringComparer.Ordinal);

                return new Accumulator<PatternResult>(
                    candidate =>
                    {
                        string template = candidate.Event.MessageTemplate.Text;
                        if (!groups.TryGetValue(template, out PatternTally? tally))
                        {
                            // Newest first, so the first entry seen is the newest: the sample.
                            tally = new PatternTally(candidate.Record);
                            groups.Add(template, tally);
                        }

                        tally.Count++;
                        tally.Levels[BandOf(candidate.SeverityNumber)]++;
                        int bucket = (int)((candidate.Event.Timestamp - range.From) / width);
                        tally.Sparkline[Math.Min(bucket, SparklineBuckets - 1)]++;
                    },
                    (approximate, _) =>
                        new PatternResult(
                            [
                                .. groups
                                    .OrderByDescending(pair => pair.Value.Count)
                                    .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                                    .Take(top)
                                    .Select(pair => new Pattern(
                                        pair.Value.Sample.TemplateHash!,
                                        pair.Key,
                                        pair.Value.Count,
                                        LevelCounts(band => pair.Value.Levels[band]),
                                        pair.Value.Sample,
                                        pair.Value.Sparkline
                                    )),
                            ],
                            approximate
                        )
                );
            },
            cancellationToken
        );
    }

    /// <summary>The fields present in the matching entries, for autocomplete and the fields panel.</summary>
    /// <param name="query">The query; its paging and sort are ignored.</param>
    /// <param name="cancellationToken">Checked between events.</param>
    /// <returns>
    /// The portable <c>@</c> fields that have a value in some match, in
    /// <see cref="LogFields.Portable"/> order, then attribute paths in ordinal order, with nested
    /// objects expanded as dotted paths (<c>Cart.Total</c>, as <see cref="LogFields.Resolve"/>
    /// reads them) and arrays listed as one <c>array</c> field. A field's kind is that of the first
    /// value seen, newest first. The list has no approximate flag; the scanned range travels on the
    /// wrapper.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is null.</exception>
    /// <exception cref="ArgumentException">The range is invalid, or a regex filter does not parse.</exception>
    /// <exception cref="NotSupportedException">The query has a native query.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public FileAggregate<IReadOnlyList<FieldInfo>> GetFields(
        LogQuery query,
        CancellationToken cancellationToken
    ) =>
        Aggregate<IReadOnlyList<FieldInfo>>(
            "fields",
            query,
            parameters: null,
            applyLevels: true,
            _ =>
            {
                long matched = 0;
                var portable = new Dictionary<string, (string Kind, long Count)>(
                    StringComparer.Ordinal
                );
                var attributes = new Dictionary<string, (string Kind, long Count)>(
                    StringComparer.Ordinal
                );
                var seen = new HashSet<string>(StringComparer.Ordinal);

                return new Accumulator<IReadOnlyList<FieldInfo>>(
                    candidate =>
                    {
                        matched++;
                        LogRecord record = candidate.Record;
                        foreach (string field in LogFields.Portable)
                        {
                            if (LogFields.Resolve(record, field) is [var value, ..])
                            {
                                Tally(
                                    portable,
                                    field,
                                    field == LogFields.Timestamp ? "datetime" : KindOf(value)
                                );
                            }
                        }

                        seen.Clear();
                        foreach ((string name, JsonElement value) in record.Attributes)
                        {
                            CollectPaths(name, value, seen, attributes);
                        }
                    },
                    (_, _) =>
                        [
                            .. LogFields
                                .Portable.Where(portable.ContainsKey)
                                .Select(field => (Path: field, Entry: portable[field]))
                                .Concat(
                                    attributes
                                        .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                                        .Select(pair => (Path: pair.Key, Entry: pair.Value))
                                )
                                .Select(field => new FieldInfo(
                                    field.Path,
                                    field.Entry.Kind,
                                    Ratio(field.Entry.Count, matched)
                                )),
                        ]
                );
            },
            cancellationToken
        );

    /// <summary>
    /// Picks a histogram bucket width from the ladder: the smallest that splits
    /// <paramref name="length"/> into at most <paramref name="target"/> buckets.
    /// </summary>
    /// <param name="length">The range's length.</param>
    /// <param name="target">The wanted bucket count, at least 1.</param>
    /// <returns>A ladder width, or a whole number of days when even one day is too narrow.</returns>
    internal static TimeSpan ChooseBucketSize(TimeSpan length, int target)
    {
        foreach (TimeSpan size in BucketLadder)
        {
            if (Math.Ceiling(length / size) <= target)
            {
                return size;
            }
        }

        return TimeSpan.FromDays(Math.Ceiling(length.TotalDays / target));
    }

    // The shared scan: resolve, check the cache, read newest first until the stream ends or the
    // budget is spent, then cache what was built.
    private FileAggregate<T> Aggregate<T>(
        string operation,
        LogQuery query,
        object? parameters,
        bool applyLevels,
        Func<ResolvedRange, Accumulator<T>> start,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        if (query.NativeQuery is not null)
        {
            // Native-query evaluation for the files source arrives with #34, which wires it here
            // and in the pager together.
            throw new NotSupportedException(
                "The log file aggregations do not evaluate native queries."
            );
        }

        ResolvedRange range = RelativeRange.Resolve(query.Range, _clock);
        string key = CacheKey(operation, query, range, parameters);
        if (_cache.TryGetValue(key, out (T Result, ResolvedRange Scanned) cached))
        {
            return new FileAggregate<T>(cached.Result, cached.Scanned, BytesRead: 0);
        }

        IReadOnlySet<string>? levels = applyLevels ? query.Levels : null;
        long budget = (long)_options.Value.Files.ScanBudgetMegabytes * BytesPerMegabyte;
        Accumulator<T> accumulator = start(range);
        DateTimeOffset? oldestRead = null;
        bool approximate = false;

        using MergedLogStream stream = MergedLogStream.Open(
            _locator,
            range,
            SortDirection.Descending,
            cancellationToken
        );
        while (stream.TryRead(out (LogFile File, LogFileEvent Event) next))
        {
            oldestRead = next.Event.Event.Timestamp;
            var candidate = new Candidate(next.File, next.Event);
            if (Matches(candidate, query.Filter, levels))
            {
                accumulator.Add(candidate);
            }

            // Each stream holds its next event already, so a non-empty NextPositions means events
            // in the range remain unread.
            if (stream.BytesRead >= budget && stream.NextPositions.Count > 0)
            {
                approximate = true;
                break;
            }
        }

        ResolvedRange scanned = approximate ? range with { From = oldestRead!.Value } : range;
        T result = accumulator.Finish(approximate, scanned);
        _cache.Set(
            key,
            (result, scanned),
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheDuration,
                // Ignored unless the host set a size limit, in which case an entry without a size
                // would throw.
                Size = 1,
            }
        );
        return new FileAggregate<T>(result, scanned, stream.BytesRead);
    }

    // Without a filter only the level is needed, which the raw event carries, so most entries are
    // never mapped to a record; the mapping (rendering the message, parsing the exception) is the
    // expensive part of a scan.
    private static bool Matches(
        Candidate candidate,
        FilterNode? filter,
        IReadOnlySet<string>? levels
    )
    {
        if (filter is null)
        {
            return SeverityMap.IsInLevels(candidate.SeverityNumber, levels);
        }

        return LogRecordFilter.Matches(candidate.Record, filter, levels);
    }

    private string CacheKey(
        string operation,
        LogQuery query,
        ResolvedRange range,
        object? parameters
    )
    {
        // Canonical JSON of everything that changes the answer: paging and sort do not, so they
        // are left out, and the level set is sorted so {warn, error} and {error, warn} share a key.
        string request = JsonSerializer.Serialize(
            new
            {
                operation,
                from = range.From.UtcTicks,
                to = range.To.UtcTicks,
                levels = query
                    .Levels?.Select(level => level.ToLowerInvariant())
                    .Distinct()
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
                filter = query.Filter,
                parameters,
            },
            LogJson.Options
        );

        var material = new StringBuilder(request);
        foreach (LogFile file in MergedLogStream.GetCandidateFiles(_locator, range))
        {
            var info = new FileInfo(file.Path);
            material.Append(
                CultureInfo.InvariantCulture,
                $"\n{file.FileName}|{(info.Exists ? info.Length : -1)}|{(info.Exists ? info.LastWriteTimeUtc.Ticks : 0)}"
            );
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(material.ToString()));
        return "LogExplorer:files:aggregate:" + Convert.ToHexStringLower(hash);
    }

    private static Dictionary<string, long> LevelCounts(Func<int, long> countOfBand) =>
        SeverityMap
            .ShortNames.Select((name, band) => (name, band))
            .ToDictionary(
                level => level.name,
                level => countOfBand(level.band),
                StringComparer.Ordinal
            );

    // Index into SeverityMap.ShortNames; unspecified severities count as INFO, as everywhere else.
    private static int BandOf(int severityNumber) =>
        BandIndexes[SeverityMap.ToShortName(severityNumber)];

    private static DateTimeOffset AlignDown(DateTimeOffset time, TimeSpan size)
    {
        long ticks =
            time.UtcTicks - (time.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) % size.Ticks;
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    private static double Ratio(long part, long whole) => whole == 0 ? 0 : (double)part / whole;

    private static void CollectPaths(
        string path,
        JsonElement value,
        HashSet<string> seen,
        Dictionary<string, (string Kind, long Count)> paths
    )
    {
        // Seen per record, so a record counts once per path; nulls count as absent, as in LogFields.
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined || !seen.Add(path))
        {
            return;
        }

        Tally(paths, path, KindOf(value));
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in value.EnumerateObject())
            {
                CollectPaths(path + "." + property.Name, property.Value, seen, paths);
            }
        }
    }

    private static void Tally(
        Dictionary<string, (string Kind, long Count)> paths,
        string path,
        string kind
    ) =>
        paths[path] = paths.TryGetValue(path, out var entry)
            ? (entry.Kind, entry.Count + 1)
            : (kind, 1);

    private static string KindOf(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.Number => "number",
            JsonValueKind.True or JsonValueKind.False => "bool",
            JsonValueKind.Object => "object",
            JsonValueKind.Array => "array",
            JsonValueKind.String when IsIsoDate(value.GetString()!) => "datetime",
            _ => "string",
        };

    // The same shape test LogRecordFilter uses for date ordering, so a "datetime" field compares
    // chronologically in a filter.
    private static bool IsIsoDate(string text) =>
        text.Length >= 10
        && text[4] == '-'
        && text[7] == '-'
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    /// <summary>One event read by a scan, mapped to a record only when something asks for it.</summary>
    private sealed class Candidate(LogFile file, LogFileEvent fileEvent)
    {
        private LogRecord? _record;

        public LogEvent Event => fileEvent.Event;

        // The same conversion CompactLogEventMapper makes, without mapping the rest.
        public int SeverityNumber => SeverityMap.FromSerilog(fileEvent.Event.Level.ToString());

        public LogRecord Record =>
            _record ??= CompactLogEventMapper.Map(fileEvent.Event, file, fileEvent.Offset);
    }

    /// <summary>
    /// One aggregation's running state: what to do per matching event, and how to build the result
    /// from whether the scan stopped early and the range it covered.
    /// </summary>
    private sealed record Accumulator<T>(
        Action<Candidate> Add,
        Func<bool, ResolvedRange, T> Finish
    );

    private sealed class PatternTally(LogRecord sample)
    {
        public LogRecord Sample { get; } = sample;

        public long Count { get; set; }

        public long[] Levels { get; } = new long[SeverityMap.ShortNames.Count];

        public long[] Sparkline { get; } = new long[SparklineBuckets];
    }
}
