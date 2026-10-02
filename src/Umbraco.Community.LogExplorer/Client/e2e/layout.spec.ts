// UI brief §11 items 1 and 2 (issue #51): what the Search view fits at the two reference window
// sizes. Screenshots of both are attached to the report for the §11 item 5 visual comparison.
import type { Page } from "@playwright/test";
import { openExplorer, searchView } from "./support/explorer.js";
import { expect, test } from "./support/test.js";

/** Whether the document scrolls in either direction (the view must not; only its panels do). */
function pageOverflow(page: Page) {
  return page.evaluate(() => ({
    horizontal: document.documentElement.scrollWidth > document.documentElement.clientWidth,
    vertical: document.documentElement.scrollHeight > document.documentElement.clientHeight,
  }));
}

/** How many result rows are fully inside the window. */
async function rowsInView(page: Page): Promise<number> {
  const viewport = page.viewportSize()!;
  const boxes = await Promise.all((await searchView(page).rows.all()).map((row) => row.boundingBox()));
  return boxes.filter((box) => box && box.y >= 0 && box.y + box.height <= viewport.height).length;
}

test("at 1440 x 900 the Search view shows the query bar, histogram, fields panel and 11 rows", async ({
  page,
}, testInfo) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openExplorer(page, "search", { src: "sample" });
  const view = searchView(page);
  await expect(view.rows.first()).toBeVisible();
  await expect(page.locator("uui-loader-bar")).toHaveCount(0);

  await expect(view.timeRange, "query bar").toBeInViewport();
  await expect(view.histogramBars.first(), "histogram").toBeInViewport();
  await expect(page.getByRole("button", { name: "Hide fields panel" }), "fields panel open").toBeInViewport();
  expect(await rowsInView(page), "result rows in view").toBeGreaterThanOrEqual(11); // UI brief §2, a guide (ADR 0023)
  expect(await pageOverflow(page), "page scroll").toEqual({ horizontal: false, vertical: false });
  await testInfo.attach("search-1440x900", { path: await shot(page, testInfo.outputPath("search-1440x900.png")) });
});

test("at 1280 x 800 nothing overflows and the fields panel starts collapsed", async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 1280, height: 800 });
  await openExplorer(page, "search", { src: "sample" });
  await expect(searchView(page).rows.first()).toBeVisible();
  await expect(page.locator("uui-loader-bar")).toHaveCount(0);

  await expect(page.getByRole("button", { name: "Show fields panel" }), "fields panel collapsed").toBeVisible();
  expect((await pageOverflow(page)).horizontal, "horizontal page scroll").toBe(false);
  await testInfo.attach("search-1280x800", { path: await shot(page, testInfo.outputPath("search-1280x800.png")) });
});

/** Saves a screenshot of the window and returns its path. */
async function shot(page: Page, path: string): Promise<string> {
  await page.screenshot({ path });
  return path;
}
