# ADR 0025: The fields panel is resized by a custom separator in the gap, not `umb-split-panel`

- Status: Accepted
- Date: 2026-10-02

## Context

Jack asked for the fields panel to be resizable the way Umbraco's section sidebar is: hover the
gap between the fields panel and the results for a `col-resize` cursor, then drag. UI brief §4.0
puts backoffice elements first, and the backoffice ships `umb-split-panel` (the element behind the
sidebar, with a `#divider-touch-area`), present in 17.0.0, 17.7, 18.0.0 and 18.2. Checked against
17.7:

- It keeps its divider position as a percentage; `lock="start"` keeps the start panel's pixel width
  on resize but parses `position` as pixels or percent only (a `28ch` default becomes 28 px).
- Its touch area starts at the divider and extends into the end panel, and it has no gap between
  the panels, so the gap the user hovers would need padding tricks to become the target.
- It knows nothing of the fields panel's collapsed strip (UI brief §4.8); collapsing would leave the
  column at its dragged width unless the view rewrote `position` on every toggle.
- Its touch area is focusable with `aria-valuetext` but no role or label, which axe reports, and
  its arrow keys move by 1% of the width.

The fields panel already exposes its width as `--log-explorer-fields-panel-width`.

## Decision

- A small `log-explorer-fields-resizer` sits between the fields panel and the results and is the
  gap (`--uui-size-space-4` wide): `role="separator"`, `aria-orientation="vertical"`, labelled
  "Resize fields panel", focusable, with `aria-valuenow`/`min`/`max` in pixels and a hint tooltip.
- Pointer drag (with pointer capture), Left/Right by 16 px (64 px with Shift), Home/End to the
  minimum and maximum, and double-click to reset to the default width. A line shows down the
  middle of the gap on hover, focus and drag.
- Widths are clamped between 200 px and whatever leaves the results 40% of the body
  (`panel-width.ts`). The Search view applies the width to the panel and stores it in
  `localStorage` (`logExplorer.fieldsPanelWidth`) when a drag or key press ends.
- The fields panel reflects a `collapsed` attribute; while it is collapsed or hidden the divider is
  hidden and the plain gap returns.

## Consequences

- One more custom element, small and covered by unit tests (`panel-width.test.ts`) and an e2e
  drag, reload, keyboard and reset test (`e2e/layout.spec.ts`).
- If a later backoffice version gives `umb-split-panel` pixel positions, a role and collapse
  support, revisit this.
