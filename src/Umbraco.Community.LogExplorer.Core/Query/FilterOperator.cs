namespace Umbraco.Community.LogExplorer.Core.Query;

/// <summary>
/// Comparison applied by a <see cref="ConditionNode"/>. String comparisons honour the node's
/// <c>CaseInsensitive</c> flag; ordering operators compare numbers, then ISO dates, then strings.
/// A field that resolves to several values (an array path) matches when any value matches,
/// except for <see cref="NotEquals"/>, which requires that none does.
/// </summary>
public enum FilterOperator
{
    /// <summary>A value equals the condition value.</summary>
    Equals,

    /// <summary>No value equals the condition value; true when the field is missing.</summary>
    NotEquals,

    /// <summary>A value's text contains the condition value.</summary>
    Contains,

    /// <summary>A value's text starts with the condition value.</summary>
    StartsWith,

    /// <summary>A value's text ends with the condition value.</summary>
    EndsWith,

    /// <summary>A value is greater than the condition value.</summary>
    GreaterThan,

    /// <summary>A value is greater than or equal to the condition value.</summary>
    GreaterOrEqual,

    /// <summary>A value is less than the condition value.</summary>
    LessThan,

    /// <summary>A value is less than or equal to the condition value.</summary>
    LessOrEqual,

    /// <summary>A value equals any element of the condition value, which must be a JSON array.</summary>
    In,

    /// <summary>The field has a non-null value; the condition value is ignored.</summary>
    Exists,

    /// <summary>The field is missing or null; the condition value is ignored.</summary>
    NotExists,

    /// <summary>A value's text matches the condition value as a .NET regular expression.</summary>
    Matches,
}
