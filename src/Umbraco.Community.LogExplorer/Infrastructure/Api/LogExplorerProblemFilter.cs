using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Umbraco.Community.LogExplorer.Core.Sources;
using Umbraco.Community.LogExplorer.Features.Context;

namespace Umbraco.Community.LogExplorer.Infrastructure.Api;

/// <summary>
/// Turns the exceptions providers and the registry throw (ADR 0008) into ProblemDetails responses
/// carrying a machine-readable <c>code</c> extension (BRIEF §11.1), so callers can react to the
/// code rather than parse messages. Other exceptions are left for ASP.NET's own handling.
/// </summary>
/// <remarks>
/// Mapping: <see cref="KeyNotFoundException"/> -> 404 <c>source_not_found</c>;
/// <see cref="RecordNotFoundException"/> -> 404 <c>record_not_found</c>;
/// <see cref="ForbiddenSourceException"/> -> 403 <c>forbidden_source</c>;
/// <see cref="NotSupportedException"/> -> 400 <c>unsupported_feature</c>;
/// <see cref="RangeTooLargeException"/> -> 400 <c>range_too_large</c>;
/// <see cref="InvalidNativeQueryException"/> -> 400 <c>invalid_native_query</c> with a
/// <c>position</c> extension (zero-based offset, or null) so the search box can mark the error;
/// any other <see cref="ArgumentException"/> (a bad range, cursor, page size or regex in the
/// query) -> 400 <c>invalid_query</c>. Further codes (<c>upstream_error</c>,
/// <c>throttled</c>) are added with the endpoints that raise them.
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
            RecordNotFoundException => (
                StatusCodes.Status404NotFound,
                LogExplorerApi.ProblemCodes.RecordNotFound
            ),
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
            InvalidNativeQueryException => (
                StatusCodes.Status400BadRequest,
                LogExplorerApi.ProblemCodes.InvalidNativeQuery
            ),
            RangeTooLargeException => (
                StatusCodes.Status400BadRequest,
                LogExplorerApi.ProblemCodes.RangeTooLarge
            ),
            ArgumentException => (
                StatusCodes.Status400BadRequest,
                LogExplorerApi.ProblemCodes.InvalidQuery
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
        if (context.Exception is InvalidNativeQueryException invalidNative)
        {
            details.Extensions["position"] = invalidNative.Position;
        }

        context.Result = new ObjectResult(details) { StatusCode = problem.status };
        context.ExceptionHandled = true;
    }
}
