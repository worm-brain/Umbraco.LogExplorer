using System.Text.Json;
using LogExplorer.Samples.LogGenerator;

namespace LogExplorer.Samples.Tests.LogGenerator;

/// <summary>
/// Checks the file layout the generator produces for the files provider to cope with: a second
/// machine's base file and its <c>_001</c> roll, a truncated last line, and no repeat on restart.
/// Each test writes into its own temporary directory.
/// </summary>
public sealed class FileScenariosTests : IDisposable
{
    private const string Machine = "TEST-NODE2";
    private readonly string _directory = Directory
        .CreateTempSubdirectory("le-file-scenarios-")
        .FullName;

    /// <inheritdoc />
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Write_EmptyDirectory_CreatesBaseAndRolledFiles()
    {
        // Arrange
        string today = DateTime.Now.ToString(
            "yyyyMMdd",
            System.Globalization.CultureInfo.InvariantCulture
        );

        // Act
        FileScenarios.Write(_directory, Machine, new Random(1));

        // Assert
        string[] names = Directory
            .GetFiles(_directory)
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray()!;
        Assert.Equal(
            [
                $"UmbracoTraceLog.{Machine}.{today}.json",
                $"UmbracoTraceLog.{Machine}.{today}_001.json",
            ],
            names
        );
    }

    [Fact]
    public void Write_EmptyDirectory_LeavesOnlyTheLastLineOfTheRolledFileUnparseable()
    {
        // Arrange
        FileScenarios.Write(_directory, Machine, new Random(1));
        string rolled = Directory.GetFiles(_directory, "*_001.json").Single();

        // Act
        string[] lines = File.ReadAllLines(rolled);
        bool[] parses = lines.Select(TryParse).ToArray();

        // Assert
        Assert.Equal([.. Enumerable.Repeat(true, lines.Length - 1), false], parses);
    }

    [Fact]
    public void Write_CalledTwiceOnTheSameDay_DoesNotWriteAgain()
    {
        // Arrange
        FileScenarios.Write(_directory, Machine, new Random(1));
        long sizeAfterFirstRun = Directory
            .GetFiles(_directory)
            .Sum(path => new FileInfo(path).Length);

        // Act
        FileScenarios.Write(_directory, Machine, new Random(1));

        // Assert
        Assert.Equal(
            sizeAfterFirstRun,
            Directory.GetFiles(_directory).Sum(path => new FileInfo(path).Length)
        );
    }

    private static bool TryParse(string line)
    {
        try
        {
            using var _ = JsonDocument.Parse(line);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
