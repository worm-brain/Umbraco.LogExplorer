using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Core.Sources;
using Umbraco.Community.LogExplorer.Infrastructure.Api;

namespace Umbraco.Community.LogExplorer.Features.Fields;

/// <summary>Lists the fields entries carry on one source (BRIEF §6.2, §6.6, §11.1 <c>GET /sources/{alias}/fields</c>).</summary>
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "Fields")]
public sealed class GetFieldsController(
    ILogSourceRegistry registry,
    IUserContextAccessor userContextAccessor,
    TimeProvider clock
) : LogExplorerApiControllerBase
{
    /// <summary>
    /// Returns every field path seen in the time range, with its kind and the share of entries
    /// that have it. The range is unfiltered: fields are discovered per source and range, not per
    /// query, so the client can cache the list while the user changes filters. Pass either
    /// <paramref name="relative"/> or an absolute <paramref name="from"/>/<paramref name="to"/>.
    /// </summary>
    /// <param name="alias">The source alias.</param>
    /// <param name="from">Inclusive absolute start.</param>
    /// <param name="to">Exclusive absolute end; omitted means now.</param>
    /// <param name="relative">A relative range such as <c>1h</c> (BRIEF §6.5).</param>
    /// <param name="cancellationToken">Aborted when the client drops the request.</param>
    /// <returns>The fields, in the source's order.</returns>
    /// <response code="400">
    /// <c>unsupported_feature</c> (the source has no field discovery), <c>range_too_large</c> or
    /// <c>invalid_query</c>.
    /// </response>
    /// <response code="403"><c>forbidden_source</c>: the source exists but is hidden from the user.</response>
    /// <response code="404"><c>source_not_found</c>.</response>
    [HttpGet("sources/{alias}/fields")]
    [ProducesResponseType<IReadOnlyList<FieldInfo>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IReadOnlyList<FieldInfo>> GetFields(
        string alias,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? relative,
        CancellationToken cancellationToken
    )
    {
        ILogSource source = registry.GetForUser(alias, userContextAccessor.GetCurrent());
        var query = new LogQuery { Range = new TimeRange(from, to, relative) };
        QueryRangeGuard.EnsureAllowed(source, query, clock);
        return await source.GetFieldsAsync(query, cancellationToken);
    }
}
