using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Core.Tests.TestSupport;

namespace Umbraco.Community.LogExplorer.Core.Tests.Query;

public class RelativeRangeTests
{
    private static readonly FixedClock Clock = new(FixedClock.Noon);

    [Theory]
    [InlineData("15m", "2026-09-02T11:45:00Z")]
    [InlineData("1h", "2026-09-02T11:00:00Z")]
    [InlineData("4h", "2026-09-02T08:00:00Z")]
    [InlineData("24h", "2026-09-01T12:00:00Z")]
    [InlineData("7d", "2026-08-26T12:00:00Z")]
    [InlineData("30d", "2026-08-03T12:00:00Z")]
    public void Resolve_SupportedRelativeValue_EndsNowAndStartsThatFarBack(
        string relative,
        string expectedFrom
    )
    {
        // Arrange
        var range = new TimeRange(null, null, relative);

        // Act
        ResolvedRange resolved = RelativeRange.Resolve(range, Clock);

        // Assert
        Assert.Equal(
            new ResolvedRange(DateTimeOffset.Parse(expectedFrom, null), FixedClock.Noon),
            resolved
        );
    }

    [Fact]
    public void Resolve_RelativeAndAbsoluteBothSet_RelativeWins()
    {
        // Arrange
        var range = new TimeRange(FixedClock.Noon.AddDays(-3), FixedClock.Noon.AddDays(-2), "1h");

        // Act
        ResolvedRange resolved = RelativeRange.Resolve(range, Clock);

        // Assert
        Assert.Equal(new ResolvedRange(FixedClock.Noon.AddHours(-1), FixedClock.Noon), resolved);
    }

    [Theory]
    [InlineData("2h")]
    [InlineData("yesterday")]
    [InlineData("")]
    public void Resolve_UnknownRelativeValue_ThrowsArgumentException(string relative)
    {
        // Arrange
        var range = new TimeRange(null, null, relative);

        // Act
        void Act() => RelativeRange.Resolve(range, Clock);

        // Assert
        Assert.Throws<ArgumentException>(Act);
    }

    [Fact]
    public void Resolve_AbsoluteRange_ReturnsItUnchanged()
    {
        // Arrange
        DateTimeOffset from = FixedClock.Noon.AddDays(-2);
        DateTimeOffset to = FixedClock.Noon.AddDays(-1);

        // Act
        ResolvedRange resolved = RelativeRange.Resolve(new TimeRange(from, to, null), Clock);

        // Assert
        Assert.Equal(new ResolvedRange(from, to), resolved);
    }

    [Fact]
    public void Resolve_AbsoluteRangeWithoutTo_EndsNow()
    {
        // Arrange
        DateTimeOffset from = FixedClock.Noon.AddHours(-2);

        // Act
        ResolvedRange resolved = RelativeRange.Resolve(new TimeRange(from, null, null), Clock);

        // Assert
        Assert.Equal(new ResolvedRange(from, FixedClock.Noon), resolved);
    }

    [Fact]
    public void Resolve_NeitherRelativeNorFrom_ThrowsArgumentException()
    {
        // Arrange
        var range = new TimeRange(null, FixedClock.Noon, null);

        // Act
        void Act() => RelativeRange.Resolve(range, Clock);

        // Assert
        Assert.Throws<ArgumentException>(Act);
    }

    [Fact]
    public void Resolve_FromNotBeforeTo_ThrowsArgumentException()
    {
        // Arrange
        var range = new TimeRange(FixedClock.Noon, FixedClock.Noon, null);

        // Act
        void Act() => RelativeRange.Resolve(range, Clock);

        // Assert
        Assert.Throws<ArgumentException>(Act);
    }
}
