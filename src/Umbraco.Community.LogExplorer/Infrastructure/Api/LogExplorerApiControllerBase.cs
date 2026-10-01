using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Common.Attributes;
using Umbraco.Cms.Api.Common.Filters;
using Umbraco.Cms.Web.Common.Authorization;
using Umbraco.Cms.Web.Common.Routing;
using CoreConstants = Umbraco.Cms.Core.Constants;

namespace Umbraco.Community.LogExplorer.Infrastructure.Api;

/// <summary>
/// Base class for every Log Explorer Management API controller (BRIEF §11.1, ADR 0009). Routes
/// under <c>/umbraco/log-explorer/api/v{version}</c>, maps to the package's own <c>log-explorer</c>
/// Swagger document, requires Settings-section access (BRIEF §12), and turns the provider
/// exceptions listed in <see cref="LogExplorerProblemFilter"/> into ProblemDetails with a <c>code</c>.
/// </summary>
/// <remarks>
/// Uses the backoffice JSON options, as the core Management API does, so enums serialise as
/// strings, matching what the Swagger document (and so the generated client) promises.
/// </remarks>
[ApiController]
[BackOfficeRoute($"{LogExplorerApi.Name}/api/v{{version:apiVersion}}")]
[Authorize(Policy = AuthorizationPolicies.SectionAccessSettings)]
[MapToApi(LogExplorerApi.Name)]
[JsonOptionsName(CoreConstants.JsonOptionsNames.BackOffice)]
[TypeFilter<LogExplorerProblemFilter>]
public abstract class LogExplorerApiControllerBase : ControllerBase { }
