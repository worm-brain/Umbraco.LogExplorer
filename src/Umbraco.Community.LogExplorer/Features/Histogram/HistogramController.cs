using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Core.Sources;
using Umbraco.Community.LogExplorer.Infrastructure.Api;

namespace Umbraco.Community.LogExplorer.Features.Histogram;

/// <summary>The body of <c>POST /sources/{alias}/histogram</c> (BRIEF §11.1).</summary>
/// <param name="Query">
/// The query. Its <c>levels</c> are accepted but do not change the counts (ADR 0004); the
/// <c>take</c>, <c>cursor</c> and <c>sort</c> are ignored.
/// </param>
/// <param name="TargetBuckets">
/// About how many buckets to return, from 1 to <see cref="HistogramController.MaxTargetBuckets"/>.
/// Each source picks its own bucket width from it, so the count it returns is close to, not
/// exactly, this.
/// </param>
public sealed record HistogramRequest(LogQuery Query, int TargetBuckets);

/// <summary>Counts entries over time on one source (BRIEF §6.4, §11.1 <c>POST /sources/{alias}/histogram</c>).</summary>
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "Histogram")]
public sealed class HistogramController(
    ILogSourceRegistry registry,
    IUserContextAccessor userContextAccessor,
    TimeProvider clock
) : LogExplorerApiControllerBase
{
    /// <summary>
    /// The most buckets a request may ask for. The UI asks for 30 to 120 (UI brief §4.7); the cap
    /// stops a stray request making a source allocate a bucket per second over a month.
    /// </summary>
    public const int MaxTargetBuckets = 1000;

    /// <summary>
    /// Returns entry counts per bucket and level over the query's range, honouring its filter and
    /// native query but not its level set, so the level toggles can show what they hide.
    /// </summary>
    /// <param name="alias">The source alias.</param>
    /// <param name="request">The query and the bucket target.</param>
    /// <param name="cancellationToken">Aborted when the client drops the request.</param>
    /// <returns>The histogram, with the absolute range that ran.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="HistogramRequest.TargetBuckets"/> is outside 1 to <see cref="MaxTargetBuckets"/>
    /// (400 <c>invalid_query</c>).
    /// </exception>
    /// <response code="400">
    /// <c>unsupported_feature</c> (the source has no histogram), <c>range_too_large</c> or
    /// <c>invalid_query</c>.
    /// </response>
    /// <response code="403"><c>forbidden_source</c>: the source exists but is hidden from the user.</response>
    /// <response code="404"><c>source_not_found</c>.</response>
    [HttpPost("sources/{alias}/histogram")]
    [ProducesResponseType<HistogramResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<HistogramResult> GetHistogram(
        string alias,
        [FromBody] HistogramRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        ILogSource source = registry.GetForUser(alias, userContextAccessor.GetCurrent());

        // Checked here rather than left to the provider: LogSourceBase only rejects targets below
        // 1, and the cap should not depend on each provider remembering to clamp.
        ArgumentOutOfRangeException.ThrowIfLessThan(request.TargetBuckets, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(request.TargetBuckets, MaxTargetBuckets);

        QueryRangeGuard.EnsureAllowed(source, request.Query, clock);
        return await source.GetHistogramAsync(
            request.Query,
            request.TargetBuckets,
            cancellationToken
        );
    }
}
