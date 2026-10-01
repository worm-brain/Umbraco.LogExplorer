// BRIEF §5 J5 and §6.11: Share copies a URL that reproduces the view, for a colleague who opens it
// in their own browser. Runs on the `sample` source so the same absolute zoom shows the same data.
import type { Page } from "@playwright/test";
import { logIn, NO_SESSION } from "./support/auth.js";
import { dragAcrossBars, openExplorer, pressedLevels, searchView } from "./support/explorer.js";
import { expect, test, watchConsole } from "./support/test.js";

/** Everything the shared link must carry, read from the Search tab. */
async function searchState(page: Page) {
  const view = searchView(page);
  await expect(view.rows.first()).toBeVisible();
  return {
    chips: await view.chips.allTextContents(),
    levels: await pressedLevels(page),
    zoom: await view.zoomChip.getAttribute("aria-label"),
    sort: await view.sort.getAttribute("aria-label"),
    showQueryPressed: await view.showQuery.getAttribute("aria-pressed"),
    rows: await view.rows.allTextContents(),
  };
}

test("Copy link to this view copies a URL that opens the identical view in a fresh browser", async ({
  page,
  browser,
  site,
}) => {
  // Arrange: change every part of the view state the link carries, ending on the Patterns tab.
  await page.context().grantPermissions(["clipboard-read", "clipboard-write"], { origin: site.baseURL });
  await openExplorer(page, "search", { src: "sample" });
  const view = searchView(page);
  await view.searchBox.fill("-path:/healtz");
  await view.searchBox.press("Enter");
  await view.levelToggle("INFO").click();
  await dragAcrossBars(page, 40, 50);
  await expect(view.zoomChip).toBeVisible();
  await view.sort.click();
  await expect(view.sort).toHaveAccessibleName(/^Time, oldest first/);
  await view.showQuery.click();
  const shared = await searchState(page);
  await page.getByRole("tab", { name: "Patterns" }).click();
  await expect(page).toHaveURL(/\/view\/patterns/);
  const patterns = page.getByRole("region", { name: "Message patterns" });
  // The header appears once the patterns have loaded; reading earlier captures an empty list.
  await expect
    .poll(() => patterns.innerText(), { message: "Patterns tab loaded" })
    .toMatch(/patterns? in the current results/);
  const sharedPatterns = await patterns.innerText();

  // Act
  await searchView(page).share.click();
  await expect(page.getByRole("alert").filter({ hasText: "Link to this exact view copied" })).toBeVisible();
  const link = await page.evaluate(() => navigator.clipboard.readText());

  // Assert: a new, logged-out context that logs in as the colleague would, so the view comes from
  // the URL alone and the colleague's session never shares the test's refresh token.
  const fresh = await browser.newContext({
    storageState: NO_SESSION,
    ignoreHTTPSErrors: true,
    viewport: { width: 1440, height: 900 },
    locale: "en-GB",
  });
  const colleague = await fresh.newPage();
  await logIn(colleague, site);
  const problems = watchConsole(colleague);
  await colleague.goto(link);

  await expect(colleague).toHaveURL(link);
  const colleaguePatterns = colleague.getByRole("region", { name: "Message patterns" });
  await expect(colleaguePatterns).toBeVisible();
  await expect.poll(() => colleaguePatterns.innerText(), { message: "Patterns tab" }).toBe(sharedPatterns);
  await colleague.getByRole("tab", { name: "Search" }).click();
  expect(await searchState(colleague), "Search tab").toEqual(shared);
  expect(problems, "browser console errors in the fresh browser").toEqual([]);

  await fresh.close();
});
