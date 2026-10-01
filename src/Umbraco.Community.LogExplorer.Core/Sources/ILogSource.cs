using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Records;
using Umbraco.Community.LogExplorer.Core.Results;

namespace Umbraco.Community.LogExplorer.Core.Sources;

/// <summary>
/// A configured log store the explorer can read (BRIEF §8.4). Only <see cref="QueryAsync"/> is
/// mandatory; every other operation is optional and throws <see cref="NotSupportedException"/>
/// unless <see cref="Capabilities"/> declares the matching <see cref="LogSourceFeatures"/> flag.
/// Derive from <see cref="LogSourceBase"/> to get that rule for free.
/// <para>
/// Every query-taking member applies the query's range (from inclusive, to exclusive), level set
/// and filter, except where noted. Every async member honours its cancellation token.
/// </para>
/// </summary>
public interface ILogSource
{
    /// <summary>Unique alias from configuration; the only identifier the UI uses.</summary>
    string Alias { get; }

    /// <summary>Name shown in the source picker.</summary>
    string DisplayName { get; }

    /// <summary>Provider type, for example <c>UmbracoFiles</c>; matches <see cref="ILogSourceFactory.Type"/>.</summary>
    string Type { get; }

    /// <summary>Whether the source holds sensitive data (restricted to admins by default, audited).</summary>
    bool Sensitive { get; }

    /// <summary>What the source supports.</summary>
    LogSourceCapabilities Capabilities { get; }

    /// <summary>Searches for one page of records.</summary>
    /// <param name="query">The query; its cursor selects the page.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The page, with the next cursor when more records match.</returns>
    /// <exception cref="NotSupportedException">The filter uses an operator the source does not declare.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    Task<LogPage> QueryAsync(LogQuery query, CancellationToken ct);

    /// <summary>
    /// Counts entries per time bucket and level. Ignores the query's level set so the UI can show
    /// what the level toggles hide (ADR 0004); cursor, take and sort are ignored.
    /// </summary>
    /// <param name="query">The query.</param>
    /// <param name="targetBuckets">Desired bucket count; the source may round the bucket size.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The histogram.</returns>
    /// <exception cref="NotSupportedException"><see cref="LogSourceFeatures.Histogram"/> is not declared.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    Task<HistogramResult> GetHistogramAsync(
        LogQuery query,
        int targetBuckets,
        CancellationToken ct
    );

    /// <summary>Computes presence and top values for each field over the matching entries.</summary>
    /// <param name="query">The query; cursor, take and sort are ignored.</param>
    /// <param name="fields">Field paths, as accepted by a condition node.</param>
    /// <param name="top">Maximum values per field.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>One facet per field, in the order requested.</returns>
    /// <exception cref="NotSupportedException"><see cref="LogSourceFeatures.Facets"/> is not declared.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    Task<FacetResult> GetFacetsAsync(
        LogQuery query,
        IReadOnlyList<string> fields,
        int top,
        CancellationToken ct
    );

    /// <summary>Groups the matching entries by message template.</summary>
    /// <param name="query">The query; cursor, take and sort are ignored.</param>
    /// <param name="top">Maximum patterns returned.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Patterns, highest count first.</returns>
    /// <exception cref="NotSupportedException"><see cref="LogSourceFeatures.Patterns"/> is not declared.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    Task<PatternResult> GetPatternsAsync(LogQuery query, int top, CancellationToken ct);

    /// <summary>Returns the entries either side of one record in time order, ignoring all filters.</summary>
    /// <param name="recordId">A <see cref="LogRecord.Id"/> this source produced.</param>
    /// <param name="before">Maximum entries before the anchor.</param>
    /// <param name="after">Maximum entries after the anchor.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The anchor and its neighbours.</returns>
    /// <exception cref="NotSupportedException"><see cref="LogSourceFeatures.Context"/> is not declared.</exception>
    /// <exception cref="KeyNotFoundException">No record has that id.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    Task<ContextResult> GetContextAsync(
        string recordId,
        int before,
        int after,
        CancellationToken ct
    );

    /// <summary>Lists the fields present in the matching entries, with kind and presence.</summary>
    /// <param name="query">The query; cursor, take and sort are ignored.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The discovered fields.</returns>
    /// <exception cref="NotSupportedException"><see cref="LogSourceFeatures.FieldDiscovery"/> is not declared.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    Task<IReadOnlyList<FieldInfo>> GetFieldsAsync(LogQuery query, CancellationToken ct);

    /// <summary>Streams new matching records as they are written, until cancelled.</summary>
    /// <param name="query">The query; its range start is where tailing begins.</param>
    /// <param name="ct">Cancellation token that ends the stream.</param>
    /// <returns>An endless sequence of records.</returns>
    /// <exception cref="NotSupportedException"><see cref="LogSourceFeatures.Tail"/> is not declared.</exception>
    IAsyncEnumerable<LogRecord> TailAsync(LogQuery query, CancellationToken ct);

    /// <summary>Translates the query to the native language, for "Show query".</summary>
    /// <param name="query">The query.</param>
    /// <returns>The native text and any nodes that could not be translated.</returns>
    /// <exception cref="NotSupportedException"><see cref="LogSourceFeatures.NativeQuery"/> is not declared.</exception>
    CompileResult Compile(LogQuery query);

    /// <summary>Checks a native query without running it.</summary>
    /// <param name="nativeQuery">Query text in <see cref="LogSourceCapabilities.NativeLanguage"/>.</param>
    /// <returns>Whether it is valid, and where it fails if not.</returns>
    /// <exception cref="NotSupportedException"><see cref="LogSourceFeatures.NativeQuery"/> is not declared.</exception>
    ValidationResult ValidateNative(string nativeQuery);
}
