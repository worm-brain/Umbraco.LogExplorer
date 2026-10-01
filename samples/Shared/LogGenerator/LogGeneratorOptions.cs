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
}
