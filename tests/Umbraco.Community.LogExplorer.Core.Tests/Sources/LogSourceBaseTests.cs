using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Core.Sources;

namespace Umbraco.Community.LogExplorer.Core.Tests.Sources;

public class LogSourceBaseTests
{
    private static readonly LogQuery AnyQuery = new() { Range = new TimeRange(null, null, "1h") };

    // Every optional ILogSource member, invoked with valid arguments.
    private static readonly Dictionary<string, Func<ILogSource, Task>> OptionalMembers = new()
    {
        [nameof(ILogSource.GetHistogramAsync)] = s =>
            s.GetHistogramAsync(AnyQuery, 60, CancellationToken.None),
        [nameof(ILogSource.GetFacetsAsync)] = s =>
            s.GetFacetsAsync(AnyQuery, ["RequestPath"], 10, CancellationToken.None),
        [nameof(ILogSource.GetPatternsAsync)] = s =>
            s.GetPatternsAsync(AnyQuery, 10, CancellationToken.None),
        [nameof(ILogSource.GetContextAsync)] = s =>
            s.GetContextAsync("id", 7, 7, CancellationToken.None),
        [nameof(ILogSource.GetFieldsAsync)] = s =>
            s.GetFieldsAsync(AnyQuery, CancellationToken.None),
        [nameof(ILogSource.TailAsync)] = s =>
            Task.FromResult(s.TailAsync(AnyQuery, CancellationToken.None)),
        [nameof(ILogSource.Compile)] = s => Task.FromResult(s.Compile(AnyQuery)),
        [nameof(ILogSource.ValidateNative)] = s => Task.FromResult(s.ValidateNative("x")),
    };

    public static TheoryData<string> OptionalMemberNames => [.. OptionalMembers.Keys];

    [Theory]
    [MemberData(nameof(OptionalMemberNames))]
    public async Task OptionalMember_NoFeaturesDeclared_ThrowsNotSupportedException(string member)
    {
        // Arrange
        var source = new StubSource(LogSourceFeatures.None);

        // Act
        Task Act() => OptionalMembers[member](source);

        // Assert
        await Assert.ThrowsAsync<NotSupportedException>(Act);
    }

    [Fact]
    public void GetHistogramAsync_OverriddenButNotDeclared_ThrowsNotSupportedException()
    {
        // Arrange
        var source = new StubSource(LogSourceFeatures.None);

        // Act
        void Act() => source.GetHistogramAsync(AnyQuery, 60, CancellationToken.None);

        // Assert
        Assert.Throws<NotSupportedException>(Act);
    }

    [Fact]
    public async Task GetHistogramAsync_Declared_ReturnsTheProviderResult()
    {
        // Arrange
        var source = new StubSource(LogSourceFeatures.Histogram);

        // Act
        HistogramResult result = await source.GetHistogramAsync(
            AnyQuery,
            60,
            CancellationToken.None
        );

        // Assert
        Assert.Same(StubSource.Histogram, result);
    }

    [Fact]
    public void Compile_DeclaredButNotOverridden_ThrowsNotSupportedException()
    {
        // Arrange
        var source = new StubSource(LogSourceFeatures.NativeQuery);

        // Act
        void Act() => source.Compile(AnyQuery);

        // Assert
        Assert.Throws<NotSupportedException>(Act);
    }

    [Fact]
    public async Task QueryAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        // Arrange
        var source = new StubSource(LogSourceFeatures.None);

        // Act
        Task Act() => source.QueryAsync(AnyQuery, new CancellationToken(canceled: true));

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(Act);
    }

    /// <summary>
    /// Implements only search and the histogram, declaring whatever the test asks for, so the
    /// base class's gating is what decides the outcome.
    /// </summary>
    private sealed class StubSource(LogSourceFeatures features) : LogSourceBase
    {
        public static readonly HistogramResult Histogram = new(
            new ResolvedRange(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(1)),
            TimeSpan.FromMinutes(1),
            [],
            false
        );

        public override string Alias => "stub";
        public override string DisplayName => "Stub";
        public override string Type => "Stub";
        public override LogSourceCapabilities Capabilities { get; } =
            new(features, new HashSet<FilterOperator>(), null, null, 100);

        protected override Task<LogPage> QueryCoreAsync(LogQuery query, CancellationToken ct) =>
            Task.FromResult(
                new LogPage([], null, Histogram.Range, 0, TotalIsLowerBound: false, [])
            );

        protected override Task<HistogramResult> GetHistogramCoreAsync(
            LogQuery query,
            int targetBuckets,
            CancellationToken ct
        ) => Task.FromResult(Histogram);
    }
}
