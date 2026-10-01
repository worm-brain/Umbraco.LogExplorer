namespace Umbraco.Community.LogExplorer.Core.Severity;

/// <summary>An inclusive range of OpenTelemetry severity numbers covered by one level.</summary>
/// <param name="Min">Lowest severity number in the band.</param>
/// <param name="Max">Highest severity number in the band.</param>
public readonly record struct SeverityBand(int Min, int Max)
{
    /// <summary>Whether <paramref name="severityNumber"/> falls inside the band.</summary>
    /// <param name="severityNumber">An OTel severity number.</param>
    /// <returns>True when <c>Min &lt;= severityNumber &lt;= Max</c>.</returns>
    public bool Contains(int severityNumber) => severityNumber >= Min && severityNumber <= Max;
}
