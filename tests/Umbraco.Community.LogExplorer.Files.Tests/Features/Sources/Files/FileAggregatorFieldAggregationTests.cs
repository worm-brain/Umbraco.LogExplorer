using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Features.Sources.Files;
using Umbraco.Community.LogExplorer.Files.Tests.TestSupport;

namespace Umbraco.Community.LogExplorer.Files.Tests.Features.Sources.Files;

/// <summary>
/// Field discovery lists the portable fields present, then attribute paths with nested objects as
/// dotted paths, each with its kind and presence (#32).
/// </summary>
public class FileAggregatorFieldAggregationTests
{
    [Fact]
    public void GetFields_TypedAndNestedProperties_ListsPortableFieldsThenAttributePathsWithKinds()
    {
        // Arrange
        using var directory = new TempDirectory();
        directory.Write(
            Aggregators.DayFile,
            LogLines.File(
                LogLines.Event(
                    Aggregators.Noon.AddMinutes(1),
                    "Priced {RequestPath}",
                    extraProperties: "\"RequestPath\":\"/cart\",\"Duration\":12,\"Ok\":true,"
                        + "\"When\":\"2026-10-01T12:00:00Z\",\"Cart\":{\"Total\":9.5},\"Tags\":[\"a\"]"
                ),
                LogLines.Event(Aggregators.Noon.AddMinutes(2), "Idle")
            )
        );
        FileAggregator aggregator = Aggregators.Create(directory.Path);

        // Act
        IReadOnlyList<FieldInfo> fields = aggregator
            .GetFields(Aggregators.NoonHour(), Token)
            .Result;

        // Assert
        Assert.Equal(
            new FieldInfo[]
            {
                new("@timestamp", "datetime", 1),
                new("@severity", "string", 1),
                new("@body", "string", 1),
                new("@template", "string", 1),
                new("Cart", "object", 0.5),
                new("Cart.Total", "number", 0.5),
                new("Duration", "number", 0.5),
                new("Ok", "bool", 0.5),
                new("RequestPath", "string", 0.5),
                new("Tags", "array", 0.5),
                new("When", "datetime", 0.5),
            },
            fields
        );
    }

    [Fact]
    public void GetFields_NoEntriesInRange_ReturnsNoFields()
    {
        // Arrange
        using var directory = new TempDirectory();
        FileAggregator aggregator = Aggregators.Create(directory.Path);

        // Act
        IReadOnlyList<FieldInfo> fields = aggregator
            .GetFields(Aggregators.NoonHour(), Token)
            .Result;

        // Assert
        Assert.Empty(fields);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
