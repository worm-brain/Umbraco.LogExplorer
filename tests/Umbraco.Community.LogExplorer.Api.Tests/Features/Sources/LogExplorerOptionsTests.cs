using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Umbraco.Community.LogExplorer.Features.Sources;

namespace Umbraco.Community.LogExplorer.Api.Tests.Features.Sources;

/// <summary>
/// The <c>LogExplorer</c> section binds with the BRIEF §13 defaults, and configured lists replace
/// those defaults instead of being appended to them.
/// </summary>
public class LogExplorerOptionsTests
{
    [Fact]
    public void Bind_EmptySection_HidesTheCoreLogViewer()
    {
        // Arrange
        IConfiguration configuration = Configuration([]);

        // Act
        LogExplorerOptions options = Bind(configuration);

        // Assert
        Assert.True(options.HideCoreLogViewer);
    }

    [Fact]
    public void Bind_EmptySection_UsesTheDefaultCorrelationFields()
    {
        // Arrange
        IConfiguration configuration = Configuration([]);

        // Act
        LogExplorerOptions options = Bind(configuration);

        // Assert
        Assert.Equal(["@traceId", "RequestId", "HttpRequestId"], options.CorrelationFields);
    }

    [Fact]
    public void Bind_EmptySection_UsesTheDefaultPinnedFacets()
    {
        // Arrange
        IConfiguration configuration = Configuration([]);

        // Act
        LogExplorerOptions options = Bind(configuration);

        // Assert
        Assert.Equal(LogExplorerOptionsDefaults.PinnedFacets, options.PinnedFacets);
    }

    [Fact]
    public void Bind_ConfiguredPinnedFacets_ReplaceTheDefaults()
    {
        // Arrange
        IConfiguration configuration = Configuration(
            new() { ["LogExplorer:PinnedFacets:0"] = "RequestPath" }
        );

        // Act
        LogExplorerOptions options = Bind(configuration);

        // Assert
        Assert.Equal(["RequestPath"], options.PinnedFacets);
    }

    [Fact]
    public void Bind_SourceWithGroupsAndSettings_BindsEveryField()
    {
        // Arrange
        IConfiguration configuration = Configuration(
            new()
            {
                ["LogExplorer:Sources:0:Alias"] = "prod-ai",
                ["LogExplorer:Sources:0:Type"] = "ApplicationInsights",
                ["LogExplorer:Sources:0:Sensitive"] = "true",
                ["LogExplorer:Sources:0:AllowedUserGroups:0"] = "admin",
                ["LogExplorer:Sources:0:Settings:WorkspaceId"] = "abc",
            }
        );

        // Act
        LogExplorerOptions options = Bind(configuration);

        // Assert
        var source = Assert.Single(options.Sources);
        Assert.Equal(
            ("prod-ai", "ApplicationInsights", true, "admin", "abc"),
            (
                source.Alias,
                source.Type,
                source.Sensitive,
                source.AllowedUserGroups.Single(),
                source.Settings["WorkspaceId"]
            )
        );
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    // Binds the way the composer does: Configure on the section plus the post-configure defaults.
    private static LogExplorerOptions Bind(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.Configure<LogExplorerOptions>(
            configuration.GetSection(LogExplorerOptions.SectionName)
        );
        services.AddSingleton<
            IPostConfigureOptions<LogExplorerOptions>,
            LogExplorerOptionsDefaults
        >();
        return services
            .BuildServiceProvider()
            .GetRequiredService<IOptions<LogExplorerOptions>>()
            .Value;
    }
}
