using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Records;
using Umbraco.Community.LogExplorer.Core.Severity;

namespace Umbraco.Community.LogExplorer.Core.Filtering;

/// <summary>
/// Evaluates a <see cref="FilterNode"/> and a level set against a <see cref="LogRecord"/> in
/// memory. This is the reference semantics for the filter tree: <c>FakeLogSource</c> and the
/// files provider use it directly, and the contract suite uses it to compute expected results.
/// <para>Typed comparison rules, applied to each value a field resolves to (see <see cref="LogFields"/>):</para>
/// <list type="bullet">
/// <item>Equality is numeric when either side is a JSON number and the other is a number or a
/// numeric string; otherwise it compares text (honouring <c>CaseInsensitive</c>).</item>
/// <item>Ordering is numeric when both sides are numbers or numeric strings, chronological when
/// both are ISO 8601 date strings, and textual when both are other strings. Anything else does
/// not match.</item>
/// <item>Contains, starts with, ends with and regex match work on the value's text: a string's
/// content, or the raw JSON of numbers, booleans, objects and arrays.</item>
/// </list>
/// </summary>
public static class LogRecordFilter
{
    // Guards Matches against catastrophic backtracking in a user-typed pattern.
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    /// <summary>Whether a record passes both the level set and the filter.</summary>
    /// <param name="record">The record.</param>
    /// <param name="filter">The filter; null matches everything.</param>
    /// <param name="levels">The level set; null matches every level (ADR 0004).</param>
    /// <returns>True when the record should be kept.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="record"/> is null.</exception>
    /// <exception cref="ArgumentException">A <see cref="FilterOperator.Matches"/> pattern is not a valid regular expression.</exception>
    public static bool Matches(LogRecord record, FilterNode? filter, IReadOnlySet<string>? levels)
    {
        ArgumentNullException.ThrowIfNull(record);
        return SeverityMap.IsInLevels(record.SeverityNumber, levels)
            && (filter is null || Matches(record, filter));
    }

    /// <summary>Whether a record passes the filter, ignoring levels.</summary>
    /// <param name="record">The record.</param>
    /// <param name="filter">The filter.</param>
    /// <returns>True when the record matches.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">A <see cref="FilterOperator.Matches"/> pattern is not a valid regular expression.</exception>
    public static bool Matches(LogRecord record, FilterNode filter)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(filter);

        return filter switch
        {
            AndNode and => and.Children.All(child => Matches(record, child)),
            OrNode or => or.Children.Any(child => Matches(record, child)),
            NotNode not => !Matches(record, not.Child),
            ConditionNode condition => MatchesCondition(record, condition),
            TextNode text => MatchesText(record, text),
            _ => throw new ArgumentException(
                $"Unknown filter node {filter.GetType().Name}.",
                nameof(filter)
            ),
        };
    }

    private static bool MatchesText(LogRecord record, TextNode node)
    {
        string haystack = record.Body + "\n" + record.Exception?.Message;
        if (node.Phrase)
        {
            return haystack.Contains(node.Text, StringComparison.OrdinalIgnoreCase);
        }

        return node
            .Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .All(word => haystack.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesCondition(LogRecord record, ConditionNode node)
    {
        IReadOnlyList<JsonElement> values = LogFields.Resolve(record, node.Field);
        StringComparison comparison = node.CaseInsensitive
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        switch (node.Op)
        {
            case FilterOperator.Exists:
                return values.Count > 0;
            case FilterOperator.NotExists:
                return values.Count == 0;
        }

        // Every remaining operator needs a value; a missing or null one matches nothing.
        if (
            node.Value
            is not { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } target
        )
        {
            return node.Op == FilterOperator.NotEquals;
        }

        return node.Op switch
        {
            FilterOperator.Equals => values.Any(v => AreEqual(v, target, comparison)),
            FilterOperator.NotEquals => !values.Any(v => AreEqual(v, target, comparison)),
            FilterOperator.In => values.Any(v =>
                InCandidates(target).Any(candidate => AreEqual(v, candidate, comparison))
            ),
            FilterOperator.Contains => values.Any(v =>
                TextOf(v).Contains(TextOf(target), comparison)
            ),
            FilterOperator.StartsWith => values.Any(v =>
                TextOf(v).StartsWith(TextOf(target), comparison)
            ),
            FilterOperator.EndsWith => values.Any(v =>
                TextOf(v).EndsWith(TextOf(target), comparison)
            ),
            FilterOperator.GreaterThan => values.Any(v => Compare(v, target, comparison) > 0),
            FilterOperator.GreaterOrEqual => values.Any(v => Compare(v, target, comparison) >= 0),
            FilterOperator.LessThan => values.Any(v => Compare(v, target, comparison) < 0),
            FilterOperator.LessOrEqual => values.Any(v => Compare(v, target, comparison) <= 0),
            FilterOperator.Matches => values.Any(v =>
                IsRegexMatch(v, target, node.CaseInsensitive)
            ),
            _ => throw new ArgumentException($"Unknown operator {node.Op}.", nameof(node)),
        };
    }

    // In takes an array; a single value is accepted as a one-element list.
    private static JsonElement[] InCandidates(JsonElement target) =>
        target.ValueKind == JsonValueKind.Array ? [.. target.EnumerateArray()] : [target];

    private static bool AreEqual(JsonElement value, JsonElement target, StringComparison comparison)
    {
        bool eitherIsNumber =
            value.ValueKind == JsonValueKind.Number || target.ValueKind == JsonValueKind.Number;
        if (
            eitherIsNumber
            && TryGetNumber(value, out double a)
            && TryGetNumber(target, out double b)
        )
        {
            return a.Equals(b);
        }

        return string.Equals(TextOf(value), TextOf(target), comparison);
    }

    // Null means "not comparable", which every ordering test treats as no match (null > 0 is false).
    private static int? Compare(JsonElement value, JsonElement target, StringComparison comparison)
    {
        if (TryGetNumber(value, out double a) && TryGetNumber(target, out double b))
        {
            return a.CompareTo(b);
        }

        if (value.ValueKind != JsonValueKind.String || target.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string left = value.GetString()!;
        string right = target.GetString()!;
        if (
            TryGetIsoDate(left, out DateTimeOffset leftDate)
            && TryGetIsoDate(right, out DateTimeOffset rightDate)
        )
        {
            return leftDate.CompareTo(rightDate);
        }

        return string.Compare(left, right, comparison);
    }

    private static bool IsRegexMatch(JsonElement value, JsonElement pattern, bool caseInsensitive)
    {
        RegexOptions options = caseInsensitive
            ? RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
            : RegexOptions.CultureInvariant;

        // The static Regex methods cache compiled patterns, so one pattern evaluated over many
        // records is parsed once.
        return Regex.IsMatch(TextOf(value), TextOf(pattern), options, RegexTimeout);
    }

    private static string TextOf(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString()! : element.GetRawText();

    private static bool TryGetNumber(JsonElement element, out double number)
    {
        number = 0;
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetDouble(out number),
            JsonValueKind.String => double.TryParse(
                element.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out number
            ),
            _ => false,
        };
    }

    // Only strings shaped like ISO 8601 (yyyy-MM-dd...) count as dates, so ordinary text that
    // DateTimeOffset happens to parse is still compared as text.
    private static bool TryGetIsoDate(string text, out DateTimeOffset date)
    {
        date = default;
        return text.Length >= 10
            && text[4] == '-'
            && text[7] == '-'
            && DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out date
            );
    }
}
