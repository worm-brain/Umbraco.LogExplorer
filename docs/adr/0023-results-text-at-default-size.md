# ADR 0023: Results text uses the default type size; the 1440 x 900 row count is a guide

- Status: Accepted
- Date: 2026-10-02

## Context

UI brief §2 and §11 item 1 asked for at least 12 result rows at a 1440 x 900 window, and the results
list used `--uui-type-small-size` (12 px) for its header and rows to reach it. Using the explorer,
Jack found 12 px too small to read comfortably. At `--uui-type-default-size` (14 px) a row grows from
about 41 px to about 47 px, and 11 rows fit at 1440 x 900. The 12-row figure was an estimate from
the design stage, not a measured user need.

## Decision

- The results header and rows use `--uui-type-default-size`. The level badge stays at the small
  size so it never sets the row height.
- Rows keep their fixed two-line height (ADR 0015) and centre their content vertically
  (`align-content` and `align-items`), so one-line messages sit in the middle of the row.
- The 1440 x 900 row count is a guide. The target is now at least 11 rows, and
  `e2e/layout.spec.ts` checks 11 so a further loss of space is still caught.

## Consequences

- One row fewer is visible at 1440 x 900; the list still scrolls and `j`/`k` reach every row.
- A later readability change may trade rows again; record it here rather than in a test comment.
