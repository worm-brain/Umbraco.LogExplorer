namespace Umbraco.Community.LogExplorer.Core.Tests.TestSupport;

/// <summary>A clock frozen at one instant, so time-dependent results are exact.</summary>
internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public static readonly DateTimeOffset Noon = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => now;
}
