using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using NSubstitute;
using Umbraco.Community.LogExplorer.Core.Sources;
using Umbraco.Community.LogExplorer.Infrastructure.Api;

namespace Umbraco.Community.LogExplorer.Api.Tests.Infrastructure.Api;

/// <summary>
/// Known provider exceptions become ProblemDetails with a BRIEF §11.1 <c>code</c>; anything else is
/// left for ASP.NET's own error handling.
/// </summary>
public class LogExplorerProblemFilterTests
{
    private readonly LogExplorerProblemFilter _filter = new(StubFactory());

    [Theory]
    [InlineData(typeof(KeyNotFoundException), 404, "source_not_found")]
    [InlineData(typeof(ForbiddenSourceException), 403, "forbidden_source")]
    [InlineData(typeof(NotSupportedException), 400, "unsupported_feature")]
    public void OnException_KnownException_WritesProblemDetailsWithTheCode(
        Type exceptionType,
        int status,
        string code
    )
    {
        // Arrange
        ExceptionContext context = Context((Exception)Activator.CreateInstance(exceptionType)!);

        // Act
        _filter.OnException(context);

        // Assert
        var details = (ProblemDetails)((ObjectResult)context.Result!).Value!;
        Assert.Equal((status, code), (details.Status!.Value, (string)details.Extensions["code"]!));
    }

    [Fact]
    public void OnException_UnknownException_LeavesItUnhandled()
    {
        // Arrange
        ExceptionContext context = Context(new InvalidOperationException("boom"));

        // Act
        _filter.OnException(context);

        // Assert
        Assert.False(context.ExceptionHandled);
    }

    private static ExceptionContext Context(Exception exception) =>
        new(
            new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()),
            []
        )
        {
            Exception = exception,
        };

    // Mirrors the real factory closely enough for these tests: status and detail are kept.
    private static ProblemDetailsFactory StubFactory()
    {
        ProblemDetailsFactory factory = Substitute.For<ProblemDetailsFactory>();
        factory
            .CreateProblemDetails(
                Arg.Any<HttpContext>(),
                Arg.Any<int?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>()
            )
            .Returns(call => new ProblemDetails
            {
                Status = call.ArgAt<int?>(1),
                Detail = call.ArgAt<string?>(4),
            });
        return factory;
    }
}
