// BRIEF §14 Phase 1 criterion 1: from an ERROR entry, a user who does not know Serilog syntax sees
// every entry from the same request in at most 2 clicks. Also covers Around this (UI brief §4.10).
// Runs on the `sample` source: its SQL-timeout requests (UI brief §12) log a "Request starting"
// INFO, the unhandled-exception ERROR and a "Slow request" WARN under one request.
import type { Page } from "@playwright/test";
import { openExplorer, searchView } from "./support/explorer.js";
import { expect, test } from "./support/test.js";

/** Opens the sample source's newest SQL-timeout ERROR row (the first click). */
async function openSqlTimeoutError(page: Page): Promise<void> {
  await openExplorer(page, "search", { src: "sample" });
  const view = searchView(page);
  // The level filter only gets the row on screen; Same request must clear it again.
  await view.levelToggle("INFO").click();
  await view.levelToggle("WARN").click();
  await view.rows.filter({ hasText: "An unhandled exception has occurred" }).first().click();
  await expect(page.getByRole("region", { name: /ERROR/ })).toBeVisible();
}

test("from an ERROR entry, Same request shows the whole request in two clicks", async ({ page }) => {
  // Arrange
  await openSqlTimeoutError(page);
  const view = searchView(page);

  // Act: the second click.
  await page.getByRole("button", { name: "Same request", exact: true }).click();

  // Assert: one chip, every level back on, and the request's three entries.
  await expect(view.chips).toHaveCount(1);
  await expect(page).not.toHaveURL(/[?&]levels=/);
  await expect(view.rows).toHaveCount(3);
  await expect(view.rows.filter({ hasText: "Request starting" })).toHaveCount(1);
  await expect(view.rows.filter({ hasText: "An unhandled exception has occurred" })).toHaveCount(1);
  await expect(view.rows.filter({ hasText: "Slow request" })).toHaveCount(1);
});

test("Around this shows 7 entries either side ignoring filters, survives a reload, and Back restores the list", async ({
  page,
}) => {
  // Arrange
  await openSqlTimeoutError(page);
  const view = searchView(page);
  const filteredRows = await view.rows.count();

  // Act
  await page.getByRole("button", { name: "Around this", exact: true }).click();

  // Assert: the anchor and 7 either side, INFO and WARN included although they are filtered out.
  await expect(page.getByText(/Showing 7 entries either side of .+, ignoring filters/)).toBeVisible();
  await expect(view.rows).toHaveCount(15);
  await expect(view.rows.filter({ hasText: "INFO" }).first()).toBeVisible();
  await expect(page).toHaveURL(/[?&]around=/);
  const around = await view.rows.allTextContents();

  await page.reload();
  await expect(view.rows).toHaveCount(15);
  expect(await view.rows.allTextContents(), "rows after reload").toEqual(around);

  await page.getByRole("button", { name: "Back to filtered results" }).click();
  await expect(page).not.toHaveURL(/[?&]around=/);
  await expect(view.rows).toHaveCount(filteredRows);
});
