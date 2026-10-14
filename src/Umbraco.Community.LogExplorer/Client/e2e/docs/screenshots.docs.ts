import type { Locator, Page } from "@playwright/test";
import { dragAcrossBars, openExplorer, searchView, type ExplorerView } from "../support/explorer.js";
import { repoRoot } from "../support/sites.js";
import { expect, test } from "../support/test.js";

/**
 * The documentation screenshots, written to `docs/images/`. Run with `bun run docs:screenshots`
 * (playwright.docs.config.ts). Each test sets up one view the docs show and saves it. The source
 * is the site's default "files" source, which the docs config points at the sample data, so the
 * entries match the sample source's (UI brief §12).
 */

/** A part of the page to save, in CSS pixels; the published file is twice the size. */
interface Area {
  x: number;
  y: number;
  width: number;
  height: number;
}

/** Waits for the view to settle, so a shot never catches a half-drawn view. */
async function settle(page: Page): Promise<void> {
  await page.waitForLoadState("networkidle");
  await expect(page.locator("uui-loader-bar")).toHaveCount(0);
  await page.waitForTimeout(500);
}

/**
 * Saves the viewport, or one area of it, to `docs/images/{name}.png`.
 *
 * @param page - The page to shoot.
 * @param name - The file name, without the extension.
 * @param clip - The area to save; the whole viewport when omitted.
 */
async function shoot(page: Page, name: string, clip?: Area): Promise<void> {
  await settle(page);
  await page.screenshot({ path: `${repoRoot}docs/images/${name}.png`, clip });
}

/**
 * A fixed-size area whose top-left corner sits at an offset from an element's, so a cropped shot
 * keeps the published size whatever the data.
 *
 * @param element - The element the area is placed against.
 * @param dx - Horizontal offset of the area from the element's left edge.
 * @param dy - Vertical offset of the area from the element's top edge.
 * @param width - The area's width.
 * @param height - The area's height.
 * @returns The area, in page coordinates.
 */
async function areaFrom(element: Locator, dx: number, dy: number, width: number, height: number): Promise<Area> {
  const box = await element.boundingBox();
  if (!box) throw new Error(`${element} is not laid out`);
  return { x: box.x + dx, y: box.y + dy, width, height };
}

/**
 * The query bar and the box under it, 1140 px wide, from just outside the time range button and
 * kept inside the viewport.
 */
async function queryBarArea(page: Page, height: number): Promise<Area> {
  const area = await areaFrom(searchView(page).timeRange, -21, -10, 1140, height);
  const viewport = page.viewportSize();
  return viewport ? { ...area, x: Math.min(area.x, viewport.width - area.width) } : area;
}

/**
 * Opens a view on the default source with no filters, and waits for its data.
 *
 * @param page - A logged-in page.
 * @param view - Which tab.
 */
async function open(page: Page, view: ExplorerView): Promise<void> {
  await openExplorer(page, view);
  if (view === "search") await expect(searchView(page).rows.first()).toBeVisible();
}

/**
 * Types a query into the search box and turns it into chips.
 *
 * @param page - A page showing a view with the search box.
 * @param query - The forgiving-syntax query.
 */
async function search(page: Page, query: string): Promise<void> {
  const { searchBox } = searchView(page);
  await searchBox.fill(query);
  await searchBox.press("Enter");
  await expect(searchBox).toHaveValue("");
}

/**
 * Drags across the five histogram bars around the busiest one: the sample data's incident.
 *
 * @param page - A page showing the Search view.
 */
async function zoomToIncident(page: Page): Promise<void> {
  const labels = await searchView(page).histogramBars.evaluateAll((bars) =>
    bars.map((bar) => bar.getAttribute("aria-label") ?? ""),
  );
  // Each bar's name is "{time}, {count} entries, select to zoom in".
  const counts = labels.map((label) => Number(/, ([\d,]+) entr/.exec(label)?.[1]?.replace(/,/g, "") ?? 0));
  const busiest = counts.indexOf(Math.max(...counts));
  await dragAcrossBars(page, Math.max(0, busiest - 2), Math.min(labels.length - 1, busiest + 2));
  await expect(searchView(page).zoomChip).toBeVisible();
}

/**
 * Opens the newest SQL-timeout ERROR entry in the drawer.
 *
 * @param page - A page showing the Search view.
 */
async function openSqlTimeoutError(page: Page): Promise<void> {
  await searchView(page)
    .rows.filter({ hasText: "An unhandled exception has occurred" })
    .filter({ hasText: "ERROR" })
    .first()
    .click();
  await expect(page.getByRole("region", { name: /ERROR/ })).toBeVisible();
}

/**
 * Closes the entry drawer if it is open, so a shot shows the whole results list.
 *
 * @param page - A page showing the Search view.
 */
async function closeDrawer(page: Page): Promise<void> {
  const close = page.getByRole("button", { name: "Close entry details" });
  if (await close.isVisible()) await close.click();
  await expect(close).toBeHidden();
}

test("search", async ({ page }) => {
  await open(page, "search");
  await search(page, "-path:/healtz");
  await expect(searchView(page).chips).toHaveText(["not Path: /healtz"]);
  await shoot(page, "search");
});

test("search box", async ({ page }) => {
  await open(page, "search");
  await search(page, "level:warn path:/umbraco* -status:200");
  await expect(searchView(page).chips).toHaveCount(2);
  await shoot(page, "search-box", await queryBarArea(page, 202));
});

test("fields panel", async ({ page }) => {
  await open(page, "search");
  const panel = page.getByRole("region", { name: "Fields" });
  await panel.getByRole("button", { name: /^Include Path \/umbraco\/surface\/contact\/submit,/ }).click();
  await expect(searchView(page).chips).toHaveCount(1);
  await shoot(page, "fields-panel", await areaFrom(panel, 1, 1, 223, 530));
});

test("time range", async ({ page }) => {
  await open(page, "search");
  await searchView(page).timeRange.click();
  await expect(page.getByRole("menu", { name: "Time ranges" })).toBeVisible();
  await shoot(page, "time-range", await areaFrom(searchView(page).timeRange, -16, -16, 178, 318));
});

test("histogram zoom", async ({ page }) => {
  await open(page, "search");
  await zoomToIncident(page);
  await shoot(page, "histogram-zoom", await queryBarArea(page, 202));
});

test("entry detail", async ({ page }) => {
  await open(page, "search");
  await search(page, "path:/umbraco/surface/contact/submit");
  await zoomToIncident(page);
  await openSqlTimeoutError(page);
  await shoot(page, "entry-detail");
});

test("entry exception", async ({ page }) => {
  await open(page, "search");
  await search(page, "path:/umbraco/surface/contact/submit");
  await openSqlTimeoutError(page);
  await page.getByRole("button", { name: "Show framework frames" }).click();
  const exception = page.locator("log-explorer-entry-detail section.exception");
  await exception.scrollIntoViewIfNeeded();
  await shoot(page, "entry-exception", await areaFrom(exception, -18, -19, 479, 314));
});

test("same request", async ({ page }) => {
  await open(page, "search");
  const view = searchView(page);
  // Only so the error is on screen to open; Same request clears the level filter again.
  await view.levelToggle("INFO").click();
  await view.levelToggle("WARN").click();
  await openSqlTimeoutError(page);
  await page.getByRole("button", { name: "Same request", exact: true }).click();
  await expect(view.rows).toHaveCount(3);
  await closeDrawer(page);
  await shoot(page, "same-request");
});

test("around this", async ({ page }) => {
  await open(page, "search");
  const view = searchView(page);
  for (const level of ["INFO", "WARN", "FATAL"] as const) await view.levelToggle(level).click();
  await openSqlTimeoutError(page);
  await page.getByRole("button", { name: "Around this", exact: true }).click();
  await expect(view.rows).toHaveCount(15);
  await closeDrawer(page);
  await shoot(page, "around-this");
});

test("show query", async ({ page }) => {
  await open(page, "search");
  await search(page, "path:/umbraco* -status:200 timeout");
  await expect(searchView(page).chips).toHaveCount(3);
  await searchView(page).showQuery.click();
  await expect(page.getByRole("button", { name: "Edit as native" })).toBeVisible();
  await shoot(page, "show-query", await queryBarArea(page, 220));
});

test("native mode", async ({ page }) => {
  await open(page, "search");
  await search(page, "path:/umbraco* -status:200 timeout");
  await searchView(page).showQuery.click();
  await page.getByRole("button", { name: "Edit as native" }).click();
  await expect(page.getByRole("button", { name: /^Leave .+ mode/ })).toBeVisible();
  await shoot(page, "native-mode", await queryBarArea(page, 307));
});

test("patterns", async ({ page }) => {
  await open(page, "patterns");
  await expect(page.getByRole("button", { name: /^Focus on / }).first()).toBeVisible();
  await shoot(page, "patterns");
});

test("overview", async ({ page }) => {
  await open(page, "overview");
  await expect(page.getByRole("heading", { name: /^Entries by level/ })).toBeVisible();
  await shoot(page, "overview", { x: 0, y: 0, width: 1440, height: 580 });
});
