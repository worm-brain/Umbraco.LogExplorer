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
