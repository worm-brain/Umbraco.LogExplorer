/** The `localStorage` key for the fields panel's open/collapsed choice (UI brief §2.1). */
export const PANEL_PREFERENCE_KEY = "logExplorer.fieldsPanel";

/**
 * Below this workspace width the panel starts collapsed unless the user chose otherwise: the
 * medium and narrow workspaces of UI brief §2.1 (under about 1100 px).
 */
export const COLLAPSE_BELOW_WIDTH = 1100;

/** The user's explicit choice; `undefined` means they never toggled the panel. */
export type PanelPreference = "open" | "collapsed" | undefined;

/** The part of `Storage` the preference needs, so tests can pass a stub. */
export type PreferenceStorage = Pick<Storage, "getItem" | "setItem">;

/**
 * Reads the stored choice. Storage can be missing or throw (private windows, blocked site data),
 * and anything unrecognised is treated as no choice.
 *
 * @param storage - Defaults to `localStorage`.
 * @returns The stored choice, or `undefined`.
 */
export function readPanelPreference(storage?: PreferenceStorage): PanelPreference {
  try {
    const value = (storage ?? localStorage).getItem(PANEL_PREFERENCE_KEY);
    return value === "open" || value === "collapsed" ? value : undefined;
  } catch {
    return undefined;
  }
}

/**
 * Stores the choice. A storage failure is ignored: the panel still toggles, it just will not be
 * remembered.
 *
 * @param preference - The choice to remember.
 * @param storage - Defaults to `localStorage`.
 */
export function writePanelPreference(preference: "open" | "collapsed", storage?: PreferenceStorage): void {
  try {
    (storage ?? localStorage).setItem(PANEL_PREFERENCE_KEY, preference);
  } catch {
    // Remembering the choice is a convenience; the toggle itself has already happened.
  }
}

/**
 * Whether the panel is collapsed: the user's choice when they made one, otherwise collapsed on
 * workspaces narrower than {@link COLLAPSE_BELOW_WIDTH}.
 *
 * @param preference - The stored choice.
 * @param workspaceWidth - The Search view's width in CSS pixels; `undefined` before it is measured,
 *   which leaves the panel open.
 * @returns `true` when collapsed.
 */
export function isPanelCollapsed(preference: PanelPreference, workspaceWidth: number | undefined): boolean {
  if (preference !== undefined) return preference === "collapsed";
  return workspaceWidth !== undefined && workspaceWidth < COLLAPSE_BELOW_WIDTH;
}
