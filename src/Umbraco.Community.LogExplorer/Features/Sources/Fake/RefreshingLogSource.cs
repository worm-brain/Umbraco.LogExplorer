using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Records;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Core.Sources;

namespace Umbraco.Community.LogExplorer.Features.Sources.Fake;

/// <summary>
/// Wraps a source whose data is fixed at construction (the fake) and rebuilds it once it is older
/// than a set interval, so its data keeps tracking the current time. Every member delegates to
/// the current instance; identity and capabilities come from it too.
/// </summary>
/// <remarks>
/// A cursor from before a rebuild may point into the old data set; for sample data that only
/// means a page can repeat or skip entries once every interval, which is acceptable.
/// </remarks>
internal sealed class RefreshingLogSource : ILogSource
{
    private readonly Func<ILogSource> _create;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _interval;
    private readonly Lock _gate = new();
    private ILogSource _current;
    private DateTimeOffset _createdAt;

    /// <summary>Creates the wrapper and the first instance.</summary>
    /// <param name="create">Builds a fresh inner source.</param>
    /// <param name="clock">Clock used to age the inner source.</param>
    /// <param name="interval">Age after which the inner source is rebuilt.</param>
    public RefreshingLogSource(Func<ILogSource> create, TimeProvider clock, TimeSpan interval)
    {
        _create = create;
        _clock = clock;
        _interval = interval;
        _current = create();
        _createdAt = clock.GetUtcNow();
    }

    /// <inheritdoc />
    public string Alias => Current.Alias;

    /// <inheritdoc />
    public string DisplayName => Current.DisplayName;

    /// <inheritdoc />
    public string Type => Current.Type;

    /// <inheritdoc />
    public bool Sensitive => Current.Sensitive;

    /// <inheritdoc />
    public LogSourceCapabilities Capabilities => Current.Capabilities;

    /// <summary>The live inner source, rebuilt first when it has aged past the interval.</summary>
    internal ILogSource Current
    {
        get
        {
            lock (_gate)
            {
                if (_clock.GetUtcNow() - _createdAt >= _interval)
                {
                    _current = _create();
                    _createdAt = _clock.GetUtcNow();
                }

                return _current;
            }
        }
    }

    /// <inheritdoc />
    public Task<LogPage> QueryAsync(LogQuery query, CancellationToken ct) =>
        Current.QueryAsync(query, ct);

    /// <inheritdoc />
    public Task<HistogramResult> GetHistogramAsync(
        LogQuery query,
        int targetBuckets,
        CancellationToken ct
    ) => Current.GetHistogramAsync(query, targetBuckets, ct);

    /// <inheritdoc />
    public Task<FacetResult> GetFacetsAsync(
        LogQuery query,
        IReadOnlyList<string> fields,
        int top,
        CancellationToken ct
    ) => Current.GetFacetsAsync(query, fields, top, ct);

    /// <inheritdoc />
    public Task<PatternResult> GetPatternsAsync(LogQuery query, int top, CancellationToken ct) =>
        Current.GetPatternsAsync(query, top, ct);

    /// <inheritdoc />
    public Task<ContextResult> GetContextAsync(
        string recordId,
        int before,
        int after,
        CancellationToken ct
    ) => Current.GetContextAsync(recordId, before, after, ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<FieldInfo>> GetFieldsAsync(LogQuery query, CancellationToken ct) =>
        Current.GetFieldsAsync(query, ct);

    /// <inheritdoc />
    public IAsyncEnumerable<LogRecord> TailAsync(LogQuery query, CancellationToken ct) =>
        Current.TailAsync(query, ct);

    /// <inheritdoc />
    public CompileResult Compile(LogQuery query) => Current.Compile(query);

    /// <inheritdoc />
    public ValidationResult ValidateNative(string nativeQuery) =>
        Current.ValidateNative(nativeQuery);
}
