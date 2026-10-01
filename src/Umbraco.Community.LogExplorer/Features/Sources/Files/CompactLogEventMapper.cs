using System.Buffers;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Serilog.Events;
using Umbraco.Community.LogExplorer.Core.Records;
using Umbraco.Community.LogExplorer.Core.Severity;

namespace Umbraco.Community.LogExplorer.Features.Sources.Files;

/// <summary>
/// Maps an event read from an Umbraco compact JSON log file to a <see cref="LogRecord"/>, per the
/// Umbraco files column of BRIEF §9.1.
/// </summary>
/// <remarks>
/// <para>
/// Properties keep their JSON kind in <see cref="LogRecord.Attributes"/>. A structured object's
/// Serilog type tag is kept as a <c>$type</c> member, as the compact formatter wrote it, so the
/// attribute reads exactly like the stored JSON. <c>SourceContext</c> and <c>MachineName</c> stay
/// in the attributes as well as feeding <see cref="LogRecord.Scope"/> and <c>host.name</c>,
/// because both are pinned facets.
/// </para>
/// <para>
/// The body is the message rendered the way the core Log Viewer renders it (strings quoted,
/// <c>@r</c> renderings applied), with the invariant culture so it does not depend on the
/// server's locale.
/// </para>
/// </remarks>
internal static class CompactLogEventMapper
{
    /// <summary>The resource attribute holding the machine that wrote the event.</summary>
    public const string HostNameResource = "host.name";

    private const string SourceContextProperty = "SourceContext";
    private const string MachineNameProperty = "MachineName";
    private const string TypeTagMember = "$type";

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    // Where the frames begin in Exception.ToString() output: the first line break followed by an
    // "at" frame or an "--- End of ..." marker. Everything before it is the "Type: message" header,
    // which may itself span lines.
    private static readonly Regex StackStart = new(
        @"\r?\n(?=[ \t]*(?:at |--- End of ))",
        RegexOptions.CultureInvariant,
        MatchTimeout
    );

    // "Namespace.Type: message", optionally with an HRESULT ("SqliteException (0x80004005): ...").
    // Requiring a dotted name stops an ordinary "Something: happened" message reading as a type.
    private static readonly Regex Header = new(
        @"^(?<type>[\w`+\[\]]+(?:\.[\w`+\[\]]+)+)(?: \(0x[0-9A-Fa-f]+\))?(?::[ ]?(?<message>[\s\S]*))?$",
        RegexOptions.CultureInvariant,
        MatchTimeout
    );

    /// <summary>Maps one event.</summary>
    /// <param name="logEvent">The event as <c>LogEventReader</c> parsed it.</param>
    /// <param name="file">The file the event came from.</param>
    /// <param name="offset">Byte offset of the event's line in <paramref name="file"/>.</param>
    /// <returns>
    /// The record, with <see cref="LogRecord.Id"/> from <see cref="FilePosition.ToRecordId"/>.
    /// <see cref="LogRecord.SourceAlias"/> is left for the source to set.
    /// </returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static LogRecord Map(LogEvent logEvent, LogFile file, long offset) =>
        Map(logEvent, file, offset, RecordParts.All);

    /// <summary>
    /// Maps one event, leaving out the parts not asked for: their members keep their defaults
    /// (null, or empty dictionaries). The parts that are mapped are exactly what
    /// <see cref="Map(LogEvent, LogFile, long)"/> gives, so a filter that reads only them matches
    /// the partial record as it matches the whole one (ADR 0017).
    /// </summary>
    /// <param name="logEvent">The event as <c>LogEventReader</c> parsed it.</param>
    /// <param name="file">The file the event came from.</param>
    /// <param name="offset">Byte offset of the event's line in <paramref name="file"/>.</param>
    /// <param name="parts">The costly parts to map; the cheap members are always mapped.</param>
    /// <returns>The record, complete when <paramref name="parts"/> is <see cref="RecordParts.All"/>.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static LogRecord Map(LogEvent logEvent, LogFile file, long offset, RecordParts parts)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(file);

        // Compact JSON omits @l for Information, which the reader already turns into that level.
        string level = logEvent.Level.ToString();
        string? template = parts.HasFlag(RecordParts.Template)
            ? logEvent.MessageTemplate.Text
            : null;
        string? machineName = parts.HasFlag(RecordParts.Resource)
            ? GetString(logEvent, MachineNameProperty) ?? file.MachineName
            : null;

        return new LogRecord
        {
            Id = new FilePosition(file.FileName, offset).ToRecordId(),
            Timestamp = logEvent.Timestamp,
            SeverityNumber = SeverityMap.FromSerilog(level),
            SeverityText = level,
            Body = parts.HasFlag(RecordParts.Body)
                ? logEvent.RenderMessage(CultureInfo.InvariantCulture)
                : null,
            MessageTemplate = template,
            TemplateHash = template is null ? null : TemplateHash.Compute(template),
            TraceId = logEvent.TraceId?.ToHexString(),
            SpanId = logEvent.SpanId?.ToHexString(),
            Scope = GetString(logEvent, SourceContextProperty),
            Exception =
                parts.HasFlag(RecordParts.Exception) && logEvent.Exception is { } exception
                    ? ParseException(exception.ToString())
                    : null,
            Attributes = parts.HasFlag(RecordParts.Attributes)
                ? ToAttributes(logEvent.Properties)
                : new Dictionary<string, JsonElement>(),
            Resource = machineName is null
                ? new Dictionary<string, JsonElement>()
                : new Dictionary<string, JsonElement>
                {
                    [HostNameResource] = JsonSerializer.SerializeToElement(machineName),
                },
        };
    }

    /// <summary>
    /// Splits <c>Exception.ToString()</c> text (all <c>@x</c> holds) into type, message and stack.
    /// For a wrapped exception the message is the outer one, and the stack trace starts with the
    /// <c>---&gt; Inner: message</c> part so nothing is lost. Text that does not start with a
    /// dotted type name becomes the message, with no type.
    /// </summary>
    private static LogException ParseException(string text)
    {
        Match stackStart = StackStart.Match(text);
        string header = stackStart.Success ? text[..stackStart.Index] : text;
        string? stackTrace = stackStart.Success
            ? text[(stackStart.Index + stackStart.Length)..]
            : null;

        int inner = header.IndexOf(" ---> ", StringComparison.Ordinal);
        if (inner >= 0)
        {
            string innerPart = header[(inner + 1)..];
            stackTrace = stackStart.Success ? innerPart + text[stackStart.Index..] : innerPart;
            header = header[..inner];
        }

        Match match = Header.Match(header);
        if (!match.Success)
        {
            return new LogException(null, header, stackTrace);
        }

        Group message = match.Groups["message"];
        return new LogException(
            match.Groups["type"].Value,
            message.Success ? message.Value : null,
            stackTrace
        );
    }

    private static string? GetString(LogEvent logEvent, string property) =>
        logEvent.Properties.TryGetValue(property, out LogEventPropertyValue? value)
        && value is ScalarValue { Value: string text }
            ? text
            : null;

    private static Dictionary<string, JsonElement> ToAttributes(
        IReadOnlyDictionary<string, LogEventPropertyValue> properties
    )
    {
        // One JSON document for all properties, parsed once; each attribute is a member of it.
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (KeyValuePair<string, LogEventPropertyValue> property in properties)
            {
                writer.WritePropertyName(property.Key);
                WriteValue(writer, property.Value);
            }

            writer.WriteEndObject();
        }

        JsonElement root = JsonSerializer.Deserialize<JsonElement>(buffer.WrittenSpan);
        var attributes = new Dictionary<string, JsonElement>(
            properties.Count,
            StringComparer.Ordinal
        );
        foreach (JsonProperty member in root.EnumerateObject())
        {
            attributes[member.Name] = member.Value;
        }

        return attributes;
    }

    private static void WriteValue(Utf8JsonWriter writer, LogEventPropertyValue value)
    {
        switch (value)
        {
            case ScalarValue scalar:
                WriteScalar(writer, scalar.Value);
                break;

            case SequenceValue sequence:
                writer.WriteStartArray();
                foreach (LogEventPropertyValue element in sequence.Elements)
                {
                    WriteValue(writer, element);
                }

                writer.WriteEndArray();
                break;

            case StructureValue structure:
                writer.WriteStartObject();
                if (structure.TypeTag is not null)
                {
                    writer.WriteString(TypeTagMember, structure.TypeTag);
                }

                foreach (LogEventProperty member in structure.Properties)
                {
                    writer.WritePropertyName(member.Name);
                    WriteValue(writer, member.Value);
                }

                writer.WriteEndObject();
                break;

            // The reader turns JSON objects into structures; dictionaries only arrive from other
            // producers of LogEvent, so they are handled for completeness.
            case DictionaryValue dictionary:
                writer.WriteStartObject();
                foreach (
                    KeyValuePair<ScalarValue, LogEventPropertyValue> entry in dictionary.Elements
                )
                {
                    writer.WritePropertyName(
                        Convert.ToString(entry.Key.Value, CultureInfo.InvariantCulture) ?? ""
                    );
                    WriteValue(writer, entry.Value);
                }

                writer.WriteEndObject();
                break;

            default:
                writer.WriteStringValue(value.ToString(null, CultureInfo.InvariantCulture));
                break;
        }
    }

    // The reader produces string, bool, long, double, BigInteger and null; the other cases cover
    // events built in code. Non-finite doubles have no JSON number form, so they become strings.
    private static void WriteScalar(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string text:
                writer.WriteStringValue(text);
                break;
            case bool flag:
                writer.WriteBooleanValue(flag);
                break;
            case long number:
                writer.WriteNumberValue(number);
                break;
            case int number:
                writer.WriteNumberValue(number);
                break;
            case ulong number:
                writer.WriteNumberValue(number);
                break;
            case uint number:
                writer.WriteNumberValue(number);
                break;
            case short or ushort or byte or sbyte:
                writer.WriteNumberValue(Convert.ToInt64(value, CultureInfo.InvariantCulture));
                break;
            case double number when double.IsFinite(number):
                writer.WriteNumberValue(number);
                break;
            case float number when float.IsFinite(number):
                writer.WriteNumberValue(number);
                break;
            case decimal number:
                writer.WriteNumberValue(number);
                break;
            case BigInteger number:
                writer.WriteRawValue(number.ToString(CultureInfo.InvariantCulture));
                break;
            case DateTime moment:
                writer.WriteStringValue(moment);
                break;
            case DateTimeOffset moment:
                writer.WriteStringValue(moment);
                break;
            case Guid guid:
                writer.WriteStringValue(guid);
                break;
            case IFormattable formattable:
                writer.WriteStringValue(formattable.ToString(null, CultureInfo.InvariantCulture));
                break;
            default:
                writer.WriteStringValue(value.ToString());
                break;
        }
    }
}
