using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Core.Sources;
using Umbraco.Community.LogExplorer.Infrastructure.Api;

namespace Umbraco.Community.LogExplorer.Features.Facets;

/// <summary>The body of <c>POST /sources/{alias}/facets</c> (BRIEF §11.1).</summary>
/// <param name="Query">
/// The query whose matches are counted, levels included; <c>take</c>, <c>cursor</c> and
/// <c>sort</c> are ignored.
/// </param>
/// <param name="Fields">
/// Field paths to count, portable (<c>@exception.type</c>) or attribute (<c>RequestPath</c>), in
/// the order the facets come back. At most <see cref="FacetsController.MaxFields"/>, each
/// non-empty and at most <see cref="FacetsController.MaxFieldLength"/> characters.
/// </param>
/// <param name="Top">Values per field, from 1 to <see cref="FacetsController.MaxTop"/>.</param>
public sealed record FacetsRequest(LogQuery Query, IReadOnlyList<string> Fields, int Top);

/// <summary>Counts the top values of fields on one source (BRIEF §6.6, §11.1 <c>POST /sources/{alias}/facets</c>).</summary>
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "Facets")]
public sealed class FacetsController(
    ILogSourceRegistry registry,
    IUserContextAccessor userContextAccessor,
    TimeProvider clock
) : LogExplorerApiControllerBase
{
    /// <summary>The most values per field: BRIEF §6.6 has the API return up to 10.</summary>
    public const int MaxTop = 10;

    /// <summary>
    /// The most fields per request. The fields panel asks for its pinned fields plus a capped set
    /// of discovered ones; the limit stops one request making a source facet every attribute.
    /// </summary>
    public const int MaxFields = 50;

    /// <summary>The longest accepted field path, far beyond any real attribute path.</summary>
    public const int MaxFieldLength = 256;

    /// <summary>
    /// Returns, per requested field, the share of matching entries that have it and its most
    /// frequent values with counts. <c>approximate</c> is true when the source sampled or stopped
    /// at its scan budget; <c>scannedRange</c> then says what was actually counted.
    /// </summary>
    /// <param name="alias">The source alias.</param>
    /// <param name="request">The query, fields and value count.</param>
    /// <param name="cancellationToken">Aborted when the client drops the request.</param>
    /// <returns>The facets, in the requested field order.</returns>
    /// <exception cref="ArgumentException">
    /// The field list or <see cref="FacetsRequest.Top"/> is outside the limits (400 <c>invalid_query</c>).
    /// </exception>
    /// <response code="400">
    /// <c>unsupported_feature</c> (the source has no facets), <c>range_too_large</c> or
    /// <c>invalid_query</c>.
    /// </response>
    /// <response code="403"><c>forbidden_source</c>: the source exists but is hidden from the user.</response>
    /// <response code="404"><c>source_not_found</c>.</response>
    [HttpPost("sources/{alias}/facets")]
    [ProducesResponseType<FacetResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<FacetResult> GetFacets(
        string alias,
        [FromBody] FacetsRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        ILogSource source = registry.GetForUser(alias, userContextAccessor.GetCurrent());

        ArgumentOutOfRangeException.ThrowIfLessThan(request.Top, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(request.Top, MaxTop);
        ValidateFields(request.Fields);

        QueryRangeGuard.EnsureAllowed(source, request.Query, clock);
        return await source.GetFacetsAsync(
            request.Query,
            request.Fields,
            request.Top,
            cancellationToken
        );
    }

    private static void ValidateFields(IReadOnlyList<string>? fields)
    {
        // A JSON body with "fields": null binds to null despite the non-nullable type.
        if (fields is null)
        {
            throw new ArgumentException("A field list is required.", nameof(fields));
        }

        if (fields.Count > MaxFields)
        {
            throw new ArgumentException(
                $"At most {MaxFields} fields can be faceted at once.",
                nameof(fields)
            );
        }

        if (fields.Any(field => string.IsNullOrWhiteSpace(field) || field.Length > MaxFieldLength))
        {
            throw new ArgumentException(
                $"Field names must be non-empty and at most {MaxFieldLength} characters.",
                nameof(fields)
            );
        }
    }
}
