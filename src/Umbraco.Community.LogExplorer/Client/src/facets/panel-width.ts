import type { PreferenceStorage } from "./panel-preference.js";

/** The `localStorage` key for the fields panel's dragged width, in CSS pixels (ADR 0025). */
export const PANEL_WIDTH_KEY = "logExplorer.fieldsPanelWidth";

/** The narrowest the fields panel can be dragged: room for a field name and its count. */
export const MIN_PANEL_WIDTH = 200;

/** The share of the Search body the results always keep, so the panel never squeezes them out. */
export const MIN_RESULTS_SHARE = 0.4;

/** How far one arrow key press moves the divider; Shift multiplies it by {@link SHIFT_FACTOR}. */
export const KEY_STEP = 16;

/** The multiplier for Shift + arrow key. */
export const SHIFT_FACTOR = 4;

/**
 * The widest the panel may be in a body of the given width: whatever leaves the results their
 * {@link MIN_RESULTS_SHARE}, but never less than {@link MIN_PANEL_WIDTH}.
 *
 * @param bodyWidth - The width of the Search view's body (fields panel, divider and results).
 * @returns The maximum panel width in CSS pixels.
 */
export function maxPanelWidth(bodyWidth: number): number {
  return Math.max(MIN_PANEL_WIDTH, Math.floor(bodyWidth * (1 - MIN_RESULTS_SHARE)));
}

/**
 * Keeps a width between {@link MIN_PANEL_WIDTH} and {@link maxPanelWidth}, rounded to whole
 * pixels.
 *
 * @param width - The requested width.
 * @param bodyWidth - The width of the Search view's body.
 * @returns The width the panel can take.
 */
export function clampPanelWidth(width: number, bodyWidth: number): number {
  return Math.round(Math.min(Math.max(width, MIN_PANEL_WIDTH), maxPanelWidth(bodyWidth)));
}

/**
 * The width a key press on the divider asks for: Left/Right move it by {@link KEY_STEP} (times
 * {@link SHIFT_FACTOR} with Shift), Home and End go to the minimum and maximum.
 *
 * @param key - `KeyboardEvent.key`.
 * @param shift - Whether Shift is held.
 * @param width - The panel's current width.
 * @param bodyWidth - The width of the Search view's body.
 * @returns The new, clamped width, or `undefined` when the key does not resize.
 */
export function keyPanelWidth(key: string, shift: boolean, width: number, bodyWidth: number): number | undefined {
  const step = KEY_STEP * (shift ? SHIFT_FACTOR : 1);
  switch (key) {
    case "ArrowLeft":
      return clampPanelWidth(width - step, bodyWidth);
    case "ArrowRight":
      return clampPanelWidth(width + step, bodyWidth);
    case "Home":
      return MIN_PANEL_WIDTH;
    case "End":
      return maxPanelWidth(bodyWidth);
    default:
      return undefined;
  }
}

/**
 * Reads the stored width. Storage can be missing or throw, and anything that is not a positive
 * number counts as no stored width.
 *
 * @param storage - Defaults to `localStorage`.
 * @returns The stored width in CSS pixels, or `undefined` for the panel's default width.
 */
export function readPanelWidth(storage?: PreferenceStorage): number | undefined {
  try {
    const width = Number((storage ?? localStorage).getItem(PANEL_WIDTH_KEY));
    return Number.isFinite(width) && width > 0 ? width : undefined;
  } catch {
    return undefined;
  }
}

/**
 * Stores the width, or forgets it (back to the default) when `undefined`. A storage failure is
 * ignored: the panel keeps its width for this page, it just will not be remembered.
 *
 * @param width - The width in CSS pixels, or `undefined` to forget it.
 * @param storage - Defaults to `localStorage`.
 */
export function writePanelWidth(
  width: number | undefined,
  storage?: PreferenceStorage & Pick<Storage, "removeItem">,
): void {
  try {
    const target = storage ?? localStorage;
    if (width === undefined) target.removeItem(PANEL_WIDTH_KEY);
    else target.setItem(PANEL_WIDTH_KEY, String(Math.round(width)));
  } catch {
    // Remembering the width is a convenience; the panel has already resized.
  }
}
