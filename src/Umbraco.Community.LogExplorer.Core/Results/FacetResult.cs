using System.Text.Json;

namespace Umbraco.Community.LogExplorer.Core.Results;

/// <summary>Top values for each requested field (the fields panel, BRIEF §6.6).</summary>
/// <param name="Facets">One facet per requested field, in request order.</param>
/// <param name="Approximate">Whether the source sampled or stopped at a scan budget.</param>
/// <param name="ScannedRange">The range actually scanned; narrower than requested when approximate.</param>
public sealed record FacetResult(
    IReadOnlyList<Facet> Facets,
    bool Approximate,
    ResolvedRange ScannedRange
);

/// <summary>The value distribution of one field.</summary>
/// <param name="Field">The field path as requested.</param>
/// <param name="PresenceRatio">Share of matching entries that have the field, 0 to 1.</param>
/// <param name="TopValues">Most frequent values, highest count first.</param>
public sealed record Facet(string Field, double PresenceRatio, IReadOnlyList<FacetValue> TopValues);

/// <summary>One value of a facet and how often it occurs.</summary>
/// <param name="Value">The value with its JSON kind.</param>
/// <param name="Count">Number of matching entries with this value.</param>
public sealed record FacetValue(JsonElement Value, long Count);
