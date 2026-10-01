#if UMBRACO_18
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Api.Common.OpenApi;
using Umbraco.Cms.Api.Management.OpenApi;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Community.LogExplorer.Core.Json;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Umbraco.Community.LogExplorer.Infrastructure.Api;

/// <summary>
/// Umbraco 18: the API document is a Microsoft.AspNetCore.OpenApi document registered with
/// Umbraco's <c>AddBackOfficeOpenApiDocument</c> (the shape the 18.2 <c>umbraco-extension</c>
/// template generates; present since 18.0.0).
/// </summary>
internal static partial class LogExplorerApiDocument
{
    /// <inheritdoc cref="Register" />
    internal static partial void Register(IUmbracoBuilder builder)
    {
        // The document's schemas come from the minimal-API JsonOptions with this name, not the
        // MVC ones the controllers format with (LogExplorerApiComposer), so they get the same
        // LogJson configuration: camelCase string enums instead of integers. Strict number
        // handling only tidies the schema ("take" is an integer, not "integer or string").
        builder.Services.Configure<HttpJsonOptions>(
            LogExplorerApi.Name,
            options =>
            {
                LogJson.Apply(options.SerializerOptions);
                options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
            }
        );

        builder.AddBackOfficeOpenApiDocument(
            LogExplorerApi.Name,
            document =>
                document
                    .WithTitle(Title)
                    .WithBackOfficeAuthentication()
                    .WithJsonOptions(LogExplorerApi.Name)
                    .ConfigureOpenApiOptions(options =>
                        options.AddDocumentTransformer(
                            (doc, _, _) =>
                            {
                                doc.Info.Version = "1.0";
                                return Task.CompletedTask;
                            }
                        )
                    )
        );
    }
}
#endif
