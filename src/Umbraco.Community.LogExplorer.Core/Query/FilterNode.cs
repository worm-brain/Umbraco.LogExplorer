using System.Text.Json;
using System.Text.Json.Serialization;

namespace Umbraco.Community.LogExplorer.Core.Query;

/// <summary>
/// A node in the provider-neutral filter tree (BRIEF §8.2). Serialised with a <c>kind</c>
/// discriminator so the browser and the server exchange the same tree.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(AndNode), "and")]
[JsonDerivedType(typeof(OrNode), "or")]
[JsonDerivedType(typeof(NotNode), "not")]
[JsonDerivedType(typeof(ConditionNode), "condition")]
[JsonDerivedType(typeof(TextNode), "text")]
public abstract record FilterNode;

/// <summary>Matches when every child matches; an empty list matches everything.</summary>
/// <param name="Children">The child nodes.</param>
public sealed record AndNode(IReadOnlyList<FilterNode> Children) : FilterNode;

/// <summary>Matches when any child matches; an empty list matches nothing.</summary>
/// <param name="Children">The child nodes.</param>
public sealed record OrNode(IReadOnlyList<FilterNode> Children) : FilterNode;

/// <summary>Matches when the child does not.</summary>
/// <param name="Child">The negated node.</param>
public sealed record NotNode(FilterNode Child) : FilterNode;

/// <summary>A comparison of one field against a typed value.</summary>
/// <param name="Field">
/// A portable field such as <c>@severity</c> or <c>@exception.type</c>, or an attribute path with
/// dot segments where a <c>[]</c> suffix means "any element of this array" (<c>Cart.Total</c>,
/// <c>Tags[]</c>).
/// </param>
/// <param name="Op">The comparison.</param>
/// <param name="Value">
/// The value to compare with, keeping its JSON kind; an array for <see cref="FilterOperator.In"/>,
/// ignored for <see cref="FilterOperator.Exists"/> and <see cref="FilterOperator.NotExists"/>.
/// </param>
/// <param name="CaseInsensitive">Whether string comparisons ignore case.</param>
public sealed record ConditionNode(
    string Field,
    FilterOperator Op,
    JsonElement? Value,
    bool CaseInsensitive = true
) : FilterNode;

/// <summary>
/// Free-text search over the body and exception message, always case-insensitive. Without
/// <paramref name="Phrase"/>, every whitespace-separated word must appear, in any order.
/// </summary>
/// <param name="Text">The words or phrase to find.</param>
/// <param name="Phrase">Whether <paramref name="Text"/> must appear as one exact substring.</param>
public sealed record TextNode(string Text, bool Phrase = false) : FilterNode;
