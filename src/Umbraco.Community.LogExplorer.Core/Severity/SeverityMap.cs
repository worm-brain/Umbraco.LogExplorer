namespace Umbraco.Community.LogExplorer.Core.Severity;

/// <summary>
/// Converts levels between OpenTelemetry severity numbers, the six OTel short names, Serilog,
/// Microsoft.Extensions.Logging and Application Insights (BRIEF §9.2).
/// <para>
/// Internally everything is an OTel number. Short names are lower case on the wire
/// (<c>LogQuery.Levels</c>, histogram keys) and parsed case-insensitively; the UI upper-cases them
/// for display. Severity 0 is "unspecified": it counts as INFO for filtering and counting, and
/// displays as <see cref="UnspecifiedDisplayText"/>. Any unrecognised source level maps to 0.
/// </para>
/// </summary>
public static class SeverityMap
{
    /// <summary>Short name for OTel severities 1 to 4.</summary>
    public const string Trace = "trace";

    /// <summary>Short name for OTel severities 5 to 8.</summary>
    public const string Debug = "debug";

    /// <summary>Short name for OTel severities 9 to 12, and for unspecified (0).</summary>
    public const string Info = "info";

    /// <summary>Short name for OTel severities 13 to 16.</summary>
    public const string Warn = "warn";

    /// <summary>Short name for OTel severities 17 to 20.</summary>
    public const string Error = "error";

    /// <summary>Short name for OTel severities 21 to 24.</summary>
    public const string Fatal = "fatal";

    /// <summary>How an unspecified severity (0) is displayed.</summary>
    public const string UnspecifiedDisplayText = "—";

    /// <summary>OTel severity number meaning "unspecified".</summary>
    public const int Unspecified = 0;

    /// <summary>The six short names, least to most severe.</summary>
    public static IReadOnlyList<string> ShortNames { get; } =
    [Trace, Debug, Info, Warn, Error, Fatal];

    // Serilog, MEL and the OTel short names line up one-to-one with the six bands, so each
    // vocabulary is an array indexed by band (0 = TRACE ... 5 = FATAL).
    private static readonly string[] SerilogNames =
    [
        "Verbose",
        "Debug",
        "Information",
        "Warning",
        "Error",
        "Fatal",
    ];

    private static readonly string[] MicrosoftNames =
    [
        "Trace",
        "Debug",
        "Information",
        "Warning",
        "Error",
        "Critical",
    ];

    // App Insights has no DEBUG: both TRACE and DEBUG are SeverityLevel 0 (Verbose), which maps
    // back to TRACE.
    private static readonly int[] AppInsightsLevels = [0, 0, 1, 2, 3, 4];

    /// <summary>The short name a severity number filters as; 0 and out-of-range numbers are <see cref="Info"/>.</summary>
    /// <param name="severityNumber">An OTel severity number.</param>
    /// <returns>One of <see cref="ShortNames"/>.</returns>
    public static string ToShortName(int severityNumber) => ShortNames[BandIndex(severityNumber)];

    /// <summary>
    /// The text a severity is shown as: the upper-case short name, or
    /// <see cref="UnspecifiedDisplayText"/> for 0 or an out-of-range number.
    /// </summary>
    /// <param name="severityNumber">An OTel severity number.</param>
    /// <returns>For example <c>WARN</c>.</returns>
    public static string ToDisplayText(int severityNumber) =>
        IsSpecified(severityNumber)
            ? ShortNames[BandIndex(severityNumber)].ToUpperInvariant()
            : UnspecifiedDisplayText;

    /// <summary>The lowest severity number of a short name's band, for example 13 for <c>warn</c>.</summary>
    /// <param name="shortName">A short name, any case.</param>
    /// <returns>The band's lowest number, or 0 when the name is unknown.</returns>
    public static int FromShortName(string? shortName) =>
        IndexOf(ShortNames, shortName) is { } index ? (index * 4) + 1 : Unspecified;

    /// <summary>The severity numbers a short name covers.</summary>
    /// <param name="shortName">A short name, any case.</param>
    /// <returns>For example 13 to 16 for <c>warn</c>.</returns>
    /// <exception cref="ArgumentException">The name is not one of <see cref="ShortNames"/>.</exception>
    public static SeverityBand GetBand(string shortName)
    {
        int min = FromShortName(shortName);
        if (min == Unspecified)
        {
            throw new ArgumentException(
                $"Unknown level '{shortName}'. Expected one of: {string.Join(", ", ShortNames)}.",
                nameof(shortName)
            );
        }

        return new SeverityBand(min, min + 3);
    }

    /// <summary>
    /// The bands a level set covers, least severe first, so providers can compile the set
    /// (ADR 0004). Unknown names are ignored.
    /// </summary>
    /// <param name="levels">Short names, any case.</param>
    /// <returns>One band per known name, without duplicates.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="levels"/> is null.</exception>
    public static IReadOnlyList<SeverityBand> ToBands(IEnumerable<string> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        return levels
            .Select(FromShortName)
            .Where(min => min != Unspecified)
            .Distinct()
            .Order()
            .Select(min => new SeverityBand(min, min + 3))
            .ToArray();
    }

    /// <summary>
    /// Whether a severity passes a level filter. Null, or a set naming all six levels, is no
    /// filter; severity 0 is tested as INFO.
    /// </summary>
    /// <param name="severityNumber">An OTel severity number.</param>
    /// <param name="levels">The query's level set.</param>
    /// <returns>True when the entry should be kept.</returns>
    public static bool IsInLevels(int severityNumber, IReadOnlySet<string>? levels)
    {
        if (levels is null)
        {
            return true;
        }

        string shortName = ToShortName(severityNumber);
        return levels.Any(level =>
            string.Equals(level, shortName, StringComparison.OrdinalIgnoreCase)
        );
    }

    /// <summary>Maps a Serilog level name (<c>Verbose</c> ... <c>Fatal</c>) to an OTel number.</summary>
    /// <param name="level">Serilog level name, any case.</param>
    /// <returns>The band's lowest number, or 0 when unknown or null.</returns>
    public static int FromSerilog(string? level) =>
        IndexOf(SerilogNames, level) is { } index ? (index * 4) + 1 : Unspecified;

    /// <summary>Maps an OTel number to a Serilog level name; 0 maps to <c>Information</c>.</summary>
    /// <param name="severityNumber">An OTel severity number.</param>
    /// <returns>For example <c>Warning</c>.</returns>
    public static string ToSerilog(int severityNumber) => SerilogNames[BandIndex(severityNumber)];

    /// <summary>
    /// Maps a Microsoft.Extensions.Logging level name (<c>Trace</c> ... <c>Critical</c>) to an OTel
    /// number. <c>None</c> is not a level and maps to 0.
    /// </summary>
    /// <param name="level">MEL <c>LogLevel</c> name, any case.</param>
    /// <returns>The band's lowest number, or 0 when unknown or null.</returns>
    public static int FromMicrosoftLogLevel(string? level) =>
        IndexOf(MicrosoftNames, level) is { } index ? (index * 4) + 1 : Unspecified;

    /// <summary>Maps an OTel number to a MEL level name; 0 maps to <c>Information</c>.</summary>
    /// <param name="severityNumber">An OTel severity number.</param>
    /// <returns>For example <c>Critical</c>.</returns>
    public static string ToMicrosoftLogLevel(int severityNumber) =>
        MicrosoftNames[BandIndex(severityNumber)];

    /// <summary>
    /// Maps an Application Insights <c>SeverityLevel</c> (0 Verbose ... 4 Critical) to an OTel
    /// number. Verbose becomes TRACE.
    /// </summary>
    /// <param name="severityLevel">App Insights severity level.</param>
    /// <returns>The band's lowest number, or 0 when outside 0 to 4.</returns>
    public static int FromAppInsights(int severityLevel) =>
        severityLevel switch
        {
            0 => 1,
            >= 1 and <= 4 => (severityLevel + 1) * 4 + 1,
            _ => Unspecified,
        };

    /// <summary>
    /// Maps an OTel number to an Application Insights <c>SeverityLevel</c>; TRACE and DEBUG both
    /// become 0, and 0 becomes 1 (Information).
    /// </summary>
    /// <param name="severityNumber">An OTel severity number.</param>
    /// <returns>0 to 4.</returns>
    public static int ToAppInsights(int severityNumber) =>
        AppInsightsLevels[BandIndex(severityNumber)];

    private static bool IsSpecified(int severityNumber) => severityNumber is >= 1 and <= 24;

    // Band 0..5 for TRACE..FATAL; unspecified and out-of-range numbers fall in INFO (band 2).
    private static int BandIndex(int severityNumber) =>
        IsSpecified(severityNumber) ? (severityNumber - 1) / 4 : 2;

    private static int? IndexOf(IReadOnlyList<string> names, string? value)
    {
        for (int i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i], value, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return null;
    }
}
