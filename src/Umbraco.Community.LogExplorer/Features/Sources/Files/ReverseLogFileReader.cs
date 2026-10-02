using System.Buffers;
using Serilog.Events;

namespace Umbraco.Community.LogExplorer.Features.Sources.Files;

/// <summary>
/// Reads one Serilog compact JSON file backwards, newest line first, in fixed-size blocks from the
/// end, so a newest-first page touches only the tail of a large file (BRIEF §10.1).
/// </summary>
/// <remarks>
/// <para>
/// Lines are split on <c>\n</c> bytes, which never occur inside a UTF-8 multi-byte sequence, so a
/// character cut by a block boundary is reassembled before decoding. Each event carries the same
/// <see cref="LogFileEvent.Offset"/> and <see cref="LogFileEvent.End"/> the forward
/// <see cref="LogFileReader"/> gives it, and lines are parsed by the same
/// <see cref="LogLineParser"/>.
/// </para>
/// <para>
/// The lines each block completes are collected first and then handed out in order. By default
/// each is parsed only when it is handed out, so a page that stops early parses no more than it
/// uses. With parallel parsing (for aggregations, which read up to the scan budget anyway), a
/// block's lines are parsed across cores before the first is handed out; parsing is most of the
/// cost of a scan (ADR 0026). Either way the events, their order, their offsets and the
/// malformed-line count are the same.
/// </para>
/// </remarks>
internal sealed class ReverseLogFileReader
{
    /// <summary>The block size for reading a page: a newest-first page usually needs one block.</summary>
    internal const int DefaultBlockSize = 64 * 1024;

    /// <summary>
    /// The block size for parallel parsing: about 800 lines of a typical Umbraco log, enough work
    /// per block to spread over the cores.
    /// </summary>
    internal const int ParallelBlockSize = 1024 * 1024;

    private readonly string _path;
    private readonly int _blockSize;
    private readonly bool _parallelParse;

    /// <summary>Creates a reader that reads 64 KB blocks; nothing is opened until the events are enumerated.</summary>
    /// <param name="path">Full path to the log file.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    public ReverseLogFileReader(string path)
        : this(path, DefaultBlockSize) { }

    /// <summary>Creates a reader with a chosen block size, so tests can put lines across block boundaries.</summary>
    /// <param name="path">Full path to the log file.</param>
    /// <param name="blockSize">Bytes per read, at least 1.</param>
    /// <param name="parallelParse">
    /// True to parse each block's lines across cores before handing out the first; false to parse
    /// each line as it is handed out.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="blockSize"/> is less than 1.</exception>
    internal ReverseLogFileReader(string path, int blockSize, bool parallelParse = false)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(blockSize, 1);
        _path = path;
        _blockSize = blockSize;
        _parallelParse = parallelParse;
    }

    /// <summary>
    /// Lines that are not valid compact JSON events, counted as the events are enumerated. The
    /// segment after the last line break of the range read is not counted when it fails to parse:
    /// it is an event Serilog has not finished writing.
    /// </summary>
    public int MalformedLineCount { get; private set; }

    /// <summary>
    /// Bytes read from the file so far, across every enumeration. With parallel parsing, the
    /// count follows the lines handed out, in <see cref="DefaultBlockSize"/> steps, exactly as a
    /// reader without it would count them; so a scan budget measured on it stops at the same event
    /// either way, although the larger blocks are read from disk sooner.
    /// </summary>
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
        long readBefore = BytesRead;
        byte[] block = new byte[_blockSize];
        var line = new ReverseByteBuffer();
        var batch = new LineBatch();

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
            if (!_parallelParse)
            {
                BytesRead += size;
            }

            // Each \n found ends the line above it, so the bytes after it complete the line below.
            int scanEnd = size;
            int newline;
            while ((newline = block.AsSpan(0, scanEnd).LastIndexOf((byte)'\n')) >= 0)
            {
                line.Prepend(block.AsSpan(newline + 1, scanEnd - newline - 1));
                long lineStart = position + newline + 1;
                batch.Add(line.WrittenSpan, lineStart, lineEnd, isUnterminated);
                line.Clear();
                lineEnd = lineStart;
                isUnterminated = false;
                scanEnd = newline;
            }

            line.Prepend(block.AsSpan(0, scanEnd));
            foreach (LogFileEvent logEvent in Drain(batch, readBefore, windowEnd))
            {
                yield return logEvent;
            }
        }

        batch.Add(line.WrittenSpan, 0, lineEnd, isUnterminated);
        foreach (LogFileEvent logEvent in Drain(batch, readBefore, windowEnd))
        {
            yield return logEvent;
        }

        // The whole window has been read, as it would have been in default blocks.
        BytesRead = readBefore + windowEnd;
    }

    // Hands out a block's lines in the order they were added, then empties the batch.
    private IEnumerable<LogFileEvent> Drain(LineBatch batch, long readBefore, long windowEnd)
    {
        if (_parallelParse)
        {
            batch.ParseAll();
        }

        for (int i = 0; i < batch.Count; i++)
        {
            LogEvent? logEvent = batch.Parse(i, out bool malformed);
            if (malformed && !batch.IsUnterminated(i))
            {
                MalformedLineCount++;
            }

            if (logEvent is not null)
            {
                if (_parallelParse)
                {
                    BytesRead = readBefore + DefaultBlocksReadBefore(batch.Start(i), windowEnd);
                }

                yield return new LogFileEvent(logEvent, batch.Start(i), batch.End(i));
            }
        }

        batch.Clear();
    }

    /// <summary>
    /// How many bytes a reader reading <see cref="DefaultBlockSize"/> blocks back from
    /// <paramref name="windowEnd"/> has read when it hands out the line at
    /// <paramref name="lineStart"/>: every block up to the one holding the <c>\n</c> just before
    /// it, or the whole window for the first line of the file.
    /// </summary>
    /// <param name="lineStart">Offset of the line's first byte.</param>
    /// <param name="windowEnd">Exclusive end of the range being read.</param>
    /// <returns>The bytes read, at most <paramref name="windowEnd"/>.</returns>
    internal static long DefaultBlocksReadBefore(long lineStart, long windowEnd)
    {
        long blocks = (windowEnd - lineStart + DefaultBlockSize) / DefaultBlockSize;
        return Math.Min(windowEnd, blocks * DefaultBlockSize);
    }

    /// <summary>
    /// The lines one block completed, newest first: their bytes in one buffer, where each starts and
    /// ends in the file, and, once parsed, each one's event.
    /// </summary>
    private sealed class LineBatch
    {
        private readonly ArrayBufferWriter<byte> _bytes = new();
        private readonly List<Line> _lines = [];
        private LogEvent?[] _events = [];
        private bool[] _malformed = [];
        private bool _parsed;

        public int Count => _lines.Count;

        public void Add(ReadOnlySpan<byte> bytes, long start, long end, bool isUnterminated)
        {
            _lines.Add(new Line(_bytes.WrittenCount, bytes.Length, start, end, isUnterminated));
            _bytes.Write(bytes);
        }

        public long Start(int index) => _lines[index].Start;

        public long End(int index) => _lines[index].End;

        public bool IsUnterminated(int index) => _lines[index].IsUnterminated;

        // Parses every line at once, across cores; the results wait for Parse to hand them out.
        public void ParseAll()
        {
            if (_events.Length < _lines.Count)
            {
                _events = new LogEvent?[_lines.Count];
                _malformed = new bool[_lines.Count];
            }

            ReadOnlyMemory<byte> bytes = _bytes.WrittenMemory;
            ParallelWork.For(
                _lines.Count,
                index =>
                {
                    Line line = _lines[index];
                    _events[index] = LogLineParser.Parse(
                        bytes.Span.Slice(line.BufferStart, line.Length),
                        line.Start,
                        out _malformed[index]
                    );
                }
            );
            _parsed = true;
        }

        // The event for one line: the parallel result when there is one, else parsed now.
        public LogEvent? Parse(int index, out bool malformed)
        {
            if (_parsed)
            {
                malformed = _malformed[index];
                return _events[index];
            }

            Line line = _lines[index];
            return LogLineParser.Parse(
                _bytes.WrittenSpan.Slice(line.BufferStart, line.Length),
                line.Start,
                out malformed
            );
        }

        public void Clear()
        {
            _bytes.ResetWrittenCount();
            _lines.Clear();
            // Drop the parsed events so they can be collected while the next block is read.
            Array.Clear(_events);
            _parsed = false;
        }

        private readonly record struct Line(
            int BufferStart,
            int Length,
            long Start,
            long End,
            bool IsUnterminated
        );
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
