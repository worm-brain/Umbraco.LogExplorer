using System.Collections.ObjectModel;
using NSubstitute;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.LogExplorer.Core.Sources;
using Umbraco.Community.LogExplorer.Features.MinimumLevels;
using Umbraco.Community.LogExplorer.Infrastructure.Api;
using UmbracoLogLevel = Umbraco.Cms.Core.Logging.LogLevel;

namespace Umbraco.Community.LogExplorer.Api.Tests.Features.MinimumLevels;

/// <summary>
/// <c>GET /sources/{alias}/minimum-levels</c> reports the sink levels Umbraco reads from the Serilog
/// configuration, for files sources only (#48, ADR 0019). <see cref="ILogViewerService"/> is
/// Umbraco's, so it is substituted.
/// </summary>
public class GetMinimumLevelsControllerTests
{
    private static readonly UserContext Editor = new(
        Guid.NewGuid(),
        new HashSet<string>(["editor"], StringComparer.OrdinalIgnoreCase)
    );

    [Fact]
    public void GetMinimumLevels_FilesSource_ReturnsEachSinkWithItsShortLevelName()
    {
        // Arrange
        GetMinimumLevelsController controller = CreateController(
            "UmbracoFiles",
            new Dictionary<string, UmbracoLogLevel>
            {
                ["Global"] = UmbracoLogLevel.Information,
                ["UmbracoFile"] = UmbracoLogLevel.Verbose,
            }
        );

        // Act
        MinimumLevelsResult result = controller.GetMinimumLevels("files");

        // Assert
        Assert.Equal(
            [new SinkMinimumLevel("Global", "info"), new SinkMinimumLevel("UmbracoFile", "trace")],
            result.Sinks
        );
    }

    [Theory]
    [InlineData(UmbracoLogLevel.Debug, "debug")]
    [InlineData(UmbracoLogLevel.Warning, "warn")]
    [InlineData(UmbracoLogLevel.Error, "error")]
    [InlineData(UmbracoLogLevel.Fatal, "fatal")]
    public void GetMinimumLevels_EachUmbracoLevel_MapsToItsOtelShortName(
        UmbracoLogLevel level,
        string expected
    )
    {
        // Arrange
        GetMinimumLevelsController controller = CreateController(
            "UmbracoFiles",
            new Dictionary<string, UmbracoLogLevel> { ["Global"] = level }
        );

        // Act
        MinimumLevelsResult result = controller.GetMinimumLevels("files");

        // Assert
        Assert.Equal(expected, Assert.Single(result.Sinks).Level);
    }

    [Fact]
    public void GetMinimumLevels_SourceOfAnotherType_ThrowsNotSupported()
    {
        // Arrange
        GetMinimumLevelsController controller = CreateController(
            "Fake",
            new Dictionary<string, UmbracoLogLevel> { ["Global"] = UmbracoLogLevel.Information }
        );

        // Act
        Exception? thrown = Record.Exception(() => controller.GetMinimumLevels("files"));

        // Assert
        Assert.IsType<NotSupportedException>(thrown);
    }

    private static GetMinimumLevelsController CreateController(
        string sourceType,
        Dictionary<string, UmbracoLogLevel> sinks
    )
    {
        ILogViewerService logViewer = Substitute.For<ILogViewerService>();
        logViewer
            .GetLogLevelsFromSinks()
            .Returns(new ReadOnlyDictionary<string, UmbracoLogLevel>(sinks));
        return CreateController(sourceType, logViewer);
    }

    private static GetMinimumLevelsController CreateController(
        string sourceType,
        ILogViewerService logViewer
    )
    {
        ILogSource source = Substitute.For<ILogSource>();
        source.Alias.Returns("files");
        source.Type.Returns(sourceType);
        ILogSourceRegistry registry = Substitute.For<ILogSourceRegistry>();
        registry.GetForUser("files", Editor).Returns(source);
        IUserContextAccessor users = Substitute.For<IUserContextAccessor>();
        users.GetCurrent().Returns(Editor);
        return new GetMinimumLevelsController(registry, users, logViewer);
    }
}
