# ADR 0028: `docs/` holds the public documentation, and NuGet gets its own readme

- Status: Accepted; where the internal material lives is amended by ADR 0030
- Date: 2026-10-02
- Issue: #76

## Context

`docs/` held the build briefs, the session handoff, the landscape research and the prototype:
material written for the maintainer and for coding agents, not for people installing the package.
The repository is about to be opened up, and #76 asks for a README with screenshots and user
documentation. Research into well-regarded open source docs (Diataxis, Standard Readme, the
Umbraco Marketplace and NuGet rules) pointed to three things:

- user docs read best organised by what the reader is doing: tutorials, how-to guides, reference
  and explanation (Diataxis);
- the GitHub README, the NuGet readme and the Marketplace listing have different readers and
  different rendering rules;
- nuget.org renders no relative links, no relative images and no raw HTML, and a published
  readme cannot be changed without a new package version.

The root `README.md` was packed into every package, so a screenshot-heavy README would show broken
images on nuget.org.

## Decision

- `docs/` is the public documentation: `index.md`, `tutorials/`, `how-to/` (one page per feature,
  with screenshots), `reference/`, `explanation/`, `extend/` and `adr/`. Screenshots live in
  `docs/images/` and are shared with the root README.
- The internal material moves to `private/` for now: `BRIEF.md`, `UI-BRIEF.md`, `HANDOFF.md`,
  `research.md` and `design/`. `CLAUDE.md` imports the briefs from there. They are expected to move
  to a private planning repository before the repository is made public.
- ADRs stay public in `docs/adr/`.
- The root has `README.md`, `CHANGELOG.md`, `CONTRIBUTING.md`, `SECURITY.md` and `LICENSE`.
- The packages carry `assets/nuget/README.md`: a short, text-only readme with absolute links to the
  GitHub docs. The root README keeps relative links and screenshots for GitHub.

## Consequences

- A user-visible change updates its page in `docs/` as well as `CHANGELOG.md` (CONTRIBUTING).
- Two readmes must agree on install steps and status; the NuGet one stays short so that rarely
  matters.
- Screenshots go stale as the UI changes. They were taken at 1440 x 900, device scale 2, from the
  Umbraco 17 sample site reading the UI brief's sample hour written out as real log files.
- References to the briefs in issues and older commits say `docs/BRIEF.md` or
  `private/BRIEF.md`; since ADR 0030 they live in the private planning repository.
