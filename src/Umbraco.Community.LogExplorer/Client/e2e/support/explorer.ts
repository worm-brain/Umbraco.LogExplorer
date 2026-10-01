import { expect, type Locator, type Page } from "@playwright/test";

/** The workspace's three views, by their route path. */
export type ExplorerView = "search" | "patterns" | "overview";

/** The level toggles' names, in display order (UI brief §4.7). */
export const LEVEL_NAMES = ["TRACE", "DEBUG", "INFO", "WARN", "ERROR", "FATAL"] as const;

/**
 * The backoffice path of a Log Explorer view. The menu item opens
 * `section/settings/workspace/{entityType}`; each workspace view adds `view/{pathname}`.
 *
 * @param view - Which tab.
 * @param params - View-state query parameters (`src`, `range`, `f`, `zf`, ...).
 * @returns A path relative to the site's base URL.
 */
export function explorerPath(view: ExplorerView, params: Record<string, string> = {}): string {
  const query = new URLSearchParams(params).toString();
  return `/umbraco/section/settings/workspace/log-explorer/view/${view}${query ? `?${query}` : ""}`;
}

/**
 * Opens a Log Explorer view and waits until the workspace has rendered its header.
 *
 * @param page - A logged-in page.
 * @param view - Which tab.
 * @param params - View-state query parameters; `src: "sample"` gives the deterministic Fake data.
 */
export async function openExplorer(page: Page, view: ExplorerView, params: Record<string, string> = {}): Promise<void> {
  await page.goto(explorerPath(view, params));
  await expect(page.getByRole("heading", { name: "Log Explorer", exact: true })).toBeVisible();
}

/**
 * Locators for the Search view's controls, by their accessible names (UI brief §4).
 *
 * @param page - A page showing the Search view.
 */
export function searchView(page: Page) {
  const histogram = page.getByRole("region", { name: "Entries over time" });
  const levels = histogram.getByRole("group", { name: "Show or hide levels" });
  return {
    timeRange: page.getByRole("button", { name: /^Time range: / }),
    searchBox: page.getByRole("textbox", { name: "Search logs" }),
    /** Filter chips, through each chip's edit button ("Edit filter {description}"). */
    chips: page.getByRole("button", { name: /^Edit filter / }),
    /** The time-zoom chip, through its remove button. */
    zoomChip: page.getByRole("button", { name: /^Remove filter Time: / }),
    histogram,
    histogramBars: histogram.getByRole("button", { name: /select to zoom in$/ }),
    /** One level toggle, e.g. `levelToggle("ERROR")`. */
    levelToggle: (level: (typeof LEVEL_NAMES)[number]): Locator =>
      levels.getByRole("button", { name: new RegExp(`^${level},`) }),
    rows: page.getByRole("button", { name: /^Open entry / }),
  };
}

/**
 * Reads which level toggles are on, from their `aria-pressed` state.
 *
 * @param page - A page showing the Search view.
 * @returns The pressed levels in display order, e.g. `["ERROR", "FATAL"]`.
 */
export async function pressedLevels(page: Page): Promise<Array<string>> {
  const levels = searchView(page).histogram.getByRole("group", { name: "Show or hide levels" });
  const pressed: Array<string> = [];
  for (const level of LEVEL_NAMES) {
    const toggle = levels.getByRole("button", { name: new RegExp(`^${level},`), pressed: true });
    if ((await toggle.count()) > 0) pressed.push(level);
  }
  return pressed;
}
