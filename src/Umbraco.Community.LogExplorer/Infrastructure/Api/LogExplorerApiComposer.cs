using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Api.Common.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Community.LogExplorer.Core.Json;

namespace Umbraco.Community.LogExplorer.Infrastructure.Api;

/// <summary>
/// Registers the package's Management API: the <c>log-explorer</c> OpenAPI document (with
/// backoffice security, so the Swagger UI and the generated client authenticate as the signed-in
/// user) and the user-context accessor the controllers use.
/// </summary>
/// <remarks>
/// Registering an API document differs between Umbraco majors (Swashbuckle in 17,
/// Microsoft.AspNetCore.OpenApi in 18), so <see cref="LogExplorerApiDocument.Register"/> has one
/// implementation per major (ADR 0010). Everything else here is shared.
/// </remarks>
public sealed class LogExplorerApiComposer : IComposer
{
    /// <summary>Adds the API services.</summary>
    /// <param name="builder">The Umbraco builder.</param>
    public void Compose(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddScoped<IUserContextAccessor, BackOfficeUserContextAccessor>();

        // Named JSON options for the package's controllers (see LogExplorerApiControllerBase):
        // the backoffice options cannot read the Core contracts (no IReadOnlySet<string>
        // converter, and they require FilterNode's "kind" to come first).
        builder
            .Services.AddControllers()
            .AddJsonOptions(
                LogExplorerApi.Name,
                options => LogJson.Apply(options.JsonSerializerOptions)
            );

        LogExplorerApiDocument.Register(builder);
    }
}

/// <summary>
/// Registers the <c>log-explorer</c> OpenAPI document. Implemented once per Umbraco major in
/// <c>LogExplorerApiDocument.V17.cs</c> and <c>LogExplorerApiDocument.V18.cs</c>.
/// </summary>
internal static partial class LogExplorerApiDocument
{
    /// <summary>The document title shown in the Swagger UI.</summary>
    internal const string Title = "Umbraco Log Explorer Management API";

    /// <summary>Adds the document and its backoffice security requirements.</summary>
    /// <param name="builder">The Umbraco builder.</param>
    internal static partial void Register(IUmbracoBuilder builder);
}
