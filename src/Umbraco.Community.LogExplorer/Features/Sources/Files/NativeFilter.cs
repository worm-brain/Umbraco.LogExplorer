using System.Buffers;
using System.Globalization;
using System.Text.RegularExpressions;
using Serilog.Events;
using Serilog.Expressions;
using Umbraco.Cms.Infrastructure.Logging.Viewer;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Core.Sources;

namespace Umbraco.Community.LogExplorer.Features.Sources.Files;

/// <summary>
/// Compiles a native query for the files source into a predicate over the <see cref="LogEvent"/>s
/// read from the log files, in exactly the dialect the core Log Viewer accepts (ADR 0013), so a
/// core saved search runs here unchanged.
/// </summary>
/// <remarks>
/// <para>
/// The settings are the core viewer's own (<c>ExpressionFilter</c> in Umbraco 17 and 18):
/// Serilog.Expressions with no format provider (the current culture) and Umbraco's
/// <see cref="SerilogLegacyNameResolver"/> over <see cref="SerilogExpressionsFunctions"/>, which adds
/// <c>Has()</c> and the long built-in names (<c>@Level</c>, <c>@Message</c>, <c>@MessageTemplate</c>,
/// <c>@Exception</c>, <c>@Properties</c>, <c>@Timestamp</c>). Like the core viewer, an input with no
/// space and none of <c>()+=*&lt;&gt;%-</c> is a plain, case-sensitive search of the rendered message.
/// </para>
/// <para>
/// One deliberate difference: where the core viewer silently falls back to a message search when
/// an expression does not compile, this throws, so native mode shows the error and its position.
/// </para>
/// </remarks>
internal static class NativeFilter
{
    // The core viewer treats an input without any of these (and without a space) as plain text.
    private static readonly SearchValues<char> ExpressionOperators = SearchValues.Create(
        "()+=*<>%-"
    );

    // Serilog.Expressions' parse errors read "Syntax error (line 1, column 5): unexpected ...".
    private static readonly Regex ErrorLocation = new(
        @"\(line (?<line>\d+), column (?<column>\d+)\)",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1)
    );

    /// <summary>Compiles a native query.</summary>
    /// <param name="nativeQuery">The expression; null, empty or whitespace matches every event.</param>
    /// <returns>A predicate that is true for the events the core Log Viewer would show.</returns>
    /// <exception cref="InvalidNativeQueryException">
    /// The expression does not parse, names an unknown function, or holds an invalid regular
    /// expression; <see cref="InvalidNativeQueryException.Position"/> is set for parse errors.
    /// </exception>
    public static Func<LogEvent, bool> Compile(string? nativeQuery)
    {
        if (string.IsNullOrWhiteSpace(nativeQuery))
        {
            return static _ => true;
        }

        string expression = ToExpression(nativeQuery);

        var resolver = new SerilogLegacyNameResolver(typeof(SerilogExpressionsFunctions));
        bool compiled;
        CompiledExpression? evaluate;
        string? error;
        try
        {
            compiled = SerilogExpression.TryCompile(
                expression,
                formatProvider: null,
                resolver,
                out evaluate,
                out error
            );
        }
        catch (ArgumentException exception)
        {
            // TryCompile only reports parse errors; binding errors (an unknown function, a bad
            // regular expression in IsMatch) throw, and carry no position.
            throw new InvalidNativeQueryException(exception.Message, exception);
        }

        if (!compiled)
        {
            throw new InvalidNativeQueryException(error!)
            {
                Position = FindPosition(nativeQuery, error!),
            };
        }

        return logEvent => ExpressionResult.IsTrue(evaluate!(logEvent));
    }

    /// <summary>
    /// Checks a native query without running it, with the same rules as <see cref="Compile"/>.
    /// </summary>
    /// <param name="nativeQuery">The expression.</param>
    /// <returns>
    /// Valid, or the compiler's message and, for parse errors, the zero-based offset it names. A
    /// parse error at the end of the input ("unexpected end of input") has no position.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="nativeQuery"/> is null.</exception>
    public static ValidationResult Validate(string nativeQuery)
    {
        ArgumentNullException.ThrowIfNull(nativeQuery);
        try
        {
            Compile(nativeQuery);
            return new ValidationResult(true, null, null);
        }
        catch (InvalidNativeQueryException exception)
        {
            return new ValidationResult(false, exception.Message, exception.Position);
        }
    }

    /// <summary>
    /// The expression a native query stands for: itself, or for plain text the case-sensitive
    /// message search the core viewer runs instead. Lets the query be embedded in a larger
    /// expression without the plain-text reading being lost.
    /// </summary>
    /// <param name="nativeQuery">A non-blank native query.</param>
    /// <returns>An expression in the core viewer's dialect.</returns>
    public static string ToExpression(string nativeQuery) =>
        IsPlainText(nativeQuery)
            ? $"@Message like '%{SerilogExpression.EscapeLikeExpressionContent(nativeQuery)}%'"
            : nativeQuery;

    private static bool IsPlainText(string nativeQuery) =>
        !nativeQuery.Contains(' ', StringComparison.Ordinal)
        && !nativeQuery.AsSpan().ContainsAny(ExpressionOperators);

    // Turns the 1-based line and column in the message into an offset into the input. Lines are
    // counted on '\n', as Serilog's parser does.
    private static int? FindPosition(string input, string error)
    {
        Match match = ErrorLocation.Match(error);
        if (!match.Success)
        {
            return null;
        }

        int line = int.Parse(match.Groups["line"].Value, CultureInfo.InvariantCulture);
        int column = int.Parse(match.Groups["column"].Value, CultureInfo.InvariantCulture);
        int offset = 0;
        for (int current = 1; current < line; current++)
        {
            int newline = input.IndexOf('\n', offset);
            if (newline < 0)
            {
                return null;
            }

            offset = newline + 1;
        }

        int position = offset + column - 1;
        return position <= input.Length ? position : null;
    }
}
