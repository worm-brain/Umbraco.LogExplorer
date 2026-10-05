# ADR 0030: The maintainer briefs live in a private planning repository

- Status: Accepted
- Date: 2026-10-05
- Issue: #52

## Context

ADR 0028 moved the build brief, the UI brief, the session handoff, the landscape research and the
design prototype into `private/`, to move out before the repository went public. Releasing 1.0
(ADR 0029) and listing the package on the Umbraco Marketplace need a public repository: the
Marketplace reads `umbraco-marketplace.json` and the screenshots from the default branch, and the
NuGet readme and SourceLink link to it.

GitHub has no private folder, branch or wiki inside a public repository, and a normal delete commit
leaves files readable in history.

## Decision

- The internal material lives in one private repository for all of the maintainer's projects,
  `worm-brain/planning`, with a folder per project (`Umbraco.LogExplorer/`).
- This repository keeps no copy. `private/` is removed, and the history is rewritten before the
  repository is made public so that no commit carries the briefs (`private/`, and the earlier
  `docs/BRIEF.md`, `docs/UI-BRIEF.md`, `docs/HANDOFF.md`, `docs/research.md` and prototype paths).
- `CLAUDE.md` keeps the working rules and says where the briefs are. A gitignored `CLAUDE.local.md`
  at the checkout root imports them by absolute path, so sessions in `.claude/worktrees/*` pick
  them up from the checkout root.
- ADRs stay public in `docs/adr/`.

## Consequences

- References to "BRIEF §n" and "UI brief §n" in ADRs, issues and code comments point at files
  contributors cannot read. New ADRs should state the facts they rely on rather than cite a brief
  section.
- Rewriting history changes every commit id. Open clones and worktrees must be re-cloned or reset,
  and links to old commit ids break. Pull request refs on GitHub keep the old commits until GitHub
  Support purges them.
- A brief change is committed in the planning repository alongside the ADR here.
