using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Records;
using Umbraco.Community.LogExplorer.Core.Results;

namespace Umbraco.Community.LogExplorer.Features.Sources.Files;

/// <summary>
/// Reads the entries either side of an anchor record ("Around this", BRIEF §6.8) across every
/// machine's files, ignoring filters and levels.
/// </summary>
/// <remarks>
/// <para>
/// "Either side" means in <see cref="MergedLogStream"/>'s order (ADR 0012, decision 4): timestamp,
/// then file name, then offset. So an event on another machine with exactly the anchor's timestamp
/// lands before the anchor when its file name sorts first and after it otherwise, the same place
/// the pager puts it, and the anchor itself is never in either list.
/// </para>
/// <para>
/// The reads are not bounded by a time range: before the anchor runs back to the oldest located
/// file, after it runs forward to the newest. Fewer entries than asked for come back only at the
/// start or end of the logs. The work is synchronous file I/O; the source that calls it (#35)
/// decides which thread it runs on.
/// </para>
/// </remarks>
internal sealed class FileContextReader
{
    /// <summary>
    /// The most entries returned on each side; larger requests are clamped. The UI asks for 7
    /// (BRIEF §6.8), and this keeps a hand-made request from reading the whole log.
    /// </summary>
    public const int MaxEntriesPerSide = 100;

    private readonly UmbracoLogFileLocator _locator;

    /// <summary>Creates a reader over the files the locator finds.</summary>
    /// <param name="locator">Lists the log files; record ids are resolved only against its list.</param>
    /// <exception cref="ArgumentNullException"><paramref name="locator"/> is null.</exception>
    public FileContextReader(UmbracoLogFileLocator locator)
    {
        ArgumentNullException.ThrowIfNull(locator);
        _locator = locator;
    }

    /// <summary>Reads the anchor and the entries around it.</summary>
    /// <param name="recordId">A record id from <see cref="FilePosition.ToRecordId"/>.</param>
    /// <param name="before">Entries wanted before the anchor; clamped to <see cref="MaxEntriesPerSide"/>.</param>
    /// <param name="after">Entries wanted after the anchor; clamped to <see cref="MaxEntriesPerSide"/>.</param>
    /// <param name="cancellationToken">Checked between events and file reads.</param>
    /// <returns>
    /// The context, both lists oldest first. Records have no <see cref="LogRecord.SourceAlias"/>;
    /// the source sets it.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="recordId"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="before"/> or <paramref name="after"/> is negative.</exception>
    /// <exception cref="KeyNotFoundException">
    /// The id does not decode, names no located file (including one retention has deleted), or
    /// does not point at the start of a readable event (ADR 0008).
    /// </exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public ContextResult Read(
        string recordId,
        int before,
        int after,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(recordId);
        ArgumentOutOfRangeException.ThrowIfNegative(before);
        ArgumentOutOfRangeException.ThrowIfNegative(after);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<LogFile> files = _locator.GetFiles();
        (LogFile file, LogFileEvent anchor) = FindAnchor(recordId, files, cancellationToken);
        var anchorKey = new OrderKey(anchor.Event.Timestamp, file.FileName, anchor.Offset);

        // Wide enough for every located file: a file's date is the writer's local day, which can
        // start or end up to 14 hours either side of the UTC day, and MergedLogStream already
        // admits files a day either side of the range. The anchor's own timestamp widens it in
        // case its file is misdated.
        DateTimeOffset oldest = Min(Midnight(files.Min(f => f.Date)).AddDays(-1), anchorKey.Time);
        DateTimeOffset newest = Max(Midnight(files.Max(f => f.Date)).AddDays(2), anchorKey.Time);

        List<LogRecord> preceding = ReadSide(
            new ResolvedRange(oldest, anchorKey.Time.AddTicks(1)),
            SortDirection.Descending,
            new FilePosition(file.FileName, anchor.Offset),
            anchorKey,
            Math.Min(before, MaxEntriesPerSide),
            cancellationToken
        );
        preceding.Reverse();

        List<LogRecord> following = ReadSide(
            new ResolvedRange(anchorKey.Time, newest),
            SortDirection.Ascending,
            new FilePosition(file.FileName, anchor.End),
            anchorKey,
            Math.Min(after, MaxEntriesPerSide),
            cancellationToken
        );

        return new ContextResult(
            preceding,
            CompactLogEventMapper.Map(anchor.Event, file, anchor.Offset),
            following
        );
    }

    /// <summary>
    /// Re-reads the anchor's line, so a stale or tampered id fails here rather than producing
    /// context around a position that is not an event.
    /// </summary>
    private static (LogFile File, LogFileEvent Anchor) FindAnchor(
        string recordId,
        IReadOnlyList<LogFile> files,
        CancellationToken cancellationToken
    )
    {
        if (!FilePosition.TryParseRecordId(recordId, out FilePosition position))
        {
            throw NotFound(recordId, "it is not a log file record id");
        }

        // Matched against the listed names only, so the id's name never reaches the file system.
        LogFile file =
            files.FirstOrDefault(candidate =>
                string.Equals(candidate.FileName, position.FileName, StringComparison.Ordinal)
            ) ?? throw NotFound(recordId, $"there is no log file named '{position.FileName}'");

        try
        {
            if (!IsLineStart(file.Path, position.Offset))
            {
                throw NotFound(recordId, "its offset is not the start of a line");
            }

            // The reader skips blank and malformed lines, so its first event is the anchor only if
            // the line at the offset parsed.
            foreach (
                LogFileEvent candidate in new LogFileReader(file.Path).ReadEvents(
                    position.Offset,
                    cancellationToken
                )
            )
            {
                if (candidate.Offset == position.Offset)
                {
                    return (file, candidate);
                }

                break;
            }
        }
        catch (FileNotFoundException)
        {
            // Deleted by retention after it was listed.
        }

        throw NotFound(recordId, "there is no event at its offset");
    }

    private List<LogRecord> ReadSide(
        ResolvedRange range,
        SortDirection direction,
        FilePosition start,
        OrderKey anchorKey,
        int count,
        CancellationToken cancellationToken
    )
    {
        var records = new List<LogRecord>(count);
        if (count == 0)
        {
            return records;
        }

        bool descending = direction == SortDirection.Descending;
        using MergedLogStream stream = MergedLogStream.OpenAt(
            _locator,
            range,
            direction,
            [start],
            cancellationToken
        );
        while (records.Count < count && stream.TryRead(out (LogFile File, LogFileEvent Event) next))
        {
            // The range edge includes every event at the anchor's timestamp, on both sides; keep
            // only those the merge order puts on this side. They all come first in the stream.
            int comparison = new OrderKey(
                next.Event.Event.Timestamp,
                next.File.FileName,
                next.Event.Offset
            ).CompareTo(anchorKey);
            if (descending ? comparison >= 0 : comparison <= 0)
            {
                continue;
            }

            records.Add(CompactLogEventMapper.Map(next.Event.Event, next.File, next.Event.Offset));
        }

        return records;
    }

    private static bool IsLineStart(string path, long offset)
    {
        if (offset == 0)
        {
            return true;
        }

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete
        );
        if (offset > stream.Length)
        {
            return false;
        }

        stream.Seek(offset - 1, SeekOrigin.Begin);
        return stream.ReadByte() == '\n';
    }

    private static KeyNotFoundException NotFound(string recordId, string reason) =>
        new($"Log file record '{recordId}' was not found: {reason}.");

    private static DateTimeOffset Midnight(DateOnly day) =>
        new(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;

    /// <summary>
    /// An event's place in <see cref="MergedLogStream"/>'s ascending order; must stay identical to
    /// its tie-break (ADR 0012, decision 4).
    /// </summary>
    private readonly record struct OrderKey(DateTimeOffset Time, string FileName, long Offset)
        : IComparable<OrderKey>
    {
        public int CompareTo(OrderKey other)
        {
            int comparison = Time.CompareTo(other.Time);
            if (comparison == 0)
            {
                comparison = string.CompareOrdinal(FileName, other.FileName);
            }

            return comparison != 0 ? comparison : Offset.CompareTo(other.Offset);
        }
    }
}
