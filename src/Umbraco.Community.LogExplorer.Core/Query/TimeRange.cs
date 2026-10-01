namespace Umbraco.Community.LogExplorer.Core.Query;

/// <summary>
/// The requested time range: either relative to now or absolute. <paramref name="Relative"/> wins
/// when both are set, so a saved view stays relative. Resolve it with
/// <see cref="RelativeRange.Resolve"/>.
/// </summary>
/// <param name="From">Inclusive absolute start.</param>
/// <param name="To">Exclusive absolute end; null means now.</param>
/// <param name="Relative">One of <see cref="RelativeRange.Supported"/>, for example <c>1h</c>.</param>
public sealed record TimeRange(DateTimeOffset? From, DateTimeOffset? To, string? Relative);
