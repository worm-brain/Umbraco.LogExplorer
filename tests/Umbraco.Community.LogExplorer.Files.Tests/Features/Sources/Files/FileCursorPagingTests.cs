using System.Text;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Features.Sources.Files;

namespace Umbraco.Community.LogExplorer.Files.Tests.Features.Sources.Files;

/// <summary>Cursors and record ids are base64url and decode back to file positions (#31, ADR 0012).</summary>
public class FileCursorPagingTests
{
    [Fact]
    public void Decode_EncodedCursor_ReturnsTheSamePositions()
    {
        // Arrange
        var cursor = new FileCursor(
            SortDirection.Descending,
            [
                new FilePosition("UmbracoTraceLog.NODE2.20260930_001.json", 190),
                new FilePosition("UmbracoTraceLog.WORM.20261001.json", 995),
            ]
        );

        // Act
        FileCursor decoded = FileCursor.Decode(cursor.Encode(), SortDirection.Descending);

        // Assert
        Assert.Equal(cursor.Positions, decoded.Positions);
    }

    [Fact]
    public void Encode_Cursor_IsBase64UrlJson()
    {
        // Arrange
        var cursor = new FileCursor(SortDirection.Ascending, [new FilePosition("a.json", 7)]);

        // Act
        string encoded = cursor.Encode();

        // Assert: base64url of {"d":"asc","p":[{"f":"a.json","o":7}]}.
        Assert.Equal("eyJkIjoiYXNjIiwicCI6W3siZiI6ImEuanNvbiIsIm8iOjd9XX0", encoded);
    }

    [Theory]
    [InlineData("not a cursor")]
    [InlineData("bm90IGpzb24")] // "not json"
    [InlineData("eyJkIjoidXAiLCJwIjpbXX0")] // {"d":"up","p":[]}
    [InlineData("eyJkIjoiYXNjIn0")] // {"d":"asc"}
    [InlineData("eyJkIjoiYXNjIiwicCI6W3siZiI6ImEuanNvbiIsIm8iOi0xfV19")] // {"d":"asc","p":[{"f":"a.json","o":-1}]}
    [InlineData("eyJkIjoiYXNjIiwicCI6W251bGxdfQ")] // {"d":"asc","p":[null]}
    public void Decode_InvalidCursor_ThrowsArgumentException(string cursor)
    {
        // Act
        void Decode() => FileCursor.Decode(cursor, SortDirection.Ascending);

        // Assert
        Assert.Throws<ArgumentException>(Decode);
    }

    [Fact]
    public void Decode_CursorForTheOtherDirection_ThrowsArgumentException()
    {
        // Arrange
        string cursor = new FileCursor(SortDirection.Descending, []).Encode();

        // Act
        void Decode() => FileCursor.Decode(cursor, SortDirection.Ascending);

        // Assert
        Assert.Throws<ArgumentException>(Decode);
    }

    [Fact]
    public void TryParseRecordId_IdFromToRecordId_ReturnsTheSamePosition()
    {
        // Arrange: a colon in the name must not be mistaken for the separator.
        var position = new FilePosition("odd:name.json", 523);

        // Act
        bool parsed = FilePosition.TryParseRecordId(position.ToRecordId(), out FilePosition actual);

        // Assert
        Assert.Equal((true, position), (parsed, actual));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("bm8tb2Zmc2V0")] // "no-offset"
    [InlineData("YS5qc29uOi0x")] // "a.json:-1"
    [InlineData("OjE0Nw")] // ":147"
    public void TryParseRecordId_InvalidId_ReturnsFalse(string? recordId)
    {
        // Act
        bool parsed = FilePosition.TryParseRecordId(recordId, out _);

        // Assert
        Assert.False(parsed);
    }

    [Fact]
    public void TryParseRecordId_InvalidUtf8_ReturnsFalse()
    {
        // Arrange
        string recordId = System.Buffers.Text.Base64Url.EncodeToString([
            0xFF,
            .. Encoding.ASCII.GetBytes(":1"),
        ]);

        // Act
        bool parsed = FilePosition.TryParseRecordId(recordId, out _);

        // Assert
        Assert.False(parsed);
    }
}
