# Contributing

Thanks for your interest in Umbraco Log Explorer. This project is small and maintainer time is the
scarcest resource, so the contribution rules below are deliberately strict about **pull
requests** while staying wide open to **ideas and issues**.

Read this before opening a pull request. It applies to everyone, and it applies with
particular force to AI agents and AI-assisted contributions.

---

## TL;DR

- **Ideas, bug reports, feature requests: always welcome**, from humans and AI alike. Open an
  issue.
- **Pull requests are issue-first and invitation-only.** Open a PR only against an issue a
  maintainer has already accepted (labelled `accepted` or `help wanted`).
- **No drive-by AI PRs.** Unsolicited, AI-generated PRs for low-value changes are closed
  unread. This is not hostility toward AI - it is about protecting review capacity.

---

## Ideas and issues are welcome (this part is easy)

File an issue for anything: a bug, a rough edge, a missing feature, an idea, a question about
intended behaviour. We do not care whether you are a person, a person using an AI, or an agent
acting on its own. What we care about is that the issue is **specific and grounded**:

- **Bugs:** what you did, what you saw, what you expected, and the versions: the package, Umbraco,
  and the browser for anything in the backoffice. Say which log source you were using. A
  copy-pasteable repro beats a description.
- **Feature requests:** the task you are trying to accomplish and why the explorer does not cover
  it today.
- **One issue per topic.** Do not batch unrelated ideas into one issue.

Good issues are the single most useful contribution you can make. They are how work gets
prioritised, and they are a prerequisite for every PR. A security problem is the exception: report
it privately, as [SECURITY.md](SECURITY.md) describes.

---

## Pull requests: the rules

### 1. Issue-first, always

Every PR must close exactly one issue that a maintainer has **accepted** (labelled `accepted`
or `help wanted`). If the issue does not exist yet, open it and wait for it to be triaged. If
it exists but is not accepted, wait - or comment to ask whether a PR would be welcome. A PR
that arrives before its issue is accepted may be closed with a pointer back to this document,
regardless of how good the code is.

Why: an accepted issue means the change is wanted, the approach is roughly agreed, and review
time is budgeted. A PR without that is a request for unbudgeted review time.

### 2. A human is accountable

Every PR must have a human who takes responsibility for it, will respond to review comments,
and understands the change well enough to defend and revise it. "An agent wrote it and I
forwarded it" is not accountability. If you cannot explain why each hunk is there, do not open
the PR.

### 3. Disclose AI assistance

If AI tools were used to write or substantially shape the change, say so in the PR
description. This is not a mark against the PR - it is context that helps review. The PR
template has a checkbox for it. Do not hide it.

### 4. Scope: one accepted issue, nothing extra

Keep the diff to what the accepted issue needs. Do not fold in unrelated reformatting,
renames, dependency bumps, or "while I was here" cleanups. If you spot something else worth
doing, open an issue for it.

### 5. It must build green and follow the repo conventions

- Every check under [Test](#test) passes, for both Umbraco majors.
- The change follows the [Conventions](#conventions) below, and a user-visible change updates
  `CHANGELOG.md` and its page in `docs/`.

See [AGENTS.md](AGENTS.md) for the working rules and the project facts to follow.

---

## Specifically for AI agents and AI-assisted contributors

As a contributor, the bar for PRs is the same as for anyone, and the patterns below are the ones
that get an unsolicited PR closed. Do not open PRs that consist mainly of:

- typo, spelling, or comment-wording sweeps;
- whitespace, formatting, or import-ordering changes;
- speculative refactors or renames with no accepted issue behind them;
- dependency version bumps with no stated reason or failing build behind them;
- mass "added tests" or "added XML docs" changes to code nobody asked you to touch;
- documentation churn that restates what is already documented;
- re-implementations of things that already work.

If you believe one of the above is genuinely worth doing, the path is the same as for
everyone: **open an issue, make the case, wait for it to be accepted.** Then implement it.

This is the whole policy in one sentence: **bring us your issues and ideas freely; bring us
code only after we have agreed the code is worth writing.**

---

The rest of this page explains how the repository is laid out, how to build and test it, and the
conventions a change should follow.

## Layout

```text
src/
  Umbraco.Community.LogExplorer.Core/   contracts, the search syntax parser, filters; no Umbraco references
  Umbraco.Community.LogExplorer/        the Umbraco package: API, sources, files provider, backoffice client (Client/)
tests/                                  xUnit v3 test projects, one per area, plus the provider contract suite
samples/                                Umbraco 17 and 18 sample sites with a log generator
docs/                                   the public documentation
```

Code is organised by feature folder, not by technical layer: each feature owns its endpoint, DTOs,
handler and client elements (ADR 0002). The Core project must not reference Umbraco packages.

## Prerequisites

- .NET SDK 10 (see `global.json`)
- [Bun](https://bun.sh/) for the backoffice client

## Build and run

The package builds once per Umbraco major (ADR 0010). `dotnet build` targets Umbraco 17;
`-p:UmbracoMajor=18` targets Umbraco 18. Major-specific code goes only in paired `*.V17.cs` and
`*.V18.cs` files.

```sh
# The backoffice client (its output is gitignored)
cd src/Umbraco.Community.LogExplorer/Client && bun install && bun run build

# A sample site: https://localhost:44370/umbraco (17) or https://localhost:44380/umbraco (18)
dotnet run --project samples/LogExplorer.Site17
dotnet run --project samples/LogExplorer.Site18 -p:UmbracoMajor=18
```

Each sample site runs on one major. A solution build for the other major skips it, and building or
running it on its own for the other major fails with the switch to use. Both sample sites install
themselves on first run (SQLite) and sign in with `admin@example.com` / `1234567890` (development
only). [samples/README.md](samples/README.md) covers the log generator.

## Test

Run every suite before you commit, for both majors when your change touches Umbraco APIs:

```sh
dotnet build && dotnet test
dotnet build -p:UmbracoMajor=18 && dotnet test --solution Umbraco.Community.LogExplorer.slnx -p:UmbracoMajor=18

cd src/Umbraco.Community.LogExplorer/Client
bun run format:check && bun run typecheck && bun run test && bun run build
bun run e2e   # Playwright on both sample sites
```

Stop any running sample site first: a locked DLL shows up as build errors.

## Conventions

- **Decisions:** recorded as [architecture decision records](docs/adr/README.md). Read the ones
  near your change; write a new one when your change alters a contract, a dependency, a supported
  version, or something an earlier ADR decided.
- **Tooling:** Bun, Vite, Vitest and Prettier for the client; xUnit v3, NSubstitute and CSharpier
  for .NET (ADR 0006).
- **UI:** use UUI and the backoffice's `umb-*` elements, with their variants, before writing a
  custom element. Every control must be keyboard-reachable and labelled.
- **Docs:** C# members carry XML doc comments; exported TypeScript carries TSDoc.
- **Tests:** new or changed code comes with tests.
- **Commits:** [Conventional Commits](https://www.conventionalcommits.org/), ASCII only.
- **Changelog:** add a line to [CHANGELOG.md](CHANGELOG.md) for every user-visible change.
- **Docs pages:** a user-visible change updates the page in `docs/` that describes it, following
  [Writing docs](#writing-docs).

## Writing docs

The pages in `docs/` follow [Diataxis](https://diataxis.fr/): a tutorial, how-to guides,
reference and explanation, with one kind per page. Write them in this voice:

- **Speak to the reader.** Second person, imperative, present tense: "Click a value to filter on
  it." Never "we".
- **Active voice when a person acts.** Passive is fine when the system is the actor and naming it
  adds nothing: "a warning is logged at startup".
- **No contractions.** Write "do not", "cannot", "it is".
- **One idea per sentence.** Aim for under 25 words. When a sentence strings three or more clauses
  together, make it a list or split it.
- **No parentheses with clauses inside.** Keyboard alternatives and other asides get their own
  sentence or bullet.
- **Action verbs:** *choose* a labelled control (a button, tab or menu item); *click* or *drag*
  something on the canvas (a histogram bar, a row, a value, a chip, a level toggle) or a box you
  type in; *press* a key. Do not use *select* for an action.
- **UI names in bold, spelled as on screen:** **Settings > Advanced > Log Explorer**, **Same
  request**.
- **What the reader can act on, not how it is built.** Leave out internals the reader cannot see
  or change, such as threading or parsing strategy; those belong in an ADR.
- **Numbers, not adjectives.** "7 entries either side", "256 MB by default". No "easy", "simply",
  "powerful" or "blazing".
- **Shipped features only.** Planned work goes under a **Coming next** heading.
- **en-GB spelling:** organise, summarise, licence (noun).
- **Descriptive links** that name the page they open, and a **Related** list at the end of each
  page.

## Licence

By contributing you agree that your contributions are licensed under the repository's
[MIT licence](LICENSE).
