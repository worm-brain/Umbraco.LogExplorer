# ADR 0015: The results list virtualises with its own fixed-height row window

- Status: Accepted
- Date: 2026-10-01
- Issue: #38

## Context

BRIEF §6.7 and UI brief §4.0 ask for a virtualised results list and say to verify
`@lit-labs/virtualizer` "or what the backoffice already ships". Acceptance for #38: scrolling
10,000 loaded rows drops no frames.

Verified:

- `@umbraco-cms/backoffice` 17.0.0 to 17.7.0 and 18.0.0 ship no virtualiser: `external/lit`
  re-exports Lit, its decorators and directives only, and no `umb-*`/`uui-*` element virtualises.
- `@lit-labs/virtualizer` 2.1.1 can be bundled onto the backoffice's Lit (Vite aliases for its five
  Lit imports plus a shim for `PartType`), and worked. But it renders through Lit's keyed `repeat`,
  and Lit 3.3's `removePart` (`lit-html/directive-helpers.js`) clears a part and removes its start
  marker but leaves its end marker. Every row scrolled past therefore leaves one comment node in
  the list (reproduced with plain `repeat`, both with the Lit the 17.7 backoffice serves and with
  `lit-html` 3.3.3: 10 rows shifted 20 times leave 20 extra comments). The virtualizer's `_children` getter walks `firstElementChild` once per
  child per frame, so the cost grows with the distance scrolled: after scrolling 11,424 rows the
  list held 14,905 comments and frames took 30 to 50 ms (20 fps).

## Decision

- No virtualiser dependency. The results list renders a window of rows (the viewport plus eight
  rows either side) with a plain `map`, so Lit reuses the same row elements positionally and only
  updates their bindings as the window slides; nothing is inserted or removed while scrolling.
- Every row has the same height: two clamped message lines plus padding, measured once from the
  first rendered row. Rows are positioned with `translateY(index * rowHeight)` inside a spacer as
  tall as all loaded rows. The window maths is a pure function (`rowWindow`) with unit tests.
- Severity colours stay the brief's six hexes as `--log-explorer-level-*` tokens rather than
  `--uui-color-positive`/`-danger`: those pairs fall to about 4.4:1 and 4.3:1 in the backoffice's
  dark theme.

## Consequences

- Measured on Site17 with 11,424 loaded rows: 60 fps throughout (median and p99 frame 16.7 ms,
  no frame over 20 ms while scrolling 120 px per frame for 5 s); the DOM holds about 28 rows.
- One-line messages still take a two-line row. That keeps rows dense (39 px at the default font
  size) and makes positioning exact; variable heights would need per-row measurement.
- Rows outside the window are not in the tab order; keyboard movement through the whole list
  comes with `j`/`k` (#45). Because rows are reused positionally, a focused row that scrolls out
  of the window shows another entry; the drawer (#41) opens from the record, not the element.
- Chrome counts the moved rows as layout shifts (CLS) in a trace. CLS is a page-load metric and
  does not apply to a backoffice view.
