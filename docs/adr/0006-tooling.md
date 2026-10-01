# ADR 0006: Tooling

- Status: Accepted
- Date: 2026-10-01

## Decision

| Concern | Choice |
| --- | --- |
| Client package manager and script runner | Bun (replacing the npm setup the Umbraco extension template generates) |
| Client build | Vite, TypeScript strict, Lit, `@umbraco-cms/backoffice` |
| Client unit tests | Vitest |
| Client end-to-end tests | Playwright, without Umbraco's helpers (ADR 0020) |
| Client formatting | Prettier |
| .NET tests | xUnit v3, Arrange-Act-Assert, `MethodName_Scenario_ExpectedBehaviour` names |
| .NET mocking | NSubstitute |
| .NET formatting | CSharpier |
| .NET build | `Directory.Build.props`, central package management, nullable enabled, warnings as errors, analyzers |
| Integration containers | Testcontainers (`datalust/seq`) |
| CI | GitHub Actions |

## Consequences

- CI installs Bun; the .NET build invokes `bun run build` for the client before pack.
- Anyone contributing needs Bun and the .NET 10 SDK.
