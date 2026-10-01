// BRIEF §14 Phase 1 criterion 2: the forgiving search box turns typed syntax into chips through
// POST /parse (UI brief §4.3), on the deterministic `sample` source.
import { openExplorer, pressedLevels, searchView } from "./support/explorer.js";
import { expect, test } from "./support/test.js";

test.describe("search box", () => {
  test.beforeEach(async ({ page }) => {
    await openExplorer(page, "search", { src: "sample" });
  });

  test("level:error path:/api* gives one path chip and turns on ERROR and FATAL only", async ({ page }) => {
    const view = searchView(page);

    await view.searchBox.fill("level:error path:/api*");
    await view.searchBox.press("Enter");

    await expect(view.chips).toHaveCount(1);
    await expect(view.chips).toHaveText("Path: /api*");
    await expect.poll(() => pressedLevels(page)).toEqual(["ERROR", "FATAL"]);
    await expect(view.searchBox).toHaveValue("");
  });

  test("a bare word gives a text chip", async ({ page }) => {
    const view = searchView(page);

    await view.searchBox.fill("timeout");
    await view.searchBox.press("Enter");

    await expect(view.chips).toHaveCount(1);
    await expect(view.chips).toHaveText('"timeout"');
  });

  test("an unbalanced quote searches the whole input as text and says why", async ({ page }) => {
    const view = searchView(page);

    await view.searchBox.fill('path:"/api');
    await view.searchBox.press("Enter");

    await expect(
      page.getByRole("alert").filter({ hasText: "Unbalanced quote, so this was searched as plain text" }),
    ).toBeVisible();
    // One text chip of the whole input; the parser drops the stray quote characters.
    await expect(view.chips).toHaveCount(1);
    await expect(view.chips).toHaveText('"path:/api"');
  });
});
