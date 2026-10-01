// BRIEF §14 Phase 1 criterion 7: the three views work on both sample sites with no console errors
// (the console guard in support/test.ts fails any test that logs one), and the main controls are
// keyboard-reachable.
import type { Locator, Page } from "@playwright/test";
import { openExplorer, searchView } from "./support/explorer.js";
import { expect, test } from "./support/test.js";

/**
 * Presses Tab until `target` has focus, failing after `maxPresses`. Focus moves on from wherever
 * it is, so successive calls check the targets come in that order.
 */
async function tabTo(page: Page, target: Locator, maxPresses = 200): Promise<void> {
  for (let presses = 0; presses < maxPresses; presses++) {
    await page.keyboard.press("Tab");
    // `:focus` matches inside shadow roots, where every UUI control's focusable element lives.
    if (await target.evaluate((element) => element.matches(":focus"))) return;
  }
  throw new Error(`Tab did not reach ${target} within ${maxPresses} presses`);
}

/** The Overview view's first panel heading, "Entries by level · {range}" (UI brief §4.13). */
function overviewHeading(page: Page): Locator {
  return page.getByRole("heading", { name: /^Entries by level/ });
}

test("the three views load on the site's own log files", async ({ page }) => {
  // No `src`: the sample sites' default source is `files`, filled by the log generator.
  await openExplorer(page, "search");
  await expect(page.getByRole("button", { name: /^Log source: This server's log files/ })).toBeVisible();
  await expect(searchView(page).rows.first()).toBeVisible();

  await page.getByRole("tab", { name: "Patterns" }).click();
  await expect(page).toHaveURL(/\/view\/patterns/);
  await expect(page.getByRole("region", { name: "Message patterns" })).toBeVisible();

  await page.getByRole("tab", { name: "Overview" }).click();
  await expect(page).toHaveURL(/\/view\/overview/);
  await expect(overviewHeading(page)).toBeVisible();
});

test("the three views load on the sample source", async ({ page }) => {
  await openExplorer(page, "search", { src: "sample" });
  await expect(searchView(page).rows.first()).toBeVisible();

  await openExplorer(page, "patterns", { src: "sample" });
  await expect(page.getByRole("region", { name: "Message patterns" })).toBeVisible();

  await openExplorer(page, "overview", { src: "sample" });
  await expect(overviewHeading(page)).toBeVisible();
});

test("Tab reaches the time range, search box, level toggles and result rows in order", async ({ page }) => {
  await openExplorer(page, "search", { src: "sample" });
  const view = searchView(page);
  await expect(view.rows.first()).toBeVisible();
  // Start from the workspace header, as a keyboard user arriving from the Settings tree would.
  await page.getByRole("heading", { name: "Log Explorer", exact: true }).click();

  await tabTo(page, view.timeRange);
  await tabTo(page, view.searchBox);
  await tabTo(page, view.levelToggle("TRACE"));
  await tabTo(page, view.levelToggle("FATAL"));
  await tabTo(page, view.rows.first());
});
