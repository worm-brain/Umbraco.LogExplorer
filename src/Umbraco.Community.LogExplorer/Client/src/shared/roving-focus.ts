/**
 * Which arrow keys move a roving focus: Up/Down for a vertical list such as a menu, Left/Right
 * for a horizontal strip such as the histogram bars.
 */
export type RovingOrientation = "vertical" | "horizontal";

/** How a roving focus behaves at its ends. */
export interface RovingOptions {
  orientation: RovingOrientation;
  /** Whether moving past the last item lands on the first (and back), as menus do. */
  wrap: boolean;
}

/**
 * Where a roving focus goes on a key press: the one-tab-stop pattern of WAI-ARIA composite
 * widgets, where Tab enters and leaves the group and the arrow keys move inside it.
 *
 * Home and End go to the first and last item in either orientation. The arrow keys of the other
 * orientation are left alone, so a vertical menu does not swallow Left/Right.
 *
 * @param current - Index of the item that has focus, or a negative number when none has; an index
 *   past the end (the group shrank) is treated as the last item.
 * @param key - `KeyboardEvent.key` of the press.
 * @param count - Number of items in the group.
 * @param options - The group's orientation and end behaviour.
 * @returns The index to focus, or `undefined` when the key does not move the focus (so the
 *   caller leaves the event alone).
 */
export function rovingIndex(current: number, key: string, count: number, options: RovingOptions): number | undefined {
  if (count <= 0) return undefined;
  const last = count - 1;
  const from = Math.min(current, last);
  const [previousKey, nextKey] =
    options.orientation === "vertical" ? ["ArrowUp", "ArrowDown"] : ["ArrowLeft", "ArrowRight"];

  switch (key) {
    case "Home":
      return 0;
    case "End":
      return last;
    // No item focused yet (a negative index): the next key starts at the first, previous at the last.
    case previousKey:
      if (from < 0) return last;
      if (from > 0) return from - 1;
      return options.wrap ? last : 0;
    case nextKey:
      if (from < 0) return 0;
      if (from < last) return from + 1;
      return options.wrap ? 0 : last;
    default:
      return undefined;
  }
}
