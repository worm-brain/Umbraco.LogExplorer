using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Common.Attributes;
using Umbraco.Cms.Api.Common.Filters;
using Umbraco.Cms.Web.Common.Authorization;
using Umbraco.Cms.Web.Common.Routing;

namespace Umbraco.Community.LogExplorer.Infrastructure.Api;

/// <summary>
/// Base class for every Log Explorer Management API controller (BRIEF §11.1, ADR 0009). Routes
/// under <c>/umbraco/log-explorer/api/v{version}</c>, maps to the package's own <c>log-explorer</c>
/// Swagger document, requires Settings-section access (BRIEF §12), and turns the provider
/// exceptions listed in <see cref="LogExplorerProblemFilter"/> into ProblemDetails with a <c>code</c>.
/// </summary>
/// <remarks>
/// Reads and writes JSON with the package's own named options, configured from
/// <c>LogJson</c> (ADR 0008, ADR 0013): camelCase enums, the <c>kind</c> discriminator anywhere in
/// a filter node, and level sets. The backoffice options cannot read <c>LogQuery</c>.
/// </remarks>
[ApiController]
[BackOfficeRoute($"{LogExplorerApi.Name}/api/v{{version:apiVersion}}")]
[Authorize(Policy = AuthorizationPolicies.SectionAccessSettings)]
[MapToApi(LogExplorerApi.Name)]
[JsonOptionsName(LogExplorerApi.Name)]
[TypeFilter<LogExplorerProblemFilter>]
public abstract class LogExplorerApiControllerBase : ControllerBase { }
