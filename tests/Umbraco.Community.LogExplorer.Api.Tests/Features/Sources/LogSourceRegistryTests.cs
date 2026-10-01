using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Community.LogExplorer.Core.Sources;
using Umbraco.Community.LogExplorer.Features.Sources;

namespace Umbraco.Community.LogExplorer.Api.Tests.Features.Sources;

/// <summary>
/// The registry builds sources from configuration with per-source validation (#24) and applies
/// the BRIEF §12 visibility rule. Factories are stubs that return a substitute source.
/// </summary>
public class LogSourceRegistryTests
{
    private static readonly UserContext Editor = User("editor");
    private static readonly UserContext Admin = User("admin");

    private readonly FakeLogger<LogSourceRegistry> _logger = new();

    [Fact]
    public void GetVisibleSources_NoSourcesConfiguredAndNoFilesProvider_ReturnsNone()
    {
        // Arrange
        LogSourceRegistry registry = CreateRegistry([], StubFactory("Fake"));

        // Act
        IReadOnlyList<ILogSource> sources = registry.GetVisibleSources(Editor);

        // Assert
        Assert.Empty(sources);
    }

    [Fact]
    public void GetVisibleSources_NoSourcesConfiguredAndNoFilesProvider_LogsTheDefaultAsUnavailable()
    {
        // Arrange
        LogSourceRegistry registry = CreateRegistry([], StubFactory("Fake"));

        // Act
        _ = registry.GetVisibleSources(Editor);

        // Assert
        Assert.Equal(LogLevel.Warning, _logger.LatestRecord.Level);
    }

    [Fact]
    public void GetVisibleSources_NoSourcesConfiguredWithFilesProvider_ReturnsTheDefaultFilesSource()
    {
        // Arrange
        LogSourceRegistry registry = CreateRegistry([], StubFactory("UmbracoFiles"));

        // Act
        IReadOnlyList<ILogSource> sources = registry.GetVisibleSources(Editor);

        // Assert
        Assert.Equal(["files"], sources.Select(source => source.Alias));
    }

    [Fact]
    public void GetVisibleSources_UnknownType_SkipsOnlyThatSource()
    {
        // Arrange
        LogSourceRegistry registry = CreateRegistry(
            [
                Definition("good", "Fake"),
                Definition("bad", "Nope"),
                Definition("also-good", "Fake"),
            ],
            StubFactory("Fake")
        );

        // Act
        IReadOnlyList<ILogSource> sources = registry.GetVisibleSources(Editor);

        // Assert
        Assert.Equal(["good", "also-good"], sources.Select(source => source.Alias));
    }

    [Fact]
    public void GetVisibleSources_UnknownType_LogsOneErrorNamingTheSource()
    {
        // Arrange
        LogSourceRegistry registry = CreateRegistry(
            [Definition("bad", "Nope")],
            StubFactory("Fake")
        );

        // Act
        _ = registry.GetVisibleSources(Editor);

        // Assert
        FakeLogRecord error = Assert.Single(
            _logger.Collector.GetSnapshot(),
            record => record.Level == LogLevel.Error
        );
        Assert.Contains("'bad'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetVisibleSources_DuplicateAlias_KeepsTheFirstOnly()
    {
        // Arrange
        LogSourceRegistry registry = CreateRegistry(
            [
                Definition("same", "Fake", displayName: "First"),
                Definition("SAME", "Fake", displayName: "Second"),
            ],
            StubFactory("Fake")
        );

        // Act
        IReadOnlyList<ILogSource> sources = registry.GetVisibleSources(Editor);

        // Assert
        Assert.Equal(["First"], sources.Select(source => source.DisplayName));
    }

    [Fact]
    public void GetVisibleSources_MissingAlias_SkipsThatSource()
    {
        // Arrange
        LogSourceRegistry registry = CreateRegistry(
            [Definition("", "Fake"), Definition("ok", "Fake")],
            StubFactory("Fake")
        );

        // Act
        IReadOnlyList<ILogSource> sources = registry.GetVisibleSources(Editor);

        // Assert
        Assert.Equal(["ok"], sources.Select(source => source.Alias));
    }

    [Fact]
    public void GetVisibleSources_FactoryRejectsSettings_SkipsOnlyThatSource()
    {
        // Arrange
        ILogSourceFactory picky = StubFactory("Picky");
        picky
            .Create(
                Arg.Is<LogSourceDefinition>(definition => definition.Alias == "broken"),
                Arg.Any<IServiceProvider>()
            )
            .Returns(_ => throw new ArgumentException("WorkspaceId is required."));
        LogSourceRegistry registry = CreateRegistry(
            [Definition("broken", "Picky"), Definition("fine", "Picky")],
            picky
        );

        // Act
        IReadOnlyList<ILogSource> sources = registry.GetVisibleSources(Editor);

        // Assert
        Assert.Equal(["fine"], sources.Select(source => source.Alias));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void GetForUser_AllowNativeQuery_WrapsOnlyWhenNativeQueriesAreOff(
        bool allowNativeQuery,
        bool wrapped
    )
    {
        // Arrange
        LogSourceRegistry registry = CreateRegistry(
            [Definition("one", "Fake") with { AllowNativeQuery = allowNativeQuery }],
            StubFactory("Fake")
        );

        // Act
        ILogSource source = registry.GetForUser("one", Editor);

        // Assert
        Assert.Equal(wrapped, source is NativeQueryDisabledSource);
    }

    [Fact]
    public void GetForUser_UnknownAlias_ThrowsKeyNotFoundException()
    {
        // Arrange
        LogSourceRegistry registry = CreateRegistry(
            [Definition("one", "Fake")],
            StubFactory("Fake")
        );

        // Act
        void Act() => registry.GetForUser("missing", Editor);

        // Assert
        Assert.Throws<KeyNotFoundException>(Act);
    }

    [Fact]
    public void GetForUser_HiddenSource_ThrowsForbiddenSourceExceptionWithTheAlias()
    {
        // Arrange
        LogSourceRegistry registry = CreateRegistry(
            [Definition("prod", "Fake", sensitive: true)],
            StubFactory("Fake")
        );

        // Act
        ForbiddenSourceException ex = Assert.Throws<ForbiddenSourceException>(() =>
            registry.GetForUser("prod", Editor)
        );

        // Assert
        Assert.Equal("prod", ex.SourceAlias);
    }

    [Fact]
    public void GetForUser_VisibleSource_ReturnsItRegardlessOfAliasCase()
    {
        // Arrange
        LogSourceRegistry registry = CreateRegistry(
            [Definition("prod", "Fake", sensitive: true)],
            StubFactory("Fake")
        );

        // Act
        ILogSource source = registry.GetForUser("PROD", Admin);

        // Assert
        Assert.Equal("prod", source.Alias);
    }

    [Theory]
    [InlineData(false, new string[0], "editor", true)]
    [InlineData(true, new string[0], "editor", false)]
    [InlineData(true, new string[0], "admin", true)]
    [InlineData(false, new[] { "ops" }, "editor", false)]
    [InlineData(false, new[] { "ops" }, "OPS", true)]
    [InlineData(true, new[] { "ops" }, "ops", true)]
    public void IsVisible_GroupsAndSensitivity_FollowTheBriefRule(
        bool sensitive,
        string[] allowedGroups,
        string userGroup,
        bool expected
    )
    {
        // Arrange
        LogSourceDefinition definition = Definition("source", "Fake", sensitive, allowedGroups);

        // Act
        bool visible = LogSourceRegistry.IsVisible(definition, User(userGroup));

        // Assert
        Assert.Equal(expected, visible);
    }

    private LogSourceRegistry CreateRegistry(
        LogSourceDefinition[] sources,
        ILogSourceFactory factory
    ) =>
        new(
            Options.Create(new LogExplorerOptions { Sources = [.. sources] }),
            [factory],
            Substitute.For<IServiceProvider>(),
            _logger
        );

    // A factory whose sources echo the definition's alias and display name.
    private static ILogSourceFactory StubFactory(string type)
    {
        ILogSourceFactory factory = Substitute.For<ILogSourceFactory>();
        factory.Type.Returns(type);
        factory
            .Create(Arg.Any<LogSourceDefinition>(), Arg.Any<IServiceProvider>())
            .Returns(call =>
            {
                var definition = call.Arg<LogSourceDefinition>();
                ILogSource source = Substitute.For<ILogSource>();
                source.Alias.Returns(definition.Alias);
                source.DisplayName.Returns(definition.DisplayName);
                return source;
            });
        return factory;
    }

    private static LogSourceDefinition Definition(
        string alias,
        string type,
        bool sensitive = false,
        string[]? allowedGroups = null,
        string displayName = ""
    ) =>
        new()
        {
            Alias = alias,
            Type = type,
            Sensitive = sensitive,
            AllowedUserGroups = allowedGroups ?? [],
            DisplayName = displayName,
        };

    private static UserContext User(string group) =>
        new(Guid.NewGuid(), new HashSet<string>([group], StringComparer.OrdinalIgnoreCase));
}
