using Umbraco.Community.LogExplorer.Core.Fake;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Records;
using Umbraco.Community.LogExplorer.Core.Sources;

namespace Umbraco.Community.LogExplorer.ContractTests.Fake;

/// <summary>The reference source: the full-featured fake must pass the whole suite.</summary>
public sealed class FakeLogSourceContractTests(FakeLogSourceFixture fixture)
    : LogSourceContractTests<FakeLogSourceFixture>(fixture);

/// <summary>
/// A fake declaring no optional features and only <see cref="FilterOperator.Equals"/>, proving the
/// suite skips what a narrow provider does not declare and checks that it refuses the rest.
/// </summary>
public sealed class MinimalFakeLogSourceContractTests(MinimalFakeLogSourceFixture fixture)
    : LogSourceContractTests<MinimalFakeLogSourceFixture>(fixture);

/// <summary>The sample hour with every feature except Tail, on a frozen clock.</summary>
public sealed class FakeLogSourceFixture : FakeFixtureBase
{
    /// <summary>Creates the fixture.</summary>
    public FakeLogSourceFixture()
        : base(new FakeLogSourceOptions()) { }
}

/// <summary>The sample hour with search and Equals only.</summary>
public sealed class MinimalFakeLogSourceFixture : FakeFixtureBase
{
    /// <summary>Creates the fixture.</summary>
    public MinimalFakeLogSourceFixture()
        : base(
            new FakeLogSourceOptions
            {
                Alias = "fake-minimal",
                Features = LogSourceFeatures.None,
                Operators = new HashSet<FilterOperator> { FilterOperator.Equals },
            }
        ) { }
}

/// <summary>Shared fixture shape for both fakes.</summary>
public abstract class FakeFixtureBase : ILogSourceContractFixture
{
    private readonly FakeLogSource _source;

    /// <summary>Creates the source on a frozen clock so every run sees the same data.</summary>
    /// <param name="options">The fake's capabilities.</param>
    protected FakeFixtureBase(FakeLogSourceOptions options) =>
        _source = new FakeLogSource(
            options with
            {
                FixedNow = new DateTimeOffset(2026, 9, 2, 1, 0, 0, TimeSpan.Zero),
            }
        );

    /// <inheritdoc />
    public ILogSource Source => _source;

    /// <inheritdoc />
    public IReadOnlyList<LogRecord> Records => _source.Records;

    /// <inheritdoc />
    public string StringField => "RequestPath";

    /// <inheritdoc />
    public string NumberField => "StatusCode";
}
