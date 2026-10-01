using Microsoft.Extensions.Options;

namespace Umbraco.Community.LogExplorer.Features.Sources;

/// <summary>
/// Fills the BRIEF §13 list defaults into <see cref="LogExplorerOptions"/> after configuration has
/// been bound, but only for lists the configuration left empty. Doing it here rather than in
/// property initialisers stops the binder appending configured values to the defaults.
/// </summary>
internal sealed class LogExplorerOptionsDefaults : IPostConfigureOptions<LogExplorerOptions>
{
    /// <summary>The default "Same request" correlation fields.</summary>
    public static readonly string[] CorrelationFields = ["@traceId", "RequestId", "HttpRequestId"];

    /// <summary>The default pinned facets.</summary>
    public static readonly string[] PinnedFacets =
    [
        "SourceContext",
        "RequestPath",
        "StatusCode",
        "MachineName",
        "@exception.type",
    ];

    /// <summary>The default masked attribute-name globs.</summary>
    public static readonly string[] MaskedAttributes =
    [
        "*Password*",
        "*Secret*",
        "Authorization",
        "Cookie",
        "*Token*",
    ];

    /// <summary>Applies the defaults to any list the configuration left empty.</summary>
    /// <param name="name">The options name (unused; the options are not named).</param>
    /// <param name="options">The bound options.</param>
    public void PostConfigure(string? name, LogExplorerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.CorrelationFields.Count == 0)
        {
            options.CorrelationFields = [.. CorrelationFields];
        }

        if (options.PinnedFacets.Count == 0)
        {
            options.PinnedFacets = [.. PinnedFacets];
        }

        if (options.Masking.Attributes.Count == 0)
        {
            options.Masking.Attributes = [.. MaskedAttributes];
        }

        if (options.DeepLinks.Count == 0)
        {
            options.DeepLinks = new Dictionary<string, IList<string>>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["document"] = ["ContentId", "NodeId", "DocumentKey"],
                ["media"] = ["MediaKey"],
                ["user"] = ["UserKey", "UserId"],
            };
        }
    }
}
