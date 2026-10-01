using Umbraco.Community.LogExplorer.Core.Severity;

namespace Umbraco.Community.LogExplorer.Core.Tests.Severity;

/// <summary>Locks in the BRIEF §9.2 table and the ADR 0004 level bands.</summary>
public class SeverityMapTests
{
    [Theory]
    [InlineData(1, "trace")]
    [InlineData(4, "trace")]
    [InlineData(5, "debug")]
    [InlineData(9, "info")]
    [InlineData(12, "info")]
    [InlineData(13, "warn")]
    [InlineData(17, "error")]
    [InlineData(21, "fatal")]
    [InlineData(24, "fatal")]
    [InlineData(0, "info")]
    [InlineData(99, "info")]
    public void ToShortName_SeverityNumber_ReturnsItsBandName(int number, string expected)
    {
        // Act
        string shortName = SeverityMap.ToShortName(number);

        // Assert
        Assert.Equal(expected, shortName);
    }

    [Theory]
    [InlineData(13, "WARN")]
    [InlineData(0, "—")]
    [InlineData(-1, "—")]
    public void ToDisplayText_SeverityNumber_ReturnsUpperCaseNameOrDash(int number, string expected)
    {
        // Act
        string text = SeverityMap.ToDisplayText(number);

        // Assert
        Assert.Equal(expected, text);
    }

    [Theory]
    [InlineData("trace", 1)]
    [InlineData("debug", 5)]
    [InlineData("INFO", 9)]
    [InlineData("warn", 13)]
    [InlineData("error", 17)]
    [InlineData("fatal", 21)]
    [InlineData("loud", 0)]
    [InlineData(null, 0)]
    public void FromShortName_Name_ReturnsBandStartOrZero(string? name, int expected)
    {
        // Act
        int number = SeverityMap.FromShortName(name);

        // Assert
        Assert.Equal(expected, number);
    }

    [Theory]
    [InlineData("Verbose", 1)]
    [InlineData("Debug", 5)]
    [InlineData("Information", 9)]
    [InlineData("warning", 13)]
    [InlineData("Error", 17)]
    [InlineData("Fatal", 21)]
    [InlineData("Critical", 0)]
    [InlineData(null, 0)]
    public void FromSerilog_LevelName_ReturnsOtelNumber(string? level, int expected)
    {
        // Act
        int number = SeverityMap.FromSerilog(level);

        // Assert
        Assert.Equal(expected, number);
    }

    [Theory]
    [InlineData(1, "Verbose")]
    [InlineData(5, "Debug")]
    [InlineData(9, "Information")]
    [InlineData(13, "Warning")]
    [InlineData(17, "Error")]
    [InlineData(21, "Fatal")]
    [InlineData(0, "Information")]
    public void ToSerilog_SeverityNumber_ReturnsSerilogName(int number, string expected)
    {
        // Act
        string level = SeverityMap.ToSerilog(number);

        // Assert
        Assert.Equal(expected, level);
    }

    [Theory]
    [InlineData("Trace", 1)]
    [InlineData("Debug", 5)]
    [InlineData("Information", 9)]
    [InlineData("Warning", 13)]
    [InlineData("Error", 17)]
    [InlineData("critical", 21)]
    [InlineData("None", 0)]
    public void FromMicrosoftLogLevel_LevelName_ReturnsOtelNumber(string level, int expected)
    {
        // Act
        int number = SeverityMap.FromMicrosoftLogLevel(level);

        // Assert
        Assert.Equal(expected, number);
    }

    [Theory]
    [InlineData(1, "Trace")]
    [InlineData(5, "Debug")]
    [InlineData(9, "Information")]
    [InlineData(13, "Warning")]
    [InlineData(17, "Error")]
    [InlineData(21, "Critical")]
    public void ToMicrosoftLogLevel_SeverityNumber_ReturnsMelName(int number, string expected)
    {
        // Act
        string level = SeverityMap.ToMicrosoftLogLevel(number);

        // Assert
        Assert.Equal(expected, level);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 9)]
    [InlineData(2, 13)]
    [InlineData(3, 17)]
    [InlineData(4, 21)]
    [InlineData(5, 0)]
    [InlineData(-1, 0)]
    public void FromAppInsights_SeverityLevel_ReturnsOtelNumber(int level, int expected)
    {
        // Act
        int number = SeverityMap.FromAppInsights(level);

        // Assert
        Assert.Equal(expected, number);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(5, 0)]
    [InlineData(9, 1)]
    [InlineData(13, 2)]
    [InlineData(17, 3)]
    [InlineData(21, 4)]
    [InlineData(0, 1)]
    public void ToAppInsights_SeverityNumber_ReturnsSeverityLevel(int number, int expected)
    {
        // Act
        int level = SeverityMap.ToAppInsights(number);

        // Assert
        Assert.Equal(expected, level);
    }

    [Fact]
    public void GetBand_Warn_Covers13To16()
    {
        // Act
        SeverityBand band = SeverityMap.GetBand("warn");

        // Assert
        Assert.Equal(new SeverityBand(13, 16), band);
    }

    [Fact]
    public void GetBand_UnknownName_ThrowsArgumentException()
    {
        // Act
        static void Act() => SeverityMap.GetBand("loud");

        // Assert
        Assert.Throws<ArgumentException>(Act);
    }

    [Fact]
    public void ToBands_NonContiguousSet_ReturnsEachBandInSeverityOrder()
    {
        // Act
        IReadOnlyList<SeverityBand> bands = SeverityMap.ToBands(["error", "DEBUG", "nonsense"]);

        // Assert
        Assert.Equal([new SeverityBand(5, 8), new SeverityBand(17, 20)], bands);
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(19, true)]
    [InlineData(9, false)]
    [InlineData(13, false)]
    public void IsInLevels_NonContiguousSet_KeepsOnlyThoseBands(int number, bool expected)
    {
        // Arrange
        var levels = new HashSet<string> { "debug", "error" };

        // Act
        bool kept = SeverityMap.IsInLevels(number, levels);

        // Assert
        Assert.Equal(expected, kept);
    }

    [Fact]
    public void IsInLevels_UnspecifiedSeverityAndInfoSelected_IsKept()
    {
        // Act
        bool kept = SeverityMap.IsInLevels(0, new HashSet<string> { "info" });

        // Assert
        Assert.True(kept);
    }

    [Fact]
    public void IsInLevels_UnspecifiedSeverityAndInfoNotSelected_IsDropped()
    {
        // Act
        bool kept = SeverityMap.IsInLevels(0, new HashSet<string> { "warn" });

        // Assert
        Assert.False(kept);
    }

    [Fact]
    public void IsInLevels_NullSet_KeepsEverything()
    {
        // Act
        bool kept = SeverityMap.IsInLevels(1, null);

        // Assert
        Assert.True(kept);
    }
}
