#if UMBRACO_18
using Umbraco.Cms.Api.Common.OpenApi;
using Umbraco.Cms.Api.Management.OpenApi;
using Umbraco.Cms.Core.DependencyInjection;

namespace Umbraco.Community.LogExplorer.Infrastructure.Api;

/// <summary>
/// Umbraco 18: the API document is a Microsoft.AspNetCore.OpenApi document registered with
/// Umbraco's <c>AddBackOfficeOpenApiDocument</c> (the shape the 18.2 <c>umbraco-extension</c>
/// template generates; present since 18.0.0).
/// </summary>
internal static partial class LogExplorerApiDocument
{
    /// <inheritdoc cref="Register" />
    internal static partial void Register(IUmbracoBuilder builder) =>
        builder.AddBackOfficeOpenApiDocument(
            LogExplorerApi.Name,
            document =>
                document
                    .WithTitle(Title)
                    .WithBackOfficeAuthentication()
                    .WithJsonOptions(Umbraco.Cms.Core.Constants.JsonOptionsNames.BackOffice)
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
#endif
