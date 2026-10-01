using Umbraco.Community.LogExplorer.Core.Results;

namespace Umbraco.Community.LogExplorer.Core.Query;

/// <summary>
/// Turns a <see cref="TimeRange"/> into the absolute <see cref="ResolvedRange"/> that actually
/// runs. Relative ranges are resolved against an injected clock so tests stay deterministic.
/// </summary>
public static class RelativeRange
{
    /// <summary>
    /// The relative ranges the UI offers (BRIEF §6.5), matched case-insensitively. Anything else is
    /// rejected rather than guessed, so a typo in a URL fails loudly instead of silently searching
    /// the wrong window.
    /// </summary>
    public static IReadOnlyDictionary<string, TimeSpan> Supported { get; } =
        new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase)
        {
            ["15m"] = TimeSpan.FromMinutes(15),
            ["1h"] = TimeSpan.FromHours(1),
            ["4h"] = TimeSpan.FromHours(4),
            ["24h"] = TimeSpan.FromHours(24),
            ["7d"] = TimeSpan.FromDays(7),
            ["30d"] = TimeSpan.FromDays(30),
        };

    /// <summary>
    /// Resolves <paramref name="range"/> to absolute bounds. A relative range ends at the clock's
    /// current UTC time; an absolute range without <c>To</c> also ends now.
    /// </summary>
    /// <param name="range">The requested range.</param>
    /// <param name="clock">Source of "now".</param>
    /// <returns>The range with inclusive <c>From</c> and exclusive <c>To</c>.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// The relative value is not one of <see cref="Supported"/>; neither a relative value nor
    /// <c>From</c> is given; or <c>From</c> is not before <c>To</c>.
    /// </exception>
    public static ResolvedRange Resolve(TimeRange range, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(clock);

        DateTimeOffset now = clock.GetUtcNow();

        if (range.Relative is not null)
        {
            if (!Supported.TryGetValue(range.Relative, out TimeSpan span))
            {
                throw new ArgumentException(
                    $"Unknown relative range '{range.Relative}'. Expected one of: {string.Join(", ", Supported.Keys)}.",
                    nameof(range)
                );
            }

            return new ResolvedRange(now - span, now);
        }

        if (range.From is null)
        {
            throw new ArgumentException(
                "A time range needs either a relative value or an absolute From.",
                nameof(range)
            );
        }

        DateTimeOffset to = range.To ?? now;
        if (range.From.Value >= to)
        {
            throw new ArgumentException(
                "The time range's From must be before its To.",
                nameof(range)
            );
        }

        return new ResolvedRange(range.From.Value, to);
    }
}
