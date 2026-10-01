using Serilog.Events;

namespace Umbraco.Community.LogExplorer.Features.Sources.Files;

/// <summary>
/// Reads one Serilog compact JSON file backwards, newest line first, in fixed-size blocks from the
/// end, so a newest-first page touches only the tail of a large file (BRIEF §10.1).
/// </summary>
/// <remarks>
/// Lines are split on <c>\n</c> bytes, which never occur inside a UTF-8 multi-byte sequence, so a
/// character cut by a block boundary is reassembled before decoding. Each event carries the same
/// <see cref="LogFileEvent.Offset"/> and <see cref="LogFileEvent.End"/> the forward
/// <see cref="LogFileReader"/> gives it, and lines are parsed by the same
/// <see cref="LogLineParser"/>.
/// </remarks>
internal sealed class ReverseLogFileReader
{
    private const int DefaultBlockSize = 64 * 1024;

    private readonly string _path;
    private readonly int _blockSize;

    /// <summary>Creates a reader that reads 64 KB blocks; nothing is opened until the events are enumerated.</summary>
    /// <param name="path">Full path to the log file.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    public ReverseLogFileReader(string path)
        : this(path, DefaultBlockSize) { }

    /// <summary>Creates a reader with a chosen block size, so tests can put lines across block boundaries.</summary>
    /// <param name="path">Full path to the log file.</param>
    /// <param name="blockSize">Bytes per read, at least 1.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="blockSize"/> is less than 1.</exception>
    internal ReverseLogFileReader(string path, int blockSize)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(blockSize, 1);
        _path = path;
        _blockSize = blockSize;
    }

    /// <summary>
    /// Lines that are not valid compact JSON events, counted as the events are enumerated. The
    /// segment after the last line break of the range read is not counted when it fails to parse:
    /// it is an event Serilog has not finished writing.
    /// </summary>
    public int MalformedLineCount { get; private set; }

    /// <summary>Bytes read from the file so far, across every enumeration.</summary>
    public long BytesRead { get; private set; }

    /// <summary>Reads the whole file, newest line first; see <see cref="ReadEvents(long?, CancellationToken)"/>.</summary>
    /// <param name="cancellationToken">Checked between block reads.</param>
    /// <returns>Each event in reverse file order; blank and malformed lines are skipped.</returns>
    /// <exception cref="IOException">The file cannot be opened or read.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public IEnumerable<LogFileEvent> ReadEvents(CancellationToken cancellationToken = default) =>
        ReadEvents(null, cancellationToken);

    /// <summary>
    /// Reads the lines that start before <paramref name="endOffset"/>, newest first. The file is
    /// opened with <see cref="FileShare.ReadWrite"/> and <see cref="FileShare.Delete"/> so Serilog
    /// can keep writing, and retention can delete it, while it is read.
    /// </summary>
    /// <param name="endOffset">
    /// Exclusive end: the start of a line. Pass an event's <see cref="LogFileEvent.Offset"/> to
    /// continue below that event, or its <see cref="LogFileEvent.End"/> to read it again first.
    /// Null, or an offset past the end, means the file's current length.
    /// </param>
    /// <param name="cancellationToken">Checked between block reads.</param>
    /// <returns>Each event in reverse file order; blank and malformed lines are skipped.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="endOffset"/> is negative.</exception>
    /// <exception cref="IOException">The file cannot be opened or read.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public IEnumerable<LogFileEvent> ReadEvents(
        long? endOffset,
        CancellationToken cancellationToken = default
    )
    {
        if (endOffset is { } end)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(end, nameof(endOffset));
        }

        return Read(endOffset, cancellationToken);
    }

    private IEnumerable<LogFileEvent> Read(long? endOffset, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(
            _path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 0,
            FileOptions.RandomAccess
        );

        long windowEnd = Math.Min(endOffset ?? stream.Length, stream.Length);
        byte[] block = new byte[_blockSize];
        var line = new ReverseByteBuffer();

        // position: start of the bytes read so far. lineEnd: End of the line being assembled.
        // The first segment assembled is whatever follows the last \n in the window, so it is the
        // only one that can be unterminated.
        long position = windowEnd;
        long lineEnd = windowEnd;
        bool isUnterminated = true;
        while (position > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int size = (int)Math.Min(_blockSize, position);
            position -= size;
            stream.Seek(position, SeekOrigin.Begin);
            stream.ReadExactly(block, 0, size);
            BytesRead += size;

            // Each \n found ends the line above it, so the bytes after it complete the line below.
            int scanEnd = size;
            int newline;
            while ((newline = block.AsSpan(0, scanEnd).LastIndexOf((byte)'\n')) >= 0)
            {
                line.Prepend(block.AsSpan(newline + 1, scanEnd - newline - 1));
                long lineStart = position + newline + 1;
                LogEvent? logEvent = Parse(line.WrittenSpan, lineStart, isUnterminated);
                if (logEvent is not null)
                {
                    yield return new LogFileEvent(logEvent, lineStart, lineEnd);
                }

                line.Clear();
                lineEnd = lineStart;
                isUnterminated = false;
                scanEnd = newline;
            }

            line.Prepend(block.AsSpan(0, scanEnd));
        }

        LogEvent? first = Parse(line.WrittenSpan, 0, isUnterminated);
        if (first is not null)
        {
            yield return new LogFileEvent(first, 0, lineEnd);
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

    /// <summary>A byte buffer filled from the end, for assembling a line from blocks read back to front.</summary>
    private sealed class ReverseByteBuffer
    {
        private byte[] _buffer = new byte[1024];
        private int _start = 1024;

        public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(_start);

        public void Prepend(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length > _start)
            {
                int length = _buffer.Length - _start;
                int size = Math.Max(_buffer.Length * 2, length + bytes.Length);
                byte[] grown = new byte[size];
                WrittenSpan.CopyTo(grown.AsSpan(size - length));
                _buffer = grown;
                _start = size - length;
            }

            _start -= bytes.Length;
            bytes.CopyTo(_buffer.AsSpan(_start));
        }

        public void Clear() => _start = _buffer.Length;
    }
}
