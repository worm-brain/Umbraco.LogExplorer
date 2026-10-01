// BRIEF §14 Phase 1 criterion 3: dragging on the histogram narrows the range, the URL carries it,
// and reloading the URL restores the identical view. Runs on the `sample` source, whose data is
// generated once when the site starts, so the same absolute zoom shows the same rows after reload.
import type { Page } from "@playwright/test";
import { openExplorer, searchView } from "./support/explorer.js";
import { expect, test } from "./support/test.js";

/** What the Search view shows, as text, for comparing before and after a reload. */
async function snapshot(page: Page) {
  const view = searchView(page);
  return {
    chips: await view.chips.allTextContents(),
    zoom: await view.zoomChip.getAttribute("aria-label"),
    rows: await view.rows.allTextContents(),
  };
}

/** Drags across the histogram from one bar to another with the mouse (pointer events). */
async function dragAcrossBars(page: Page, fromIndex: number, toIndex: number): Promise<void> {
  const bars = searchView(page).histogramBars;
  const from = await bars.nth(fromIndex).boundingBox();
  const to = await bars.nth(toIndex).boundingBox();
  if (!from || !to) throw new Error("histogram bars are not laid out");

  await page.mouse.move(from.x + from.width / 2, from.y + from.height / 2);
  await page.mouse.down();
  await page.mouse.move(to.x + to.width / 2, to.y + to.height / 2, { steps: 10 });
  await page.mouse.up();
}

test("dragging on the histogram zooms in, and reloading the URL restores the same view", async ({ page }) => {
  // Arrange: a chip as well as the zoom, so the reload has to restore both.
  await openExplorer(page, "search", { src: "sample" });
  const view = searchView(page);
  await view.searchBox.fill("path:/healtz");
  await view.searchBox.press("Enter");
  await expect(view.chips).toHaveText(["Path: /healtz"]);
  await expect(view.histogramBars).toHaveCount(60);
  const firstBar = (await view.histogramBars.nth(10).getAttribute("aria-label"))?.split(",")[0];

  // Act: drag across 11 one-minute bars.
  await dragAcrossBars(page, 10, 20);

  // Assert: the zoom covers exactly the dragged bars, is in the URL, and the view narrowed to it.
  await expect(view.zoomChip).toBeVisible();
  await expect(page).toHaveURL(/[?&]zf=.+&zt=/);
  const url = new URL(page.url());
  const zoomFrom = new Date(url.searchParams.get("zf")!);
  const zoomTo = new Date(url.searchParams.get("zt")!);
  expect(zoomTo.getTime() - zoomFrom.getTime(), "zoom length").toBe(11 * 60_000);
  expect(zoomFrom.toLocaleTimeString("en-GB", { hour: "2-digit", minute: "2-digit" }), "zoom start").toBe(firstBar);
  await expect(view.zoomChip).toHaveAccessibleName(new RegExp(`^Remove filter Time: ${firstBar}`));
  // The sample logs one /healtz request a minute (UI brief §12), so 11 minutes is 11 rows.
  await expect(view.rows).toHaveCount(11);

  const before = await snapshot(page);
  await page.reload();
  await expect(view.rows).toHaveCount(11);

  expect(await snapshot(page), "view after reload").toEqual(before);
  expect(page.url(), "URL after reload").toBe(url.toString());
});
