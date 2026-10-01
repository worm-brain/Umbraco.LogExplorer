using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Umbraco.Cms.Core.Logging;

namespace Umbraco.Community.LogExplorer.Features.Sources.Files;

/// <summary>
/// Finds Umbraco's log files for every machine, using the directory and file name format from
/// Umbraco's logging configuration rather than a hard-coded pattern (BRIEF §10.1).
/// </summary>
/// <remarks>
/// <para>
/// Umbraco names the file sink's path <c>string.Format(LogFileNameFormat, arguments)</c>
/// (default <c>UmbracoTraceLog.{0}..json</c> with the machine name), and Serilog's daily rolling
/// inserts <c>yyyyMMdd</c>, then <c>_NNN</c> on a size roll, before the extension:
/// <c>UmbracoTraceLog.WORM.20261001.json</c>, <c>UmbracoTraceLog.WORM.20261001_001.json</c>.
/// </para>
/// <para>
/// <see cref="ILoggingConfiguration.GetLogFileNameFormatArguments"/> returns the resolved
/// argument values for this process (for example <c>WORM</c>), not their names
/// (<c>MachineName</c>, <c>EnvironmentName</c>); verified for 17.7 and 18.2. So the placeholder
/// whose value equals this machine's name is treated as the machine name and matches any machine,
/// which picks up other servers' files on shared storage. Other placeholders (the environment
/// name) must match their value, so only this environment's files are read. A placeholder with no
/// argument matches anything.
/// </para>
/// </remarks>
internal sealed class UmbracoLogFileLocator
{
    private const string MachineGroup = "machine";
    private const string DateGroup = "date";
    private const string RollGroup = "roll";

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private readonly ILoggingConfiguration _configuration;
    private readonly Regex _fileNamePattern;

    /// <summary>Creates a locator for this process's logging configuration and machine.</summary>
    /// <param name="configuration">Umbraco's logging configuration.</param>
    public UmbracoLogFileLocator(ILoggingConfiguration configuration)
        : this(configuration, Environment.MachineName) { }

    /// <summary>Creates a locator that treats <paramref name="currentMachineName"/> as this machine.</summary>
    /// <param name="configuration">Umbraco's logging configuration.</param>
    /// <param name="currentMachineName">
    /// The value Umbraco resolved the <c>MachineName</c> argument to; it identifies which
    /// placeholder is the machine name.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    internal UmbracoLogFileLocator(ILoggingConfiguration configuration, string currentMachineName)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(currentMachineName);

        _configuration = configuration;
        _fileNamePattern = BuildFileNamePattern(
            configuration.LogFileNameFormat,
            configuration.GetLogFileNameFormatArguments(),
            currentMachineName
        );
    }

    /// <summary>Lists the log files in the configured directory.</summary>
    /// <returns>
    /// Every machine's files, oldest day first, then by machine name, then by roll index (the order
    /// Serilog wrote them in). Files whose names do not match the format are ignored; a missing
    /// directory gives an empty list.
    /// </returns>
    public IReadOnlyList<LogFile> GetFiles()
    {
        string directory = _configuration.LogDirectory;
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(directory)
            .Select(TryParse)
            .OfType<LogFile>()
            .OrderBy(file => file.Date)
            .ThenBy(file => file.MachineName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.RollIndex)
            .ToArray();
    }

    private LogFile? TryParse(string path)
    {
        string fileName = Path.GetFileName(path);
        Match match = _fileNamePattern.Match(fileName);
        if (
            !match.Success
            || !DateOnly.TryParseExact(
                match.Groups[DateGroup].Value,
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateOnly date
            )
        )
        {
            return null;
        }

        int rollIndex = 0;
        Group roll = match.Groups[RollGroup];
        if (
            roll.Success
            && !int.TryParse(
                roll.Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out rollIndex
            )
        )
        {
            return null;
        }

        Group machine = match.Groups[MachineGroup];
        return new LogFile(path, fileName, machine.Success ? machine.Value : null, date, rollIndex);
    }

    private static Regex BuildFileNamePattern(
        string format,
        string[] arguments,
        string currentMachineName
    )
    {
        // Serilog splits the formatted path at its last extension; the date and roll index go between.
        string extension = Path.GetExtension(format);
        string stem = format[..^extension.Length];

        var pattern = new StringBuilder("^");
        bool machineCaptured = false;
        int literalStart = 0;
        foreach (
            Match placeholder in Regex.Matches(stem, @"\{(\d+)\}", RegexOptions.None, MatchTimeout)
        )
        {
            pattern.Append(Regex.Escape(stem[literalStart..placeholder.Index]));
            literalStart = placeholder.Index + placeholder.Length;

            int index = int.Parse(placeholder.Groups[1].Value, CultureInfo.InvariantCulture);
            string? value = index < arguments.Length ? arguments[index] : null;
            if (
                !machineCaptured
                && string.Equals(value, currentMachineName, StringComparison.OrdinalIgnoreCase)
            )
            {
                pattern.Append(CultureInfo.InvariantCulture, $"(?<{MachineGroup}>.+?)");
                machineCaptured = true;
            }
            else
            {
                pattern.Append(value is null ? ".*?" : Regex.Escape(value));
            }
        }

        pattern.Append(Regex.Escape(stem[literalStart..]));
        pattern.Append(CultureInfo.InvariantCulture, $@"(?<{DateGroup}>\d{{8}})");
        pattern.Append(CultureInfo.InvariantCulture, $@"(?:_(?<{RollGroup}>\d{{3,}}))?");
        pattern.Append(Regex.Escape(extension));
        pattern.Append('$');

        // File names compare case-insensitively on Windows, where most Umbraco sites run.
        return new Regex(
            pattern.ToString(),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            MatchTimeout
        );
    }
}
