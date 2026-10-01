using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Records;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Core.Sources;

namespace Umbraco.Community.LogExplorer.Features.Sources;

/// <summary>
/// Wraps a source configured with <c>AllowNativeQuery: false</c> (BRIEF §12): any query carrying a
/// <see cref="LogQuery.NativeQuery"/>, and <see cref="ValidateNative"/>, throw
/// <see cref="NotSupportedException"/> (<c>unsupported_feature</c>). Everything else, including
/// compiling chips for "Show query", passes through unchanged.
/// </summary>
/// <remarks>
/// The capabilities are passed through as declared rather than with
/// <see cref="LogSourceFeatures.NativeQuery"/> removed, because that flag also gates
/// <see cref="ILogSource.Compile"/>, and "Show query" stays useful when native mode is off. The UI
/// learns about the restriction from <c>allowNativeQuery</c> on <c>GET /sources</c>.
/// </remarks>
/// <param name="inner">The configured source.</param>
internal sealed class NativeQueryDisabledSource(ILogSource inner) : ILogSource
{
    /// <summary>The wrapped source.</summary>
    internal ILogSource Inner { get; } = inner;

    /// <inheritdoc />
    public string Alias => Inner.Alias;

    /// <inheritdoc />
    public string DisplayName => Inner.DisplayName;

    /// <inheritdoc />
    public string Type => Inner.Type;

    /// <inheritdoc />
    public bool Sensitive => Inner.Sensitive;

    /// <inheritdoc />
    public LogSourceCapabilities Capabilities => Inner.Capabilities;

    /// <inheritdoc />
    public Task<LogPage> QueryAsync(LogQuery query, CancellationToken ct) =>
        Inner.QueryAsync(Checked(query), ct);

    /// <inheritdoc />
    public Task<HistogramResult> GetHistogramAsync(
        LogQuery query,
        int targetBuckets,
        CancellationToken ct
    ) => Inner.GetHistogramAsync(Checked(query), targetBuckets, ct);

    /// <inheritdoc />
    public Task<FacetResult> GetFacetsAsync(
        LogQuery query,
        IReadOnlyList<string> fields,
        int top,
        CancellationToken ct
    ) => Inner.GetFacetsAsync(Checked(query), fields, top, ct);

    /// <inheritdoc />
    public Task<PatternResult> GetPatternsAsync(LogQuery query, int top, CancellationToken ct) =>
        Inner.GetPatternsAsync(Checked(query), top, ct);

    /// <inheritdoc />
    public Task<ContextResult> GetContextAsync(
        string recordId,
        int before,
        int after,
        CancellationToken ct
    ) => Inner.GetContextAsync(recordId, before, after, ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<FieldInfo>> GetFieldsAsync(LogQuery query, CancellationToken ct) =>
        Inner.GetFieldsAsync(Checked(query), ct);

    /// <inheritdoc />
    public IAsyncEnumerable<LogRecord> TailAsync(LogQuery query, CancellationToken ct) =>
        Inner.TailAsync(Checked(query), ct);

    /// <inheritdoc />
    public CompileResult Compile(LogQuery query) => Inner.Compile(Checked(query));

    /// <inheritdoc />
    public ValidationResult ValidateNative(string nativeQuery) => throw Disabled();

    private LogQuery Checked(LogQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.NativeQuery is null ? query : throw Disabled();
    }

    private NotSupportedException Disabled() =>
        new($"Native queries are turned off for log source '{Alias}' (AllowNativeQuery).");
}
