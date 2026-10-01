using System.Collections.ObjectModel;
using System.Text.Json;

namespace Umbraco.Community.LogExplorer.Core.Records;

/// <summary>
/// One log entry in the shape of the OpenTelemetry Logs Data Model (BRIEF §8.1). Every provider
/// maps its native rows to this record, so the UI and the filter evaluator see one shape.
/// </summary>
public sealed record LogRecord
{
    /// <summary>
    /// Provider-specific, opaque identifier. Only the source that produced it can interpret it;
    /// routes carry it base64url-encoded.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>When the entry was written.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// OpenTelemetry severity number, 1 to 24; 0 means unspecified and is treated as INFO when
    /// filtering (BRIEF §9.2).
    /// </summary>
    public int SeverityNumber { get; init; }

    /// <summary>The level text exactly as the source stored it, for example <c>Warning</c>.</summary>
    public string? SeverityText { get; init; }

    /// <summary>The rendered message.</summary>
    public string? Body { get; init; }

    /// <summary>
    /// The message template the body was rendered from (OTel attribute
    /// <c>message_template.text</c>); null when the source has no templates.
    /// </summary>
    public string? MessageTemplate { get; init; }

    /// <summary>Stable pattern identifier used to group entries by template (BRIEF §9.3).</summary>
    public string? TemplateHash { get; init; }

    /// <summary>W3C trace id, when the entry was written inside a trace.</summary>
    public string? TraceId { get; init; }

    /// <summary>W3C span id, when the entry was written inside a span.</summary>
    public string? SpanId { get; init; }

    /// <summary>The logger category or Serilog <c>SourceContext</c> (OTel instrumentation scope).</summary>
    public string? Scope { get; init; }

    /// <summary>The exception attached to the entry, if any.</summary>
    public LogException? Exception { get; init; }

    /// <summary>
    /// Structured properties keyed by name. Values keep their JSON kind (number, bool, string,
    /// object, array) rather than being flattened to strings, so filters can compare them typed.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> Attributes { get; init; } =
        ReadOnlyDictionary<string, JsonElement>.Empty;

    /// <summary>
    /// Attributes describing the emitter rather than the event (service, host, environment), for
    /// example <c>host.name</c>.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> Resource { get; init; } =
        ReadOnlyDictionary<string, JsonElement>.Empty;

    /// <summary>Alias of the configured source the entry came from; empty until a source sets it.</summary>
    public string SourceAlias { get; init; } = "";
}
