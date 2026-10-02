using System.Runtime.ExceptionServices;

namespace Umbraco.Community.LogExplorer.Features.Sources.Files;

/// <summary>
/// Runs a scan's per-line or per-event work across cores (ADR 0026) while keeping the exceptions
/// a one-by-one loop would throw.
/// </summary>
internal static class ParallelWork
{
    /// <summary>
    /// Runs <paramref name="body"/> for every index from 0 to <paramref name="count"/>, in any
    /// order and on any thread.
    /// </summary>
    /// <param name="count">How many indexes; 0 does nothing.</param>
    /// <param name="body">The work for one index. It must only touch state owned by that index.</param>
    /// <exception cref="Exception">
    /// The first exception <paramref name="body"/> threw, rethrown as it was rather than wrapped
    /// in an <see cref="AggregateException"/>, so callers (and the API's error mapping) see the
    /// same exception types as for sequential work, for example an <see cref="ArgumentException"/>
    /// for a regular expression that does not parse.
    /// </exception>
    public static void For(int count, Action<int> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        try
        {
            Parallel.For(0, count, body);
        }
        catch (AggregateException exception) when (exception.InnerExceptions.Count > 0)
        {
            ExceptionDispatchInfo.Throw(exception.InnerExceptions[0]);
        }
    }
}
