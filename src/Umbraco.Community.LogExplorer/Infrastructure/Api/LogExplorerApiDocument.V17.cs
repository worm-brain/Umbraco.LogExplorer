#if UMBRACO_17
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using Umbraco.Cms.Api.Common.OpenApi;
using Umbraco.Cms.Api.Management.OpenApi;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Community.LogExplorer.Core.Query;

namespace Umbraco.Community.LogExplorer.Infrastructure.Api;

/// <summary>Umbraco 17: the API document is a Swashbuckle Swagger document.</summary>
/// <remarks>See https://docs.umbraco.com/umbraco-cms/tutorials/creating-a-backoffice-api (v17).</remarks>
internal static partial class LogExplorerApiDocument
{
    /// <inheritdoc cref="Register" />
    internal static partial void Register(IUmbracoBuilder builder)
    {
        builder.Services.AddSingleton<IOperationIdHandler, LogExplorerOperationIdHandler>();

        builder.Services.Configure<SwaggerGenOptions>(options =>
        {
            options.SwaggerDoc(
                LogExplorerApi.Name,
                new OpenApiInfo { Title = Title, Version = "1.0" }
            );

            // Lets the Swagger UI authenticate as the signed-in backoffice user.
            options.OperationFilter<LogExplorerOperationSecurityFilter>();

            // Describe the Core contracts as LogJson writes them, not as the backoffice options
            // would (ADR 0013).
            options.DocumentFilter<LogJsonDocumentFilter>();
        });
    }

    /// <summary>
    /// Makes the package document's schemas for Core types match the wire format of the
    /// package's named JSON options. Umbraco 17's SwaggerGen setup names every polymorphic
    /// discriminator <c>$type</c> and writes enum values in PascalCase (the backoffice
    /// convention), whereas <c>FilterNode</c> declares <c>kind</c> and <c>LogJson</c> writes
    /// camelCase enums.
    /// </summary>
    /// <remarks>
    /// A document filter rather than a schema filter, because it must run after Umbraco's own
    /// enum schema filter. Schema ids are the bare type names in Umbraco's document setup.
    /// </remarks>
    internal sealed class LogJsonDocumentFilter : IDocumentFilter
    {
        private const string UmbracoDiscriminator = "$type";

        /// <inheritdoc />
        public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
        {
            if (
                context.DocumentName != LogExplorerApi.Name
                || swaggerDoc.Components?.Schemas is not { } schemas
            )
            {
                return;
            }

            foreach (Type type in typeof(LogQuery).Assembly.GetExportedTypes())
            {
                if (
                    !schemas.TryGetValue(type.Name, out IOpenApiSchema? found)
                    || found is not OpenApiSchema schema
                )
                {
                    continue;
                }

                if (type.IsEnum)
                {
                    schema.Type = JsonSchemaType.String;
                    schema.Format = null;
                    schema.Enum =
                    [
                        .. Enum.GetNames(type)
                            .Select(name => (JsonNode)JsonValue.Create(CamelCase(name))),
                    ];
                }
                else if (type.IsSubclassOf(typeof(FilterNode)))
                {
                    RenameDiscriminator(schema);

                    // The abstract base has no properties but "additionalProperties: false", so
                    // inheriting it through allOf makes generators intersect every node with an
                    // empty object type. Each node schema already lists all its properties.
                    schema.AllOf = null;
                    MakeDefaultedParametersOptional(type, schema);
                }
            }

            // Nothing references the empty base once the allOf links are gone; properties typed
            // FilterNode are already oneOf the concrete nodes.
            schemas.Remove(nameof(FilterNode));
        }

        // A record parameter with a default (CaseInsensitive = true, Phrase = false) may be left
        // out of the JSON; Swashbuckle marks it required because the property is non-nullable.
        private static void MakeDefaultedParametersOptional(Type type, OpenApiSchema schema)
        {
            IEnumerable<ParameterInfo> defaulted = type.GetConstructors()
                .SelectMany(constructor => constructor.GetParameters())
                .Where(parameter => parameter.HasDefaultValue && parameter.Name is not null);

            foreach (ParameterInfo parameter in defaulted)
            {
                schema.Required?.Remove(CamelCase(parameter.Name!));
            }
        }

        private static void RenameDiscriminator(OpenApiSchema schema)
        {
            string kind =
                typeof(FilterNode)
                    .GetCustomAttribute<JsonPolymorphicAttribute>()
                    ?.TypeDiscriminatorPropertyName
                ?? UmbracoDiscriminator;

            if (
                schema.Properties is { } properties
                && properties.Remove(UmbracoDiscriminator, out IOpenApiSchema? property)
            )
            {
                properties[kind] = property;
            }

            if (schema.Required?.Remove(UmbracoDiscriminator) is true)
            {
                schema.Required.Add(kind);
            }

            if (schema.Discriminator is { PropertyName: UmbracoDiscriminator } discriminator)
            {
                discriminator.PropertyName = kind;
            }
        }

        private static string CamelCase(string name) => char.ToLowerInvariant(name[0]) + name[1..];
    }

    /// <summary>Applies backoffice authentication requirements to the package's Swagger document.</summary>
    internal sealed class LogExplorerOperationSecurityFilter
        : BackOfficeSecurityRequirementsOperationFilterBase
    {
        /// <inheritdoc />
        protected override string ApiName => LogExplorerApi.Name;
    }

    /// <summary>
    /// Uses the bare action name as the operation id, so the generated client gets short method
    /// names (for example <c>getSources</c>).
    /// </summary>
    internal sealed class LogExplorerOperationIdHandler(
        IOptions<ApiVersioningOptions> apiVersioningOptions
    ) : OperationIdHandler(apiVersioningOptions)
    {
        /// <inheritdoc />
        protected override bool CanHandle(
            ApiDescription apiDescription,
            ControllerActionDescriptor controllerActionDescriptor
        ) =>
            controllerActionDescriptor.ControllerTypeInfo.Namespace?.StartsWith(
                "Umbraco.Community.LogExplorer",
                StringComparison.Ordinal
            )
                is true;

        /// <inheritdoc />
        public override string Handle(ApiDescription apiDescription) =>
            $"{apiDescription.ActionDescriptor.RouteValues["action"]}";
    }
}
#endif
