namespace Umbraco.Community.LogExplorer.Core.Results;

/// <summary>
/// The absolute range a query actually ran over, returned in every response so the UI can show
/// exactly what ran even when the request was relative.
/// </summary>
/// <param name="From">Inclusive start.</param>
/// <param name="To">Exclusive end.</param>
public sealed record ResolvedRange(DateTimeOffset From, DateTimeOffset To);
