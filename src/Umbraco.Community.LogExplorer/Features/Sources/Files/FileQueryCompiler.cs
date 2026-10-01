using System.Globalization;
using System.Text.Json;
using Serilog.Expressions;
using Umbraco.Community.LogExplorer.Core.Filtering;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Core.Severity;

namespace Umbraco.Community.LogExplorer.Features.Sources.Files;

/// <summary>
/// Compiles a <see cref="LogQuery"/> to the core Log Viewer's dialect (Serilog.Expressions with
/// Umbraco's names, ADR 0013) for "Show query", so the output pastes into the core viewer and
/// selects the same entries as the files source's own C# evaluation (<see cref="LogRecordFilter"/>).
/// One clause per line, joined by <c>and</c> at the line start (UI brief §4.6), for example:
/// <code>
/// (@Level = 'Warning' or @Level = 'Error' or @Level = 'Fatal')
/// and StartsWith(RequestPath, '/api') ci
/// and not (SourceContext = 'Umbraco.Cms.Core.Sync' ci)
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// A node the dialect cannot express with the same meaning is left out of the output and returned
/// in <see cref="CompileResult.Unsupported"/>: <c>@timestamp</c>, <c>@exception.*</c> and
/// <c>@resource.*</c> fields, ordering against anything but a number, null, object or array values,
/// and severity operators other than equality and <c>in</c>. The time range is not part of the
/// output: the core viewer has its own date range.
/// </para>
/// <para>
/// Known differences the compiler cannot remove (ADR 0013): property names are case-sensitive in
/// the dialect; <c>@Message</c> renders string properties without quotes, where the record body
/// quotes them; text search also matches an exception's type and stack trace, not only its
/// message; ordering and regex operators do not see numbers stored as strings.
/// </para>
/// </remarks>
internal static class FileQueryCompiler
{
    // Serilog.Expressions keywords, matched ignoring case; a property with one of these names has
    // to be written as an indexer.
    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "and",
        "in",
        "is",
        "like",
        "not",
        "or",
        "true",
        "false",
        "null",
        "if",
        "then",
        "else",
        "end",
        "ci",
        "each",
        "delimit",
    };

    /// <summary>Compiles the level set, the filter and any native query.</summary>
    /// <param name="query">The query; its range, paging and sort are not compiled.</param>
    /// <returns>
    /// The expression, null when nothing filters, and the nodes left out of it. A native query is
    /// appended as its own clause, in expression form, so the whole output stays one expression.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is null.</exception>
    public static CompileResult Compile(LogQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var clauses = new List<string>();
        var unsupported = new List<FilterNode>();

        if (query.Levels is not null && LevelClause(query.Levels) is { } levels)
        {
            clauses.Add(levels);
        }

        // Top-level AND children become separate lines. Every clause renders as nested so an OR
        // keeps its parentheses once it is joined to the others with "and".
        IEnumerable<FilterNode> roots = query.Filter switch
        {
            null => [],
            AndNode and => and.Children,
            _ => [query.Filter],
        };
        clauses.AddRange(
            roots.Select(root => Render(root, unsupported, nested: true)).OfType<string>()
        );

        if (!string.IsNullOrWhiteSpace(query.NativeQuery))
        {
            clauses.Add($"({NativeFilter.ToExpression(query.NativeQuery)})");
        }

        return new CompileResult(
            clauses.Count == 0 ? null : string.Join("\nand ", clauses),
            unsupported
        );
    }

    // Null when the set does not filter (null semantics are the caller's; all six levels is no
    // filter, ADR 0004). A set naming no known level matches nothing, as LogRecordFilter does.
    private static string? LevelClause(IReadOnlySet<string> levels)
    {
        IReadOnlyList<SeverityBand> bands = SeverityMap.ToBands(levels);
        if (bands.Count == SeverityMap.ShortNames.Count)
        {
            return null;
        }

        if (bands.Count == 0)
        {
            return "false";
        }

        string[] tests =
        [
            .. bands.Select(band => $"@Level = {Text(SeverityMap.ToSerilog(band.Min))}"),
        ];
        return tests.Length == 1 ? tests[0] : "(" + string.Join(" or ", tests) + ")";
    }

    // Returns null when nothing of the node survives (every condition in it was unsupported, or
    // it is an empty AND, which matches everything).
    private static string? Render(FilterNode node, List<FilterNode> unsupported, bool nested)
    {
        switch (node)
        {
            case AndNode and:
                return Join(and.Children, " and ", unsupported, nested);
            case OrNode or:
                return Join(or.Children, " or ", unsupported, nested);
            case NotNode not:
                return Render(not.Child, unsupported, nested: true) is { } inner
                    ? Negate(inner)
                    : null;
            case ConditionNode condition:
                string? clause = RenderCondition(condition);
                if (clause is null)
                {
                    unsupported.Add(condition);
                }

                return clause;
            case TextNode text:
                return RenderText(text, nested);
            default:
                throw new ArgumentException(
                    $"Unknown filter node {node.GetType().Name}.",
                    nameof(node)
                );
        }
    }

    private static string? Join(
        IReadOnlyList<FilterNode> children,
        string separator,
        List<FilterNode> unsupported,
        bool nested
    )
    {
        string[] parts =
        [
            .. children.Select(child => Render(child, unsupported, nested: true)).OfType<string>(),
        ];

        return parts.Length switch
        {
            0 => null,
            1 => parts[0],
            _ when nested => "(" + string.Join(separator, parts) + ")",
            _ => string.Join(separator, parts),
        };
    }

    // Parentheses keep a trailing "ci" with the test it modifies rather than leaving its binding
    // to operator precedence.
    private static string Negate(string inner) =>
        inner.StartsWith('(') ? "not " + inner : $"not ({inner})";

    // Bare words must each appear; LogRecordFilter searches the body and the exception message.
    // `like` does not see exceptions (it only matches string values), so the exception side uses
    // Contains, which converts the exception to its full text.
    private static string? RenderText(TextNode node, bool nested)
    {
        string[] terms = node.Phrase
            ? [node.Text]
            : node.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        string[] parts =
        [
            .. terms.Select(term =>
                $"(@Message like '%{SerilogExpression.EscapeLikeExpressionContent(term)}%' ci"
                + $" or Contains(@Exception, {Text(term)}) ci)"
            ),
        ];

        return parts.Length switch
        {
            0 => null,
            1 => parts[0],
            _ when nested => "(" + string.Join(" and ", parts) + ")",
            _ => string.Join(" and ", parts),
        };
    }

    // Null when the dialect cannot express the condition with LogRecordFilter's meaning.
    private static string? RenderCondition(ConditionNode node)
    {
        if (string.Equals(node.Field, LogFields.Severity, StringComparison.OrdinalIgnoreCase))
        {
            return RenderSeverity(node);
        }

        if (FieldExpression(node.Field) is not { } field)
        {
            return null;
        }

        string ci = node.CaseInsensitive ? " ci" : "";
        switch (node.Op)
        {
            case FilterOperator.Exists:
                return $"{field.Text} is not null";
            // "is null" on a wildcard would ask whether any element is null; the field is
            // missing when no element is set.
            case FilterOperator.NotExists:
                return field.HasWildcard
                    ? $"not ({field.Text} is not null)"
                    : $"{field.Text} is null";
        }

        if (
            node.Value
            is not { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } value
        )
        {
            return null;
        }

        switch (node.Op)
        {
            case FilterOperator.Equals:
                return EqualityTest(field.Text, [value], node.CaseInsensitive);
            case FilterOperator.NotEquals:
                return EqualityTest(field.Text, [value], node.CaseInsensitive) is { } equals
                    ? Negate(equals)
                    : null;
            case FilterOperator.In:
                JsonElement[] candidates =
                    value.ValueKind == JsonValueKind.Array ? [.. value.EnumerateArray()] : [value];
                return candidates.Length == 0
                    ? null
                    : EqualityTest(field.Text, candidates, node.CaseInsensitive);
            case FilterOperator.Contains:
            case FilterOperator.StartsWith:
            case FilterOperator.EndsWith:
                return TextOf(value) is { } text
                    ? $"{node.Op}({TextSubject(field.Text, text)}, {Text(text)}){ci}"
                    : null;
            case FilterOperator.Matches:
                return TextOf(value) is { } pattern
                    ? $"IsMatch({field.Text}, {Text(pattern)}){ci}"
                    : null;
            case FilterOperator.GreaterThan:
            case FilterOperator.GreaterOrEqual:
            case FilterOperator.LessThan:
            case FilterOperator.LessOrEqual:
                // The dialect only orders numbers; dates and text have no comparison form.
                return Number(value) is { } number
                    ? $"{field.Text} {ComparisonSymbol(node.Op)} {number}"
                    : null;
            default:
                throw new ArgumentException($"Unknown operator {node.Op}.", nameof(node));
        }
    }

    private static string ComparisonSymbol(FilterOperator op) =>
        op switch
        {
            FilterOperator.GreaterThan => ">",
            FilterOperator.GreaterOrEqual => ">=",
            FilterOperator.LessThan => "<",
            _ => "<=",
        };

    // @Level is the Serilog level name; LogFields reads @severity as the OTel short name, so the
    // value is translated. Ordering short names as text has no meaning worth carrying over.
    private static string? RenderSeverity(ConditionNode node)
    {
        if (node.Value is not { } value)
        {
            return null;
        }

        JsonElement[] names =
            value.ValueKind == JsonValueKind.Array ? [.. value.EnumerateArray()] : [value];
        var levels = new List<string>();
        foreach (JsonElement name in names)
        {
            int severity =
                name.ValueKind == JsonValueKind.String
                    ? SeverityMap.FromShortName(name.GetString())
                    : SeverityMap.Unspecified;
            if (severity == SeverityMap.Unspecified)
            {
                return null;
            }

            levels.Add(Text(SeverityMap.ToSerilog(severity)));
        }

        return node.Op switch
        {
            FilterOperator.Equals when levels.Count == 1 => $"@Level = {levels[0]}",
            FilterOperator.NotEquals when levels.Count == 1 => $"@Level <> {levels[0]}",
            FilterOperator.In when levels.Count > 0 => $"@Level in [{string.Join(", ", levels)}]",
            _ => null,
        };
    }

    /// <summary>
    /// Equality the way <see cref="LogRecordFilter"/> defines it, which is looser than the
    /// dialect's: a number equals a numeric string, and a boolean equals its text. Each value is
    /// offered in every typed form it could match, so <c>StatusCode:200</c> (a string from the
    /// search box) still matches the number 200 the log file holds.
    /// </summary>
    private static string? EqualityTest(string field, JsonElement[] values, bool caseInsensitive)
    {
        var literals = new List<string>();
        foreach (JsonElement value in values)
        {
            if (Literals(value, caseInsensitive) is not { } forms)
            {
                return null;
            }

            literals.AddRange(forms.Where(form => !literals.Contains(form)));
        }

        // ci only changes string comparisons, and is noise when every form is a number.
        string ci =
            caseInsensitive && literals.Any(literal => literal.StartsWith('\'')) ? " ci" : "";
        return literals.Count == 1
            ? $"{field} = {literals[0]}{ci}"
            : $"{field} in [{string.Join(", ", literals)}]{ci}";
    }

    private static List<string>? Literals(JsonElement value, bool caseInsensitive)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                string text = value.GetString()!;
                var forms = new List<string> { Text(text) };
                if (Number(value) is { } number)
                {
                    forms.Add(number);
                }

                StringComparison comparison = caseInsensitive
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                if (
                    string.Equals(text, "true", comparison)
                    || string.Equals(text, "false", comparison)
                )
                {
                    forms.Add(text.ToLowerInvariant());
                }

                return forms;
            case JsonValueKind.Number:
                return Number(value) is { } literal ? [literal, Text(value.GetRawText())] : null;
            case JsonValueKind.True:
            case JsonValueKind.False:
                string flag = value.GetRawText();
                return [flag, Text(flag)];
            default:
                return null;
        }
    }

    // A numeric-looking text value probably targets a number, which the dialect's string
    // functions do not see; ToString hands them its text, and leaves strings as they are.
    private static string TextSubject(string field, string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
            ? $"ToString({field})"
            : field;

    // The text LogRecordFilter matches text operators against: a string's content, or the raw
    // JSON of a number or boolean. Objects and arrays have no useful text form.
    private static string? TextOf(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
            _ => null,
        };

    // A number literal, from a JSON number or a numeric string (LogRecordFilter treats both as
    // numbers). The dialect has no exponent syntax, so the value goes through decimal.
    private static string? Number(JsonElement value)
    {
        decimal number = 0;
        bool parsed = value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetDecimal(out number),
            JsonValueKind.String => decimal.TryParse(
                value.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out number
            ),
            _ => false,
        };
        return parsed ? number.ToString(CultureInfo.InvariantCulture) : null;
    }

    private static string Text(string value) => $"'{SerilogExpression.EscapeStringContent(value)}'";

    /// <summary>A field as the dialect names it, and whether it crosses an array.</summary>
    private sealed record Field(string Text, bool HasWildcard);

    // Portable fields with a dialect equivalent, else an attribute path. Paths keep dot segments;
    // "[]" becomes the any-element wildcard "[?]"; names that are not plain identifiers (or are
    // keywords) become indexers. A dotted key such as "host.name" is read as a path here, where
    // LogFields would first try it whole.
    private static Field? FieldExpression(string field)
    {
        if (field.StartsWith('@'))
        {
            string? builtIn = field.ToUpperInvariant() switch
            {
                "@BODY" => "@Message",
                "@TEMPLATE" => "@MessageTemplate",
                "@SCOPE" => "SourceContext",
                "@TRACEID" => "@tr",
                "@SPANID" => "@sp",
                _ => null,
            };
            return builtIn is null ? null : new Field(builtIn, HasWildcard: false);
        }

        var text = new System.Text.StringBuilder();
        bool wildcard = false;
        string[] segments = field.Split('.');
        for (int i = 0; i < segments.Length; i++)
        {
            string segment = segments[i];
            bool expands = segment.EndsWith("[]", StringComparison.Ordinal);
            string name = expands ? segment[..^2] : segment;
            if (name.Length == 0)
            {
                return null;
            }

            bool identifier = SerilogExpression.IsValidIdentifier(name) && !Keywords.Contains(name);
            text.Append(
                (i, identifier) switch
                {
                    (0, true) => name,
                    (0, false) => $"@Properties[{Text(name)}]",
                    (_, true) => "." + name,
                    _ => $"[{Text(name)}]",
                }
            );
            if (expands)
            {
                text.Append("[?]");
                wildcard = true;
            }
        }

        return new Field(text.ToString(), wildcard);
    }
}
