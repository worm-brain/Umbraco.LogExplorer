namespace Umbraco.Community.LogExplorer.Features.NativeQuery;

/// <summary>The body of <c>POST /sources/{alias}/validate</c>.</summary>
/// <param name="Native">The native query text, in the source's own language. Required.</param>
public sealed record ValidateRequest(string? Native);
