using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Community.LogExplorer.Core.Sources;

namespace Umbraco.Community.LogExplorer.Features.Sources;

/// <summary>
/// Builds the configured log sources once, from <c>LogExplorer:Sources</c> and the registered
/// <see cref="ILogSourceFactory"/> provider types (BRIEF §7.3, §13), and answers which of them a
/// user may see.
/// </summary>
/// <remarks>
/// <para>
/// Validation is per source: an unknown type, a missing or duplicate alias, or a factory that
/// rejects its settings logs one error and skips only that source, so one bad entry never breaks
/// the others. With no sources configured, a single <c>files</c> source of type
/// <c>UmbracoFiles</c> is used; until that provider is registered it is reported as unavailable
/// (a warning) rather than failing start-up.
/// </para>
/// <para>
/// Visibility (BRIEF §12): a source with <see cref="LogSourceDefinition.AllowedUserGroups"/> is
/// visible to members of those groups; a sensitive source with no groups listed is visible to the
/// <c>admin</c> group only; any other source is visible to everyone with Settings access.
/// </para>
/// <para>
/// A source configured with <c>AllowNativeQuery: false</c> is wrapped in
/// <see cref="NativeQueryDisabledSource"/>, so every caller gets the restriction.
/// </para>
/// </remarks>
internal sealed partial class LogSourceRegistry : ILogSourceRegistry
{
    /// <summary>The source used when none is configured (BRIEF §13).</summary>
    internal static readonly LogSourceDefinition DefaultFilesSource = new()
    {
        Alias = "files",
        Type = "UmbracoFiles",
        DisplayName = "This server's log files",
    };

    private const string AdminGroupAlias = "admin";

    private readonly Lazy<IReadOnlyList<RegisteredSource>> _sources;
    private readonly ILogger<LogSourceRegistry> _logger;

    /// <summary>Creates the registry; sources are built on first use.</summary>
    /// <param name="options">The bound <c>LogExplorer</c> options.</param>
    /// <param name="factories">Every registered provider type.</param>
    /// <param name="services">Passed to factories to resolve provider dependencies.</param>
    /// <param name="logger">Receives one error per invalid source.</param>
    public LogSourceRegistry(
        IOptions<LogExplorerOptions> options,
        IEnumerable<ILogSourceFactory> factories,
        IServiceProvider services,
        ILogger<LogSourceRegistry> logger
    )
    {
        _logger = logger;
        _sources = new Lazy<IReadOnlyList<RegisteredSource>>(() =>
            Build(options.Value.Sources, factories, services)
        );
    }

    /// <inheritdoc />
    public IReadOnlyList<ILogSource> GetVisibleSources(UserContext user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return
        [
            .. _sources
                .Value.Where(source => IsVisible(source.Definition, user))
                .Select(source => source.Source),
        ];
    }

    /// <inheritdoc />
    public ILogSource GetForUser(string alias, UserContext user)
    {
        ArgumentNullException.ThrowIfNull(alias);
        ArgumentNullException.ThrowIfNull(user);

        RegisteredSource? match = _sources.Value.FirstOrDefault(source =>
            string.Equals(source.Definition.Alias, alias, StringComparison.OrdinalIgnoreCase)
        );
        if (match is null)
        {
            throw new KeyNotFoundException($"No log source has the alias '{alias}'.");
        }

        if (!IsVisible(match.Definition, user))
        {
            throw new ForbiddenSourceException(
                $"The log source '{alias}' is not available to this user."
            )
            {
                SourceAlias = match.Definition.Alias,
            };
        }

        return match.Source;
    }

    /// <summary>Applies the BRIEF §12 visibility rule to one source.</summary>
    /// <param name="definition">The source's configuration.</param>
    /// <param name="user">The user asking.</param>
    /// <returns>True when the user may see the source.</returns>
    internal static bool IsVisible(LogSourceDefinition definition, UserContext user)
    {
        IReadOnlyList<string> groups =
            definition.AllowedUserGroups.Count > 0 ? definition.AllowedUserGroups
            : definition.Sensitive ? [AdminGroupAlias]
            : [];

        // No group restriction: everyone who reached the API (it already requires Settings access).
        if (groups.Count == 0)
        {
            return true;
        }

        return groups.Any(group =>
            user.GroupAliases.Contains(group, StringComparer.OrdinalIgnoreCase)
        );
    }

    private List<RegisteredSource> Build(
        IList<LogSourceDefinition> configured,
        IEnumerable<ILogSourceFactory> factories,
        IServiceProvider services
    )
    {
        Dictionary<string, ILogSourceFactory> factoriesByType = new(
            StringComparer.OrdinalIgnoreCase
        );
        foreach (ILogSourceFactory factory in factories)
        {
            factoriesByType.TryAdd(factory.Type, factory);
        }

        bool usingDefault = configured.Count == 0;
        IEnumerable<LogSourceDefinition> definitions = usingDefault
            ? [DefaultFilesSource]
            : configured;

        List<RegisteredSource> sources = [];
        HashSet<string> aliases = new(StringComparer.OrdinalIgnoreCase);
        int position = 0;

        foreach (LogSourceDefinition definition in definitions)
        {
            position++;

            // Each check skips only this entry, after logging why.
            if (string.IsNullOrWhiteSpace(definition.Alias))
            {
                LogMissingAlias(position);
                continue;
            }

            if (!aliases.Add(definition.Alias))
            {
                LogDuplicateAlias(definition.Alias, position);
                continue;
            }

            if (!factoriesByType.TryGetValue(definition.Type, out ILogSourceFactory? factory))
            {
                if (usingDefault)
                {
                    // The zero-configuration files source before its provider ships (P1-07).
                    LogDefaultUnavailable(definition.Alias, definition.Type);
                }
                else
                {
                    LogUnknownType(
                        definition.Alias,
                        definition.Type,
                        string.Join(", ", factoriesByType.Keys)
                    );
                }

                continue;
            }

            try
            {
                ILogSource source = factory.Create(definition, services);
                sources.Add(
                    new RegisteredSource(
                        definition,
                        definition.AllowNativeQuery ? source : new NativeQueryDisabledSource(source)
                    )
                );
            }
            catch (ArgumentException ex)
            {
                LogInvalidSettings(ex, definition.Alias, definition.Type);
            }
        }

        return sources;
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Log Explorer source #{Position} has no Alias and was skipped."
    )]
    private partial void LogMissingAlias(int position);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Log Explorer source #{Position} reuses the alias '{Alias}' and was skipped; aliases must be unique."
    )]
    private partial void LogDuplicateAlias(string alias, int position);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Log Explorer source '{Alias}' has unknown Type '{Type}' and was skipped. Registered types: {KnownTypes}."
    )]
    private partial void LogUnknownType(string alias, string type, string knownTypes);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Log Explorer source '{Alias}' ({Type}) has missing or invalid settings and was skipped."
    )]
    private partial void LogInvalidSettings(Exception exception, string alias, string type);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The default Log Explorer source '{Alias}' is unavailable: no provider of type '{Type}' is registered."
    )]
    private partial void LogDefaultUnavailable(string alias, string type);

    /// <summary>A built source and the configuration it came from.</summary>
    /// <param name="Definition">The configuration entry, kept for visibility checks.</param>
    /// <param name="Source">The source the factory created.</param>
    private sealed record RegisteredSource(LogSourceDefinition Definition, ILogSource Source);
}
