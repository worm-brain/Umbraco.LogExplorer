using Umbraco.Community.LogExplorer.Core.Query;

namespace Umbraco.Community.LogExplorer.Core.SimpleSyntax;

/// <summary>
/// What <see cref="SimpleSyntaxParser.Parse"/> made of the search box input; the body of the
/// <c>POST /parse</c> response (ADR 0005).
/// </summary>
/// <param name="Chips">
/// One node per chip, in input order. Leftover text is a chip too: all bare words share one
/// <see cref="TextNode"/>, and each quoted phrase is its own <see cref="TextNode"/> with
/// <c>Phrase</c> set.
/// </param>
/// <param name="Levels">
/// The level set the input asked for (lower-case OTel short names, ADR 0004), or null when it
/// named no level, in which case the current level toggles stay as they are.
/// </param>
/// <param name="Fallback">Why the input was searched as plain text, or null when it parsed.</param>
public sealed record ParseResult(
    IReadOnlyList<FilterNode> Chips,
    IReadOnlySet<string>? Levels,
    ParseFallback? Fallback
);

/// <summary>Explains why the input could not be tokenised and was searched as plain text.</summary>
/// <param name="Code">Machine-readable reason, for example <c>unbalanced_quote</c>.</param>
/// <param name="Message">The sentence the UI shows (UI brief §4.14).</param>
public sealed record ParseFallback(string Code, string Message);
