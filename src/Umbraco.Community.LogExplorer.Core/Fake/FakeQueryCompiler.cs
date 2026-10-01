using System.Text.Json;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Core.Severity;

namespace Umbraco.Community.LogExplorer.Core.Fake;

/// <summary>
/// Compiles a <see cref="LogQuery"/> to the fake source's readable pseudo-language, one clause per
/// line joined by <c>and</c>, for example:
/// <code>
/// @severity in [warn, error]
/// and (RequestPath startswith "/api" or RequestPath = "/")
/// and not text("timeout")
/// </code>
/// Conditions whose operator the source does not declare are left out and reported as unsupported,
/// the way a real provider reports what its language cannot express.
/// </summary>
internal static class FakeQueryCompiler
{
    public static CompileResult Compile(LogQuery query, IReadOnlySet<FilterOperator> operators)
    {
        var clauses = new List<string>();
        var unsupported = new List<FilterNode>();

        if (query.Levels is not null && query.Levels.Count < SeverityMap.ShortNames.Count)
        {
            IEnumerable<string> ordered = SeverityMap.ShortNames.Where(name =>
                query.Levels.Contains(name, StringComparer.OrdinalIgnoreCase)
            );
            clauses.Add($"@severity in [{string.Join(", ", ordered)}]");
        }

        if (query.Filter is AndNode root)
        {
            // Top-level AND children become separate lines, which is what the show-query panel wants.
            // Every clause is rendered as nested so a lone OR keeps its parentheses once it is
            // joined to the level clause with "and".
            clauses.AddRange(
                root.Children.Select(child => Render(child, operators, unsupported, nested: true))
                    .OfType<string>()
            );
        }
        else if (
            query.Filter is not null
            && Render(query.Filter, operators, unsupported, nested: true) is { } single
        )
        {
            clauses.Add(single);
        }

        if (!string.IsNullOrWhiteSpace(query.NativeQuery))
        {
            clauses.Add($"({query.NativeQuery})");
        }

        return new CompileResult(
            clauses.Count == 0 ? null : string.Join("\nand ", clauses),
            unsupported
        );
    }

    /// <summary>Every condition in the tree whose operator is not declared.</summary>
    public static IEnumerable<ConditionNode> Undeclared(
        FilterNode? node,
        IReadOnlySet<FilterOperator> operators
    ) =>
        node switch
        {
            AndNode and => and.Children.SelectMany(child => Undeclared(child, operators)),
            OrNode or => or.Children.SelectMany(child => Undeclared(child, operators)),
            NotNode not => Undeclared(not.Child, operators),
            ConditionNode condition when !operators.Contains(condition.Op) => [condition],
            _ => [],
        };

    // Returns null when nothing of the node survives (every condition in it was unsupported).
    private static string? Render(
        FilterNode node,
        IReadOnlySet<FilterOperator> operators,
        List<FilterNode> unsupported,
        bool nested
    )
    {
        switch (node)
        {
            case AndNode and:
                return Join(and.Children, " and ", operators, unsupported, nested);
            case OrNode or:
                return Join(or.Children, " or ", operators, unsupported, nested);
            case NotNode not:
                return Render(not.Child, operators, unsupported, nested: true) is { } inner
                    ? "not " + inner
                    : null;
            case ConditionNode condition when !operators.Contains(condition.Op):
                unsupported.Add(condition);
                return null;
            case ConditionNode condition:
                return RenderCondition(condition);
            case TextNode text:
                return $"text({JsonSerializer.Serialize(text.Text)}{(text.Phrase ? ", phrase" : "")})";
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
        IReadOnlySet<FilterOperator> operators,
        List<FilterNode> unsupported,
        bool nested
    )
    {
        string[] parts = children
            .Select(child => Render(child, operators, unsupported, nested: true))
            .OfType<string>()
            .ToArray();

        return parts.Length switch
        {
            0 => null,
            1 => parts[0],
            _ when nested => "(" + string.Join(separator, parts) + ")",
            _ => string.Join(separator, parts),
        };
    }

    private static string RenderCondition(ConditionNode condition)
    {
        string field = condition.Field;
        string value = condition.Value?.GetRawText() ?? "null";
        string clause = condition.Op switch
        {
            FilterOperator.Equals => $"{field} = {value}",
            FilterOperator.NotEquals => $"{field} != {value}",
            FilterOperator.Contains => $"{field} contains {value}",
            FilterOperator.StartsWith => $"{field} startswith {value}",
            FilterOperator.EndsWith => $"{field} endswith {value}",
            FilterOperator.GreaterThan => $"{field} > {value}",
            FilterOperator.GreaterOrEqual => $"{field} >= {value}",
            FilterOperator.LessThan => $"{field} < {value}",
            FilterOperator.LessOrEqual => $"{field} <= {value}",
            FilterOperator.In => $"{field} in {value}",
            FilterOperator.Exists => $"has({field})",
            FilterOperator.NotExists => $"not has({field})",
            FilterOperator.Matches => $"{field} matches {value}",
            _ => throw new ArgumentException(
                $"Unknown operator {condition.Op}.",
                nameof(condition)
            ),
        };

        bool comparesText = condition.Op is not (FilterOperator.Exists or FilterOperator.NotExists);
        return comparesText && !condition.CaseInsensitive ? clause + " (case-sensitive)" : clause;
    }
}
