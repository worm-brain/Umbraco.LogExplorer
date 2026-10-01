using System.Buffers.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Umbraco.Community.LogExplorer.Core.Query;

namespace Umbraco.Community.LogExplorer.Features.Sources.Files;

/// <summary>
/// The files source's paging cursor: the direction plus, for each machine's stream of files that
/// still has events, the position of its next unread line (ADR 0012). Encoded as base64url JSON,
/// <c>{"d":"desc","p":[{"f":"UmbracoTraceLog.WORM.20261001.json","o":523}]}</c>.
/// </summary>
/// <param name="Direction">The sort the cursor was made for; a cursor only continues the same sort.</param>
/// <param name="Positions">
/// One entry per unfinished stream. Ascending, the offset is the start of the next line to read;
/// descending, it is the exclusive end, so lines starting before it are read next. A stream with no
/// entry is finished.
/// </param>
internal sealed record FileCursor(SortDirection Direction, IReadOnlyList<FilePosition> Positions)
{
    private const string Ascending = "asc";
    private const string Descending = "desc";

    /// <summary>Encodes the cursor for <c>LogPage.NextCursor</c>.</summary>
    /// <returns>Base64url of the JSON form, without padding.</returns>
    public string Encode() =>
        Base64Url.EncodeToString(
            JsonSerializer.SerializeToUtf8Bytes(
                new CursorJson
                {
                    Direction = Direction == SortDirection.Ascending ? Ascending : Descending,
                    Positions =
                    [
                        .. Positions.Select(position => new PositionJson
                        {
                            FileName = position.FileName,
                            Offset = position.Offset,
                        }),
                    ],
                }
            )
        );

    /// <summary>Decodes a cursor made by <see cref="Encode"/>.</summary>
    /// <param name="cursor">The query's cursor.</param>
    /// <param name="sort">The query's sort, which must match the cursor's direction.</param>
    /// <returns>The cursor.</returns>
    /// <exception cref="ArgumentException">
    /// The cursor is not one this source made, or was made for the other sort direction.
    /// </exception>
    public static FileCursor Decode(string cursor, SortDirection sort)
    {
        ArgumentNullException.ThrowIfNull(cursor);

        CursorJson? json;
        try
        {
            json = JsonSerializer.Deserialize<CursorJson>(Base64Url.DecodeFromChars(cursor));
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            throw Invalid(cursor, exception);
        }

        SortDirection direction = json?.Direction switch
        {
            Ascending => SortDirection.Ascending,
            Descending => SortDirection.Descending,
            _ => throw Invalid(cursor),
        };
        if (
            json?.Positions is not { } positions
            || positions.Any(position =>
                string.IsNullOrEmpty(position?.FileName) || position.Offset < 0
            )
        )
        {
            throw Invalid(cursor);
        }

        if (direction != sort)
        {
            throw new ArgumentException(
                $"The cursor continues a {direction} query, but this query is {sort}.",
                nameof(cursor)
            );
        }

        return new FileCursor(
            direction,
            [.. positions.Select(position => new FilePosition(position.FileName!, position.Offset))]
        );
    }

    private static ArgumentException Invalid(string cursor, Exception? inner = null) =>
        new($"Invalid cursor '{cursor}'.", nameof(cursor), inner);

    private sealed class CursorJson
    {
        [JsonPropertyName("d")]
        public string? Direction { get; init; }

        [JsonPropertyName("p")]
        public List<PositionJson>? Positions { get; init; }
    }

    private sealed class PositionJson
    {
        [JsonPropertyName("f")]
        public string? FileName { get; init; }

        [JsonPropertyName("o")]
        public long Offset { get; init; }
    }
}
