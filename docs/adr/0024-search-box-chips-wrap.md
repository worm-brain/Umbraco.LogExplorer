# ADR 0024: Search box chips wrap onto up to three rows instead of scrolling on one line

- Status: Accepted
- Date: 2026-10-02

## Context

UI brief §4.3 kept the search box to one line: chips scrolled sideways, with no scrollbar, in an
area capped at 70% of the box so the text input kept room. With several chips in a narrow window
the last chip was cut off at the cap with nothing to say more were hidden, so filters that were
running could not be seen. Jack reported it from the Site17 sample site.

## Decision

- Chips wrap. The chip list shows up to three rows and grows the search box, and with it the
  query bar, on all three views. Beyond three rows the list scrolls vertically, with a thin
  scrollbar. Adding a chip scrolls the list to the end.
- The text input keeps the remaining width beside the chips (the 70% cap stays).
- The other query-bar controls stay one control tall (`--uui-size-11`) and align to the top.
- In a search box narrower than 900 px each filter chip is capped at 180 px
  (`--uui-size-100` x 0.6), so long values truncate sooner; the full filter is in the tooltip.

## Rejected

- Icon-only chips with the label in a tooltip: active filters would no longer be readable at a
  glance (UI brief principle 3), and tooltips do not work on touch.
- A "+N more" pill that opens the hidden chips in a popover: keeps the bar's height but hides
  filters and needs measuring plus a keyboard-accessible popover. The fallback if wrapping is
  not right in use.

## Consequences

- The histogram and results move down while chips wrap; the 1440 x 900 row guide (ADR 0023)
  assumes one row of chips.
- The wheel-to-horizontal-scroll handler is gone; the list scrolls natively.
