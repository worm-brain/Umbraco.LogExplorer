# ADR 0021: Menus and toggles patch the ARIA UUI does not expose

- Status: Accepted
- Date: 2026-10-01
- Issue: #51

## Context

UI brief §4.0 builds the source picker and time range picker from `uui-button` +
`uui-popover-container` + `uui-menu-item`, and §7 asks for `aria-expanded`/`aria-haspopup` on menu
buttons, `aria-pressed` on toggles and arrow-key navigation in menus. Checked against UUI 2.0.2
(backoffice 17.7) and in the accessibility tree on both sample sites:

- `uui-button` forwards only `aria-label`/`aria-labelledby` to the `<button>` in its shadow root,
  which is what takes focus. `aria-expanded`, `aria-pressed` or `aria-haspopup` on the host never
  reach assistive technology. `umb-dropdown` has the same gap (and closes on any inner click).
- `uui-menu-item` is a navigation-tree item: its host takes `role="menu"` unless it already has a
  role, and its shadow root wraps the label `<button>` in `div role="menuitem" aria-label="menuitem"`.
  A list of them reads as one menu per item, each item named "menuitem" around a nested button.
  Nothing in UUI handles arrow keys between them.
- `uui-popover-container` wraps its content in a `uui-scroll-container`, which gives itself
  `tabindex="0"`: one Tab stop before the first item. Its `no-scroll` attribute renders the slot
  without it.
- A native popover returns focus to its invoker only when it moved focus in itself (autofocus), so
  choosing an item left focus on `<body>`.

No other UUI or `umb-*` element offers a menu button with menu semantics.

## Decision

- Keep the UUI elements and add what they lack in two small helpers in `src/shared/`:
  - `syncButtonAria(root)` copies `data-pressed`, `data-expanded` and `data-haspopup` from each
    `uui-button` host onto its inner `#button` after every update. Used by the level toggles, the
    show-query toggle, the fields panel and the drawer's expand toggles.
  - `PopoverMenuController` turns a trigger + popover + `uui-menu-item` list into a WAI-ARIA menu
    button: `aria-haspopup`/`aria-expanded` on the trigger, one `role="menu"` wrapper with the items'
    hosts and wrappers set to `role="none"` and their label buttons to `role="menuitemradio"` with
    `aria-checked` and `tabindex="-1"`; arrow keys, Home and End between items (wrapping); focus to
    the checked item on open; focus back to the trigger after a choice or Escape; closed when Tab
    moves focus out. Menus set `no-scroll` on the popover.
- The histogram bars become one Tab stop with a roving tabindex (Left/Right/Home/End), using the
  same pure `rovingIndex` as the menus.

## Consequences

- Both helpers reach into UUI shadow roots by id (`#button`, `#menu-item`, `#label-button`). A UUI
  release that renames them silently drops the patched ARIA; the e2e suite (`e2e/a11y.spec.ts`)
  asserts the roles, states and focus moves on both majors, so it would fail rather than regress
  unseen. Remove the patching if UUI gains these attributes.
- The patching runs after every host update and is idempotent; it never adds elements.
