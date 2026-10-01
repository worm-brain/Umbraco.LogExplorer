using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Records;
using Umbraco.Community.LogExplorer.Core.Results;

namespace Umbraco.Community.LogExplorer.Core.Sources;

/// <summary>
/// Base class for providers. The public <see cref="ILogSource"/> members are fixed here: each one
/// checks that <see cref="Capabilities"/> declares its feature (throwing
/// <see cref="NotSupportedException"/> otherwise), checks the cancellation token, and then calls
/// the matching protected <c>...Core</c> member. Providers override the <c>...Core</c> members
/// for the features they declare.
/// <para>
/// Gating in the base, rather than leaving each override to check, means a source's declared
/// features and its behaviour cannot drift apart, even when capabilities are configurable.
/// </para>
/// </summary>
public abstract class LogSourceBase : ILogSource
{
    /// <inheritdoc />
    public abstract string Alias { get; }

    /// <inheritdoc />
    public abstract string DisplayName { get; }

    /// <inheritdoc />
    public abstract string Type { get; }

    /// <inheritdoc />
    public virtual bool Sensitive => false;

    /// <inheritdoc />
    public abstract LogSourceCapabilities Capabilities { get; }

    /// <inheritdoc />
    public Task<LogPage> QueryAsync(LogQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();
        return QueryCoreAsync(query, ct);
    }

    /// <inheritdoc />
    public Task<HistogramResult> GetHistogramAsync(
        LogQuery query,
        int targetBuckets,
        CancellationToken ct
    )
    {
        Require(LogSourceFeatures.Histogram, nameof(GetHistogramAsync));
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(targetBuckets, 1);
        ct.ThrowIfCancellationRequested();
        return GetHistogramCoreAsync(query, targetBuckets, ct);
    }

    /// <inheritdoc />
    public Task<FacetResult> GetFacetsAsync(
        LogQuery query,
        IReadOnlyList<string> fields,
        int top,
        CancellationToken ct
    )
    {
        Require(LogSourceFeatures.Facets, nameof(GetFacetsAsync));
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentOutOfRangeException.ThrowIfLessThan(top, 1);
        ct.ThrowIfCancellationRequested();
        return GetFacetsCoreAsync(query, fields, top, ct);
    }

    /// <inheritdoc />
    public Task<PatternResult> GetPatternsAsync(LogQuery query, int top, CancellationToken ct)
    {
        Require(LogSourceFeatures.Patterns, nameof(GetPatternsAsync));
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(top, 1);
        ct.ThrowIfCancellationRequested();
        return GetPatternsCoreAsync(query, top, ct);
    }

    /// <inheritdoc />
    public Task<ContextResult> GetContextAsync(
        string recordId,
        int before,
        int after,
        CancellationToken ct
    )
    {
        Require(LogSourceFeatures.Context, nameof(GetContextAsync));
        ArgumentNullException.ThrowIfNull(recordId);
        ArgumentOutOfRangeException.ThrowIfNegative(before);
        ArgumentOutOfRangeException.ThrowIfNegative(after);
        ct.ThrowIfCancellationRequested();
        return GetContextCoreAsync(recordId, before, after, ct);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<FieldInfo>> GetFieldsAsync(LogQuery query, CancellationToken ct)
    {
        Require(LogSourceFeatures.FieldDiscovery, nameof(GetFieldsAsync));
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();
        return GetFieldsCoreAsync(query, ct);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<LogRecord> TailAsync(LogQuery query, CancellationToken ct)
    {
        // Checked eagerly, on the call rather than on first MoveNextAsync, so callers learn about
        // an unsupported feature before they start streaming.
        Require(LogSourceFeatures.Tail, nameof(TailAsync));
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();
        return TailCore(query, ct);
    }

    /// <inheritdoc />
    public CompileResult Compile(LogQuery query)
    {
        Require(LogSourceFeatures.NativeQuery, nameof(Compile));
        ArgumentNullException.ThrowIfNull(query);
        return CompileCore(query);
    }

    /// <inheritdoc />
    public ValidationResult ValidateNative(string nativeQuery)
    {
        Require(LogSourceFeatures.NativeQuery, nameof(ValidateNative));
        ArgumentNullException.ThrowIfNull(nativeQuery);
        return ValidateNativeCore(nativeQuery);
    }

    /// <summary>Implements <see cref="QueryAsync"/>; arguments are already checked.</summary>
    /// <param name="query">The query.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The page.</returns>
    protected abstract Task<LogPage> QueryCoreAsync(LogQuery query, CancellationToken ct);

    /// <summary>Implements <see cref="GetHistogramAsync"/>; only called when the feature is declared.</summary>
    /// <param name="query">The query.</param>
    /// <param name="targetBuckets">Desired bucket count, at least 1.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The histogram.</returns>
    /// <exception cref="NotSupportedException">Not overridden.</exception>
    protected virtual Task<HistogramResult> GetHistogramCoreAsync(
        LogQuery query,
        int targetBuckets,
        CancellationToken ct
    ) => throw NotImplemented(nameof(GetHistogramAsync));

    /// <summary>Implements <see cref="GetFacetsAsync"/>; only called when the feature is declared.</summary>
    /// <param name="query">The query.</param>
    /// <param name="fields">Field paths.</param>
    /// <param name="top">Maximum values per field, at least 1.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The facets.</returns>
    /// <exception cref="NotSupportedException">Not overridden.</exception>
    protected virtual Task<FacetResult> GetFacetsCoreAsync(
        LogQuery query,
        IReadOnlyList<string> fields,
        int top,
        CancellationToken ct
    ) => throw NotImplemented(nameof(GetFacetsAsync));

    /// <summary>Implements <see cref="GetPatternsAsync"/>; only called when the feature is declared.</summary>
    /// <param name="query">The query.</param>
    /// <param name="top">Maximum patterns, at least 1.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The patterns.</returns>
    /// <exception cref="NotSupportedException">Not overridden.</exception>
    protected virtual Task<PatternResult> GetPatternsCoreAsync(
        LogQuery query,
        int top,
        CancellationToken ct
    ) => throw NotImplemented(nameof(GetPatternsAsync));

    /// <summary>Implements <see cref="GetContextAsync"/>; only called when the feature is declared.</summary>
    /// <param name="recordId">The anchor id.</param>
    /// <param name="before">Entries before, not negative.</param>
    /// <param name="after">Entries after, not negative.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The context.</returns>
    /// <exception cref="NotSupportedException">Not overridden.</exception>
    protected virtual Task<ContextResult> GetContextCoreAsync(
        string recordId,
        int before,
        int after,
        CancellationToken ct
    ) => throw NotImplemented(nameof(GetContextAsync));

    /// <summary>Implements <see cref="GetFieldsAsync"/>; only called when the feature is declared.</summary>
    /// <param name="query">The query.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The fields.</returns>
    /// <exception cref="NotSupportedException">Not overridden.</exception>
    protected virtual Task<IReadOnlyList<FieldInfo>> GetFieldsCoreAsync(
        LogQuery query,
        CancellationToken ct
    ) => throw NotImplemented(nameof(GetFieldsAsync));

    /// <summary>Implements <see cref="TailAsync"/>; only called when the feature is declared.</summary>
    /// <param name="query">The query.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The stream.</returns>
    /// <exception cref="NotSupportedException">Not overridden.</exception>
    protected virtual IAsyncEnumerable<LogRecord> TailCore(LogQuery query, CancellationToken ct) =>
        throw NotImplemented(nameof(TailAsync));

    /// <summary>Implements <see cref="Compile"/>; only called when the feature is declared.</summary>
    /// <param name="query">The query.</param>
    /// <returns>The compiled query.</returns>
    /// <exception cref="NotSupportedException">Not overridden.</exception>
    protected virtual CompileResult CompileCore(LogQuery query) =>
        throw NotImplemented(nameof(Compile));

    /// <summary>Implements <see cref="ValidateNative"/>; only called when the feature is declared.</summary>
    /// <param name="nativeQuery">The native query text.</param>
    /// <returns>The validation outcome.</returns>
    /// <exception cref="NotSupportedException">Not overridden.</exception>
    protected virtual ValidationResult ValidateNativeCore(string nativeQuery) =>
        throw NotImplemented(nameof(ValidateNative));

    private void Require(LogSourceFeatures feature, string member)
    {
        if (!Capabilities.Supports(feature))
        {
            throw new NotSupportedException(
                $"Log source '{Alias}' does not declare {feature}, so {member} is not supported."
            );
        }
    }

    // A source that declares a feature but forgets to override its member is a provider bug; it
    // still surfaces as NotSupportedException so callers see one failure type.
    private NotSupportedException NotImplemented(string member) =>
        new($"Log source '{Alias}' declares the feature for {member} but does not implement it.");
}
