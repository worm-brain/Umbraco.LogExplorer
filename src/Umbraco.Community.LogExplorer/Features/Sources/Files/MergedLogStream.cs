using System.Globalization;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Results;

namespace Umbraco.Community.LogExplorer.Features.Sources.Files;

/// <summary>
/// Every machine's log files in a range, read as one stream of events in timestamp order, newest or
/// oldest first (ADR 0012). The pager, the aggregation scans and the context reader all read the
/// files through this, so they agree on order, record positions and cursors.
/// </summary>
/// <remarks>
/// <para>
/// Each machine's files form one stream, ordered by day then roll index (reversed for newest-first);
/// files whose name format has no machine form one stream. Only files dated from the day before
/// <c>From</c> to the day after <c>To</c> are read, because a file's date is the writer's local day
/// while timestamps are UTC. The reverse reader serves newest-first and the forward reader
/// oldest-first. Streams merge on their next event's timestamp, ties broken by file name then
/// offset (exactly reversed for newest-first), so the order is total.
/// </para>
/// <para>
/// The range includes <c>From</c> and excludes <c>To</c>. Events before the near edge are skipped;
/// a stream ends at its first event past the far edge, on the assumption (ADR 0012, decision 6)
/// that each file is in time order. A consumer that wants a different far edge passes a different
/// range; one that wants to stop early, for a page size or a byte budget, just stops calling
/// <see cref="TryRead"/> and reads <see cref="NextPositions"/>.
/// </para>
/// <para>
/// Each stream reads one event ahead of what <see cref="TryRead"/> has returned, so its next event
/// is known for the merge. Not thread-safe; the work is synchronous file I/O.
/// </para>
/// </remarks>
internal sealed class MergedLogStream : IDisposable
{
    private readonly List<MachineStream> _streams;
    private readonly List<MachineStream> _active;
    private readonly ReadTally _tally;
    private readonly CancellationToken _cancellationToken;
    private bool _disposed;

    private MergedLogStream(
        List<MachineStream> streams,
        ReadTally tally,
        CancellationToken cancellationToken
    )
    {
        _streams = streams;
        _tally = tally;
        _cancellationToken = cancellationToken;
        _active = [];
    }

    /// <summary>
    /// Where each unfinished stream resumes, in stream order: the cursor positions of ADR 0012.
    /// Ascending, the offset is the start of the next unread event; descending, it is that event's
    /// end, the exclusive end the reverse reader takes. Empty once every stream is exhausted.
    /// Pass these to <see cref="Resume"/> to continue where this stream stopped.
    /// </summary>
    public IReadOnlyList<FilePosition> NextPositions =>
        [.. _active.Select(stream => stream.NextPosition)];

    /// <summary>
    /// Bytes read so far across every file, including files still open, so a scan can check a byte
    /// budget between reads. Includes the one event each stream has read ahead.
    /// </summary>
    public long BytesRead => _tally.BytesRead + _streams.Sum(stream => stream.OpenCounts.Bytes);

    /// <summary>
    /// One warning per file with malformed lines seen so far, for <c>LogPage.Warnings</c>; files
    /// already finished come first, in the order they finished, then open files in stream order.
    /// An unterminated last line is not malformed: Serilog is still writing it.
    /// </summary>
    public IReadOnlyList<string> Warnings =>
        [
            .. _tally
                .Malformed.Concat(
                    _streams
                        .Select(stream => stream.OpenCounts)
                        .Where(counts => counts.FileName is not null)
                        .Select(counts => (FileName: counts.FileName!, Count: counts.Malformed))
                )
                .Where(entry => entry.Count > 0)
                .Select(entry =>
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{entry.Count} malformed {(entry.Count == 1 ? "line" : "lines")} skipped in {entry.FileName}"
                    )
                ),
        ];

    /// <summary>Opens every stream at the near edge of the range.</summary>
    /// <param name="locator">Lists the log files.</param>
    /// <param name="range">The range to read; see the remarks on the class for the edges.</param>
    /// <param name="direction">Descending reads newest first.</param>
    /// <param name="cancellationToken">Checked between events and file reads.</param>
    /// <param name="parallelParse">
    /// True for a scan that will read far (an aggregation up to its budget): newest-first files are
    /// then read in <see cref="ReverseLogFileReader.ParallelBlockSize"/> blocks whose lines are
    /// parsed across cores (ADR 0026). The events and their order are the same either way; only
    /// <see cref="BytesRead"/> grows in larger steps. Oldest-first reads ignore it.
    /// </param>
    /// <returns>The stream, already holding each machine's first event in the range.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="locator"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public static MergedLogStream Open(
        UmbracoLogFileLocator locator,
        ResolvedRange range,
        SortDirection direction,
        CancellationToken cancellationToken,
        bool parallelParse = false
    ) =>
        Create(
            locator,
            range,
            direction,
            starts: null,
            unlistedFinished: false,
            cancellationToken,
            parallelParse
        );

    /// <summary>
    /// Continues from a cursor's positions (<see cref="FileCursor.Positions"/>, or an earlier
    /// stream's <see cref="NextPositions"/>). A machine with no position finished earlier and is not
    /// read. A position whose file no longer exists (retention) or is now outside the range resumes
    /// that machine at the next file after it in the stream's order.
    /// </summary>
    /// <param name="locator">Lists the log files and parses the positions' file names.</param>
    /// <param name="range">The range to read; see the remarks on the class for the edges.</param>
    /// <param name="direction">The direction the positions were made for.</param>
    /// <param name="positions">At most one per machine; file names are matched against the located files, never opened as paths.</param>
    /// <param name="cancellationToken">Checked between events and file reads.</param>
    /// <returns>The stream, already holding each listed machine's next event.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="locator"/> or <paramref name="positions"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// A position's file name is not a log file name, or two positions name the same machine.
    /// </exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public static MergedLogStream Resume(
        UmbracoLogFileLocator locator,
        ResolvedRange range,
        SortDirection direction,
        IReadOnlyList<FilePosition> positions,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(positions);
        return Create(
            locator,
            range,
            direction,
            positions,
            unlistedFinished: true,
            cancellationToken
        );
    }

    /// <summary>
    /// Opens the listed machines at explicit positions and every other machine at the near edge of
    /// the range: the start for reading outwards from an anchor record. Positions follow the same
    /// rules as <see cref="Resume"/>, and a missing file moves on to the next one the same way.
    /// </summary>
    /// <remarks>
    /// To read around an anchor decoded with <see cref="FilePosition.TryParseRecordId"/>, seed the
    /// anchor's machine and set the near edge of the range to the anchor's timestamp, so the other
    /// machines start there too:
    /// <list type="bullet">
    /// <item>After the anchor: ascending, position <c>(file, anchor.End)</c>, <c>From</c> at the
    /// anchor's timestamp. <c>anchor.Offset</c> instead includes the anchor itself.</item>
    /// <item>Before the anchor: descending, position <c>(file, anchor.Offset)</c>, <c>To</c> just
    /// after the anchor's timestamp (<c>To</c> is exclusive).</item>
    /// </list>
    /// Events on other machines with exactly the anchor's timestamp are then read on the side the
    /// merge tie-break would not place them; the caller drops those it does not want.
    /// </remarks>
    /// <param name="locator">Lists the log files and parses the positions' file names.</param>
    /// <param name="range">The range to read; see the remarks on the class for the edges.</param>
    /// <param name="direction">Descending reads newest first.</param>
    /// <param name="starts">At most one per machine, with offsets as <see cref="NextPositions"/> describes for this direction.</param>
    /// <param name="cancellationToken">Checked between events and file reads.</param>
    /// <returns>The stream, already holding each machine's first event.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="locator"/> or <paramref name="starts"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// A start's file name is not a log file name, or two starts name the same machine.
    /// </exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public static MergedLogStream OpenAt(
        UmbracoLogFileLocator locator,
        ResolvedRange range,
        SortDirection direction,
        IReadOnlyList<FilePosition> starts,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(starts);
        return Create(
            locator,
            range,
            direction,
            starts,
            unlistedFinished: false,
            cancellationToken
        );
    }

    /// <summary>Takes the next event in merged order.</summary>
    /// <param name="next">The event and the file it came from, when there is one.</param>
    /// <returns>False once every stream is exhausted.</returns>
    /// <exception cref="ObjectDisposedException">The stream has been disposed.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public bool TryRead(out (LogFile File, LogFileEvent Event) next)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _cancellationToken.ThrowIfCancellationRequested();
        if (_active.Count == 0)
        {
            next = default;
            return false;
        }

        MachineStream best = _active.Aggregate(
            (leader, candidate) => candidate.ComesBefore(leader) ? candidate : leader
        );
        next = (best.HeadFile!, best.Head);

        // Advance before returning, so NextPositions never includes an event already handed out.
        if (!best.Advance())
        {
            _active.Remove(best);
        }

        return true;
    }

    /// <summary>
    /// Closes every open file. <see cref="NextPositions"/>, <see cref="BytesRead"/> and
    /// <see cref="Warnings"/> stay readable afterwards.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (MachineStream stream in _streams)
        {
            stream.Close();
        }
    }

    private static MergedLogStream Create(
        UmbracoLogFileLocator locator,
        ResolvedRange range,
        SortDirection direction,
        IReadOnlyList<FilePosition>? starts,
        bool unlistedFinished,
        CancellationToken cancellationToken,
        bool parallelParse = false
    )
    {
        ArgumentNullException.ThrowIfNull(locator);
        bool descending = direction == SortDirection.Descending;
        Dictionary<string, (LogFile File, long Offset)>? positions = starts is null
            ? null
            : ParsePositions(locator, starts);

        var tally = new ReadTally();
        var streams = new List<MachineStream>();
        foreach (
            IGrouping<string, LogFile> machine in GetCandidateFiles(locator, range)
                .GroupBy(StreamKey, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
        )
        {
            List<LogFile> files = machine
                .OrderBy(file => file.Date)
                .ThenBy(file => file.RollIndex)
                .ToList();
            if (descending)
            {
                files.Reverse();
            }

            int start = 0;
            long? offset = null;
            if (
                positions is not null
                && positions.TryGetValue(machine.Key, out (LogFile File, long Offset) position)
            )
            {
                // Start at the position's file, or, if retention deleted it or it is now outside
                // the range, at the next file after it in the stream's order.
                start = files.FindIndex(file => !IsBefore(file, position.File, descending));
                if (start < 0)
                {
                    continue;
                }

                if (
                    string.Equals(
                        files[start].FileName,
                        position.File.FileName,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    offset = position.Offset;
                }
            }
            else if (positions is not null && unlistedFinished)
            {
                // A cursor with no position for this machine: its stream finished on an earlier page.
                continue;
            }

            streams.Add(
                new MachineStream(
                    files,
                    start,
                    offset,
                    descending,
                    range,
                    tally,
                    parallelParse,
                    cancellationToken
                )
            );
        }

        var merged = new MergedLogStream(streams, tally, cancellationToken);
        try
        {
            merged._active.AddRange(streams.Where(stream => stream.Advance()));
        }
        catch
        {
            merged.Dispose();
            throw;
        }

        return merged;
    }

    /// <summary>
    /// The files a stream over <paramref name="range"/> reads from: those dated from the day before
    /// <c>From</c> to the day after <c>To</c>, as the remarks on the class explain. Callers that key
    /// a cache on the files' state fingerprint exactly these.
    /// </summary>
    /// <param name="locator">Lists the log files.</param>
    /// <param name="range">The range to read.</param>
    /// <returns>The candidate files, in the locator's order.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static IReadOnlyList<LogFile> GetCandidateFiles(
        UmbracoLogFileLocator locator,
        ResolvedRange range
    )
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(range);

        // A file's date is the writer's local day while timestamps are UTC, so a day either side
        // keeps files from writers in any time zone.
        DateOnly firstDay = DateOnly.FromDateTime(range.From.UtcDateTime).AddDays(-1);
        DateOnly lastDay = DateOnly.FromDateTime(range.To.UtcDateTime).AddDays(1);
        return [.. locator.GetFiles().Where(file => file.Date >= firstDay && file.Date <= lastDay)];
    }

    private static Dictionary<string, (LogFile File, long Offset)> ParsePositions(
        UmbracoLogFileLocator locator,
        IReadOnlyList<FilePosition> starts
    )
    {
        var positions = new Dictionary<string, (LogFile, long)>(StringComparer.OrdinalIgnoreCase);
        foreach (FilePosition position in starts)
        {
            LogFile file =
                locator.ParseFileName(position.FileName)
                ?? throw new ArgumentException(
                    $"Invalid position: '{position.FileName}' is not a log file name.",
                    nameof(starts)
                );
            if (!positions.TryAdd(StreamKey(file), (file, position.Offset)))
            {
                throw new ArgumentException(
                    "Invalid positions: there are two for one machine.",
                    nameof(starts)
                );
            }
        }

        return positions;
    }

    // Files without a machine name form one stream; "" stands in for null as a dictionary key.
    private static string StreamKey(LogFile file) => file.MachineName ?? "";

    private static bool IsBefore(LogFile file, LogFile other, bool descending)
    {
        int comparison = (file.Date, file.RollIndex).CompareTo((other.Date, other.RollIndex));
        return descending ? comparison > 0 : comparison < 0;
    }

    /// <summary>Malformed lines per file and bytes read, collected as each file is closed.</summary>
    private sealed class ReadTally
    {
        private readonly List<(string FileName, int Count)> _malformed = [];

        public long BytesRead { get; private set; }

        public IEnumerable<(string FileName, int Count)> Malformed => _malformed;

        public void Add(string fileName, int malformedLines, long bytesRead)
        {
            BytesRead += bytesRead;
            if (malformedLines > 0)
            {
                _malformed.Add((fileName, malformedLines));
            }
        }
    }

    /// <summary>
    /// One machine's files read as a single stream, always holding its next in-range event as
    /// <see cref="Head"/> until it is exhausted.
    /// </summary>
    private sealed class MachineStream(
        List<LogFile> files,
        int startIndex,
        long? startOffset,
        bool descending,
        ResolvedRange range,
        ReadTally tally,
        bool parallelParse,
        CancellationToken cancellationToken
    )
    {
        private int _index = startIndex;
        private long? _offset = startOffset;
        private IEnumerator<LogFileEvent>? _events;
        private Func<(int Malformed, long Bytes)>? _counts;

        public LogFileEvent Head { get; private set; }

        public LogFile? HeadFile { get; private set; }

        /// <summary>
        /// Where this stream resumes: ascending, the start of <see cref="Head"/>; descending, its
        /// end, so the reverse reader returns it first.
        /// </summary>
        public FilePosition NextPosition =>
            new(HeadFile!.FileName, descending ? Head.End : Head.Offset);

        /// <summary>The counts of the file being read; no file name and zeros when none is open.</summary>
        public (string? FileName, int Malformed, long Bytes) OpenCounts
        {
            get
            {
                if (_events is null)
                {
                    return (null, 0, 0);
                }

                (int malformed, long bytes) = _counts!();
                return (files[_index].FileName, malformed, bytes);
            }
        }

        /// <summary>Whether <see cref="Head"/> sorts before <paramref name="other"/>'s in this direction.</summary>
        public bool ComesBefore(MachineStream other)
        {
            int comparison = Head.Event.Timestamp.CompareTo(other.Head.Event.Timestamp);
            if (comparison == 0)
            {
                comparison = string.CompareOrdinal(HeadFile!.FileName, other.HeadFile!.FileName);
            }

            if (comparison == 0)
            {
                comparison = Head.Offset.CompareTo(other.Head.Offset);
            }

            return descending ? comparison > 0 : comparison < 0;
        }

        /// <summary>Moves <see cref="Head"/> to the next event inside the range.</summary>
        /// <returns>False once the stream has no more events in the range.</returns>
        public bool Advance()
        {
            while (_index < files.Count)
            {
                _events ??= Open(files[_index]);
                if (!MoveNext())
                {
                    CloseFile();
                    _index++;
                    continue;
                }

                LogFileEvent current = _events.Current;
                DateTimeOffset timestamp = current.Event.Timestamp;
                bool beforeNearEdge = descending ? timestamp >= range.To : timestamp < range.From;
                bool pastFarEdge = descending ? timestamp < range.From : timestamp >= range.To;
                if (pastFarEdge)
                {
                    break;
                }

                if (beforeNearEdge)
                {
                    continue;
                }

                Head = current;
                HeadFile = files[_index];
                return true;
            }

            Close();
            _index = files.Count;
            HeadFile = null;
            return false;
        }

        public void Close() => CloseFile();

        private IEnumerator<LogFileEvent> Open(LogFile file)
        {
            long? offset = _offset;
            _offset = null;
            if (descending)
            {
                var reader = parallelParse
                    ? new ReverseLogFileReader(
                        file.Path,
                        ReverseLogFileReader.ParallelBlockSize,
                        parallelParse: true
                    )
                    : new ReverseLogFileReader(file.Path);
                _counts = () => (reader.MalformedLineCount, reader.BytesRead);
                return reader.ReadEvents(offset, cancellationToken).GetEnumerator();
            }

            var forward = new LogFileReader(file.Path);
            _counts = () => (forward.MalformedLineCount, forward.BytesRead);
            return forward.ReadEvents(offset ?? 0, cancellationToken).GetEnumerator();
        }

        private bool MoveNext()
        {
            try
            {
                return _events!.MoveNext();
            }
            // Deleted by retention between listing and opening: nothing left to read in it.
            catch (FileNotFoundException)
            {
                return false;
            }
        }

        private void CloseFile()
        {
            if (_events is null)
            {
                return;
            }

            _events.Dispose();
            _events = null;
            (int malformed, long bytes) = _counts!();
            tally.Add(files[_index].FileName, malformed, bytes);
        }
    }
}
