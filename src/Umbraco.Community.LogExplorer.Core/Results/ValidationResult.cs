namespace Umbraco.Community.LogExplorer.Core.Results;

/// <summary>Outcome of checking a native query before it runs.</summary>
/// <param name="Valid">Whether the query can run.</param>
/// <param name="Error">Why not, when invalid.</param>
/// <param name="Position">Zero-based character offset of the error, when the source knows it.</param>
public sealed record ValidationResult(bool Valid, string? Error, int? Position);
