using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Serilog.Events;
using Umbraco.Cms.Core.Logging;
using Umbraco.Cms.Infrastructure.Logging.Viewer;
using Umbraco.Community.LogExplorer.Core.Query;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Core.Sources;
using Umbraco.Community.LogExplorer.Features.Sources.Files;
using Umbraco.Community.LogExplorer.Files.Tests.TestSupport;

namespace Umbraco.Community.LogExplorer.Files.Tests.Features.Sources.Files;

/// <summary>
/// Native mode on the files source runs the core Log Viewer's own dialect, so core saved searches
/// work unchanged, and reports where an invalid expression goes wrong (#34, ADR 0013).
/// </summary>
public class NativeQueryCompileTests
{
    // Saved searches of the kind the core Log Viewer's docs show, plus its plain-text shortcut.
    public static TheoryData<string, string[]> CoreSavedSearches =>
        new()
        {
            { "@Message like '%timeout%'", ["Request timeout for {Name}"] },
            {
                "Has(Duration) and Duration > 1000",
                ["Slow request {RequestMethod} {RequestPath} took {Duration} ms"]
            },
            { "StartsWith(SourceContext, 'Umbraco.Core')", ["Timeout check after {Elapsed} ms"] },
            {
                "@Level = 'Error' or @Level = 'Fatal'",
                ["An unhandled exception has occurred while executing the request.", "Boot failed"]
            },
            { "timeout", ["Request timeout for {Name}"] },
        };

    [Theory]
    [MemberData(nameof(CoreSavedSearches))]
    public void Compile_CoreSavedSearch_SelectsTheEntriesItDescribes(
        string expression,
        string[] expectedTemplates
    )
    {
        // Arrange
        Func<LogEvent, bool> filter = NativeFilter.Compile(expression);

        // Act
        string[] templates = Select(filter);

        // Assert
        Assert.Equal(expectedTemplates, templates);
    }

    [Theory]
    [InlineData("@Message like '%timeout%'")]
    [InlineData("Has(Duration) and Duration > 1000")]
    [InlineData("StartsWith(SourceContext, 'Umbraco.Core')")]
    [InlineData("@Level = 'Warning' or @Level = 'Error'")]
    [InlineData("timeout")]
    [InlineData("Boot")]
    [InlineData("@MessageTemplate = 'Boot failed'")]
    [InlineData("Contains(@Exception, 'Timeout')")]
    [InlineData("StatusCode = 200 and RequestPath like '/api%'")]
    [InlineData("Tags[?] = 'sale'")]
    [InlineData("Cart.Total > 100")]
    [InlineData("@tr = 'f73dfa962952f5780ed2108020fe2029'")]
    public void Compile_AnyExpression_SelectsWhatTheCoreLogViewerSelects(string expression)
    {
        // Arrange
        Func<LogEvent, bool> filter = NativeFilter.Compile(expression);
        Func<LogEvent, bool> coreViewer = CoreViewerFilter(expression);

        // Act
        string[] templates = Select(filter);

        // Assert
        Assert.Equal(Select(coreViewer), templates);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Compile_Blank_MatchesEveryEvent(string? expression)
    {
        // Arrange
        Func<LogEvent, bool> filter = NativeFilter.Compile(expression);

        // Act
        string[] templates = Select(filter);

        // Assert
        Assert.Equal(DialectFixtures.Events.Count, templates.Length);
    }

    [Fact]
    public void Compile_InvalidExpression_ThrowsWithThePosition()
    {
        // Arrange
        const string expression = "@Level = = 'Error'";

        // Act
        void Compile() => NativeFilter.Compile(expression);

        // Assert
        InvalidNativeQueryException exception = Assert.Throws<InvalidNativeQueryException>(Compile);
        Assert.Equal(9, exception.Position);
    }

    [Fact]
    public void Validate_ValidExpression_IsValid()
    {
        // Act
        ValidationResult result = NativeFilter.Validate("Has(Duration) and Duration > 1000");

        // Assert
        Assert.Equal(new ValidationResult(true, null, null), result);
    }

    [Fact]
    public void Validate_PlainText_IsValid()
    {
        // Act
        ValidationResult result = NativeFilter.Validate("timeout");

        // Assert
        Assert.True(result.Valid);
    }

    [Theory]
    [InlineData("@Level = = 'Error'", 9)]
    [InlineData("Has(Duration)\nand and Duration > 1", 18)]
    [InlineData("StartsWith(SourceContext 'x')", 25)]
    public void Validate_SyntaxError_ReportsTheZeroBasedOffset(string expression, int position)
    {
        // Act
        ValidationResult result = NativeFilter.Validate(expression);

        // Assert
        Assert.Equal((false, position), (result.Valid, result.Position));
    }

    [Fact]
    public void Validate_SyntaxError_ExplainsTheProblem()
    {
        // Act
        ValidationResult result = NativeFilter.Validate("@Level = = 'Error'");

        // Assert
        Assert.StartsWith("Syntax error (line 1, column 10): unexpected", result.Error);
    }

    [Theory]
    [InlineData("Duration > ")]
    [InlineData("Nope(Duration) = 1")]
    [InlineData("IsMatch(RequestPath, '[')")]
    public void Validate_ErrorWithoutALocation_HasNoPosition(string expression)
    {
        // Act
        ValidationResult result = NativeFilter.Validate(expression);

        // Assert
        Assert.Equal((false, (int?)null), (result.Valid, result.Position));
    }

    [Fact]
    public void Query_NativeQueryOnly_ReturnsTheEntriesItSelects()
    {
        // Arrange
        using var directory = new TempDirectory();
        LogFilePager pager = PagerOverFixtures(directory);
        LogQuery query = DayQuery() with { NativeQuery = "Has(Duration) and Duration > 1000" };

        // Act
        FilePage page = pager.Query(query, 100, Token);

        // Assert
        Assert.Equal(
            ["Slow request {RequestMethod} {RequestPath} took {Duration} ms"],
            Templates(page)
        );
    }

    [Fact]
    public void Query_NativeQueryWithChips_ReturnsEntriesMatchingBoth()
    {
        // Arrange
        using var directory = new TempDirectory();
        LogFilePager pager = PagerOverFixtures(directory);
        LogQuery query = DayQuery() with
        {
            NativeQuery = "StatusCode = 500",
            Filter = new ConditionNode(
                "@body",
                FilterOperator.Contains,
                JsonSerializer.SerializeToElement("slow")
            ),
        };

        // Act
        FilePage page = pager.Query(query, 100, Token);

        // Assert
        Assert.Equal(
            ["Slow request {RequestMethod} {RequestPath} took {Duration} ms"],
            Templates(page)
        );
    }

    [Fact]
    public void Query_InvalidNativeQuery_ThrowsInvalidNativeQuery()
    {
        // Arrange
        using var directory = new TempDirectory();
        LogFilePager pager = PagerOverFixtures(directory);
        LogQuery query = DayQuery() with { NativeQuery = "Duration >" };

        // Act
        void Query() => pager.Query(query, 100, Token);

        // Assert
        Assert.Throws<InvalidNativeQueryException>(Query);
    }

    [Fact]
    public void GetPatterns_NativeQuery_CountsOnlyTheEntriesItSelects()
    {
        // Arrange
        using var directory = new TempDirectory();
        FileAggregator aggregator = AggregatorOverFixtures(directory);
        LogQuery query = Aggregators.NoonHour() with { NativeQuery = "StatusCode = 500" };

        // Act
        PatternResult result = aggregator.GetPatterns(query, 10, Token).Result;

        // Assert
        Assert.Equal(
            [
                "An unhandled exception has occurred while executing the request.",
                "Slow request {RequestMethod} {RequestPath} took {Duration} ms",
            ],
            result.Patterns.Select(pattern => pattern.Template).Order(StringComparer.Ordinal)
        );
    }

    [Fact]
    public void GetHistogram_SameQueryWithANativeQuery_IsNotServedFromTheCacheOfTheOther()
    {
        // Arrange
        using var directory = new TempDirectory();
        FileAggregator aggregator = AggregatorOverFixtures(directory);
        LogQuery query = Aggregators.NoonHour();
        aggregator.GetHistogram(query, 60, Token);

        // Act
        HistogramResult result = aggregator
            .GetHistogram(query with { NativeQuery = "@Level = 'Fatal'" }, 60, Token)
            .Result;

        // Assert
        Assert.Equal(
            1,
            result.Buckets.Sum(bucket => bucket.CountsBySeverityShortName.Values.Sum())
        );
    }

    [Fact]
    public void GetFacets_InvalidNativeQuery_ThrowsInvalidNativeQuery()
    {
        // Arrange
        using var directory = new TempDirectory();
        FileAggregator aggregator = AggregatorOverFixtures(directory);
        LogQuery query = Aggregators.NoonHour() with { NativeQuery = "Duration >" };

        // Act
        void Facets() => aggregator.GetFacets(query, ["StatusCode"], 5, Token);

        // Assert
        Assert.Throws<InvalidNativeQueryException>(Facets);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string[] Select(Func<LogEvent, bool> filter) =>
        [
            .. DialectFixtures
                .Events.Where(pair => filter(pair.Event))
                .Select(pair => pair.Event.MessageTemplate.Text),
        ];

    // The core viewer's own filter (internal to Umbraco.Infrastructure in 17 and 18), so the
    // comparison is with what the core Log Viewer actually runs rather than a copy of it.
    private static Func<LogEvent, bool> CoreViewerFilter(string expression)
    {
        Type type = typeof(SerilogLegacyNameResolver).Assembly.GetType(
            "Umbraco.Cms.Core.Logging.Viewer.ExpressionFilter",
            throwOnError: true
        )!;
        object filter = Activator.CreateInstance(type, expression)!;
        MethodInfo take = type.GetMethod("TakeLogEvent")!;
        return logEvent => (bool)take.Invoke(filter, [logEvent])!;
    }

    private static FileAggregator AggregatorOverFixtures(TempDirectory directory)
    {
        directory.Write(Aggregators.DayFile, LogLines.File(DialectFixtures.Lines));
        return Aggregators.Create(directory.Path);
    }

    private static LogFilePager PagerOverFixtures(TempDirectory directory)
    {
        directory.Write(Aggregators.DayFile, LogLines.File(DialectFixtures.Lines));
        var configuration = Substitute.For<ILoggingConfiguration>();
        configuration.LogDirectory.Returns(directory.Path);
        configuration.LogFileNameFormat.Returns("UmbracoTraceLog.{0}..json");
        configuration.GetLogFileNameFormatArguments().Returns(["WORM"]);
        return new LogFilePager(
            new UmbracoLogFileLocator(configuration, currentMachineName: "WORM"),
            new FakeTimeProvider(new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero))
        );
    }

    private static LogQuery DayQuery() =>
        new()
        {
            Range = new TimeRange(
                new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero),
                null
            ),
            Sort = SortDirection.Ascending,
        };

    private static string?[] Templates(FilePage page) =>
        [.. page.Page.Records.Select(record => record.MessageTemplate)];
}
