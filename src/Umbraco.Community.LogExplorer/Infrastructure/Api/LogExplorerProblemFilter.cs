using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Umbraco.Community.LogExplorer.Core.Sources;

namespace Umbraco.Community.LogExplorer.Infrastructure.Api;

/// <summary>
/// Turns the exceptions providers and the registry throw (ADR 0008) into ProblemDetails responses
/// carrying a machine-readable <c>code</c> extension (BRIEF §11.1), so callers can react to the
/// code rather than parse messages. Other exceptions are left for ASP.NET's own handling.
/// </summary>
/// <remarks>
/// Mapping: <see cref="KeyNotFoundException"/> -> 404 <c>source_not_found</c>;
/// <see cref="ForbiddenSourceException"/> -> 403 <c>forbidden_source</c>;
/// <see cref="NotSupportedException"/> -> 400 <c>unsupported_feature</c>. Further codes
/// (<c>invalid_native_query</c>, <c>range_too_large</c>, <c>upstream_error</c>, <c>throttled</c>)
/// are added with the endpoints that raise them.
/// </remarks>
internal sealed class LogExplorerProblemFilter(ProblemDetailsFactory problemDetailsFactory)
    : IExceptionFilter
{
    /// <summary>Writes a ProblemDetails result for a known exception and marks it handled.</summary>
    /// <param name="context">The exception context.</param>
    public void OnException(ExceptionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        (int status, string code)? mapped = context.Exception switch
        {
            KeyNotFoundException => (
                StatusCodes.Status404NotFound,
                LogExplorerApi.ProblemCodes.SourceNotFound
            ),
            ForbiddenSourceException => (
                StatusCodes.Status403Forbidden,
                LogExplorerApi.ProblemCodes.ForbiddenSource
            ),
            NotSupportedException => (
                StatusCodes.Status400BadRequest,
                LogExplorerApi.ProblemCodes.UnsupportedFeature
            ),
            _ => null,
        };

        if (mapped is not { } problem)
        {
            return;
        }

        ProblemDetails details = problemDetailsFactory.CreateProblemDetails(
            context.HttpContext,
            statusCode: problem.status,
            detail: context.Exception.Message
        );
        details.Extensions["code"] = problem.code;

        context.Result = new ObjectResult(details) { StatusCode = problem.status };
        context.ExceptionHandled = true;
    }
}
