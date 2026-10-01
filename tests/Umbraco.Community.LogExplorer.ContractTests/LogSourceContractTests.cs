using System.Text.Json;
using System.Text.RegularExpressions;
using Umbraco.Community.LogExplorer.Core.Filtering;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Records;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Core.Severity;
using Umbraco.Community.LogExplorer.Core.Sources;

namespace Umbraco.Community.LogExplorer.ContractTests;

/// <summary>
/// The behaviour every <see cref="ILogSource"/> must share (BRIEF §16). A provider's test project
/// subclasses this with its own fixture:
/// <code>
/// public sealed class SeqContractTests(SeqFixture fixture)
///     : LogSourceContractTests&lt;SeqFixture&gt;(fixture);
/// </code>
/// Expected results come from the fixture's known records evaluated with
/// <see cref="LogRecordFilter"/>, the reference semantics for the filter tree. Tests about an
/// operator or feature the source does not declare are skipped, except the ones asserting that
/// undeclared features throw.
/// </summary>
/// <typeparam name="TFixture">The provider's fixture.</typeparam>
public abstract class LogSourceContractTests<TFixture> : IClassFixture<TFixture>
    where TFixture : class, ILogSourceContractFixture
{
    private const int PageSize = 7;

    private readonly TFixture _fixture;

    /// <summary>Creates the suite over a fixture.</summary>
    /// <param name="fixture">The provider's fixture, shared by every test in the class.</param>
    protected LogSourceContractTests(TFixture fixture) => _fixture = fixture;

    private ILogSource Source => _fixture.Source;

    private IReadOnlyList<LogRecord> Records => _fixture.Records;

    private LogSourceCapabilities Capabilities => Source.Capabilities;

    // An absolute range covering every known record; To is exclusive, so it sits one tick past the newest.
    private TimeRange WholeRange =>
        new(
            Records.Min(record => record.Timestamp),
            Records.Max(record => record.Timestamp).AddTicks(1),
            null
        );

    [Theory]
    [MemberData(nameof(ContractTheoryData.SortDirections), MemberType = typeof(ContractTheoryData))]
    public async Task QueryAsync_WalkingEveryCursor_ReturnsEachRecordExactlyOnce(SortDirection sort)
    {
        // Act
        List<LogRecord> walked = await ReadAllAsync(Query() with { Sort = sort });

        // Assert: comparing sorted id lists catches both gaps and duplicates.
        Assert.Equal(Ids(Records), Ids(walked));
    }

    [Theory]
    [MemberData(nameof(ContractTheoryData.SortDirections), MemberType = typeof(ContractTheoryData))]
    public async Task QueryAsync_Sort_OrdersRecordsByTimestamp(SortDirection sort)
    {
        // Act
        List<LogRecord> walked = await ReadAllAsync(Query() with { Sort = sort });

        // Assert
        IEnumerable<DateTimeOffset> timestamps = walked.Select(record => record.Timestamp);
        Assert.Equal(
            sort == SortDirection.Ascending ? timestamps.Order() : timestamps.OrderDescending(),
            timestamps
        );
    }

    [Fact]
    public async Task QueryAsync_RangeEdges_IncludeFromAndExcludeTo()
    {
        // Arrange: both edges sit exactly on a record's timestamp.
        DateTimeOffset[] timestamps = Records.Select(r => r.Timestamp).Distinct().Order().ToArray();
        DateTimeOffset from = timestamps[timestamps.Length / 3];
        DateTimeOffset to = timestamps[timestamps.Length * 2 / 3];
        IEnumerable<LogRecord> expected = Records.Where(r =>
            r.Timestamp >= from && r.Timestamp < to
        );

        // Act
        List<LogRecord> walked = await ReadAllAsync(
            Query() with
            {
                Range = new TimeRange(from, to, null),
            }
        );

        // Assert
        Assert.Equal(Ids(expected), Ids(walked));
    }

    [Theory]
    [MemberData(nameof(ContractTheoryData.Operators), MemberType = typeof(ContractTheoryData))]
    public async Task QueryAsync_DeclaredOperator_MatchesReferenceSemantics(FilterOperator op)
    {
        // Arrange
        Assert.SkipUnless(
            Capabilities.Operators.Contains(op),
            $"{Source.Alias} does not declare {op}."
        );
        ConditionNode condition = ConditionFor(op);

        // Act
        List<LogRecord> walked = await ReadAllAsync(Query() with { Filter = condition });

        // Assert
        Assert.Equal(
            Ids(Records.Where(record => LogRecordFilter.Matches(record, condition))),
            Ids(walked)
        );
    }

    [Theory]
    [MemberData(
        nameof(ContractTheoryData.NonContiguousLevelSets),
        MemberType = typeof(ContractTheoryData)
    )]
    public async Task QueryAsync_NonContiguousLevelSet_ReturnsOnlyThoseLevels(string levels)
    {
        // Arrange
        var set = new HashSet<string>(levels.Split(','), StringComparer.OrdinalIgnoreCase);

        // Act
        List<LogRecord> walked = await ReadAllAsync(Query() with { Levels = set });

        // Assert
        Assert.Equal(
            Ids(Records.Where(record => SeverityMap.IsInLevels(record.SeverityNumber, set))),
            Ids(walked)
        );
    }

    [Theory]
    [MemberData(
        nameof(ContractTheoryData.OptionalFeatures),
        MemberType = typeof(ContractTheoryData)
    )]
    public async Task OptionalMember_FeatureDeclared_Succeeds(LogSourceFeatures feature)
    {
        // Arrange
        Assert.SkipUnless(
            Capabilities.Supports(feature),
            $"{Source.Alias} does not declare {feature}."
        );

        // Act
        object? result = await InvokeAsync(feature, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
    }

    [Theory]
    [MemberData(
        nameof(ContractTheoryData.OptionalFeatures),
        MemberType = typeof(ContractTheoryData)
    )]
    public async Task OptionalMember_FeatureNotDeclared_ThrowsNotSupportedException(
        LogSourceFeatures feature
    )
    {
        // Arrange
        Assert.SkipWhen(Capabilities.Supports(feature), $"{Source.Alias} declares {feature}.");

        // Act
        Task Act() => InvokeAsync(feature, CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<NotSupportedException>(Act);
    }

    [Theory]
    [MemberData(
        nameof(ContractTheoryData.CancellableFeatures),
        MemberType = typeof(ContractTheoryData)
    )]
    public async Task AsyncMember_CancelledToken_ThrowsOperationCanceledException(
        LogSourceFeatures feature
    )
    {
        // Arrange
        Assert.SkipUnless(
            Capabilities.Supports(feature),
            $"{Source.Alias} does not declare {feature}."
        );
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        Task Act() => InvokeAsync(feature, cancellation.Token);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(Act);
    }

    private LogQuery Query() => new() { Range = WholeRange, Take = PageSize };

    // Follows NextCursor to the end. The page cap turns a cursor that never ends into a failure
    // rather than a hang.
    private async Task<List<LogRecord>> ReadAllAsync(LogQuery query)
    {
        var all = new List<LogRecord>();
        string? cursor = null;
        for (int page = 0; page <= (Records.Count / PageSize) + 1; page++)
        {
            LogPage result = await Source.QueryAsync(
                query with
                {
                    Cursor = cursor,
                },
                CancellationToken.None
            );
            all.AddRange(result.Records);
            cursor = result.NextCursor;
            if (cursor is null)
            {
                return all;
            }
        }

        Assert.Fail($"{Source.Alias} kept returning a next cursor after every record was read.");
        return all;
    }

    // Calls the member behind a feature; LogSourceFeatures.None means plain search.
    private async Task<object?> InvokeAsync(LogSourceFeatures feature, CancellationToken ct)
    {
        LogQuery query = Query();
        return feature switch
        {
            LogSourceFeatures.None => await Source.QueryAsync(query, ct),
            LogSourceFeatures.Histogram => await Source.GetHistogramAsync(query, 30, ct),
            LogSourceFeatures.Facets => await Source.GetFacetsAsync(
                query,
                [_fixture.StringField],
                5,
                ct
            ),
            LogSourceFeatures.Patterns => await Source.GetPatternsAsync(query, 5, ct),
            LogSourceFeatures.Context => await Source.GetContextAsync(
                Records[Records.Count / 2].Id,
                3,
                3,
                ct
            ),
            LogSourceFeatures.FieldDiscovery => await Source.GetFieldsAsync(query, ct),
            // Only the call is checked; a declared tail is never iterated because it never ends.
            LogSourceFeatures.Tail => Source.TailAsync(query, ct),
            LogSourceFeatures.NativeQuery => Source.Compile(query),
            _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, null),
        };
    }

    // Builds a condition whose value comes from the data set, so it matches some records.
    private ConditionNode ConditionFor(FilterOperator op)
    {
        string field = _fixture.StringField;
        string[] strings = Records
            .SelectMany(record => LogFields.Resolve(record, field))
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString()!)
            .ToArray();
        string common = strings
            .GroupBy(s => s)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .First()
            .Key;
        string other = strings.First(s => s != common);
        string prefix = common[..Math.Max(1, common.Length / 2)];

        double[] numbers = Records
            .SelectMany(record => LogFields.Resolve(record, _fixture.NumberField))
            .Where(value => value.ValueKind == JsonValueKind.Number)
            .Select(value => value.GetDouble())
            .Order()
            .ToArray();
        double median = numbers[numbers.Length / 2];

        return op switch
        {
            FilterOperator.Equals or FilterOperator.NotEquals => Condition(field, op, common),
            FilterOperator.Contains => Condition(
                field,
                op,
                common.Length > 2 ? common[1..^1] : common
            ),
            FilterOperator.StartsWith => Condition(field, op, prefix),
            FilterOperator.EndsWith => Condition(field, op, common[(common.Length / 2)..]),
            FilterOperator.In => Condition(field, op, new[] { common, other }),
            FilterOperator.Matches => Condition(field, op, "^" + Regex.Escape(prefix)),
            FilterOperator.Exists or FilterOperator.NotExists => new ConditionNode(field, op, null),
            _ => Condition(_fixture.NumberField, op, median),
        };
    }

    private static ConditionNode Condition<T>(string field, FilterOperator op, T value) =>
        new(field, op, JsonSerializer.SerializeToElement(value));

    private static string[] Ids(IEnumerable<LogRecord> records) =>
        records.Select(record => record.Id).Order(StringComparer.Ordinal).ToArray();
}
