using System.Text.Json;
using System.Text.Json.Serialization;

namespace Umbraco.Community.LogExplorer.Core.Json;

/// <summary>
/// Reads and writes <see cref="IReadOnlySet{T}"/> of strings (for example <c>LogQuery.Levels</c>)
/// as a JSON array. System.Text.Json cannot instantiate that interface on its own. The set it
/// builds ignores case, because every string set in the contracts holds level names or aliases.
/// </summary>
internal sealed class ReadOnlyStringSetConverter : JsonConverter<IReadOnlySet<string>>
{
    /// <inheritdoc />
    public override IReadOnlySet<string>? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        string[] items = JsonSerializer.Deserialize<string[]>(ref reader, options) ?? [];
        return new HashSet<string>(items, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer,
        IReadOnlySet<string> value,
        JsonSerializerOptions options
    )
    {
        writer.WriteStartArray();
        foreach (string item in value)
        {
            writer.WriteStringValue(item);
        }

        writer.WriteEndArray();
    }
}
