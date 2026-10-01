using Umbraco.Community.LogExplorer.Core.Fake;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Features.Sources;

namespace Umbraco.Community.LogExplorer.Api.Tests.Features.Sources;

/// <summary>
/// A source configured with <c>AllowNativeQuery: false</c> refuses native queries and keeps
/// everything else (BRIEF §12, #43).
/// </summary>
public class NativeQueryDisabledSourceTests
{
    private static readonly LogQuery LastHour = new() { Range = new TimeRange(null, null, "1h") };

    private readonly NativeQueryDisabledSource _source = new(
        new FakeLogSource(new() { Alias = "sample" })
    );

    [Fact]
    public async Task QueryAsync_WithoutNativeQuery_ReturnsTheInnerSourcesPage()
    {
        // Act
        LogPage page = await _source.QueryAsync(LastHour, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(238, page.TotalCount);
    }

    [Fact]
    public async Task QueryAsync_WithNativeQuery_ThrowsNotSupported()
    {
        // Act
        Exception? thrown = await Record.ExceptionAsync(() =>
            _source.QueryAsync(
                LastHour with
                {
                    NativeQuery = "text(\"a\")",
                },
                TestContext.Current.CancellationToken
            )
        );

        // Assert
        Assert.IsType<NotSupportedException>(thrown);
    }

    [Fact]
    public void Compile_WithoutNativeQuery_StillCompiles()
    {
        // Act
        CompileResult result = _source.Compile(LastHour with { Filter = new TextNode("a") });

        // Assert
        Assert.Equal("text(\"a\")", result.Native);
    }
}
