using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Core.Sources;
using Umbraco.Community.LogExplorer.Infrastructure.Api;

namespace Umbraco.Community.LogExplorer.Features.NativeQuery;

/// <summary>
/// Translates a query into the source's native language for "Show query" (BRIEF §11.1
/// <c>POST /sources/{alias}/compile</c>, UI brief §4.6).
/// </summary>
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "Native query")]
public sealed class CompileController(
    ILogSourceRegistry registry,
    IUserContextAccessor userContextAccessor
) : LogExplorerApiControllerBase
{
    /// <summary>
    /// Compiles the levels, filter and native query, one clause per line. Nothing runs, so the
    /// range is not checked against the source's maximum. Nodes the language cannot express are
    /// left out of <c>native</c> and returned in <c>unsupported</c>; the UI shows the chips that
    /// hold them disabled and leaves them out of the queries it runs (BRIEF §6.3).
    /// </summary>
    /// <param name="alias">The source alias.</param>
    /// <param name="query">The query, as it would be sent to <c>/search</c>.</param>
    /// <returns>The native text (null when there is no filter at all) and the unsupported nodes.</returns>
    /// <response code="400">
    /// <c>unsupported_feature</c>: the source does not declare <c>nativeQuery</c>, so it has no
    /// language to show, or the query carries a native query the source does not allow.
    /// </response>
    /// <response code="403"><c>forbidden_source</c>.</response>
    /// <response code="404"><c>source_not_found</c>.</response>
    [HttpPost("sources/{alias}/compile")]
    [ProducesResponseType<CompileResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public CompileResult Compile(string alias, [FromBody] LogQuery query) =>
        registry.GetForUser(alias, userContextAccessor.GetCurrent()).Compile(query);
}
