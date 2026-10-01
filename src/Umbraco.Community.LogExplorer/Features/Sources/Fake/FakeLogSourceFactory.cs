using System.Globalization;
using Umbraco.Community.LogExplorer.Core.Fake;
using Umbraco.Community.LogExplorer.Core.Sources;

namespace Umbraco.Community.LogExplorer.Features.Sources.Fake;

/// <summary>
/// The <c>Fake</c> provider type: in-memory sample data (UI brief §12) for UI work and tests,
/// configured like any other source, for example
/// <c>{ "Alias": "sample", "Type": "Fake", "DisplayName": "Sample data" }</c>.
/// </summary>
/// <remarks>
/// The sources it creates regenerate their sample hour every 30 minutes (see
/// <see cref="RefreshingLogSource"/>), so a long-running dev site keeps data inside "last 1 hour".
/// </remarks>
internal sealed class FakeLogSourceFactory(TimeProvider clock) : ILogSourceFactory
{
    /// <summary>How often the sample data is regenerated against the current time.</summary>
    internal static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(30);

    /// <inheritdoc />
    public string Type => FakeLogSource.SourceType;

    /// <inheritdoc />
    public ILogSource Create(LogSourceDefinition definition, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var options = new FakeLogSourceOptions
        {
            Alias = definition.Alias,
            DisplayName = string.IsNullOrWhiteSpace(definition.DisplayName)
                ? definition.Alias
                : definition.DisplayName,
            Sensitive = definition.Sensitive,
            SampleHours = SampleHours(definition),
        };

        return new RefreshingLogSource(
            () => new FakeLogSource(options, clock),
            clock,
            RefreshInterval
        );
    }

    // Optional "SampleHours" setting (a whole number, at least 1) for volume testing; the registry
    // reports a bad value as a configuration error for this source only.
    private static int SampleHours(LogSourceDefinition definition)
    {
        if (!definition.Settings.TryGetValue("SampleHours", out string? value))
        {
            return 1;
        }

        return
            int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int hours)
            && hours >= 1
            ? hours
            : throw new ArgumentException(
                $"Setting 'SampleHours' of source '{definition.Alias}' must be a whole number of at least 1, not '{value}'.",
                nameof(definition)
            );
    }
}
