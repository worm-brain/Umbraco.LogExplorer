using System.Text.Json;
using Umbraco.Community.LogExplorer.Core.Json;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.SimpleSyntax;
using Umbraco.Community.LogExplorer.Features.Parse;

namespace Umbraco.Community.LogExplorer.Api.Tests.Features.Parse;

/// <summary>
/// <c>POST /parse</c> returns what the Core parser makes of the input (#39, ADR 0005). The parser's
/// own table lives in Core.Tests; these cover the endpoint's contract: the wire shape, the
/// fallback, empty input and the length cap.
/// </summary>
public class ParseControllerTests
{
    private readonly ParseController _controller = new();

    [Fact]
    public void Parse_LevelAndPathFilter_ReturnsOneChipAndTheLevelSet()
    {
        // Act
        ParseResult result = _controller.Parse(new ParseRequest("level:error path:/api*"));

        // Assert
        Assert.Equal(
            """{"chips":[{"kind":"condition","field":"RequestPath","op":"startsWith","value":"/api","caseInsensitive":true}],"levels":["error","fatal"],"fallback":null}""",
            JsonSerializer.Serialize(result, LogJson.Options)
        );
    }

    [Fact]
    public void Parse_BareWord_ReturnsOneTextChip()
    {
        // Act
        ParseResult result = _controller.Parse(new ParseRequest("timeout"));

        // Assert
        Assert.Equal(
            """{"chips":[{"kind":"text","text":"timeout","phrase":false}],"levels":null,"fallback":null}""",
            JsonSerializer.Serialize(result, LogJson.Options)
        );
    }

    [Fact]
    public void Parse_UnbalancedQuote_ReturnsTheWholeInputAsTextWithTheFallback()
    {
        // Act
        ParseResult result = _controller.Parse(new ParseRequest("path:/api \"connection"));

        // Assert
        Assert.Equal(
            ("path:/api connection", SimpleSyntaxParser.UnbalancedQuoteCode),
            (Assert.IsType<TextNode>(Assert.Single(result.Chips)).Text, result.Fallback?.Code)
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_EmptyInput_ReturnsNoChipsAndNoLevels(string? input)
    {
        // Act
        ParseResult result = _controller.Parse(new ParseRequest(input));

        // Assert
        Assert.Equal((0, null, null), (result.Chips.Count, result.Levels, result.Fallback));
    }

    [Fact]
    public void Parse_InputAtTheLimit_Parses()
    {
        // Act
        ParseResult result = _controller.Parse(
            new ParseRequest(new string('a', ParseController.MaxInputLength))
        );

        // Assert
        Assert.Single(result.Chips);
    }

    [Fact]
    public void Parse_InputOverTheLimit_ThrowsArgumentExceptionForInvalidQuery()
    {
        // Act
        Exception? thrown = Record.Exception(() =>
            _controller.Parse(new ParseRequest(new string('a', ParseController.MaxInputLength + 1)))
        );

        // Assert
        Assert.IsType<ArgumentException>(thrown);
    }
}
