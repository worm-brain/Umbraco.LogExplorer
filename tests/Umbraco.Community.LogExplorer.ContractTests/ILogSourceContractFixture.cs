using Umbraco.Community.LogExplorer.Core.Records;
using Umbraco.Community.LogExplorer.Core.Sources;

namespace Umbraco.Community.LogExplorer.ContractTests;

/// <summary>
/// What a provider's test project supplies to run <see cref="LogSourceContractTests{TFixture}"/>:
/// a source loaded with a known data set, and that data set as <see cref="LogRecord"/>s. The suite
/// computes every expectation from <see cref="Records"/>, so it needs no knowledge of the provider.
/// </summary>
public interface ILogSourceContractFixture
{
    /// <summary>The source under test.</summary>
    ILogSource Source { get; }

    /// <summary>
    /// Every record the source holds, with the ids and timestamps the source returns. Use at least a
    /// few dozen records spread over several levels so paging and range edges are exercised.
    /// </summary>
    IReadOnlyList<LogRecord> Records { get; }

    /// <summary>An attribute with string values, present on some records but not all.</summary>
    string StringField { get; }

    /// <summary>An attribute with numeric values, present on some records but not all.</summary>
    string NumberField { get; }
}
