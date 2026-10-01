using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Core.Sources;
using Umbraco.Community.LogExplorer.Infrastructure.Api;

namespace Umbraco.Community.LogExplorer.Features.Patterns;

/// <summary>The body of <c>POST /sources/{alias}/patterns</c> (BRIEF §11.1).</summary>
/// <param name="Query">The query; its <c>take</c>, <c>cursor</c> and <c>sort</c> are ignored.</param>
/// <param name="Top">
/// How many patterns to return, highest count first, from 1 to
/// <see cref="PatternsController.MaxTop"/>.
/// </param>
public sealed record PatternsRequest(LogQuery Query, int Top);

/// <summary>Groups entries by message template on one source (BRIEF §6.9, §11.1 <c>POST /sources/{alias}/patterns</c>).</summary>
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "Patterns")]
public sealed class PatternsController(
    ILogSourceRegistry registry,
    IUserContextAccessor userContextAccessor,
    TimeProvider clock
) : LogExplorerApiControllerBase
{
    /// <summary>
    /// The most patterns a request may ask for. The Patterns view asks for 100; the cap stops a
    /// stray request serialising a sample record for every distinct template on a busy site.
    /// </summary>
    public const int MaxTop = 200;

    /// <summary>
    /// Returns the matching entries grouped by message template, honouring the query's level set,
    /// filter and native query. Each pattern carries its level mix, a sparkline across the range
    /// and its newest entry as the sample.
    /// </summary>
    /// <param name="alias">The source alias.</param>
    /// <param name="request">The query and the pattern limit.</param>
    /// <param name="cancellationToken">Aborted when the client drops the request.</param>
    /// <returns>The patterns, highest count first.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="PatternsRequest.Top"/> is outside 1 to <see cref="MaxTop"/> (400 <c>invalid_query</c>).
    /// </exception>
    /// <response code="400">
    /// <c>unsupported_feature</c> (the source has no patterns), <c>range_too_large</c> or
    /// <c>invalid_query</c>.
    /// </response>
    /// <response code="403"><c>forbidden_source</c>: the source exists but is hidden from the user.</response>
    /// <response code="404"><c>source_not_found</c>.</response>
    [HttpPost("sources/{alias}/patterns")]
    [ProducesResponseType<PatternResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<PatternResult> GetPatterns(
        string alias,
        [FromBody] PatternsRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        ILogSource source = registry.GetForUser(alias, userContextAccessor.GetCurrent());

        // LogSourceBase only rejects values below 1; the cap is the API's, as for histogram buckets.
        ArgumentOutOfRangeException.ThrowIfLessThan(request.Top, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(request.Top, MaxTop);

        QueryRangeGuard.EnsureAllowed(source, request.Query, clock);
        return await source.GetPatternsAsync(request.Query, request.Top, cancellationToken);
    }
}
