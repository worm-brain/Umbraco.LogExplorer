using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Umbraco.Community.LogExplorer.Core.Filtering;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Severity;

namespace Umbraco.Community.LogExplorer.Core.SimpleSyntax;

/// <summary>
/// Turns search box input in the portable simple syntax (BRIEF §6.2) into chips and a level set.
/// This is the only parser of the syntax; the browser calls it through <c>POST /parse</c>
/// (ADR 0005).
/// <para>
/// Input splits on whitespace outside double quotes. A token is a filter when it reads
/// <c>[-]field:value</c>, <c>[-]field&gt;value</c> (also <c>&gt;=</c>, <c>&lt;</c>,
/// <c>&lt;=</c>), <c>[-]has:field</c>, <c>level:name</c> or <c>level=name</c>; anything else is
/// text. The parser never rejects input: a token it cannot read as a filter (an unknown level,
/// <c>-level:</c>, <c>field=value</c>, <c>field:</c> with no value) is kept as a bare word.
/// </para>
/// </summary>
public static partial class SimpleSyntaxParser
{
    /// <summary><see cref="ParseFallback.Code"/> when a double quote is left open.</summary>
    public const string UnbalancedQuoteCode = "unbalanced_quote";

    /// <summary><see cref="ParseFallback.Message"/> for <see cref="UnbalancedQuoteCode"/> (UI brief §4.14).</summary>
    public const string UnbalancedQuoteMessage =
        "Unbalanced quote, so this was searched as plain text";

    // BRIEF §6.2 aliases. "has" is not here: it is a keyword, not a field.
    private static readonly Dictionary<string, string> Aliases = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["level"] = LogFields.Severity,
        ["severity"] = LogFields.Severity,
        ["msg"] = LogFields.Body,
        ["message"] = LogFields.Body,
        ["template"] = LogFields.Template,
        ["source"] = LogFields.Scope,
        ["scope"] = LogFields.Scope,
        ["trace"] = LogFields.TraceId,
        ["ex"] = LogFields.ExceptionType,
        ["exception"] = LogFields.ExceptionType,
        ["path"] = "RequestPath",
    };

    /// <summary>Parses one search box input.</summary>
    /// <param name="input">What the user typed; empty or whitespace yields no chips.</param>
    /// <returns>
    /// The chips in input order and the level set. When a quote is left open nothing is read as a
    /// filter: the result is one text chip of the whole input without its quote characters, plus
    /// a <see cref="ParseFallback"/> saying why.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is null.</exception>
    public static ParseResult Parse(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!TryTokenise(input, out List<Token> tokens))
        {
            string text = input.Replace("\"", "", StringComparison.Ordinal).Trim();
            return new ParseResult(
                text.Length == 0 ? [] : [new TextNode(text)],
                null,
                new ParseFallback(UnbalancedQuoteCode, UnbalancedQuoteMessage)
            );
        }

        var chips = new List<FilterNode>();
        var words = new List<string>();
        int textChipIndex = -1;
        HashSet<string>? levels = null;

        foreach (Token token in tokens)
        {
            if (token.Segments is [{ Quoted: true } phrase])
            {
                if (phrase.Text.Length > 0)
                {
                    chips.Add(new TextNode(phrase.Text, Phrase: true));
                }

                continue;
            }

            FieldToken? field = SplitField(token);
            if (field is not null && TryReadLevels(field.Value, ref levels))
            {
                continue;
            }

            if (field is not null && TryReadCondition(field.Value, out FilterNode? condition))
            {
                chips.Add(condition);
                continue;
            }

            // Bare words AND together in one chip, which sits where the first word was typed.
            if (textChipIndex < 0)
            {
                textChipIndex = chips.Count;
            }

            words.Add(token.Text);
        }

        if (textChipIndex >= 0)
        {
            chips.Insert(textChipIndex, new TextNode(string.Join(' ', words)));
        }

        return new ParseResult(chips, levels, null);
    }

    // level:x adds x and everything more severe; level=x adds exactly x. Several level tokens
    // union. A negated or unknown level is not a level token and falls through to text.
    private static bool TryReadLevels(FieldToken field, ref HashSet<string>? levels)
    {
        if (
            field.Negated
            || field.Op is not (":" or "=")
            || ResolveField(field.Field) != LogFields.Severity
        )
        {
            return false;
        }

        int index = IndexOfLevel(field.Value);
        if (index < 0)
        {
            return false;
        }

        levels ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> names =
            field.Op == "=" ? [SeverityMap.ShortNames[index]] : SeverityMap.ShortNames.Skip(index);
        levels.UnionWith(names);
        return true;
    }

    private static int IndexOfLevel(string name)
    {
        for (int i = 0; i < SeverityMap.ShortNames.Count; i++)
        {
            if (string.Equals(SeverityMap.ShortNames[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool TryReadCondition(FieldToken field, [NotNullWhen(true)] out FilterNode? node)
    {
        node = null;

        if (
            field.Op == ":"
            && string.Equals(field.Field, "has", StringComparison.OrdinalIgnoreCase)
        )
        {
            node = new ConditionNode(
                ResolveField(field.Value),
                field.Negated ? FilterOperator.NotExists : FilterOperator.Exists,
                null
            );
            return true;
        }

        string resolved = ResolveField(field.Field);

        // "=" is only defined for levels, and a severity token that reached here (negated,
        // compared or unknown) has no chip form: the level toggles are the level filter.
        if (field.Op == "=" || resolved == LogFields.Severity)
        {
            return false;
        }

        ConditionNode condition = field.Op switch
        {
            ":" => Match(resolved, field.Value, field.Quoted),
            ">" => Compare(resolved, FilterOperator.GreaterThan, field.Value),
            ">=" => Compare(resolved, FilterOperator.GreaterOrEqual, field.Value),
            "<" => Compare(resolved, FilterOperator.LessThan, field.Value),
            _ => Compare(resolved, FilterOperator.LessOrEqual, field.Value),
        };

        node = field.Negated ? new NotNode(condition) : condition;
        return true;
    }

    // Stars mark a wildcard only at the ends of an unquoted value; a quoted value is literal.
    // A value of only stars ("field:*") means the field is present.
    private static ConditionNode Match(string field, string value, bool quoted)
    {
        if (quoted)
        {
            return new ConditionNode(field, FilterOperator.Equals, ToJson(value));
        }

        bool leading = value.StartsWith('*');
        bool trailing = value.EndsWith('*');
        string core = value.Trim('*');
        if (core.Length == 0)
        {
            return new ConditionNode(field, FilterOperator.Exists, null);
        }

        FilterOperator op = (leading, trailing) switch
        {
            (true, true) => FilterOperator.Contains,
            (true, false) => FilterOperator.EndsWith,
            (false, true) => FilterOperator.StartsWith,
            _ => FilterOperator.Equals,
        };
        return new ConditionNode(field, op, ToJson(core));
    }

    // A comparison value that reads as a finite invariant-culture number is sent as a JSON number;
    // anything else (typically a date) stays a string and the evaluator decides how to compare.
    private static ConditionNode Compare(string field, FilterOperator op, string value)
    {
        JsonElement json =
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double n)
            && double.IsFinite(n)
                ? JsonSerializer.SerializeToElement(n)
                : ToJson(value);
        return new ConditionNode(field, op, json);
    }

    private static JsonElement ToJson(string value) => JsonSerializer.SerializeToElement(value);

    private static string ResolveField(string name) =>
        Aliases.TryGetValue(name, out string? field) ? field : name;

    private static FieldToken? SplitField(Token token)
    {
        if (token.Segments[0].Quoted)
        {
            return null;
        }

        Match match = FieldPattern().Match(token.Segments[0].Text);
        if (!match.Success)
        {
            return null;
        }

        // The value is the rest of the first segment plus any later segments, so both
        // field:"a b" and field:a"b c" read; only a value that is wholly one quoted segment
        // counts as quoted.
        string rest = match.Groups["rest"].Value;
        bool quoted = rest.Length == 0 && token.Segments is [_, { Quoted: true }];
        string value = rest + string.Concat(token.Segments.Skip(1).Select(s => s.Text));
        if (value.Length == 0 && !quoted)
        {
            return null;
        }

        return new FieldToken(
            match.Groups["neg"].Success,
            match.Groups["field"].Value,
            match.Groups["op"].Value,
            value,
            quoted
        );
    }

    // Splits on whitespace outside quotes. Inside quotes \" is a quote and \\ a backslash; any
    // other backslash is literal. Returns false when the input ends inside a quote.
    private static bool TryTokenise(string input, out List<Token> tokens)
    {
        tokens = [];
        int i = 0;
        while (i < input.Length)
        {
            if (char.IsWhiteSpace(input[i]))
            {
                i++;
                continue;
            }

            var segments = new List<Segment>();
            var text = new StringBuilder();
            while (i < input.Length && !char.IsWhiteSpace(input[i]))
            {
                if (input[i] != '"')
                {
                    text.Append(input[i++]);
                    continue;
                }

                if (text.Length > 0)
                {
                    segments.Add(new Segment(text.ToString(), false));
                    text.Clear();
                }

                i++;
                bool closed = false;
                while (i < input.Length)
                {
                    char c = input[i];
                    if (c == '\\' && i + 1 < input.Length && input[i + 1] is '"' or '\\')
                    {
                        text.Append(input[i + 1]);
                        i += 2;
                    }
                    else if (c == '"')
                    {
                        closed = true;
                        i++;
                        break;
                    }
                    else
                    {
                        text.Append(c);
                        i++;
                    }
                }

                if (!closed)
                {
                    return false;
                }

                segments.Add(new Segment(text.ToString(), true));
                text.Clear();
            }

            if (text.Length > 0)
            {
                segments.Add(new Segment(text.ToString(), false));
            }

            tokens.Add(new Token(segments));
        }

        return true;
    }

    // Field names cannot start with "-" (that is negation) and cannot contain an operator
    // character or a quote. Operators are tried longest first so ">=" is not read as ">".
    [GeneratedRegex(
        """^(?<neg>-)?(?<field>[^\s:<>="\-][^\s:<>="]*)(?<op>:|>=|<=|>|<|=)(?<rest>.*)$""",
        RegexOptions.Singleline | RegexOptions.CultureInvariant
    )]
    private static partial Regex FieldPattern();

    private readonly record struct Segment(string Text, bool Quoted);

    private sealed record Token(IReadOnlyList<Segment> Segments)
    {
        // The token as a bare word: quote characters dropped, escapes resolved.
        public string Text => string.Concat(Segments.Select(s => s.Text));
    }

    private readonly record struct FieldToken(
        bool Negated,
        string Field,
        string Op,
        string Value,
        bool Quoted
    );
}
