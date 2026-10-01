using Asp.Versioning;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using Umbraco.Cms.Api.Common.OpenApi;
using Umbraco.Cms.Api.Management.OpenApi;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace Umbraco.Community.LogExplorer.Infrastructure.Api;

/// <summary>
/// Registers the package's <c>log-explorer</c> Swagger document, its backoffice security
/// requirements, short operation ids for the generated TypeScript client, and the user-context
/// accessor the controllers use.
/// </summary>
/// <remarks>See https://docs.umbraco.com/umbraco-cms/tutorials/creating-a-backoffice-api.</remarks>
public sealed class LogExplorerApiComposer : IComposer
{
    /// <summary>Adds the API services.</summary>
    /// <param name="builder">The Umbraco builder.</param>
    public void Compose(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSingleton<IOperationIdHandler, LogExplorerOperationIdHandler>();
        builder.Services.AddScoped<IUserContextAccessor, BackOfficeUserContextAccessor>();

        builder.Services.Configure<SwaggerGenOptions>(options =>
        {
            options.SwaggerDoc(
                LogExplorerApi.Name,
                new OpenApiInfo { Title = "Umbraco Log Explorer Management API", Version = "1.0" }
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
