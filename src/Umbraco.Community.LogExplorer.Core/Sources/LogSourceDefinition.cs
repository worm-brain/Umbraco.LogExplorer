namespace Umbraco.Community.LogExplorer.Core.Sources;

/// <summary>
/// One entry of <c>LogExplorer:Sources</c> (BRIEF §13). Settable with a parameterless constructor
/// so configuration binding can create it.
/// </summary>
public sealed record LogSourceDefinition
{
    /// <summary>Unique alias, used in routes and URLs.</summary>
    public string Alias { get; init; } = "";

    /// <summary>Provider type, matched against <see cref="ILogSourceFactory.Type"/>.</summary>
    public string Type { get; init; } = "";

    /// <summary>Name shown in the source picker; the alias is used when empty.</summary>
    public string DisplayName { get; init; } = "";

    /// <summary>Whether the source is sensitive: admin-only unless groups are listed, and audited.</summary>
    public bool Sensitive { get; init; }

    /// <summary>
    /// Group aliases that may see the source. Empty means everyone with Settings access, except
    /// that a sensitive source then defaults to the <c>admin</c> group.
    /// </summary>
    public IReadOnlyList<string> AllowedUserGroups { get; init; } = [];

    /// <summary>Whether users may run native queries against the source.</summary>
    public bool AllowNativeQuery { get; init; } = true;

    /// <summary>
    /// Provider-specific settings such as a workspace id or server URL. Secrets live here too, so
    /// this never leaves the server.
    /// </summary>
    public IReadOnlyDictionary<string, string> Settings { get; init; } =
        new Dictionary<string, string>();
}
