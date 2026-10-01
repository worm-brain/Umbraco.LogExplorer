# ADR 0009: The package API lives at /umbraco/log-explorer/api/v1

- Status: Accepted
- Date: 2026-10-01
- Issue: #27

## Context

BRIEF §11.1 assumed the endpoints would sit under the core Management API, at
`/umbraco/management/api/v1/log-explorer`, and marked the route convention as **Verify**.

Umbraco 17's convention for package APIs, used by the `umbraco-extension` template and the docs'
"Creating a backoffice API" tutorial, is a base controller with `[BackOfficeRoute("{api}/api/v{version:apiVersion}")]`
and `[MapToApi("{api}")]`. That gives the package its own route prefix and its own Swagger document,
separate from the core `management` document. Putting routes under `management/` would mix them into
the core API's URL space without being part of its document, and there is no supported hook for
adding to the core document.

## Decision

- Base route: `/umbraco/log-explorer/api/v1/...` (for example `GET /umbraco/log-explorer/api/v1/sources`).
- Swagger document: `log-explorer`, served at `/umbraco/swagger/log-explorer/swagger.json` on 17 and
  `/umbraco/openapi/log-explorer.json` on 18 (ADR 0010), with backoffice
  OAuth security requirements so the Swagger UI and the generated client authenticate as the signed-in user.
- Every controller derives from `LogExplorerApiControllerBase`, which sets the route, the
  `SectionAccessSettings` policy (verified present in 17.0.0), the backoffice JSON options and the ProblemDetails
  mapping.
- The relative paths in BRIEF §11.1 (`/sources`, `/parse`, `/sources/{alias}/search`, ...) are unchanged; only the
  base differs.

## Consequences

- BRIEF §11.1's base URL is updated.
- Scripts, CLIs and the community MCP server call `/umbraco/log-explorer/api/v1` with a backoffice token, the same
  as any package API.
