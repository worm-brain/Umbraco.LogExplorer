namespace Umbraco.Community.LogExplorer.Core.Sources;

/// <summary>
/// The configured sources, filtered per user by <see cref="LogSourceDefinition.AllowedUserGroups"/>
/// (BRIEF §12).
/// </summary>
public interface ILogSourceRegistry
{
    /// <summary>The sources <paramref name="user"/> may see, in configuration order.</summary>
    /// <param name="user">The current backoffice user.</param>
    /// <returns>Visible sources; hidden ones are left out entirely.</returns>
    IReadOnlyList<ILogSource> GetVisibleSources(UserContext user);

    /// <summary>Gets one source by alias for <paramref name="user"/>.</summary>
    /// <param name="alias">The source alias.</param>
    /// <param name="user">The current backoffice user.</param>
    /// <returns>The source.</returns>
    /// <exception cref="KeyNotFoundException">No source has that alias.</exception>
    /// <exception cref="ForbiddenSourceException">The source exists but is hidden from the user.</exception>
    ILogSource GetForUser(string alias, UserContext user);
}
