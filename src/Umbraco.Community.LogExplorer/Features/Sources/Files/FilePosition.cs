using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace Umbraco.Community.LogExplorer.Features.Sources.Files;

/// <summary>
/// A byte offset in a log file, named by file name alone (never a path), and the record id built
/// from it: base64url of <c>{fileName}:{offset}</c>, so it can go in a route as is (ADR 0012).
/// </summary>
/// <param name="FileName">The file name, as <see cref="LogFile.FileName"/> gives it.</param>
/// <param name="Offset">Byte offset of the start of a line.</param>
internal readonly record struct FilePosition(string FileName, long Offset)
{
    /// <summary>Encodes this position as a record id.</summary>
    /// <returns>Base64url of <c>{FileName}:{Offset}</c>, without padding.</returns>
    public string ToRecordId() =>
        Base64Url.EncodeToString(
            Encoding.UTF8.GetBytes(
                string.Create(CultureInfo.InvariantCulture, $"{FileName}:{Offset}")
            )
        );

    /// <summary>
    /// Decodes a record id made by <see cref="ToRecordId"/>. The file name is only a name to look up
    /// among the located files; it is never safe to open as a path.
    /// </summary>
    /// <param name="recordId">The id.</param>
    /// <param name="position">The decoded position when the id is valid.</param>
    /// <returns>False when the id is not base64url of a non-empty name, a colon and a non-negative offset.</returns>
    public static bool TryParseRecordId(string? recordId, out FilePosition position)
    {
        position = default;
        if (string.IsNullOrEmpty(recordId) || !Base64Url.IsValid(recordId))
        {
            return false;
        }

        string text;
        try
        {
            text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(
                Base64Url.DecodeFromChars(recordId)
            );
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return false;
        }

        // The name may contain colons; the offset follows the last one.
        int colon = text.LastIndexOf(':');
        if (
            colon <= 0
            || !long.TryParse(
                text.AsSpan(colon + 1),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long offset
            )
        )
        {
            return false;
        }

        position = new FilePosition(text[..colon], offset);
        return true;
    }
}
