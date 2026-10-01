namespace Umbraco.Community.LogExplorer.Core.Sources;

/// <summary>
/// The backoffice user a request runs as, reduced to what source visibility needs, so Core never
/// references Umbraco's user types.
/// </summary>
/// <param name="UserKey">The user's key.</param>
/// <param name="GroupAliases">Aliases of the user's groups, compared case-insensitively.</param>
public sealed record UserContext(Guid UserKey, IReadOnlySet<string> GroupAliases);
