using System.Globalization;
using Umbraco.Community.LogExplorer.Core.Filtering;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Records;
using Umbraco.Community.LogExplorer.Core.Results;

namespace Umbraco.Community.LogExplorer.Features.Sources.Files;

/// <summary>A page of records and how many bytes of log files were read to produce it.</summary>
/// <param name="Page">The page.</param>
/// <param name="BytesRead">Bytes read across every file; shows that a page stopped early.</param>
internal sealed record FilePage(LogPage Page, long BytesRead);

/// <summary>
/// Pages through Umbraco's log files in timestamp order, newest or oldest first, merging every
/// machine's files into one stream and stopping as soon as the page is full (BRIEF §10.1).
/// </summary>
/// <remarks>
/// <para>
/// Each machine's files form one stream, ordered by day and roll index; the reverse reader serves
/// newest-first and the forward reader oldest-first. Streams merge on their next event's timestamp,
/// ties broken by file name then offset (reversed for newest-first), so the order is total and
/// cursors resume without gaps or duplicates. The cursor holds each unfinished stream's next
/// unread position (<see cref="FileCursor"/>, ADR 0012).
/// </para>
/// <para>
/// Known limit: events within a file are assumed to be in time order, which is how Serilog writes
/// them. A stream ends at the first event beyond the far edge of the range, so an event written
/// slightly out of order just inside that edge can be missed. A machine whose files first appear
/// after a cursor was issued is not picked up by that cursor; a new query sees it.
/// </para>
/// </remarks>
internal sealed class LogFilePager
{
    private readonly UmbracoLogFileLocator _locator;
    private readonly TimeProvider _clock;

    /// <summary>Creates a pager over the files the locator finds.</summary>
    /// <param name="locator">Lists the log files and parses cursor file names.</param>
    /// <param name="clock">Resolves relative time ranges.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public LogFilePager(UmbracoLogFileLocator locator, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(clock);
        _locator = locator;
        _clock = clock;
    }

    /// <summary>
    /// Reads one page. The work is synchronous file I/O; the source that calls it (#35) decides
    /// which thread it runs on.
    /// </summary>
    /// <param name="query">
    /// The query. <see cref="LogQuery.Filter"/> and <see cref="LogQuery.Levels"/> are applied with
    /// <see cref="LogRecordFilter"/>; the range includes <c>From</c> and excludes <c>To</c>.
    /// </param>
    /// <param name="maxPageSize">The source's page size limit; <see cref="LogQuery.Take"/> is clamped to it.</param>
    /// <param name="cancellationToken">Checked between events.</param>
    /// <returns>
    /// The page, with <see cref="LogPage.NextCursor"/> null once every stream is exhausted (the
    /// last page can be empty), no total count, and one warning per file with malformed lines.
    /// Records have no <see cref="LogRecord.SourceAlias"/>; the source sets it.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="LogQuery.Take"/> or <paramref name="maxPageSize"/> is less than 1.</exception>
    /// <exception cref="ArgumentException">
    /// The cursor is invalid or was made for the other sort direction, or the range is invalid.
    /// </exception>
    /// <exception cref="NotSupportedException">The query has a native query, which this pager does not evaluate (#34).</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public FilePage Query(LogQuery query, int maxPageSize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Take, 1, nameof(query));
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPageSize, 1);
        if (query.NativeQuery is not null)
        {
            throw new NotSupportedException("The log file pager does not evaluate native queries.");
        }

        ResolvedRange range = RelativeRange.Resolve(query.Range, _clock);
        FileCursor? cursor = query.Cursor is null
            ? null
            : FileCursor.Decode(query.Cursor, query.Sort);
        int take = Math.Min(query.Take, maxPageSize);

        var tally = new ReadTally();
        List<MachineStream> streams = OpenStreams(
            range,
            query.Sort,
            cursor,
            tally,
            cancellationToken
        );
        var records = new List<LogRecord>(Math.Min(take, 1024));
        string? nextCursor;
        try
        {
            List<MachineStream> active = streams.Where(stream => stream.Advance()).ToList();
            while (records.Count < take && active.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                MachineStream next = active.Aggregate(
                    (best, candidate) => candidate.ComesBefore(best) ? candidate : best
                );
                LogRecord record = CompactLogEventMapper.Map(
                    next.Head.Event,
                    next.HeadFile!,
                    next.Head.Offset
                );
                if (LogRecordFilter.Matches(record, query.Filter, query.Levels))
                {
                    records.Add(record);
                }

                if (!next.Advance())
                {
                    active.Remove(next);
                }
            }

            nextCursor =
                active.Count == 0
                    ? null
                    : new FileCursor(
                        query.Sort,
                        [.. active.Select(stream => stream.NextPosition)]
                    ).Encode();
        }
        finally
        {
            // Closing a stream's open file adds its counts to the tally, so this comes first.
            CloseAll(streams);
        }

        return new FilePage(
            new LogPage(
                records,
                nextCursor,
                range,
                TotalCount: null,
                TotalIsLowerBound: false,
                tally.Warnings()
            ),
            tally.BytesRead
        );
    }

    private List<MachineStream> OpenStreams(
        ResolvedRange range,
        SortDirection sort,
        FileCursor? cursor,
        ReadTally tally,
        CancellationToken cancellationToken
    )
    {
        bool descending = sort == SortDirection.Descending;
        Dictionary<string, (LogFile File, long Offset)>? resume = cursor is null
            ? null
            : ResumePositions(cursor);

        // A file's date is the writer's local day while timestamps are UTC, so a day either side
        // keeps files from writers in any time zone.
        DateOnly firstDay = DateOnly.FromDateTime(range.From.UtcDateTime).AddDays(-1);
        DateOnly lastDay = DateOnly.FromDateTime(range.To.UtcDateTime).AddDays(1);

        var streams = new List<MachineStream>();
        foreach (
            IGrouping<string, LogFile> machine in _locator
                .GetFiles()
                .Where(file => file.Date >= firstDay && file.Date <= lastDay)
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
            if (resume is not null)
            {
                // No position for this machine means its stream finished on an earlier page.
                if (!resume.TryGetValue(machine.Key, out (LogFile File, long Offset) position))
                {
                    continue;
                }

                // Resume at the cursor's file, or, if retention deleted it or it is now outside
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

            streams.Add(
                new MachineStream(files, start, offset, descending, range, tally, cancellationToken)
            );
        }

        return streams;
    }

    private Dictionary<string, (LogFile File, long Offset)> ResumePositions(FileCursor cursor)
    {
        var positions = new Dictionary<string, (LogFile, long)>(StringComparer.OrdinalIgnoreCase);
        foreach (FilePosition position in cursor.Positions)
        {
            LogFile file =
                _locator.ParseFileName(position.FileName)
                ?? throw new ArgumentException(
                    $"Invalid cursor: '{position.FileName}' is not a log file name.",
                    nameof(cursor)
                );
            if (!positions.TryAdd(StreamKey(file), (file, position.Offset)))
            {
                throw new ArgumentException(
                    "Invalid cursor: it has two positions for one machine.",
                    nameof(cursor)
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

    private static void CloseAll(List<MachineStream> streams)
    {
        foreach (MachineStream stream in streams)
        {
            stream.Close();
        }
    }

    /// <summary>Malformed lines per file and bytes read, collected as each file is closed.</summary>
    private sealed class ReadTally
    {
        private readonly List<(string FileName, int Count)> _malformed = [];

        public long BytesRead { get; private set; }

        public void Add(string fileName, int malformedLines, long bytesRead)
        {
            BytesRead += bytesRead;
            if (malformedLines > 0)
            {
                _malformed.Add((fileName, malformedLines));
            }
        }

        public string[] Warnings() =>
            [
                .. _malformed.Select(entry =>
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{entry.Count} malformed {(entry.Count == 1 ? "line" : "lines")} skipped in {entry.FileName}"
                    )
                ),
            ];
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
                var reader = new ReverseLogFileReader(file.Path);
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
