# ADR 0005: One simple-syntax parser, in Core, called over the API

- Status: Accepted
- Date: 2026-10-01

## Context

The simple syntax (BRIEF §6.2) must turn typed input into chips when the user presses Enter. The
brief puts the parser in Core so that the API, scripts and a future CLI share it. The browser
also needs it. Writing it twice (C# and TypeScript) invites the two to drift; the prototype
already contains a third, informal version.

## Decision

- The parser lives only in Core (C#), with the exhaustive table-driven tests.
- The package exposes `POST /parse` (`{ input }` -> chips as `FilterNode`s, the resolved level set,
  leftover text, and any fallback explanation such as an unbalanced quote). Leftover text is
  returned as text chips in the chip list (all bare words as one `TextNode`, each quoted phrase
  as its own), not as a separate string.
- The client calls it on Enter. It does not parse locally. Autocomplete suggestions are a separate
  concern and may be computed client-side from cached field and facet data.

## Consequences

- One behaviour everywhere; one test table.
- Enter costs one local round trip, which is acceptable for a backoffice tool talking to its own
  server. If that ever proves noticeable, revisit with a generated TS port tested against the same
  table, not a hand-written one.
