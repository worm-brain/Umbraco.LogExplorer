using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Umbraco.Community.LogExplorer.Core.Fake;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Core.Sources;
using Umbraco.Community.LogExplorer.Features.NativeQuery;
using Umbraco.Community.LogExplorer.Features.Sources;
using Umbraco.Community.LogExplorer.Infrastructure.Api;

namespace Umbraco.Community.LogExplorer.Api.Tests.Features.NativeQuery;

/// <summary>
/// <c>POST /sources/{alias}/compile</c> returns the source's "Show query" text and the nodes it
/// cannot express (#43).
/// </summary>
public class CompileControllerTests
{
    private static readonly UserContext Editor = new(
        Guid.NewGuid(),
        new HashSet<string>(["editor"], StringComparer.OrdinalIgnoreCase)
    );

    private static readonly LogQuery LastHour = new() { Range = new TimeRange(null, null, "1h") };

    [Fact]
    public void Compile_FilterOnTwoFields_ReturnsOneClausePerLine()
    {
        // Arrange
        CompileController controller = CreateController(
            new FakeLogSource(new() { Alias = "sample" })
        );
        LogQuery query = LastHour with
        {
            Filter = new AndNode([
                new ConditionNode("RequestPath", FilterOperator.StartsWith, Json("/api")),
                new TextNode("timeout"),
            ]),
        };

        // Act
        CompileResult result = controller.Compile("sample", query);

        // Assert
        Assert.Equal("RequestPath startswith \"/api\"\nand text(\"timeout\")", result.Native);
    }

    [Fact]
    public void Compile_OperatorTheSourceDoesNotDeclare_ReturnsTheNodeAsUnsupported()
    {
        // Arrange
        var unsupported = new ConditionNode("RequestPath", FilterOperator.StartsWith, Json("/api"));
        CompileController controller = CreateController(
            new FakeLogSource(
                new()
                {
                    Alias = "sample",
                    Operators = new HashSet<FilterOperator>([FilterOperator.Equals]),
                }
            )
        );

        // Act
        CompileResult result = controller.Compile("sample", LastHour with { Filter = unsupported });

        // Assert
        // Same instance: the node comes back as sent, and JsonElement has no value equality.
        Assert.Equal(
            (null, true),
            (result.Native, ReferenceEquals(unsupported, Assert.Single(result.Unsupported)))
        );
    }

    [Fact]
    public void Compile_SourceWithoutNativeQuery_ThrowsNotSupported()
    {
        // Arrange
        CompileController controller = CreateController(
            new FakeLogSource(new() { Alias = "sample", Features = LogSourceFeatures.Histogram })
        );

        // Act
        Exception? thrown = Record.Exception(() => controller.Compile("sample", LastHour));

        // Assert
        Assert.IsType<NotSupportedException>(thrown);
    }

    [Fact]
    public void Compile_NativeQueryOnASourceThatDisallowsIt_ThrowsNotSupported()
    {
        // Arrange
        CompileController controller = CreateController(
            new NativeQueryDisabledSource(new FakeLogSource(new() { Alias = "sample" }))
        );

        // Act
        Exception? thrown = Record.Exception(() =>
            controller.Compile("sample", LastHour with { NativeQuery = "x" })
        );

        // Assert
        Assert.IsType<NotSupportedException>(thrown);
    }

    [Fact]
    public void Compile_HiddenSource_ThrowsForbiddenSource()
    {
        // Arrange
        ILogSourceRegistry registry = Substitute.For<ILogSourceRegistry>();
        registry.GetForUser("prod", Editor).Throws(new ForbiddenSourceException("prod"));
        CompileController controller = CreateController(registry);

        // Act
        Exception? thrown = Record.Exception(() => controller.Compile("prod", LastHour));

        // Assert
        Assert.IsType<ForbiddenSourceException>(thrown);
    }

    private static System.Text.Json.JsonElement Json(string value) =>
        System.Text.Json.JsonSerializer.SerializeToElement(value);

    private static CompileController CreateController(ILogSource source)
    {
        ILogSourceRegistry registry = Substitute.For<ILogSourceRegistry>();
        registry.GetForUser(source.Alias, Editor).Returns(source);
        return CreateController(registry);
    }

    private static CompileController CreateController(ILogSourceRegistry registry)
    {
        IUserContextAccessor users = Substitute.For<IUserContextAccessor>();
        users.GetCurrent().Returns(Editor);
        return new CompileController(registry, users);
    }
}
