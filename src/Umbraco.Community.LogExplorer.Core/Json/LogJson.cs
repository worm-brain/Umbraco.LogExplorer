using System.Text.Json;
using System.Text.Json.Serialization;

namespace Umbraco.Community.LogExplorer.Core.Json;

/// <summary>
/// The one JSON configuration every Log Explorer contract is exchanged with: camelCase names,
/// enums as camelCase strings, and the <c>kind</c> discriminator on <c>FilterNode</c> (declared on
/// the type itself).
/// </summary>
public static class LogJson
{
    /// <summary>
    /// Shared, read-only serializer options. <c>kind</c> may appear anywhere in a filter node
    /// object, because hand-written or browser-built JSON does not reliably put it first.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    /// <summary>
    /// Applies the <see cref="Options"/> configuration to options owned by someone else, for
    /// example the ASP.NET Core <c>JsonOptions</c> the package API formats requests with.
    /// </summary>
    /// <param name="options">
    /// Options that are not yet read-only. Existing converters are kept, but the Log Explorer ones
    /// are inserted first so they win.
    /// </param>
    /// <exception cref="InvalidOperationException"><paramref name="options"/> is already read-only.</exception>
    public static void Apply(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // The JsonSerializerDefaults.Web settings, spelled out because they cannot be applied to
        // an existing instance.
        options.PropertyNameCaseInsensitive = true;
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.NumberHandling = JsonNumberHandling.AllowReadingFromString;

        options.AllowOutOfOrderMetadataProperties = true;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        options.Converters.Insert(0, new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.Converters.Insert(1, new ReadOnlyStringSetConverter());
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Apply(options);
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
