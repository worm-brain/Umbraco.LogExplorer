using System.Globalization;
using System.Text.Json;
using Umbraco.Community.LogExplorer.Core.Records;
using Umbraco.Community.LogExplorer.Core.Severity;

namespace Umbraco.Community.LogExplorer.Core.Filtering;

/// <summary>
/// Reads a field path from a <see cref="LogRecord"/> (BRIEF §9.1). Portable <c>@</c> fields map to
/// record members; <c>@resource.&lt;path&gt;</c> reads <see cref="LogRecord.Resource"/>; any other
/// path reads <see cref="LogRecord.Attributes"/>.
/// <para>
/// Paths use dot segments into nested objects (<c>Cart.Total</c>). A segment ending in <c>[]</c>
/// expands an array into its elements (<c>Tags[]</c>, <c>Lines[].Sku</c>), which is why a path can
/// resolve to several values. A key that itself contains dots (<c>host.name</c>) is matched whole
/// before the path is split. Keys match exactly first, then ignoring case. JSON nulls count as
/// absent.
/// </para>
/// </summary>
public static class LogFields
{
    /// <summary>The entry's time, as an ISO 8601 string.</summary>
    public const string Timestamp = "@timestamp";

    /// <summary>The entry's level as a lower-case OTel short name (0 reads as <c>info</c>).</summary>
    public const string Severity = "@severity";

    /// <summary><see cref="LogRecord.Body"/>.</summary>
    public const string Body = "@body";

    /// <summary><see cref="LogRecord.MessageTemplate"/>.</summary>
    public const string Template = "@template";

    /// <summary><see cref="LogRecord.Scope"/>.</summary>
    public const string Scope = "@scope";

    /// <summary><see cref="LogRecord.TraceId"/>.</summary>
    public const string TraceId = "@traceId";

    /// <summary><see cref="LogRecord.SpanId"/>.</summary>
    public const string SpanId = "@spanId";

    /// <summary>The exception's type name.</summary>
    public const string ExceptionType = "@exception.type";

    /// <summary>The exception's message.</summary>
    public const string ExceptionMessage = "@exception.message";

    /// <summary>Prefix for paths into <see cref="LogRecord.Resource"/>.</summary>
    public const string ResourcePrefix = "@resource.";

    /// <summary>Every portable field other than resource paths, in display order.</summary>
    public static IReadOnlyList<string> Portable { get; } =
    [Timestamp, Severity, Body, Template, Scope, TraceId, SpanId, ExceptionType, ExceptionMessage];

    /// <summary>Resolves <paramref name="field"/> against <paramref name="record"/>.</summary>
    /// <param name="record">The record to read.</param>
    /// <param name="field">A portable field or attribute path.</param>
    /// <returns>
    /// The non-null values found: none when the field is absent, several when the path crosses an
    /// array with <c>[]</c>.
    /// </returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static IReadOnlyList<JsonElement> Resolve(LogRecord record, string field)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(field);

        if (field.StartsWith('@'))
        {
            if (field.StartsWith(ResourcePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return ResolvePath(record.Resource, field[ResourcePrefix.Length..]);
            }

            if (TryGetPortable(record, field, out string? text))
            {
                return text is null ? [] : [JsonSerializer.SerializeToElement(text)];
            }
        }

        return ResolvePath(record.Attributes, field);
    }

    private static bool TryGetPortable(LogRecord record, string field, out string? text)
    {
        text = field.ToUpperInvariant() switch
        {
            "@TIMESTAMP" => record.Timestamp.ToString("O", CultureInfo.InvariantCulture),
            "@SEVERITY" => SeverityMap.ToShortName(record.SeverityNumber),
            "@BODY" => record.Body,
            "@TEMPLATE" => record.MessageTemplate,
            "@SCOPE" => record.Scope,
            "@TRACEID" => record.TraceId,
            "@SPANID" => record.SpanId,
            "@EXCEPTION.TYPE" => record.Exception?.Type,
            "@EXCEPTION.MESSAGE" => record.Exception?.Message,
            _ => null,
        };

        return text is not null || Portable.Contains(field, StringComparer.OrdinalIgnoreCase);
    }

    private static List<JsonElement> ResolvePath(
        IReadOnlyDictionary<string, JsonElement> root,
        string path
    )
    {
        var results = new List<JsonElement>();

        // A dotted key such as "host.name" is a single attribute, not a path.
        if (TryGetKey(root, path, out JsonElement whole))
        {
            AddIfPresent(results, whole);
            return results;
        }

        string[] segments = path.Split('.');
        (string firstName, bool firstExpands) = ParseSegment(segments[0]);
        if (!TryGetKey(root, firstName, out JsonElement first))
        {
            return results;
        }

        List<JsonElement> current = Expand([first], firstExpands);
        foreach (string segment in segments.Skip(1))
        {
            (string name, bool expands) = ParseSegment(segment);
            var next = new List<JsonElement>();
            foreach (JsonElement element in current)
            {
                if (TryGetProperty(element, name, out JsonElement child))
                {
                    next.Add(child);
                }
            }

            current = Expand(next, expands);
        }

        foreach (JsonElement element in current)
        {
            AddIfPresent(results, element);
        }

        return results;
    }

    private static (string Name, bool Expands) ParseSegment(string segment) =>
        segment.EndsWith("[]", StringComparison.Ordinal) ? (segment[..^2], true) : (segment, false);

    private static List<JsonElement> Expand(List<JsonElement> elements, bool expands) =>
        expands
            ? elements
                .Where(element => element.ValueKind == JsonValueKind.Array)
                .SelectMany(element => element.EnumerateArray())
                .ToList()
            : elements;

    private static void AddIfPresent(List<JsonElement> results, JsonElement element)
    {
        if (element.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            results.Add(element);
        }
    }

    private static bool TryGetKey(
        IReadOnlyDictionary<string, JsonElement> root,
        string key,
        out JsonElement value
    )
    {
        if (root.TryGetValue(key, out value))
        {
            return true;
        }

        foreach (KeyValuePair<string, JsonElement> pair in root)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (element.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        return false;
    }
}
