using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Core.Sources;
using Umbraco.Community.LogExplorer.Infrastructure.Api;

namespace Umbraco.Community.LogExplorer.Features.NativeQuery;

/// <summary>
/// Checks native-mode input before it runs (BRIEF §6.2, §11.1
/// <c>POST /sources/{alias}/validate</c>), so the search box can mark it invalid while typing.
/// </summary>
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "Native query")]
public sealed class ValidateController(
    ILogSourceRegistry registry,
    IUserContextAccessor userContextAccessor
) : LogExplorerApiControllerBase
{
    /// <summary>
    /// Validates one native query. An invalid query is a normal 200 answer with
    /// <c>valid: false</c>, the source's message and, when the source knows it, the zero-based
    /// character position of the error.
    /// </summary>
    /// <param name="alias">The source alias.</param>
    /// <param name="request">The native query.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentException">
    /// <c>native</c> is missing; the problem filter answers 400 <c>invalid_query</c>.
    /// </exception>
    /// <response code="400">
    /// <c>unsupported_feature</c> (the source has no native language or does not allow native
    /// queries) or <c>invalid_query</c> (no <c>native</c> in the body).
    /// </response>
    /// <response code="403"><c>forbidden_source</c>.</response>
    /// <response code="404"><c>source_not_found</c>.</response>
    [HttpPost("sources/{alias}/validate")]
    [ProducesResponseType<ValidationResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public ValidationResult Validate(string alias, [FromBody] ValidateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ILogSource source = registry.GetForUser(alias, userContextAccessor.GetCurrent());
        if (request.Native is null)
        {
            throw new ArgumentException("The body needs a 'native' query.", nameof(request));
        }

        return source.ValidateNative(request.Native);
    }
}
