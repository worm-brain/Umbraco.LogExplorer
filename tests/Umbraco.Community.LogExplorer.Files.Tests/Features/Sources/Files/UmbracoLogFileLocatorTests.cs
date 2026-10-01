using NSubstitute;
using Umbraco.Cms.Core.Logging;
using Umbraco.Community.LogExplorer.Features.Sources.Files;
using Umbraco.Community.LogExplorer.Files.Tests.TestSupport;

namespace Umbraco.Community.LogExplorer.Files.Tests.Features.Sources.Files;

/// <summary>
/// The locator derives its pattern from Umbraco's logging configuration (#30). The configuration
/// is a substitute shaped like Umbraco's default: format <c>UmbracoTraceLog.{0}..json</c> with the
/// resolved machine name as the only argument.
/// </summary>
public class UmbracoLogFileLocatorTests
{
    private const string DefaultFormat = "UmbracoTraceLog.{0}..json";

    [Fact]
    public void GetFiles_DefaultFormat_ReturnsEveryMachinesFilesOldestFirst()
    {
        // Arrange
        UmbracoLogFileLocator locator = CreateLocator(
            LogFixtures.Directory,
            DefaultFormat,
            ["WORM"]
        );

        // Act
        IReadOnlyList<LogFile> files = locator.GetFiles();

        // Assert
        Assert.Equal(
            [
                "UmbracoTraceLog.WORM.20260929.json",
                "UmbracoTraceLog.NODE2.20260930.json",
                "UmbracoTraceLog.NODE2.20260930_001.json",
                "UmbracoTraceLog.WORM.20260930.json",
                "UmbracoTraceLog.WORM.20261001.json",
            ],
            files.Select(file => file.FileName)
        );
    }

    [Fact]
    public void GetFiles_RolledFile_ParsesMachineDateAndRollIndexFromTheName()
    {
        // Arrange
        UmbracoLogFileLocator locator = CreateLocator(
            LogFixtures.Directory,
            DefaultFormat,
            ["WORM"]
        );

        // Act
        LogFile rolled = locator.GetFiles().Single(file => file.FileName == LogFixtures.RolledFile);

        // Assert
        Assert.Equal(
            new LogFile(
                LogFixtures.PathOf(LogFixtures.RolledFile),
                LogFixtures.RolledFile,
                "NODE2",
                new DateOnly(2026, 9, 30),
                1
            ),
            rolled
        );
    }

    [Fact]
    public void GetFiles_UnrolledFile_HasRollIndexZero()
    {
        // Arrange
        UmbracoLogFileLocator locator = CreateLocator(
            LogFixtures.Directory,
            DefaultFormat,
            ["WORM"]
        );

        // Act
        LogFile file = locator.GetFiles().Single(file => file.FileName == LogFixtures.CrlfFile);

        // Assert
        Assert.Equal(0, file.RollIndex);
    }

    [Theory]
    [InlineData("UmbracoTraceLog.WORM.json")]
    [InlineData("UmbracoTraceLog.WORM.2026100.json")]
    [InlineData("Other.WORM.20261001.json")]
    [InlineData("UmbracoTraceLog.WORM.20261001.txt")]
    [InlineData("UmbracoTraceLog.WORM.20261399.json")]
    public void GetFiles_NameNotMatchingTheFormat_IsIgnored(string decoy)
    {
        // Arrange
        UmbracoLogFileLocator locator = CreateLocator(
            LogFixtures.Directory,
            DefaultFormat,
            ["WORM"]
        );

        // Act
        IReadOnlyList<LogFile> files = locator.GetFiles();

        // Assert
        Assert.DoesNotContain(files, file => file.FileName == decoy);
    }

    [Fact]
    public void GetFiles_MissingDirectory_ReturnsNoFiles()
    {
        // Arrange
        string missing = Path.Combine(Path.GetTempPath(), "log-explorer-missing-" + Guid.NewGuid());
        UmbracoLogFileLocator locator = CreateLocator(missing, DefaultFormat, ["WORM"]);

        // Act
        IReadOnlyList<LogFile> files = locator.GetFiles();

        // Assert
        Assert.Empty(files);
    }

    [Fact]
    public void GetFiles_EnvironmentPlaceholder_ReturnsOnlyThisEnvironmentsFilesForEveryMachine()
    {
        // Arrange
        using var directory = new TempDirectory();
        directory.Write("Log.Staging.WORM.20261001.json", []);
        directory.Write("Log.Staging.NODE2.20261001.json", []);
        directory.Write("Log.Production.WORM.20261001.json", []);
        UmbracoLogFileLocator locator = CreateLocator(
            directory.Path,
            "Log.{0}.{1}..json",
            ["Staging", "WORM"]
        );

        // Act
        IReadOnlyList<LogFile> files = locator.GetFiles();

        // Assert
        Assert.Equal(["NODE2", "WORM"], files.Select(file => file.MachineName));
    }

    [Fact]
    public void GetFiles_FormatWithoutMachinePlaceholder_HasNoMachineName()
    {
        // Arrange
        using var directory = new TempDirectory();
        directory.Write("site.20261001.json", []);
        UmbracoLogFileLocator locator = CreateLocator(directory.Path, "site..json", []);

        // Act
        LogFile file = Assert.Single(locator.GetFiles());

        // Assert
        Assert.Null(file.MachineName);
    }

    [Fact]
    public void ParseFileName_NameOfAFileThatDoesNotExist_ParsesItIntoTheLogDirectory()
    {
        // Arrange
        UmbracoLogFileLocator locator = CreateLocator(
            LogFixtures.Directory,
            DefaultFormat,
            ["WORM"]
        );

        // Act
        LogFile? file = locator.ParseFileName("UmbracoTraceLog.GONE.20250101_002.json");

        // Assert
        Assert.Equal(
            new LogFile(
                LogFixtures.PathOf("UmbracoTraceLog.GONE.20250101_002.json"),
                "UmbracoTraceLog.GONE.20250101_002.json",
                "GONE",
                new DateOnly(2025, 1, 1),
                2
            ),
            file
        );
    }

    [Fact]
    public void ParseFileName_NameWithADirectory_ReturnsNull()
    {
        // Arrange
        UmbracoLogFileLocator locator = CreateLocator(
            LogFixtures.Directory,
            DefaultFormat,
            ["WORM"]
        );

        // Act
        LogFile? file = locator.ParseFileName("UmbracoTraceLog.x/../../WORM.20261001.json");

        // Assert
        Assert.Null(file);
    }

    private static UmbracoLogFileLocator CreateLocator(
        string directory,
        string format,
        string[] arguments
    )
    {
        var configuration = Substitute.For<ILoggingConfiguration>();
        configuration.LogDirectory.Returns(directory);
        configuration.LogFileNameFormat.Returns(format);
        configuration.GetLogFileNameFormatArguments().Returns(arguments);
        return new UmbracoLogFileLocator(configuration, currentMachineName: "WORM");
    }
}
