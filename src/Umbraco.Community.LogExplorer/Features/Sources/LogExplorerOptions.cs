using Umbraco.Community.LogExplorer.Core.Sources;

namespace Umbraco.Community.LogExplorer.Features.Sources;

/// <summary>
/// The <c>LogExplorer</c> configuration section (BRIEF §13). Scalar defaults are set here; list
/// defaults (<see cref="CorrelationFields"/>, <see cref="PinnedFacets"/>, masking globs, deep
/// links) are filled in after binding by <see cref="LogExplorerOptionsDefaults"/>, because the
/// configuration binder appends to a pre-filled list instead of replacing it.
/// </summary>
public sealed class LogExplorerOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "LogExplorer";

    /// <summary>Gets or sets a value indicating whether to hide the core Settings &gt; Log Viewer item (ADR 0007).</summary>
    public bool HideCoreLogViewer { get; set; } = true;

    /// <summary>Gets or sets the alias of the source the explorer opens with.</summary>
    public string DefaultSource { get; set; } = "files";

    /// <summary>Gets or sets the relative time range the explorer opens with.</summary>
    public string DefaultTimeRange { get; set; } = "1h";

    /// <summary>Gets or sets the fields "Same request" tries, in order (BRIEF §10.1).</summary>
    public IList<string> CorrelationFields { get; set; } = [];

    /// <summary>Gets or sets the fields always shown first in the fields panel (BRIEF §6.6).</summary>
    public IList<string> PinnedFacets { get; set; } = [];

    /// <summary>Gets or sets property names per CMS entity type, rendered as editor links (BRIEF §6.12).</summary>
    public IDictionary<string, IList<string>> DeepLinks { get; set; } =
        new Dictionary<string, IList<string>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets export settings.</summary>
    public ExportOptions Export { get; set; } = new();

    /// <summary>Gets or sets masking rules (BRIEF §12).</summary>
    public MaskingOptions Masking { get; set; } = new();

    /// <summary>Gets or sets settings for the Umbraco files provider.</summary>
    public FilesOptions Files { get; set; } = new();

    /// <summary>
    /// Gets or sets the configured sources. When empty, a single <c>files</c> source of type
    /// <c>UmbracoFiles</c> is used so the package works with zero configuration.
    /// </summary>
    public IList<LogSourceDefinition> Sources { get; set; } = [];

    /// <summary>Export settings (BRIEF §6.14).</summary>
    public sealed class ExportOptions
    {
        /// <summary>Gets or sets the most rows one export returns.</summary>
        public int MaxRows { get; set; } = 50_000;
    }

    /// <summary>Masking settings (BRIEF §12).</summary>
    public sealed class MaskingOptions
    {
        /// <summary>Gets or sets attribute-name globs whose values are replaced.</summary>
        public IList<string> Attributes { get; set; } = [];

        /// <summary>Gets or sets a value indicating whether e-mail addresses are redacted in text.</summary>
        public bool RedactEmails { get; set; } = true;

        /// <summary>Gets or sets extra regular expressions redacted in text.</summary>
        public IList<string> Patterns { get; set; } = [];
    }

    /// <summary>Settings for the Umbraco files provider (BRIEF §10.1).</summary>
    public sealed class FilesOptions
    {
        /// <summary>Gets or sets the most data one aggregation scans before reporting Approximate.</summary>
        public int ScanBudgetMegabytes { get; set; } = 256;

        /// <summary>Gets or sets how often live tail polls the files.</summary>
        public int TailPollSeconds { get; set; } = 2;
    }
}
