using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Records;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Core.Sources;

namespace Umbraco.Community.LogExplorer.Features.Sources.Files;

/// <summary>
/// The <c>UmbracoFiles</c> source: this site's own Umbraco log files (BRIEF §10.1), read
/// directly rather than through <c>ILogViewerService</c> (ADR 0003).
/// </summary>
/// <remarks>
/// <para>
/// Every member hands over to the pager, aggregator or context reader, which do synchronous file
/// I/O; they run on the thread pool so a request thread never blocks on a large file. Cancellation
/// is checked between events, so a cancelled request stops reading promptly.
/// </para>
/// <para>
/// Tail and Export are not declared: neither exists yet (Phase 3 and Phase 2). NativeQuery is
/// declared only when the definition allows native queries; without it a query carrying one is
/// refused rather than run.
/// </para>
/// </remarks>
internal sealed class UmbracoFilesLogSource : LogSourceBase
{
    /// <summary>The provider type name used in <c>LogExplorer:Sources</c>.</summary>
    public const string SourceType = "UmbracoFiles";

    /// <summary>The native query language shown next to the source (ADR 0013).</summary>
    public const string NativeLanguage = "Serilog Expressions";

    /// <summary>
    /// The largest page one search returns. The UI asks for 60 at a time; 1,000 bounds a
    /// hand-made request while still letting scripts page quickly.
    /// </summary>
    public const int MaxPageSize = 1000;

    private const LogSourceFeatures BaseFeatures =
        LogSourceFeatures.Facets
        | LogSourceFeatures.Histogram
        | LogSourceFeatures.Patterns
        | LogSourceFeatures.Context
        | LogSourceFeatures.TraceCorrelation
        | LogSourceFeatures.FieldDiscovery;

    private readonly LogSourceDefinition _definition;
    private readonly LogFilePager _pager;
    private readonly FileAggregator _aggregator;
    private readonly FileContextReader _contextReader;

    /// <summary>Creates the source for one configuration entry.</summary>
    /// <param name="definition">
    /// The entry; its alias, display name (the alias when empty), sensitivity and
    /// <see cref="LogSourceDefinition.AllowNativeQuery"/> are used. It has no settings of its own:
    /// the log directory comes from Umbraco's logging configuration.
    /// </param>
    /// <param name="pager">Reads search pages.</param>
    /// <param name="aggregator">Computes histogram, facets, patterns and fields.</param>
    /// <param name="contextReader">Reads "Around this".</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public UmbracoFilesLogSource(
        LogSourceDefinition definition,
        LogFilePager pager,
        FileAggregator aggregator,
        FileContextReader contextReader
    )
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(pager);
        ArgumentNullException.ThrowIfNull(aggregator);
        ArgumentNullException.ThrowIfNull(contextReader);
        _definition = definition;
        _pager = pager;
        _aggregator = aggregator;
        _contextReader = contextReader;

        Capabilities = new LogSourceCapabilities(
            definition.AllowNativeQuery
                ? BaseFeatures | LogSourceFeatures.NativeQuery
                : BaseFeatures,
            // LogRecordFilter evaluates every operator on the mapped records.
            new HashSet<FilterOperator>(Enum.GetValues<FilterOperator>()),
            definition.AllowNativeQuery ? NativeLanguage : null,
            MaxRange: null,
            MaxPageSize
        );
    }

    /// <inheritdoc />
    public override string Alias => _definition.Alias;

    /// <inheritdoc />
    public override string DisplayName =>
        string.IsNullOrWhiteSpace(_definition.DisplayName)
            ? _definition.Alias
            : _definition.DisplayName;

    /// <inheritdoc />
    public override string Type => SourceType;

    /// <inheritdoc />
    public override bool Sensitive => _definition.Sensitive;

    /// <inheritdoc />
    public override LogSourceCapabilities Capabilities { get; }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">
    /// The query has a native query and the source does not allow them.
    /// </exception>
    protected override Task<LogPage> QueryCoreAsync(LogQuery query, CancellationToken ct)
    {
        RequireNativeAllowed(query);
        return Task.Run(
            () =>
            {
                LogPage page = _pager.Query(query, MaxPageSize, ct).Page;
                return page with { Records = [.. page.Records.Select(WithAlias)] };
            },
            ct
        );
    }

    /// <inheritdoc />
    protected override Task<HistogramResult> GetHistogramCoreAsync(
        LogQuery query,
        int targetBuckets,
        CancellationToken ct
    )
    {
        RequireNativeAllowed(query);
        return Task.Run(() => _aggregator.GetHistogram(query, targetBuckets, ct).Result, ct);
    }

    /// <inheritdoc />
    protected override Task<FacetResult> GetFacetsCoreAsync(
        LogQuery query,
        IReadOnlyList<string> fields,
        int top,
        CancellationToken ct
    )
    {
        RequireNativeAllowed(query);
        return Task.Run(() => _aggregator.GetFacets(query, fields, top, ct).Result, ct);
    }

    /// <inheritdoc />
    protected override Task<PatternResult> GetPatternsCoreAsync(
        LogQuery query,
        int top,
        CancellationToken ct
    )
    {
        RequireNativeAllowed(query);
        return Task.Run(
            () =>
            {
                PatternResult result = _aggregator.GetPatterns(query, top, ct).Result;
                return result with
                {
                    Patterns =
                    [
                        .. result.Patterns.Select(pattern =>
                            pattern with
                            {
                                Sample = WithAlias(pattern.Sample),
                            }
                        ),
                    ],
                };
            },
            ct
        );
    }

    /// <inheritdoc />
    protected override Task<ContextResult> GetContextCoreAsync(
        string recordId,
        int before,
        int after,
        CancellationToken ct
    ) =>
        Task.Run(
            () =>
            {
                ContextResult context = _contextReader.Read(recordId, before, after, ct);
                return new ContextResult(
                    [.. context.Before.Select(WithAlias)],
                    WithAlias(context.Anchor),
                    [.. context.After.Select(WithAlias)]
                );
            },
            ct
        );

    /// <inheritdoc />
    protected override Task<IReadOnlyList<FieldInfo>> GetFieldsCoreAsync(
        LogQuery query,
        CancellationToken ct
    )
    {
        RequireNativeAllowed(query);
        return Task.Run(() => _aggregator.GetFields(query, ct).Result, ct);
    }

    /// <inheritdoc />
    protected override CompileResult CompileCore(LogQuery query) =>
        FileQueryCompiler.Compile(query);

    /// <inheritdoc />
    protected override ValidationResult ValidateNativeCore(string nativeQuery) =>
        NativeFilter.Validate(nativeQuery);

    // With native queries switched off (AllowNativeQuery: false) the pager would still run one,
    // so it is refused here, for every member that takes a query.
    private void RequireNativeAllowed(LogQuery query)
    {
        if (
            !string.IsNullOrWhiteSpace(query.NativeQuery)
            && !Capabilities.Supports(LogSourceFeatures.NativeQuery)
        )
        {
            throw new NotSupportedException(
                $"Log source '{Alias}' does not accept native queries."
            );
        }
    }

    // The readers leave SourceAlias empty because they do not know which source they serve.
    private LogRecord WithAlias(LogRecord record) => record with { SourceAlias = Alias };
}
