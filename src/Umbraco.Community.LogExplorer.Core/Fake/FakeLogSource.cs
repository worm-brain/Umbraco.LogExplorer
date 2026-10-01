using System.Globalization;
using System.Text.Json;
using Umbraco.Community.LogExplorer.Core.Filtering;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Records;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Core.Severity;
using Umbraco.Community.LogExplorer.Core.Sources;

namespace Umbraco.Community.LogExplorer.Core.Fake;

/// <summary>
/// An in-memory source holding the UI brief §12 sample hour (238 entries), for UI work against no
/// real store and as the reference source for the contract suite. Performance is not a goal:
/// every call scans the whole list.
/// <para>
/// The hour is generated once, at construction, ending at the clock's "now" (or at
/// <see cref="FakeLogSourceOptions.FixedNow"/>), so a given clock always yields the same data. A
/// long-running host therefore sees the data age out of "last 1 hour"; create a new instance to
/// refresh it.
/// </para>
/// <para>
/// Cursors are offsets into the sorted matches; that is stable only because the data never
/// changes, which is fine for a fake.
/// </para>
/// </summary>
public sealed class FakeLogSource : LogSourceBase
{
    /// <summary>Provider type name.</summary>
    public const string SourceType = "Fake";

    private const int SparklineBuckets = 30;

    private readonly FakeLogSourceOptions _options;
    private readonly TimeProvider _clock;

    /// <summary>Creates the source and generates its sample hour.</summary>
    /// <param name="options">Identity and capabilities; defaults when null.</param>
    /// <param name="clock">
    /// Source of "now" for the data and for relative ranges; <see cref="TimeProvider.System"/> when
    /// null. Ignored when <see cref="FakeLogSourceOptions.FixedNow"/> is set.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="FakeLogSourceOptions.SampleHours"/> is less than 1.
    /// </exception>
    public FakeLogSource(FakeLogSourceOptions? options = null, TimeProvider? clock = null)
    {
        _options = options ?? new FakeLogSourceOptions();
        ArgumentOutOfRangeException.ThrowIfLessThan(_options.SampleHours, 1, nameof(options));
        _clock = _options.FixedNow is { } fixedNow
            ? new FixedTimeProvider(fixedNow)
            : clock ?? TimeProvider.System;

        // Oldest hour first, so Records stays in timestamp order.
        DateTimeOffset now = _clock.GetUtcNow();
        Records = Enumerable
            .Range(0, _options.SampleHours)
            .Reverse()
            .SelectMany(hoursAgo =>
                FakeLogData
                    .Generate(now.AddHours(-hoursAgo))
                    .Select(record =>
                        record with
                        {
                            Id = hoursAgo == 0 ? record.Id : $"h{hoursAgo}.{record.Id}",
                            SourceAlias = _options.Alias,
                        }
                    )
            )
            .ToArray();
        Capabilities = new LogSourceCapabilities(
            _options.Features,
            _options.Operators,
            _options.NativeLanguage,
            _options.MaxRange,
            _options.MaxPageSize
        );
    }

    /// <summary>Every record the source holds, oldest first.</summary>
    public IReadOnlyList<LogRecord> Records { get; }

    /// <inheritdoc />
    public override string Alias => _options.Alias;

    /// <inheritdoc />
    public override string DisplayName => _options.DisplayName;

    /// <inheritdoc />
    public override string Type => SourceType;

    /// <inheritdoc />
    public override bool Sensitive => _options.Sensitive;

    /// <inheritdoc />
    public override LogSourceCapabilities Capabilities { get; }

    /// <inheritdoc />
    /// <remarks>
    /// A native query is checked like <see cref="ILogSource.ValidateNative"/> (throwing
    /// <see cref="InvalidNativeQueryException"/> when invalid) but not evaluated; the page carries
    /// a warning saying so.
    /// </remarks>
    protected override Task<LogPage> QueryCoreAsync(LogQuery query, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Take, 1, nameof(query));
        if (query.NativeQuery is not null && !Capabilities.Supports(LogSourceFeatures.NativeQuery))
        {
            throw new NotSupportedException(
                $"Log source '{Alias}' does not accept native queries."
            );
        }

        if (
            query.NativeQuery is not null
            && FakeQueryCompiler.Validate(query.NativeQuery) is { Valid: false } invalid
        )
        {
            throw new InvalidNativeQueryException(invalid.Error!) { Position = invalid.Position };
        }

        List<LogRecord> matches = Select(query, applyLevels: true, out ResolvedRange range);
        if (query.Sort == SortDirection.Descending)
        {
            matches.Reverse();
        }

        int offset = ParseCursor(query.Cursor);
        int take = Math.Min(query.Take, Capabilities.MaxPageSize);
        LogRecord[] page = matches.Skip(offset).Take(take).ToArray();
        string? next =
            offset + page.Length < matches.Count
                ? (offset + page.Length).ToString(CultureInfo.InvariantCulture)
                : null;
        string[] warnings = query.NativeQuery is null
            ? []
            : ["The sample source does not evaluate native queries; only the filter was applied."];

        return Task.FromResult(
            new LogPage(page, next, range, matches.Count, TotalIsLowerBound: false, warnings)
        );
    }

    /// <inheritdoc />
    /// <remarks>Bucket size is the range divided by the target, rounded up to a whole second.</remarks>
    protected override Task<HistogramResult> GetHistogramCoreAsync(
        LogQuery query,
        int targetBuckets,
        CancellationToken ct
    )
    {
        // Levels are deliberately not applied: the histogram shows what the toggles hide (ADR 0004).
        List<LogRecord> matches = Select(query, applyLevels: false, out ResolvedRange range);

        TimeSpan length = range.To - range.From;
        long seconds = Math.Max(1, (long)Math.Ceiling(length.TotalSeconds / targetBuckets));
        TimeSpan size = TimeSpan.FromSeconds(seconds);
        int count = (int)Math.Ceiling(length / size);

        var counts = new Dictionary<string, long>[count];
        for (int i = 0; i < count; i++)
        {
            counts[i] = EmptyLevelCounts();
        }

        foreach (LogRecord record in matches)
        {
            int index = (int)((record.Timestamp - range.From) / size);
            counts[index][SeverityMap.ToShortName(record.SeverityNumber)]++;
        }

        HistogramBucket[] buckets = counts
            .Select((levels, i) => new HistogramBucket(range.From + (size * i), levels))
            .ToArray();
        return Task.FromResult(new HistogramResult(range, size, buckets, Approximate: false));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Values are compared by their exact JSON text; ties on count are ordered by that text.
    /// </remarks>
    protected override Task<FacetResult> GetFacetsCoreAsync(
        LogQuery query,
        IReadOnlyList<string> fields,
        int top,
        CancellationToken ct
    )
    {
        List<LogRecord> matches = Select(query, applyLevels: true, out ResolvedRange range);

        Facet[] facets = fields
            .Select(field =>
            {
                // Each record counts once per distinct value, even if an array repeats it.
                IReadOnlyList<JsonElement>[] perRecord = matches
                    .Select(record => LogFields.Resolve(record, field))
                    .ToArray();
                FacetValue[] values = perRecord
                    .SelectMany(found => found.DistinctBy(value => value.GetRawText()))
                    .GroupBy(value => value.GetRawText(), StringComparer.Ordinal)
                    .OrderByDescending(group => group.Count())
                    .ThenBy(group => group.Key, StringComparer.Ordinal)
                    .Take(top)
                    .Select(group => new FacetValue(group.First().Clone(), group.Count()))
                    .ToArray();

                return new Facet(
                    field,
                    Ratio(perRecord.Count(found => found.Count > 0), matches.Count),
                    values
                );
            })
            .ToArray();

        return Task.FromResult(new FacetResult(facets, Approximate: false, range));
    }

    /// <inheritdoc />
    /// <remarks>Records without a template hash are not part of any pattern.</remarks>
    protected override Task<PatternResult> GetPatternsCoreAsync(
        LogQuery query,
        int top,
        CancellationToken ct
    )
    {
        List<LogRecord> matches = Select(query, applyLevels: true, out ResolvedRange range);
        TimeSpan bucket = (range.To - range.From) / SparklineBuckets;

        Pattern[] patterns = matches
            .Where(record => record.TemplateHash is not null)
            .GroupBy(record => record.TemplateHash!, StringComparer.Ordinal)
            .Select(group =>
            {
                Dictionary<string, long> levels = EmptyLevelCounts();
                long[] sparkline = new long[SparklineBuckets];
                foreach (LogRecord record in group)
                {
                    levels[SeverityMap.ToShortName(record.SeverityNumber)]++;
                    int index = (int)((record.Timestamp - range.From) / bucket);
                    sparkline[Math.Min(index, SparklineBuckets - 1)]++;
                }

                LogRecord newest = group.MaxBy(record => record.Timestamp)!;
                return new Pattern(
                    group.Key,
                    newest.MessageTemplate ?? "",
                    group.Count(),
                    levels,
                    newest,
                    sparkline
                );
            })
            .OrderByDescending(pattern => pattern.Count)
            .ThenBy(pattern => pattern.Template, StringComparer.Ordinal)
            .Take(top)
            .ToArray();

        return Task.FromResult(new PatternResult(patterns, Approximate: false));
    }

    /// <inheritdoc />
    protected override Task<ContextResult> GetContextCoreAsync(
        string recordId,
        int before,
        int after,
        CancellationToken ct
    )
    {
        int index = Records.ToList().FindIndex(record => record.Id == recordId);
        if (index < 0)
        {
            throw new KeyNotFoundException($"Log source '{Alias}' has no record '{recordId}'.");
        }

        int first = Math.Max(0, index - before);
        LogRecord[] preceding = Records.Skip(first).Take(index - first).ToArray();
        LogRecord[] following = Records.Skip(index + 1).Take(after).ToArray();
        return Task.FromResult(new ContextResult(preceding, Records[index], following));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Lists portable fields that have a value in some match, then attribute paths (nested objects
    /// expanded with dots) in ordinal order. A field's kind comes from the first value seen.
    /// </remarks>
    protected override Task<IReadOnlyList<FieldInfo>> GetFieldsCoreAsync(
        LogQuery query,
        CancellationToken ct
    )
    {
        List<LogRecord> matches = Select(query, applyLevels: true, out _);

        // Path -> (kind, number of records containing it), in first-seen order for portable fields.
        var portable = new Dictionary<string, (string Kind, int Count)>(StringComparer.Ordinal);
        var attributes = new SortedDictionary<string, (string Kind, int Count)>(
            StringComparer.Ordinal
        );

        foreach (LogRecord record in matches)
        {
            foreach (string field in LogFields.Portable)
            {
                if (LogFields.Resolve(record, field) is [var value, ..])
                {
                    Count(
                        portable,
                        field,
                        field == LogFields.Timestamp ? "datetime" : KindOf(value)
                    );
                }
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach ((string name, JsonElement value) in record.Attributes)
            {
                CollectPaths(name, value, seen, attributes);
            }
        }

        FieldInfo[] fields = portable
            .Concat(attributes)
            .Select(pair => new FieldInfo(
                pair.Key,
                pair.Value.Kind,
                Ratio(pair.Value.Count, matches.Count)
            ))
            .ToArray();
        return Task.FromResult<IReadOnlyList<FieldInfo>>(fields);
    }

    /// <inheritdoc />
    protected override CompileResult CompileCore(LogQuery query) =>
        FakeQueryCompiler.Compile(query, Capabilities.Operators);

    /// <inheritdoc />
    /// <remarks>
    /// The pseudo-language is never executed, so only quotes and brackets are checked (see
    /// <see cref="FakeQueryCompiler.Validate"/>).
    /// </remarks>
    protected override ValidationResult ValidateNativeCore(string nativeQuery) =>
        FakeQueryCompiler.Validate(nativeQuery);

    // Range, optional level set and filter, oldest first.
    private List<LogRecord> Select(LogQuery query, bool applyLevels, out ResolvedRange range)
    {
        range = RelativeRange.Resolve(query.Range, _clock);
        if (Capabilities.MaxRange is { } max && range.To - range.From > max)
        {
            throw new ArgumentException(
                $"Log source '{Alias}' accepts ranges up to {max}.",
                nameof(query)
            );
        }

        if (
            FakeQueryCompiler.Undeclared(query.Filter, Capabilities.Operators).FirstOrDefault() is
            { } undeclared
        )
        {
            throw new NotSupportedException(
                $"Log source '{Alias}' does not support the {undeclared.Op} operator."
            );
        }

        ResolvedRange window = range;
        IReadOnlySet<string>? levels = applyLevels ? query.Levels : null;
        return Records
            .Where(record => record.Timestamp >= window.From && record.Timestamp < window.To)
            .Where(record => LogRecordFilter.Matches(record, query.Filter, levels))
            .ToList();
    }

    private static int ParseCursor(string? cursor)
    {
        if (cursor is null)
        {
            return 0;
        }

        return int.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out int offset)
            ? offset
            : throw new ArgumentException($"Invalid cursor '{cursor}'.", nameof(cursor));
    }

    private static Dictionary<string, long> EmptyLevelCounts() =>
        SeverityMap.ShortNames.ToDictionary(name => name, _ => 0L, StringComparer.Ordinal);

    private static double Ratio(int part, int whole) => whole == 0 ? 0 : (double)part / whole;

    private static void CollectPaths(
        string path,
        JsonElement value,
        HashSet<string> seen,
        IDictionary<string, (string Kind, int Count)> paths
    )
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined || !seen.Add(path))
        {
            return;
        }

        Count(paths, path, KindOf(value));
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in value.EnumerateObject())
            {
                CollectPaths(path + "." + property.Name, property.Value, seen, paths);
            }
        }
    }

    private static void Count(
        IDictionary<string, (string Kind, int Count)> paths,
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

    private static bool IsIsoDate(string text) =>
        text.Length >= 10
        && text[4] == '-'
        && text[7] == '-'
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
