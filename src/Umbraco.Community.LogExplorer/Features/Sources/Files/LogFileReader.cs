using System.Buffers;
using Serilog.Events;

namespace Umbraco.Community.LogExplorer.Features.Sources.Files;

/// <summary>A parsed event and where its line sits in the file.</summary>
/// <param name="Event">The event.</param>
/// <param name="Offset">
/// Byte offset of the start of the line in the file. A UTF-8 byte order mark at the start of the
/// file belongs to the first line, so that line's offset is 0.
/// </param>
/// <param name="End">
/// Byte offset just past the line's <c>\n</c>, which is where the next line starts; for a last line
/// with no line break, the end of the data read.
/// </param>
internal readonly record struct LogFileEvent(LogEvent Event, long Offset, long End);

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

    /// <summary>Creates a reader for one file; nothing is opened until the events are enumerated.</summary>
    /// <param name="path">Full path to the log file.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    public LogFileReader(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        _path = path;
    }

    /// <summary>
    /// Lines that are not valid compact JSON events, counted as the events are enumerated (they
    /// become <c>LogPage.Warnings</c>). An unparseable last line with no line break after it is not
    /// counted: it is an event Serilog has not finished writing.
    /// </summary>
    public int MalformedLineCount { get; private set; }

    /// <summary>Bytes read from the file so far, across every enumeration.</summary>
    public long BytesRead { get; private set; }

    /// <summary>Reads the whole file; see <see cref="ReadEvents(long, CancellationToken)"/>.</summary>
    /// <param name="cancellationToken">Checked between buffer reads.</param>
    /// <returns>Each event in file order; blank and malformed lines are skipped.</returns>
    /// <exception cref="IOException">The file cannot be opened or read.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public IEnumerable<LogFileEvent> ReadEvents(CancellationToken cancellationToken = default) =>
        ReadEvents(0, cancellationToken);

    /// <summary>
    /// Reads from <paramref name="startOffset"/> to the file's current end. The file is opened with
    /// <see cref="FileShare.ReadWrite"/> and <see cref="FileShare.Delete"/> so Serilog can keep
    /// writing, and retention can delete it, while it is read.
    /// </summary>
    /// <param name="startOffset">
    /// Where to start: 0, or the start of a line (an event's <see cref="LogFileEvent.Offset"/> or
    /// <see cref="LogFileEvent.End"/>). An offset past the end reads nothing.
    /// </param>
    /// <param name="cancellationToken">Checked between buffer reads.</param>
    /// <returns>Each event in file order; blank and malformed lines are skipped.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="startOffset"/> is negative.</exception>
    /// <exception cref="IOException">The file cannot be opened or read.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public IEnumerable<LogFileEvent> ReadEvents(
        long startOffset,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startOffset);
        return Read(startOffset, cancellationToken);
    }

    private IEnumerable<LogFileEvent> Read(long startOffset, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(
            _path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            BufferSize,
            FileOptions.SequentialScan
        );
        stream.Seek(startOffset, SeekOrigin.Begin);

        byte[] buffer = new byte[BufferSize];
        var line = new ArrayBufferWriter<byte>();
        long position = startOffset;
        long lineStart = startOffset;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BytesRead += read;

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
                    yield return new LogFileEvent(terminated, lineStart, position);
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
                yield return new LogFileEvent(last, lineStart, position);
            }
        }
    }

    private LogEvent? Parse(ReadOnlySpan<byte> bytes, long offset, bool isUnterminated)
    {
        LogEvent? logEvent = LogLineParser.Parse(bytes, offset, out bool malformed);
        if (malformed && !isUnterminated)
        {
            MalformedLineCount++;
        }

        return logEvent;
    }
}
