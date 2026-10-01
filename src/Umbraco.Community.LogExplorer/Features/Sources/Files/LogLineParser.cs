using System.Text;
using Serilog.Events;
using Serilog.Formatting.Compact.Reader;

namespace Umbraco.Community.LogExplorer.Features.Sources.Files;

/// <summary>
/// Parses one line of a Serilog compact JSON file, shared by the forward and reverse readers so
/// both treat byte order marks, CRLF, blank lines and bad lines the same way.
/// </summary>
internal static class LogLineParser
{
    /// <summary>Parses the bytes of one line.</summary>
    /// <param name="line">The line without its <c>\n</c>; a trailing <c>\r</c> is dropped here.</param>
    /// <param name="offset">
    /// Byte offset of the line in the file. Only the line at offset 0 can start with a UTF-8 byte
    /// order mark, which is skipped.
    /// </param>
    /// <param name="malformed">
    /// True when the line has content but is not a valid compact JSON event. Callers decide whether
    /// it counts: a bad last line with no line break is an event Serilog is still writing.
    /// </param>
    /// <returns>The event, or null for a blank or malformed line.</returns>
    public static LogEvent? Parse(ReadOnlySpan<byte> line, long offset, out bool malformed)
    {
        malformed = false;
        if (offset == 0 && line.StartsWith(Encoding.UTF8.Preamble))
        {
            line = line[Encoding.UTF8.Preamble.Length..];
        }

        string text = Encoding.UTF8.GetString(line.TrimEnd((byte)'\r'));
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return LogEventReader.ReadFromString(text);
        }
        // The exception types the 4.0.0 reader throws for bad JSON, a missing or unparseable @t or
        // @x, an unknown @l, and a document that is not an object.
        catch (Exception exception)
            when (exception
                    is Newtonsoft.Json.JsonException
                        or InvalidDataException
                        or FormatException
                        or ArgumentException
            )
        {
            malformed = true;
            return null;
        }
    }
}
