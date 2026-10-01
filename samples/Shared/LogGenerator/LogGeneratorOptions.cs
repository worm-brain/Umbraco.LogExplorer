namespace LogExplorer.Samples.LogGenerator;

/// <summary>
/// Settings for the sample-site log generator, bound from the <c>LogGenerator</c> configuration section.
/// Everything is off by default so a sample site only produces Umbraco's own logs unless asked.
/// </summary>
public sealed class LogGeneratorOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "LogGenerator";

    /// <summary>Gets or sets a value indicating whether the live generator runs at all.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the average number of simulated requests per second.</summary>
    public double RequestsPerSecond { get; set; } = 2;

    /// <summary>
    /// Gets or sets a value indicating whether to write the file-layout scenarios once at start-up:
    /// files under a second machine name, a file rolled past 1 MB (creating <c>_001</c>), and one
    /// truncated last line (BRIEF Appendix C).
    /// </summary>
    public bool WriteFileScenarios { get; set; }

    /// <summary>Gets or sets the machine name used for the second machine's files.</summary>
    public string SecondMachineName { get; set; } = "LOGEXPLORER-NODE2";

    /// <summary>
    /// Gets or sets how many gigabytes the bulk mode writes, once at start-up, for the files
    /// provider's performance check (#36); 0, the default, turns it off. About 2 is the brief's
    /// size. The set is skipped when the bulk machines' files already exist in the directory.
    /// </summary>
    public double BulkGigabytes { get; set; }

    /// <summary>
    /// Gets or sets where the bulk mode writes; null means the site's own log directory, so the
    /// site's files source reads the set.
    /// </summary>
    public string? BulkDirectory { get; set; }

    /// <summary>Gets or sets how many simulated days, ending now, the bulk set covers.</summary>
    public int BulkDays { get; set; } = 7;

    /// <summary>Gets or sets the size, in megabytes, at which a bulk file rolls to <c>_001</c> and on.</summary>
    public int BulkRollMegabytes { get; set; } = 100;
}
