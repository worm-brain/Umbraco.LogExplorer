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

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            AllowOutOfOrderMetadataProperties = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new ReadOnlyStringSetConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
