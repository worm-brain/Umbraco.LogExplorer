using System.Diagnostics.CodeAnalysis;

// "Alias" is a Visual Basic keyword, but it is the BRIEF §8.4 contract name, the configuration key
// and the API route segment; providers are written in C#.
[assembly: SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "Alias is the contract, configuration and route name (BRIEF 8.4, 13).",
    Scope = "member",
    Target = "~P:Umbraco.Community.LogExplorer.Core.Sources.ILogSource.Alias"
)]
[assembly: SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "Alias is the contract, configuration and route name (BRIEF 8.4, 13).",
    Scope = "member",
    Target = "~P:Umbraco.Community.LogExplorer.Core.Sources.LogSourceBase.Alias"
)]
[assembly: SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "Alias is the contract, configuration and route name (BRIEF 8.4, 13).",
    Scope = "member",
    Target = "~M:Umbraco.Community.LogExplorer.Core.Sources.ILogSourceRegistry.GetForUser(System.String,Umbraco.Community.LogExplorer.Core.Sources.UserContext)~Umbraco.Community.LogExplorer.Core.Sources.ILogSource"
)]
