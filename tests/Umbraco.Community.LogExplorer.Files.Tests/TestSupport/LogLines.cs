using System.Globalization;
using System.Text;

namespace Umbraco.Community.LogExplorer.Files.Tests.TestSupport;

/// <summary>Builds compact JSON log lines and files for tests that generate their own data.</summary>
internal static class LogLines
{
    /// <summary>One compact JSON event, without a line break. Information omits <c>@l</c>, as Serilog does.</summary>
    public static string Event(
        DateTimeOffset timestamp,
        string template,
        string? level = null,
        string? extraProperties = null
    )
    {
        var line = new StringBuilder();
        line.Append(
            CultureInfo.InvariantCulture,
            $"{{\"@t\":\"{timestamp.UtcDateTime:yyyy-MM-ddTHH:mm:ss.fffffffZ}\",\"@mt\":\"{template}\""
        );
        if (level is not null)
        {
            line.Append(CultureInfo.InvariantCulture, $",\"@l\":\"{level}\"");
        }

        if (extraProperties is not null)
        {
            line.Append(',').Append(extraProperties);
        }

        return line.Append('}').ToString();
    }

    /// <summary>The lines joined with <c>\n</c>, each terminated, as UTF-8 bytes.</summary>
    public static byte[] File(params string[] lines) =>
        Encoding.UTF8.GetBytes(string.Concat(lines.Select(line => line + "\n")));
}
