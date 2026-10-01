using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Core.Sources;
using Umbraco.Community.LogExplorer.Features.Sources.Fake;

namespace Umbraco.Community.LogExplorer.Api.Tests.Features.Sources.Fake;

/// <summary>
/// The <c>Fake</c> provider type creates sample-data sources that keep tracking the current time.
/// </summary>
public class FakeLogSourceFactoryTests
{
    private static readonly LogQuery LastHour = new() { Range = new TimeRange(null, null, "1h") };

    private readonly FakeTimeProvider _clock = new(
        new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero)
    );

    [Fact]
    public void Create_DefinitionWithoutDisplayName_UsesTheAlias()
    {
        // Arrange
        var factory = new FakeLogSourceFactory(_clock);

        // Act
        ILogSource source = factory.Create(
            new LogSourceDefinition { Alias = "sample", Type = "Fake" },
            Substitute.For<IServiceProvider>()
        );

        // Assert
        Assert.Equal("sample", source.DisplayName);
    }

    [Fact]
    public async Task Create_SampleHoursSetting_GeneratesThatManyHours()
    {
        // Arrange
        var factory = new FakeLogSourceFactory(_clock);

        // Act
        ILogSource source = factory.Create(
            new LogSourceDefinition
            {
                Alias = "sample",
                Type = "Fake",
                Settings = new Dictionary<string, string> { ["SampleHours"] = "2" },
            },
            Substitute.For<IServiceProvider>()
        );

        // Assert
        LogPage page = await source.QueryAsync(
            new LogQuery { Range = new TimeRange(null, null, "4h"), Take = 1 },
            TestContext.Current.CancellationToken
        );
        Assert.Equal(238 * 2, page.TotalCount);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("many")]
    public void Create_InvalidSampleHoursSetting_ThrowsArgumentException(string value)
    {
        // Arrange
        var factory = new FakeLogSourceFactory(_clock);

        // Act
        Exception? thrown = Record.Exception(() =>
            factory.Create(
                new LogSourceDefinition
                {
                    Alias = "sample",
                    Type = "Fake",
                    Settings = new Dictionary<string, string> { ["SampleHours"] = value },
                },
                Substitute.For<IServiceProvider>()
            )
        );

        // Assert
        Assert.IsType<ArgumentException>(thrown);
    }

    [Fact]
    public async Task QueryAsync_TwoHoursAfterCreation_StillReturnsEntriesFromTheLastHour()
    {
        // Arrange
        var factory = new FakeLogSourceFactory(_clock);
        ILogSource source = factory.Create(
            new LogSourceDefinition { Alias = "sample", Type = "Fake" },
            Substitute.For<IServiceProvider>()
        );
        _clock.Advance(TimeSpan.FromHours(2));

        // Act
        LogPage page = await source.QueryAsync(LastHour, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(page.Records);
    }
}
