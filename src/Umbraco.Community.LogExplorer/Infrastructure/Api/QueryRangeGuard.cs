using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Core.Sources;

namespace Umbraco.Community.LogExplorer.Infrastructure.Api;

/// <summary>
/// The requested time range is longer than the source's <see cref="LogSourceCapabilities.MaxRange"/>.
/// <see cref="LogExplorerProblemFilter"/> turns it into a 400 with the <c>range_too_large</c> code.
/// </summary>
/// <param name="message">Explains the limit, for the ProblemDetails <c>detail</c>.</param>
public sealed class RangeTooLargeException(string message) : Exception(message);

/// <summary>
/// Checks a query's time range against the source's limit before the source runs it, so every
/// query endpoint rejects an over-long range the same way whatever the provider does with it.
/// </summary>
internal static class QueryRangeGuard
{
    /// <summary>Throws when <paramref name="query"/> covers more time than <paramref name="source"/> allows.</summary>
    /// <param name="source">The source about to run the query.</param>
    /// <param name="query">The query.</param>
    /// <param name="clock">"Now", for relative ranges and an absolute range without an end.</param>
    /// <exception cref="RangeTooLargeException">The range is longer than the source's maximum.</exception>
    /// <exception cref="ArgumentException">The range itself is invalid (see <see cref="RelativeRange.Resolve"/>).</exception>
    public static void EnsureAllowed(ILogSource source, LogQuery query, TimeProvider clock)
    {
        if (source.Capabilities.MaxRange is not { } max)
        {
            return;
        }

        ResolvedRange range = RelativeRange.Resolve(query.Range, clock);
        if (range.To - range.From > max)
        {
            throw new RangeTooLargeException(
                $"Log source '{source.Alias}' searches at most {max.TotalHours:0.##} hours at a time; narrow the time range."
            );
        }
    }
}
