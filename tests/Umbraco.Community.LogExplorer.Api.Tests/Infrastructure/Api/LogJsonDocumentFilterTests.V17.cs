#if UMBRACO_17
using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using Umbraco.Community.LogExplorer.Infrastructure.Api;

namespace Umbraco.Community.LogExplorer.Api.Tests.Infrastructure.Api;

/// <summary>
/// Umbraco 17's Swagger setup describes the Core contracts with the backoffice conventions
/// (<c>$type</c> discriminators, PascalCase enums); the filter rewrites the package document to
/// the wire format the API actually uses (ADR 0014).
/// </summary>
public class LogJsonDocumentFilterTests
{
    private readonly LogExplorerApiDocument.LogJsonDocumentFilter _filter = new();

    [Fact]
    public void Apply_FilterNodeSubtype_UsesTheKindDiscriminator()
    {
        // Arrange
        OpenApiDocument document = Document();

        // Act
        _filter.Apply(document, Context(LogExplorerApi.Name));

        // Assert
        var condition = (OpenApiSchema)document.Components!.Schemas!["ConditionNode"];
        Assert.Equal(
            ("kind", true, false),
            (
                condition.Discriminator!.PropertyName,
                condition.Properties!.ContainsKey("kind"),
                condition.Properties.ContainsKey("$type")
            )
        );
    }

    [Fact]
    public void Apply_FilterNodeSubtype_MakesDefaultedParametersOptional()
    {
        // Arrange
        OpenApiDocument document = Document();

        // Act
        _filter.Apply(document, Context(LogExplorerApi.Name));

        // Assert
        var condition = (OpenApiSchema)document.Components!.Schemas!["ConditionNode"];
        Assert.Equal(["field", "kind", "op"], condition.Required!.Order());
    }

    [Fact]
    public void Apply_FilterNodeSubtype_DropsTheEmptyBase()
    {
        // Arrange
        OpenApiDocument document = Document();

        // Act
        _filter.Apply(document, Context(LogExplorerApi.Name));

        // Assert
        var condition = (OpenApiSchema)document.Components!.Schemas!["ConditionNode"];
        Assert.Equal(
            (false, true),
            (document.Components.Schemas.ContainsKey("FilterNode"), condition.AllOf is null)
        );
    }

    [Fact]
    public void Apply_CoreEnum_ListsCamelCaseNames()
    {
        // Arrange
        OpenApiDocument document = Document();

        // Act
        _filter.Apply(document, Context(LogExplorerApi.Name));

        // Assert
        var sort = (OpenApiSchema)document.Components!.Schemas!["SortDirection"];
        Assert.Equal(
            ["descending", "ascending"],
            sort.Enum!.Select(value => value!.GetValue<string>())
        );
    }

    [Fact]
    public void Apply_AnotherDocument_LeavesItUnchanged()
    {
        // Arrange
        OpenApiDocument document = Document();

        // Act
        _filter.Apply(document, Context("management"));

        // Assert
        var sort = (OpenApiSchema)document.Components!.Schemas!["SortDirection"];
        Assert.Equal("Descending", sort.Enum![0]!.GetValue<string>());
    }

    // The shapes Umbraco 17's SwaggerGen produces for these types (observed on Site17).
    private static OpenApiDocument Document() =>
        new()
        {
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["FilterNode"] = new OpenApiSchema { Type = JsonSchemaType.Object },
                    ["ConditionNode"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        AllOf = [new OpenApiSchemaReference("FilterNode")],
                        Required = new HashSet<string>
                        {
                            "$type",
                            "caseInsensitive",
                            "field",
                            "op",
                        },
                        Properties = new Dictionary<string, IOpenApiSchema>
                        {
                            ["$type"] = new OpenApiSchema { Type = JsonSchemaType.String },
                            ["field"] = new OpenApiSchema { Type = JsonSchemaType.String },
                            ["op"] = new OpenApiSchemaReference("FilterOperator"),
                            ["value"] = new OpenApiSchema(),
                            ["caseInsensitive"] = new OpenApiSchema
                            {
                                Type = JsonSchemaType.Boolean,
                            },
                        },
                        Discriminator = new OpenApiDiscriminator { PropertyName = "$type" },
                    },
                    ["SortDirection"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        Enum = [JsonValue.Create("Descending"), JsonValue.Create("Ascending")],
                    },
                },
            },
        };

    private static DocumentFilterContext Context(string documentName) =>
        new([], null!, new SchemaRepository(documentName));
}
#endif
