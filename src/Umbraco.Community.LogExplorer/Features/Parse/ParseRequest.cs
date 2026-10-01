namespace Umbraco.Community.LogExplorer.Features.Parse;

/// <summary>The body of <c>POST /parse</c>.</summary>
/// <param name="Input">
/// The search box text, at most <see cref="ParseController.MaxInputLength"/> characters. Null is
/// read as empty.
/// </param>
public sealed record ParseRequest(string? Input);
