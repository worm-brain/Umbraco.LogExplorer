/**
 * Logs in to one sample site through the backoffice login form and saves the session for that
 * site's tests (storageState). Runs once per site project (`setup-site17`, `setup-site18`).
 *
 * Imports the plain Playwright `test` on purpose: the login page logs a token-refresh 400 before
 * the user signs in, which the console guard would rightly flag in a product test.
 */
import { expect, test as base } from "@playwright/test";
import type { E2eOptions } from "./support/test.js";

const setup = base.extend<object, E2eOptions>({
  site: [undefined as unknown as E2eOptions["site"], { option: true, scope: "worker" }],
});

// The unattended install's admin, from the sample sites' appsettings.Development.json.
const USER = { email: "admin@example.com", password: "1234567890" };

setup("log in to the backoffice", async ({ page, site }) => {
  await page.goto("/umbraco");

  // The same fields @umbraco/playwright-testhelpers' LoginUiHelper uses (ADR 0020).
  await page.locator('[name="username"]').fill(USER.email);
  await page.locator('[name="password"]').fill(USER.password);
  await page.getByLabel("Login").click();

  await expect(page).toHaveURL(/\/umbraco\/section\//);
  await page.context().storageState({ path: site.storageState });
});
