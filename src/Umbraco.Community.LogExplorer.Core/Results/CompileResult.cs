using Umbraco.Community.LogExplorer.Core.Query;

namespace Umbraco.Community.LogExplorer.Core.Results;

/// <summary>A query translated to the source's native language ("Show query").</summary>
/// <param name="Native">The native query; null when the query has no filter at all.</param>
/// <param name="Unsupported">
/// Nodes the source cannot run; they are left out of <paramref name="Native"/> and shown disabled.
/// </param>
public sealed record CompileResult(string? Native, IReadOnlyList<FilterNode> Unsupported);
