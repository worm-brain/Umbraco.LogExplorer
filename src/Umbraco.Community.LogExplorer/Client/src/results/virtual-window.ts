/** A slice of rows to render: indices `first` (inclusive) to `end` (exclusive). */
export interface RowWindow {
  first: number;
  end: number;
}

/**
 * Works out which rows of a fixed-row-height list are worth rendering: the ones in the viewport
 * plus `overscan` rows either side, so a fast scroll does not show blank space before the next
 * render.
 *
 * @param scrollTop - The list's scroll offset in pixels.
 * @param viewportHeight - The list's visible height in pixels.
 * @param rowHeight - Height of every row in pixels; must be positive.
 * @param count - Number of rows loaded.
 * @param overscan - Extra rows rendered above and below the viewport.
 * @returns The window, clamped to `[0, count]`; empty when there are no rows.
 */
export function rowWindow(
  scrollTop: number,
  viewportHeight: number,
  rowHeight: number,
  count: number,
  overscan: number,
): RowWindow {
  if (count <= 0 || rowHeight <= 0) return { first: 0, end: 0 };
  const top = Math.max(0, scrollTop);
  const firstVisible = Math.floor(top / rowHeight);
  const lastVisible = Math.ceil((top + Math.max(0, viewportHeight)) / rowHeight);
  return {
    first: Math.min(count, Math.max(0, firstVisible - overscan)),
    end: Math.min(count, lastVisible + overscan),
  };
}

/**
 * The scroll offset that brings one row fully into view, scrolling as little as possible: a row
 * already fully visible leaves the offset alone, one above aligns to the top, one below to the
 * bottom. Used to return focus to a row the window has scrolled out of the DOM.
 *
 * @param index - The row to reveal.
 * @param rowHeight - Height of every row in pixels.
 * @param scrollTop - The list's current scroll offset.
 * @param viewportHeight - The list's visible height.
 * @returns The new scroll offset, never negative.
 */
export function scrollTopToReveal(index: number, rowHeight: number, scrollTop: number, viewportHeight: number): number {
  const top = index * rowHeight;
  const bottom = top + rowHeight;
  if (top < scrollTop) return Math.max(0, top);
  if (bottom > scrollTop + viewportHeight) return Math.max(0, bottom - viewportHeight);
  return scrollTop;
}

/**
 * The row index `j`/`k` should focus next.
 *
 * @param current - Index of the focused row, or `undefined` when no row has focus.
 * @param step - `1` for `j` (down), `-1` for `k` (up).
 * @param count - Number of loaded rows.
 * @param start - Where to start when no row has focus: the open entry's row, else the first
 *   visible one. That row is focused as is, without stepping, so the first press lands on what
 *   the user can see.
 * @returns The index to focus, or `undefined` at either end of the list (focus stays put) and
 *   when there are no rows.
 */
export function nextRowIndex(
  current: number | undefined,
  step: 1 | -1,
  count: number,
  start: number,
): number | undefined {
  if (count <= 0) return undefined;
  if (current === undefined) return Math.min(count - 1, Math.max(0, start));
  const next = current + step;
  return next >= 0 && next < count ? next : undefined;
}
