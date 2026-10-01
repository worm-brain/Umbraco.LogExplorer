# ADR 0002: Organise code by feature (vertical slices)

- Status: Accepted
- Date: 2026-10-01

## Context

The build brief fixes the project split (Core, the package, one project per provider, tests). It
does not say how code is arranged inside each project. Horizontal folders (`Controllers/`,
`Services/`, `Models/`) spread one feature across many folders and make every change touch several.

## Decision

Inside every project, code is grouped by feature or use case. Each feature folder owns its
endpoint, request/response DTOs, handler, validation and tests' counterpart.

Illustrative layout for the package project (names settle during Phase 0):

```
Umbraco.Community.LogExplorer/
  Features/
    Sources/         GET /sources, source registry, options validation
    Search/          POST /search, POST /parse
    Histogram/
    Facets/
    Patterns/
    Context/         Around this, Same request
    Fields/
    Compile/         compile + validate
    SavedViews/      table, migration, CRUD, import-core      (Phase 2)
    Export/                                                  (Phase 2)
  Providers/
    UmbracoFiles/    the files provider (a feature in its own right)
  Infrastructure/    composer, options binding, OpenAPI registration, ProblemDetails, masking, audit
  Client/src/
    workspace/  search/  chips/  histogram/  facets/  results/  entry-detail/
    patterns/  overview/  views/  shared/
```

Core is organised the same way (`Records/`, `Query/`, `SimpleSyntax/`, `Severity/`, `Sources/`,
`Fake/`), and test projects mirror the feature folders of the project they test.

Cross-cutting concerns that genuinely apply to every feature (masking, audit, auth, error mapping)
live in `Infrastructure/` and are applied once in the pipeline, not per feature.

## Consequences

- A feature change is usually local to one folder.
- Tickets are written as feature slices (endpoint + UI + tests), which matches how the work is
  tracked in GitHub issues.
