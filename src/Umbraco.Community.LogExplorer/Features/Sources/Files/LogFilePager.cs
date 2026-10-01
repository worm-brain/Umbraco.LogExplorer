using Serilog.Events;
using Umbraco.Community.LogExplorer.Core.Filtering;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Records;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Core.Sources;

namespace Umbraco.Community.LogExplorer.Features.Sources.Files;

/// <summary>A page of records and how many bytes of log files were read to produce it.</summary>
/// <param name="Page">The page.</param>
/// <param name="BytesRead">Bytes read across every file; shows that a page stopped early.</param>
internal sealed record FilePage(LogPage Page, long BytesRead);

/// <summary>
/// Pages through Umbraco's log files in timestamp order, newest or oldest first, stopping as soon
/// as the page is full (BRIEF §10.1). The merge across machines, its order and its known limits
/// are <see cref="MergedLogStream"/>'s; the cursor is that stream's next positions
/// (<see cref="FileCursor"/>, ADR 0012).
/// </summary>
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
    /// <see cref="LogRecordFilter"/>, and AND with <see cref="LogQuery.NativeQuery"/>, which runs as
    /// the core Log Viewer runs it (<see cref="NativeFilter"/>); the range includes <c>From</c> and
    /// excludes <c>To</c>.
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
    /// <exception cref="InvalidNativeQueryException">The native query does not compile.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public FilePage Query(LogQuery query, int maxPageSize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Take, 1, nameof(query));
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPageSize, 1);
        // Compiled before any file is opened, so an invalid query fails fast.
        Func<LogEvent, bool> native = NativeFilter.Compile(query.NativeQuery);

        ResolvedRange range = RelativeRange.Resolve(query.Range, _clock);
        FileCursor? cursor = query.Cursor is null
            ? null
            : FileCursor.Decode(query.Cursor, query.Sort);
        int take = Math.Min(query.Take, maxPageSize);
        // Most scanned events fail the filter, so they are tested on a record holding only the
        // parts the filter reads, and only matches are mapped in full (ADR 0017).
        RecordParts filterParts = FilterRecordParts.For(query.Filter);

        var records = new List<LogRecord>(Math.Min(take, 1024));
        using MergedLogStream stream = cursor is null
            ? MergedLogStream.Open(_locator, range, query.Sort, cancellationToken)
            : MergedLogStream.Resume(
                _locator,
                range,
                query.Sort,
                cursor.Positions,
                cancellationToken
            );
        while (records.Count < take && stream.TryRead(out (LogFile File, LogFileEvent Event) next))
        {
            // The native expression reads the event itself, so it runs before the mapping cost.
            if (!native(next.Event.Event))
            {
                continue;
            }

            LogRecord probe = CompactLogEventMapper.Map(
                next.Event.Event,
                next.File,
                next.Event.Offset,
                filterParts
            );
            if (!LogRecordFilter.Matches(probe, query.Filter, query.Levels))
            {
                continue;
            }

            records.Add(
                filterParts == RecordParts.All
                    ? probe
                    : CompactLogEventMapper.Map(next.Event.Event, next.File, next.Event.Offset)
            );
        }

        IReadOnlyList<FilePosition> positions = stream.NextPositions;
        string? nextCursor =
            positions.Count == 0 ? null : new FileCursor(query.Sort, positions).Encode();

        // Closing the open files adds their counts to the totals, so they are read after it.
        stream.Dispose();
        return new FilePage(
            new LogPage(
                records,
                nextCursor,
                range,
                TotalCount: null,
                TotalIsLowerBound: false,
                stream.Warnings
            ),
            stream.BytesRead
        );
    }
}
