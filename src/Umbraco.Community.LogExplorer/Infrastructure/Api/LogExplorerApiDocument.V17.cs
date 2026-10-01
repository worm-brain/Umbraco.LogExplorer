#if UMBRACO_17
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
        });
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
