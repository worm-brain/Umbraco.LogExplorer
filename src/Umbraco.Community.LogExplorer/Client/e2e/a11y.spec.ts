// UI brief §11 item 6 and §7 (issue #51): axe reports no serious or critical accessibility issues
// on the three views, the open drawer and the open menus, and the query bar's menus and the
// histogram behave as their ARIA patterns say. Runs on the `sample` source so every panel has content.
import AxeBuilder from "@axe-core/playwright";
import type { Page } from "@playwright/test";
import { openExplorer, searchView, type ExplorerView } from "./support/explorer.js";
import { expect, test } from "./support/test.js";

/** The impacts that fail the check; minor and moderate findings are not enforced. */
const FAILING_IMPACTS = new Set(["serious", "critical"]);

/**
 * Runs axe on the page and returns its serious and critical violations, one line per rule with
 * the offending targets, so a failure reads as a list.
 *
 * The whole page is scanned, backoffice chrome included, because `AxeBuilder.include` cannot
 * select inside the backoffice's nested shadow roots. Nothing is excluded today: Umbraco's own
 * chrome reports only moderate findings (`region` on the header sections and the section
 * sidebar, `page-has-heading-one`), which the impact filter leaves out. Add any exclusion as a
 * narrow `exclude()` with the reason beside it.
 */
async function seriousViolations(page: Page): Promise<Array<string>> {
  const results = await new AxeBuilder({ page }).analyze();
  return results.violations
    .filter((violation) => FAILING_IMPACTS.has(violation.impact ?? ""))
    .map(
      (violation) =>
        `${violation.impact} ${violation.id}: ${violation.help} -> ${violation.nodes
          .map((node) => JSON.stringify(node.target))
          .join(", ")}`,
    );
}

/** Opens a view on the sample source and waits for its main content to settle. */
async function openView(page: Page, view: ExplorerView, params: Record<string, string> = {}): Promise<void> {
  await openExplorer(page, view, { src: "sample", ...params });
  if (view === "search") await expect(searchView(page).rows.first()).toBeVisible();
  if (view === "patterns") await expect(page.getByRole("button", { name: /^Focus on / }).first()).toBeVisible();
  if (view === "overview") await expect(page.getByRole("heading", { name: /^Entries by level/ })).toBeVisible();
  // Let loader bars finish so axe sees the settled view.
  await expect(page.locator("uui-loader-bar")).toHaveCount(0);
}

/** Whether keyboard focus is on (or inside) the element. `:focus` matches inside shadow roots. */
function hasFocus(locator: ReturnType<Page["locator"]>): Promise<boolean> {
  return locator.evaluate((element) => element.matches(":focus, :focus-within"));
}

test.describe("axe", () => {
  for (const view of ["search", "patterns", "overview"] as const) {
    test(`finds no serious issues on the ${view} view`, async ({ page }) => {
      await openView(page, view);

      expect(await seriousViolations(page)).toEqual([]);
    });
  }

  test("finds no serious issues with levels hidden and the query panel open", async ({ page }) => {
    await openView(page, "search", { levels: "info,warn,error", sq: "1" });
    await expect(searchView(page).levelToggle("TRACE")).toHaveAttribute("aria-pressed", "false");

    expect(await seriousViolations(page)).toEqual([]);
  });

  test("finds no serious issues with the entry drawer open", async ({ page }) => {
    await openView(page, "search");
    await searchView(page).rows.first().click();
    await expect(page.locator("log-explorer-entry-detail section.drawer")).toBeVisible();

    expect(await seriousViolations(page)).toEqual([]);
  });

  test("finds no serious issues with the time range menu open", async ({ page }) => {
    await openView(page, "search");
    await searchView(page).timeRange.click();
    await expect(page.getByRole("menuitemradio", { name: "Last 15 minutes" })).toBeVisible();

    expect(await seriousViolations(page)).toEqual([]);
  });

  test("finds no serious issues with the source menu open", async ({ page }) => {
    await openView(page, "search");
    await page.getByRole("button", { name: /^Log source: / }).click();
    await expect(page.getByRole("menu", { name: "Log sources (from appsettings)" })).toBeVisible();

    expect(await seriousViolations(page)).toEqual([]);
  });
});

test.describe("menus", () => {
  test("the time range menu is one menu of radio items, reached and left with the keyboard", async ({ page }) => {
    await openView(page, "search");
    const trigger = searchView(page).timeRange;
    await expect(trigger).toHaveAttribute("aria-expanded", "false");
    await expect(trigger).toHaveAttribute("aria-haspopup", "menu");

    // Opening from the keyboard moves focus to the checked item.
    await trigger.focus();
    await page.keyboard.press("Enter");
    const menu = page.getByRole("menu", { name: "Time ranges" });
    await expect(menu).toBeVisible();
    // One menu, not one per item (UUI gives each uui-menu-item role="menu" of its own).
    await expect(page.locator("log-explorer-time-range-picker").getByRole("menu")).toHaveCount(1);
    await expect(trigger).toHaveAttribute("aria-expanded", "true");
    await expect(menu.getByRole("menuitemradio")).toHaveCount(7);
    const lastHour = menu.getByRole("menuitemradio", { name: "Last 1 hour" });
    await expect(lastHour).toHaveAttribute("aria-checked", "true");
    await expect(lastHour).toBeFocused();

    // Arrow keys move between items and wrap; End and Home go to the ends.
    await page.keyboard.press("ArrowDown");
    await expect(menu.getByRole("menuitemradio", { name: "Last 4 hours" })).toBeFocused();
    await page.keyboard.press("End");
    await expect(menu.getByRole("menuitemradio", { name: "Custom range…" })).toBeFocused();
    await page.keyboard.press("ArrowDown");
    await expect(menu.getByRole("menuitemradio", { name: "Last 15 minutes" })).toBeFocused();

    // Choosing returns focus to the button, which now names the choice.
    await page.keyboard.press("Enter");
    await expect(menu).toBeHidden();
    await expect(page.getByRole("button", { name: "Time range: Last 15 minutes" })).toBeFocused();
    await expect(trigger).toHaveAttribute("aria-expanded", "false");
  });

  test("Escape closes the time range menu and returns focus to its button", async ({ page }) => {
    await openView(page, "search");
    const trigger = searchView(page).timeRange;
    await trigger.focus();
    await page.keyboard.press("Enter");
    await expect(page.getByRole("menu", { name: "Time ranges" })).toBeVisible();

    await page.keyboard.press("Escape");

    await expect(page.getByRole("menu", { name: "Time ranges" })).toBeHidden();
    await expect(trigger).toBeFocused();
  });

  test("Custom range moves focus to its first input, and Escape from there returns to the button", async ({ page }) => {
    await openView(page, "search");
    const trigger = searchView(page).timeRange;
    await trigger.focus();
    await page.keyboard.press("Enter");
    await page.keyboard.press("End");

    await page.keyboard.press("Enter");

    await expect.poll(() => hasFocus(page.locator("log-explorer-time-range-picker umb-input-date#from"))).toBe(true);
    await page.keyboard.press("Escape");
    await expect(page.getByRole("menu", { name: "Time ranges" })).toBeHidden();
    await expect(trigger).toBeFocused();
  });

  test("Tab leaves the time range menu without an extra stop and closes it", async ({ page }) => {
    await openView(page, "search");
    await searchView(page).timeRange.focus();
    await page.keyboard.press("Enter");
    const menu = page.getByRole("menu", { name: "Time ranges" });
    await expect(menu.getByRole("menuitemradio", { name: "Last 1 hour" })).toBeFocused();

    await page.keyboard.press("Tab");

    // The next stop is outside the menu (the scroll container no longer takes one), so it closes.
    expect(await hasFocus(page.locator("log-explorer-time-range-picker uui-popover-container"))).toBe(false);
    await expect(menu).toBeHidden();
  });

  test("the source picker exposes its menu state", async ({ page }) => {
    await openView(page, "search");
    const trigger = page.getByRole("button", { name: /^Log source: / });
    await expect(trigger).toHaveAttribute("aria-expanded", "false");

    await trigger.click();

    const menu = page.getByRole("menu", { name: "Log sources (from appsettings)" });
    await expect(menu).toBeVisible();
    await expect(trigger).toHaveAttribute("aria-expanded", "true");
    await expect(menu.getByRole("menuitemradio", { name: /^Sample data/, checked: true })).toBeFocused();
  });
});

test.describe("query bar and histogram", () => {
  test("the query bar's icon buttons are square", async ({ page }) => {
    await openView(page, "search");
    const view = searchView(page);

    for (const button of [view.showQuery, view.share]) {
      const box = await button.boundingBox();
      expect(box, "laid out").not.toBeNull();
      expect(Math.abs(box!.width - box!.height), `${await button.getAttribute("aria-label")} is square`).toBeLessThan(
        1,
      );
    }
  });

  test("the histogram bars are one Tab stop, moved with the arrow keys", async ({ page }) => {
    await openView(page, "search");
    const view = searchView(page);
    await expect(view.histogramBars).toHaveCount(60);
    await expect(view.histogramBars.and(page.locator("[tabindex='0']"))).toHaveCount(1);

    // Tab from the last level toggle lands on the first bar, and one more Tab leaves the bars.
    await view.levelToggle("FATAL").focus();
    await page.keyboard.press("Tab");
    await expect(view.histogramBars.first()).toBeFocused();
    await page.keyboard.press("ArrowRight");
    await expect(view.histogramBars.nth(1)).toBeFocused();
    await page.keyboard.press("End");
    await expect(view.histogramBars.last()).toBeFocused();
    await page.keyboard.press("Home");
    await expect(view.histogramBars.first()).toBeFocused();
    await page.keyboard.press("ArrowRight");
    await page.keyboard.press("Tab");
    expect(await hasFocus(view.histogram.getByRole("group", { name: "Entries per time bucket" }))).toBe(false);

    // Shift+Tab comes back to the bar last used, and Enter zooms on it.
    await page.keyboard.press("Shift+Tab");
    await expect(view.histogramBars.nth(1)).toBeFocused();
    await page.keyboard.press("Enter");
    await expect(view.zoomChip).toBeVisible();
  });
});

test("the drawer's framework-frames toggle exposes whether it is open", async ({ page }) => {
  // The sample's ERROR entries carry exceptions with framework frames (UI brief §12).
  await openView(page, "search", { levels: "error" });
  await searchView(page).rows.first().click();
  const toggle = page.getByRole("button", { name: "Show framework frames" });
  await expect(toggle).toHaveAttribute("aria-expanded", "false");

  await toggle.click();

  await expect(page.getByRole("button", { name: "Hide framework frames" })).toHaveAttribute("aria-expanded", "true");
});
