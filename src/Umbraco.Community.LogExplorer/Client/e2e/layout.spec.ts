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

// ADR 0024: in a narrow window many chips wrap onto more rows instead of being cut off at the
// edge of a one-line scroller.
test("at 1280 x 800 many chips wrap so every chip is fully visible", async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 800 });
  await openExplorer(page, "search", { src: "sample" });
  const view = searchView(page);
  await view.searchBox.fill(
    "SourceContext:Umbraco.Cms.Web.Common.Middleware* RequestPath:/umbraco/surface/contact/submit MachineName:wn1xsdwk000EJK StatusCode:500 timeout",
  );
  await view.searchBox.press("Enter");
  await expect(view.chips).toHaveCount(5);

  const box = await page.locator("log-explorer-search-box").boundingBox();
  const chips = await Promise.all((await view.chips.all()).map((chip) => chip.boundingBox()));
  const outside = chips.filter(
    (chip) =>
      !chip ||
      !box ||
      chip.x < box.x ||
      chip.x + chip.width > box.x + box.width ||
      chip.y < box.y ||
      chip.y + chip.height > box.y + box.height,
  );
  expect(outside.length, "chips cut off by the search box").toBe(0);
  expect((await pageOverflow(page)).horizontal, "horizontal page scroll").toBe(false);
});

// ADR 0025: the gap between the fields panel and the results is a divider that resizes the panel
// by dragging or with the arrow keys, and the width is remembered.
test("dragging the divider resizes the fields panel and the width survives a reload", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openExplorer(page, "search", { src: "sample" });
  const divider = page.getByRole("separator", { name: "Resize fields panel" });
  const panel = page.locator("log-explorer-fields-panel");
  await expect(divider).toBeVisible();
  const before = (await panel.boundingBox())!.width;

  // Act: drag the divider 120 px to the right.
  const handle = (await divider.boundingBox())!;
  await page.mouse.move(handle.x + handle.width / 2, handle.y + handle.height / 2);
  await page.mouse.down();
  await page.mouse.move(handle.x + handle.width / 2 + 120, handle.y + handle.height / 2, { steps: 5 });
  await page.mouse.up();

  // Assert
  await expect.poll(async () => Math.round((await panel.boundingBox())!.width - before)).toBe(120);
  await page.reload();
  await expect.poll(async () => Math.round((await panel.boundingBox())!.width - before)).toBe(120);

  // The keyboard moves it too, and double-click resets it.
  await divider.focus();
  await page.keyboard.press("ArrowLeft");
  await expect.poll(async () => Math.round((await panel.boundingBox())!.width - before)).toBe(104);
  await divider.dblclick();
  await expect.poll(async () => Math.round((await panel.boundingBox())!.width)).toBe(Math.round(before));
});
