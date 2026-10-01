using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Core.Sources;
using Umbraco.Community.LogExplorer.Infrastructure.Api;

namespace Umbraco.Community.LogExplorer.Features.Search;

/// <summary>Runs a search on one source (BRIEF §11.1 <c>POST /sources/{alias}/search</c>).</summary>
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "Search")]
public sealed class SearchController(
    ILogSourceRegistry registry,
    IUserContextAccessor userContextAccessor,
    TimeProvider clock
) : LogExplorerApiControllerBase
{
    /// <summary>
    /// Returns one page of matching records in the query's sort order. Pass the page's
    /// <c>nextCursor</c> back as the query's <c>cursor</c> for the next page; the rest of the
    /// query must stay the same. The page reports the absolute range that ran.
    /// </summary>
    /// <param name="alias">The source alias.</param>
    /// <param name="query">The query; POST because it is structured (BRIEF §11.1).</param>
    /// <param name="cancellationToken">Aborted when the client drops the request.</param>
    /// <returns>The page.</returns>
    /// <response code="400">
    /// <c>unsupported_feature</c>, <c>range_too_large</c> or <c>invalid_query</c>.
    /// </response>
    /// <response code="403"><c>forbidden_source</c>: the source exists but is hidden from the user.</response>
    /// <response code="404"><c>source_not_found</c>.</response>
    [HttpPost("sources/{alias}/search")]
    [ProducesResponseType<LogPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<LogPage> Search(
        string alias,
        [FromBody] LogQuery query,
        CancellationToken cancellationToken
    )
    {
        ILogSource source = registry.GetForUser(alias, userContextAccessor.GetCurrent());
        QueryRangeGuard.EnsureAllowed(source, query, clock);
        return await source.QueryAsync(query, cancellationToken);
    }
}
