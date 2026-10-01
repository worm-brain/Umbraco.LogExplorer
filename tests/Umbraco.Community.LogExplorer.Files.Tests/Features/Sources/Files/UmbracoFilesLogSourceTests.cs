using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Core.Sources;
using Umbraco.Community.LogExplorer.Features.Sources.Files;
using Umbraco.Community.LogExplorer.Files.Tests.TestSupport;

namespace Umbraco.Community.LogExplorer.Files.Tests.Features.Sources.Files;

/// <summary>
/// The <c>UmbracoFiles</c> source and its factory (#35): what the source adds on top of the
/// readers (alias, page size, native-query switch) and the rolling-interval warning. The shared
/// behaviour is covered by <see cref="UmbracoFilesContractTests"/>.
/// </summary>
public sealed class UmbracoFilesLogSourceTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Fact]
    public async Task QueryAsync_ZeroConfigurationSource_ReturnsRecordsTaggedWithItsAlias()
    {
        // Arrange
        WriteMinutes(3);
        ILogSource source = FileSources.Create(_directory.Path);

        // Act
        LogPage page = await source.QueryAsync(Query(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["files", "files", "files"], page.Records.Select(r => r.SourceAlias));
    }

    [Fact]
    public async Task QueryAsync_TakeAboveMaxPageSize_ReturnsAtMostMaxPageSize()
    {
        // Arrange
        WriteMinutes(UmbracoFilesLogSource.MaxPageSize + 1);
        ILogSource source = FileSources.Create(_directory.Path);

        // Act
        LogPage page = await source.QueryAsync(
            Query() with
            {
                Take = UmbracoFilesLogSource.MaxPageSize + 1,
            },
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Equal(UmbracoFilesLogSource.MaxPageSize, page.Records.Count);
    }

    [Fact]
    public async Task QueryAsync_NativeQuery_FiltersAsTheCoreLogViewer()
    {
        // Arrange
        WriteMinutes(5);
        ILogSource source = FileSources.Create(_directory.Path);

        // Act
        LogPage page = await source.QueryAsync(
            Query() with
            {
                NativeQuery = "Minute >= 3",
            },
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Equal(["Minute 4", "Minute 3"], page.Records.Select(r => r.Body));
    }

    [Fact]
    public async Task QueryAsync_InvalidNativeQuery_ThrowsInvalidNativeQueryException()
    {
        // Arrange
        WriteMinutes(1);
        ILogSource source = FileSources.Create(_directory.Path);

        // Act
        Task Act() =>
            source.QueryAsync(
                Query() with
                {
                    NativeQuery = "Minute >= (",
                },
                TestContext.Current.CancellationToken
            );

        // Assert
        await Assert.ThrowsAsync<InvalidNativeQueryException>(Act);
    }

    [Fact]
    public void Capabilities_NativeQueriesAllowed_DeclaresNativeQueryInSerilogExpressions()
    {
        // Arrange
        ILogSource source = FileSources.Create(_directory.Path);

        // Act
        LogSourceCapabilities capabilities = source.Capabilities;

        // Assert
        Assert.Equal(
            (true, "Serilog Expressions"),
            (capabilities.Supports(LogSourceFeatures.NativeQuery), capabilities.NativeLanguage)
        );
    }

    [Fact]
    public void Capabilities_NativeQueriesNotAllowed_DoesNotDeclareNativeQuery()
    {
        // Arrange
        ILogSource source = FileSources.Create(_directory.Path, NoNative());

        // Act
        LogSourceCapabilities capabilities = source.Capabilities;

        // Assert
        Assert.Equal(
            (false, (string?)null),
            (capabilities.Supports(LogSourceFeatures.NativeQuery), capabilities.NativeLanguage)
        );
    }

    [Fact]
    public async Task QueryAsync_NativeQueryWhenNotAllowed_ThrowsNotSupportedException()
    {
        // Arrange
        WriteMinutes(1);
        ILogSource source = FileSources.Create(_directory.Path, NoNative());

        // Act
        Task Act() =>
            source.QueryAsync(
                Query() with
                {
                    NativeQuery = "Minute >= 0",
                },
                TestContext.Current.CancellationToken
            );

        // Assert
        await Assert.ThrowsAsync<NotSupportedException>(Act);
    }

    [Fact]
    public async Task GetHistogramAsync_NativeQueryWhenNotAllowed_ThrowsNotSupportedException()
    {
        // Arrange
        WriteMinutes(1);
        ILogSource source = FileSources.Create(_directory.Path, NoNative());

        // Act
        Task Act() =>
            source.GetHistogramAsync(
                Query() with
                {
                    NativeQuery = "Minute >= 0",
                },
                30,
                TestContext.Current.CancellationToken
            );

        // Assert
        await Assert.ThrowsAsync<NotSupportedException>(Act);
    }

    [Fact]
    public async Task GetContextAsync_Records_CarryTheSourceAlias()
    {
        // Arrange
        WriteMinutes(3);
        ILogSource source = FileSources.Create(_directory.Path);
        LogPage page = await source.QueryAsync(Query(), TestContext.Current.CancellationToken);

        // Act
        ContextResult context = await source.GetContextAsync(
            page.Records[1].Id,
            1,
            1,
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Equal(
            ["files", "files", "files"],
            new[] { context.Before[0], context.Anchor, context.After[0] }.Select(r => r.SourceAlias)
        );
    }

    [Fact]
    public async Task GetPatternsAsync_Samples_CarryTheSourceAlias()
    {
        // Arrange
        WriteMinutes(2);
        ILogSource source = FileSources.Create(_directory.Path);

        // Act
        PatternResult patterns = await source.GetPatternsAsync(
            Query(),
            5,
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Equal("files", Assert.Single(patterns.Patterns).Sample.SourceAlias);
    }

    [Fact]
    public void DisplayName_NotConfigured_IsTheAlias()
    {
        // Arrange
        ILogSource source = FileSources.Create(
            _directory.Path,
            new LogSourceDefinition { Alias = "local", Type = "UmbracoFiles" }
        );

        // Act
        string name = source.DisplayName;

        // Assert
        Assert.Equal("local", name);
    }

    [Fact]
    public void Create_DailyRollingInterval_LogsNothing()
    {
        // Arrange
        var logger = new FakeLogger<UmbracoFilesLogSourceFactory>();
        UmbracoFilesLogSourceFactory factory = FileSources.Factory(_directory.Path, logger: logger);

        // Act
        _ = factory.Create(Definition(), new ServiceCollection().BuildServiceProvider());

        // Assert
        Assert.Equal(0, logger.Collector.Count);
    }

    [Fact]
    public void Create_HourlyRollingInterval_WarnsThatOnlyDailyFilesAreRead()
    {
        // Arrange
        var logger = new FakeLogger<UmbracoFilesLogSourceFactory>();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Serilog:WriteTo:0:Name"] = "UmbracoFile",
                    ["Serilog:WriteTo:0:Args:RollingInterval"] = "Hour",
                }
            )
            .Build();
        UmbracoFilesLogSourceFactory factory = FileSources.Factory(
            _directory.Path,
            configuration,
            logger
        );

        // Act
        _ = factory.Create(Definition(), new ServiceCollection().BuildServiceProvider());

        // Assert
        Assert.Equal(LogLevel.Warning, logger.LatestRecord.Level);
    }

    private static LogQuery Query() => new() { Range = new TimeRange(Noon, Noon.AddDays(1), null) };

    private static LogSourceDefinition Definition() =>
        new() { Alias = "files", Type = UmbracoFilesLogSource.SourceType };

    private static LogSourceDefinition NoNative() => Definition() with { AllowNativeQuery = false };

    // One WORM event a minute from noon, each with a "Minute" property; bodies read "Minute N".
    private void WriteMinutes(int count) =>
        _directory.Write(
            Aggregators.DayFile,
            LogLines.File([
                .. Enumerable
                    .Range(0, count)
                    .Select(minute =>
                        LogLines.Event(
                            Noon.AddMinutes(minute),
                            "Minute {Minute}",
                            extraProperties: $"\"Minute\":{minute}"
                        )
                    ),
            ])
        );
}
