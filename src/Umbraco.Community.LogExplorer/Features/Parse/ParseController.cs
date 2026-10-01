using System.Diagnostics.CodeAnalysis;
using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Community.LogExplorer.Core.SimpleSyntax;
using Umbraco.Community.LogExplorer.Infrastructure.Api;

namespace Umbraco.Community.LogExplorer.Features.Parse;

/// <summary>
/// Turns search box input into chips and a level set with the Core simple-syntax parser
/// (BRIEF §6.2, §11.1 <c>POST /parse</c>). The browser never parses the syntax itself (ADR 0005).
/// </summary>
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "Parse")]
public sealed class ParseController : LogExplorerApiControllerBase
{
    /// <summary>
    /// The longest input accepted, in UTF-16 code units. A search box line is a few dozen
    /// characters; the cap stops a pasted log file being tokenised on every Enter.
    /// </summary>
    public const int MaxInputLength = 2000;

    /// <summary>
    /// Parses one input. Not source-specific: whether the active source can run each chip is the
    /// client's concern (BRIEF §6.3).
    /// </summary>
    /// <param name="request">The input.</param>
    /// <returns>
    /// The chips in input order, the level set (null when the input named no level), and the
    /// fallback reason when an unbalanced quote made the whole input plain text.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// The input is longer than <see cref="MaxInputLength"/>; the problem filter answers 400
    /// <c>invalid_query</c>.
    /// </exception>
    /// <response code="400"><c>invalid_query</c>: the input is too long.</response>
    [HttpPost("parse")]
    [ProducesResponseType<ParseResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "MVC only routes to instance actions."
    )]
    public ParseResult Parse([FromBody] ParseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string input = request.Input ?? "";
        if (input.Length > MaxInputLength)
        {
            throw new ArgumentException(
                $"The search input is {input.Length} characters long; the limit is {MaxInputLength}.",
                nameof(request)
            );
        }

        return SimpleSyntaxParser.Parse(input);
    }
}
