# Umbraco.Community.LogExplorer

An Umbraco 17/18 backoffice log explorer: click-to-filter search over Umbraco's own log files, then Application Insights and Seq, through a provider model built on the OpenTelemetry log record.

@docs/BRIEF.md
@docs/UI-BRIEF.md

## Working rules

- Work is tracked in GitHub issues on `worm-brain/Umbraco.LogExplorer` (milestone per phase, epic per feature area). Pick up an issue, plan it in plan mode, wait for approval, then build. Close it with a short resolution comment.
- Verify Umbraco and SDK APIs against the installed version before use; never invent aliases, routes, element names or members. When docs and the installed package disagree, the package wins.
- Decisions and Verify outcomes that change the briefs go in `docs/adr/` (see its README), and the brief is updated in the same change.
- The Core project must not reference Umbraco packages.
- Never query through `ILogViewerService` (ADR 0003).
- Organise by feature folder, not technical layer (ADR 0002).
- UI: UUI and `umb-*` components first, with their variants (UI brief §4.0); use the `umbraco-backoffice-ui-developer` agent for client work.
- Tooling (ADR 0006): Bun + Vitest + Prettier for the client; xUnit v3 + NSubstitute + CSharpier for .NET.
- Run `dotnet test` and `bun run test` (client) before every commit. Conventional commits, ASCII only. Update `CHANGELOG.md` per user-visible change.
- Branching: Phase 0 commits go to `main`; from Phase 1, one branch and PR per phase. Parallel lanes (BRIEF §15) use their own worktree branches merged into the phase branch.
