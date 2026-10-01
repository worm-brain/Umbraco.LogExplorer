using Serilog;
using Serilog.Formatting.Compact;

namespace LogExplorer.Samples.LogGenerator;

/// <summary>
/// Writes the file-layout scenarios the files provider must cope with (BRIEF Appendix C), next to the
/// site's own log files: a second machine's files, a file rolled past 1 MB (so a <c>_001</c> file
/// exists), and one truncated last line, as if a write were still in progress.
/// </summary>
/// <remarks>
/// The files are written by a separate Serilog logger with Umbraco's naming and the compact JSON
/// formatter, so they are byte-for-byte what a second load-balanced server would write. The
/// scenario runs once per day per log directory: if today's second-machine file already exists it
/// does nothing, so restarting a site does not keep rolling new files.
/// </remarks>
internal static class FileScenarios
{
    private const long RollSizeBytes = 1024 * 1024;

    /// <summary>Writes the scenarios into <paramref name="logDirectory"/>.</summary>
    /// <param name="logDirectory">Umbraco's log directory (<c>ILoggingConfiguration.LogDirectory</c>).</param>
    /// <param name="machineName">The second machine's name, used in the file names and <c>MachineName</c>.</param>
    /// <param name="random">Randomness source; a fixed seed gives the same events each time.</param>
    public static void Write(string logDirectory, string machineName, Random random)
    {
        string today = DateTime.Now.ToString(
            "yyyyMMdd",
            System.Globalization.CultureInfo.InvariantCulture
        );
        if (
            Directory
                .EnumerateFiles(logDirectory, $"UmbracoTraceLog.{machineName}.{today}*.json")
                .Any()
        )
        {
            return;
        }

        // Serilog inserts the date before the extension and rolls to _001, _002, ... past the size limit,
        // giving UmbracoTraceLog.{machine}.{yyyyMMdd}.json and UmbracoTraceLog.{machine}.{yyyyMMdd}_001.json.
        string pathTemplate = Path.Combine(logDirectory, $"UmbracoTraceLog.{machineName}..json");
        using (
            Serilog.Core.Logger node = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .Enrich.WithProperty("MachineName", machineName)
                .Enrich.WithProperty(
                    "SourceContext",
                    "Umbraco.Cms.Web.Common.Middleware.UmbracoRequestMiddleware"
                )
                .WriteTo.File(
                    new CompactJsonFormatter(),
                    pathTemplate,
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: RollSizeBytes,
                    rollOnFileSizeLimit: true
                )
                .CreateLogger()
        )
        {
            // About 1.5 MB of events, so the first file fills and the rest go to _001.
            for (int i = 0; i < 6_000; i++)
            {
                node.Information(
                    "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Duration} ms on {Node}",
                    "GET",
                    i % 10 == 0 ? "/api/products" : "/",
                    i % 50 == 0 ? 500 : 200,
                    random.Next(3, 200),
                    machineName
                );
            }
        }

        // A line cut off mid-write, at the end of the newest file only (the provider must skip it silently).
        string newest = Directory
            .EnumerateFiles(logDirectory, $"UmbracoTraceLog.{machineName}.{today}*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Last();
        File.AppendAllText(
            newest,
            "{\"@t\":\"" + DateTime.UtcNow.ToString("O") + "\",\"@mt\":\"Truncated mid-wr"
        );
    }
}
