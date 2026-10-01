using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Core.Sources;
using Umbraco.Community.LogExplorer.Infrastructure.Api;

namespace Umbraco.Community.LogExplorer.Features.Context;

/// <summary>
/// Reads the entries either side of one record, ignoring every filter (BRIEF §6.8 "Around this",
/// §11.1 <c>GET /sources/{alias}/records/{id}/context</c>).
/// </summary>
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "Context")]
public sealed class GetContextController(
    ILogSourceRegistry registry,
    IUserContextAccessor userContextAccessor
) : LogExplorerApiControllerBase
{
    /// <summary>How many entries either side the drawer's Around this asks for (UI brief §4.10).</summary>
    public const int DefaultCount = 7;

    /// <summary>
    /// The most entries a request may ask for on either side. Context is a short look around one
    /// entry, not a second paging mechanism; search with a time range for more.
    /// </summary>
    public const int MaxCount = 100;

    /// <summary>
    /// Returns up to <paramref name="before"/> entries immediately before the record and
    /// <paramref name="after"/> immediately after it, each list oldest first, with the record
    /// itself as the anchor. Fewer come back at the start or end of the log.
    /// </summary>
    /// <param name="alias">The source alias.</param>
    /// <param name="id">The record id exactly as the source returned it (already base64url-safe).</param>
    /// <param name="before">Entries before the anchor, from 0 to <see cref="MaxCount"/>.</param>
    /// <param name="after">Entries after the anchor, from 0 to <see cref="MaxCount"/>.</param>
    /// <param name="cancellationToken">Aborted when the client drops the request.</param>
    /// <returns>The entries around the record.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="before"/> or <paramref name="after"/> is outside 0 to
    /// <see cref="MaxCount"/> (400 <c>invalid_query</c>).
    /// </exception>
    /// <exception cref="RecordNotFoundException">
    /// The source has no record with that id, for example after its log file was deleted
    /// (404 <c>record_not_found</c>).
    /// </exception>
    /// <response code="400"><c>unsupported_feature</c> (the source has no context) or <c>invalid_query</c>.</response>
    /// <response code="403"><c>forbidden_source</c>: the source exists but is hidden from the user.</response>
    /// <response code="404"><c>source_not_found</c> or <c>record_not_found</c>.</response>
    [HttpGet("sources/{alias}/records/{id}/context")]
    [ProducesResponseType<ContextResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ContextResult> GetContext(
        string alias,
        string id,
        CancellationToken cancellationToken,
        [FromQuery] int before = DefaultCount,
        [FromQuery] int after = DefaultCount
    )
    {
        // Resolve the source outside the try: the registry's KeyNotFoundException means the
        // source is unknown and must stay source_not_found.
        ILogSource source = registry.GetForUser(alias, userContextAccessor.GetCurrent());

        ArgumentOutOfRangeException.ThrowIfGreaterThan(before, MaxCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(after, MaxCount);

        try
        {
            return await source.GetContextAsync(id, before, after, cancellationToken);
        }
        catch (KeyNotFoundException exception)
        {
            throw new RecordNotFoundException(exception.Message, exception);
        }
    }
}
