namespace Umbraco.Community.LogExplorer.Core.Results;

/// <summary>A field discovered in the matching entries, for autocomplete and the fields panel.</summary>
/// <param name="Path">Field path usable in a <c>ConditionNode</c>.</param>
/// <param name="Kind">
/// One of <c>string</c>, <c>number</c>, <c>bool</c>, <c>datetime</c>, <c>object</c>, <c>array</c>.
/// </param>
/// <param name="PresenceRatio">Share of matching entries that have the field, 0 to 1.</param>
public sealed record FieldInfo(string Path, string Kind, double PresenceRatio);
