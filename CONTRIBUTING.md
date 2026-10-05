# Contributing

Thanks for helping. This page explains how the repository is laid out, how to build and test it,
and the conventions a change should follow.

## Before you start

- Look for an issue, or open one, before starting anything large. Issues are grouped by milestone
  (one per phase) and by epic (one per feature area).
- Decisions are recorded as [architecture decision records](docs/adr/README.md). Read the ones near
  your change; write a new one when your change alters a contract, a dependency, a supported
  version, or something an earlier ADR decided.

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

Both sample sites install themselves on first run (SQLite) and sign in with `admin@example.com` /
`1234567890` (development only). [samples/README.md](samples/README.md) covers the log generator.

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

## Pull requests

Keep a pull request to one issue where you can, say which issue it closes, and include the test
output. Screenshots help for UI changes.
