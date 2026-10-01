using Microsoft.Extensions.Options;
using Umbraco.Community.LogExplorer.Features.Settings;
using Umbraco.Community.LogExplorer.Features.Sources;

namespace Umbraco.Community.LogExplorer.Api.Tests.Features.Settings;

/// <summary><c>GET /settings</c> reflects the configured options (#49, #86, ADR 0011).</summary>
public class GetSettingsControllerTests
{
    [Fact]
    public void GetSettings_DefaultOptions_ReturnsTheBriefDefaults()
    {
        // Arrange
        var controller = new GetSettingsController(Options.Create(new LogExplorerOptions()));

        // Act
        SettingsResponseModel settings = controller.GetSettings();

        // Assert
        Assert.Equal(
            (true, "files", "1h"),
            (settings.HideCoreLogViewer, settings.DefaultSource, settings.DefaultTimeRange)
        );
    }

    [Fact]
    public void GetSettings_CoreLogViewerShown_ReturnsHideCoreLogViewerFalse()
    {
        // Arrange
        var controller = new GetSettingsController(
            Options.Create(
                new LogExplorerOptions
                {
                    HideCoreLogViewer = false,
                    DefaultSource = "sample",
                    DefaultTimeRange = "24h",
                }
            )
        );

        // Act
        SettingsResponseModel settings = controller.GetSettings();

        // Assert
        Assert.Equal(
            (false, "sample", "24h"),
            (settings.HideCoreLogViewer, settings.DefaultSource, settings.DefaultTimeRange)
        );
    }

    [Fact]
    public void GetSettings_ConfiguredPinnedFacets_ReturnsThemInOrder()
    {
        // Arrange
        var controller = new GetSettingsController(
            Options.Create(
                new LogExplorerOptions { PinnedFacets = ["StatusCode", "SourceContext"] }
            )
        );

        // Act
        SettingsResponseModel settings = controller.GetSettings();

        // Assert
        Assert.Equal(["StatusCode", "SourceContext"], settings.PinnedFacets);
    }

    [Fact]
    public void GetSettings_ConfiguredCorrelationFields_ReturnsThemInOrder()
    {
        // Arrange
        var controller = new GetSettingsController(
            Options.Create(new LogExplorerOptions { CorrelationFields = ["RequestId", "@traceId"] })
        );

        // Act
        SettingsResponseModel settings = controller.GetSettings();

        // Assert
        Assert.Equal(["RequestId", "@traceId"], settings.CorrelationFields);
    }
}
