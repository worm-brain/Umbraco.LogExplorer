using System.Buffers;
using System.Text;
using Serilog.Events;
using Serilog.Formatting.Compact.Reader;

namespace Umbraco.Community.LogExplorer.Features.Sources.Files;

/// <summary>A parsed event and the byte offset of the line it was read from.</summary>
/// <param name="Event">The event.</param>
/// <param name="Offset">
/// Byte offset of the start of the line in the file. A UTF-8 byte order mark at the start of the
/// file belongs to the first line, so that line's offset is 0.
/// </param>
internal readonly record struct LogFileEvent(LogEvent Event, long Offset);

/// <summary>
/// Reads one Serilog compact JSON file (one event per line) forwards, while Serilog may still be
/// writing to it.
/// </summary>
/// <remarks>
/// Lines are split on bytes rather than through a <see cref="StreamReader"/>, whose position runs
/// ahead of what it has returned, so each event carries the exact offset that record ids and
/// cursors are built from. <c>\n</c> never occurs inside a UTF-8 multi-byte sequence, and a
/// <c>\r</c> before it is dropped, so LF and CRLF files give the same events.
/// </remarks>
internal sealed class LogFileReader
{
    private const int BufferSize = 64 * 1024;

    private readonly string _path;

    /// <summary>Creates a reader for one file; nothing is opened until <see cref="ReadEvents"/> is enumerated.</summary>
    /// <param name="path">Full path to the log file.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    public LogFileReader(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        _path = path;
    }

    /// <summary>
    /// Lines that are not valid compact JSON events, counted as <see cref="ReadEvents"/> is
    /// enumerated (they become <c>LogPage.Warnings</c>). An unparseable last line with no line
    /// break after it is not counted: it is an event Serilog has not finished writing.
    /// </summary>
    public int MalformedLineCount { get; private set; }

    /// <summary>
    /// Reads the file from the start to its current end. The file is opened with
    /// <see cref="FileShare.ReadWrite"/> and <see cref="FileShare.Delete"/> so Serilog can keep
    /// writing, and retention can delete it, while it is read.
    /// </summary>
    /// <param name="cancellationToken">Checked between buffer reads.</param>
    /// <returns>Each event in file order; blank and malformed lines are skipped.</returns>
    /// <exception cref="IOException">The file cannot be opened or read.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public IEnumerable<LogFileEvent> ReadEvents(CancellationToken cancellationToken = default)
    {
        using var stream = new FileStream(
            _path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            BufferSize,
            FileOptions.SequentialScan
        );

        byte[] buffer = new byte[BufferSize];
        var line = new ArrayBufferWriter<byte>();
        long position = 0;
        long lineStart = 0;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int start = 0;
            while (start < read)
            {
                int newline = buffer.AsSpan(start, read - start).IndexOf((byte)'\n');
                int end = newline < 0 ? read : start + newline;
                line.Write(buffer.AsSpan(start, end - start));
                position += end - start;
                if (newline < 0)
                {
                    break;
                }

                position++;
                start = end + 1;
                LogEvent? terminated = Parse(line.WrittenSpan, lineStart, isUnterminated: false);
                if (terminated is not null)
                {
                    yield return new LogFileEvent(terminated, lineStart);
                }

                line.ResetWrittenCount();
                lineStart = position;
            }
        }

        if (line.WrittenCount > 0)
        {
            LogEvent? last = Parse(line.WrittenSpan, lineStart, isUnterminated: true);
            if (last is not null)
            {
                yield return new LogFileEvent(last, lineStart);
            }
        }
    }

    private LogEvent? Parse(ReadOnlySpan<byte> bytes, long offset, bool isUnterminated)
    {
        if (offset == 0)
        {
            bytes = bytes.StartsWith(Encoding.UTF8.Preamble)
                ? bytes[Encoding.UTF8.Preamble.Length..]
                : bytes;
        }

        string text = Encoding.UTF8.GetString(bytes.TrimEnd((byte)'\r'));
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
            if (!isUnterminated)
            {
                MalformedLineCount++;
            }

            return null;
        }
    }
}
