# Umbraco.Community.LogExplorer

An Umbraco 17/18 backoffice log explorer: click-to-filter search over Umbraco's own log files, then Application Insights and Seq, through a provider model built on the OpenTelemetry log record.

The build brief (`BRIEF.md`), the UI brief (`UI-BRIEF.md`) and the session handoff live in the maintainer's private planning repository, not here (ADR 0030). A gitignored `CLAUDE.local.md` at the checkout root imports the briefs when it exists; references to "BRIEF §n" and "UI brief §n" mean those files.

## Working rules

- Work is tracked in the maintainer's private issue tracker (milestone per phase, epic per feature area; `CLAUDE.local.md` says where). Pick up an issue, plan it in plan mode, wait for approval, then build. Close it with a short resolution comment.
- Verify Umbraco and SDK APIs against the installed version before use; never invent aliases, routes, element names or members. When docs and the installed package disagree, the package wins.
- Decisions and Verify outcomes that change the briefs go in `docs/adr/` (see its README), and the brief is updated in the same change.
- The Core project must not reference Umbraco packages.
- Never query through `ILogViewerService` (ADR 0003).
- Organise by feature folder, not technical layer (ADR 0002).
- UI: UUI and `umb-*` components first, with their variants (UI brief §4.0); use the `umbraco-backoffice-ui-developer` agent for client work.
- Tooling (ADR 0006): Bun + Vitest + Prettier for the client; xUnit v3 + NSubstitute + CSharpier for .NET.
- One package per Umbraco major (ADR 0010): `dotnet build` targets 17, `-p:UmbracoMajor=18` targets 18. Major-specific code goes in paired `*.V17.cs` / `*.V18.cs` files. Build and test both majors before committing anything that touches Umbraco APIs.
- Run `dotnet test` and `bun run test` (client) before every commit, and read the build summary: stop any running sample site first, because a locked DLL shows up as build errors. Conventional commits, ASCII only. Update `CHANGELOG.md` per user-visible change.
- Branching: Phase 0 commits go to `main`; from Phase 1, one branch and PR per phase. Parallel lanes (BRIEF §15) use their own worktree branches merged into the phase branch.
- At the start of a session, read the handoff that `CLAUDE.local.md` points to, if it exists: it holds the current state, the lane plan and the gotchas from the last session.
