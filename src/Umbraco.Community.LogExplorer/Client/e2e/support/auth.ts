import { mkdir, rm, stat } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { expect, type Browser, type Cookie, type Page } from "@playwright/test";
import type { E2eSite } from "./sites.js";

// The unattended install's admin, from the sample sites' appsettings.Development.json.
const USER = { email: "admin@example.com", password: "1234567890" };

/**
 * An empty storage state, for a `browser.newContext()` that must start logged out: Playwright Test
 * would otherwise give it the test's own `storageState` (the worker's session).
 */
export const NO_SESSION = { cookies: [], origins: [] };

/** How long a login may hold the lock before another worker treats it as abandoned. */
const STALE_LOCK_MS = 60_000;

/**
 * Logs in to a sample site in a throwaway browser context and returns the session's cookies, for
 * a worker to hand from one test's context to the next (see `test.ts`, ADR 0020 §7).
 *
 * @param browser - The worker's browser.
 * @param site - The site to log in to.
 * @returns The backoffice session cookies, including the httpOnly access and refresh tokens.
 */
export async function newSession(browser: Browser, site: E2eSite): Promise<Array<Cookie>> {
  // Playwright Test applies the project's `use` options to `browser.newContext()`, so ask for an
  // empty storage state explicitly rather than inheriting a session.
  const context = await browser.newContext({ ignoreHTTPSErrors: true, storageState: NO_SESSION });
  try {
    const page = await context.newPage();
    await logIn(page, site);
    // Let the backoffice's first token refresh land, so the cookies hold an unredeemed token.
    await page.waitForLoadState("networkidle");
    return (await context.storageState()).cookies;
  } finally {
    await context.close();
  }
}

/**
 * Logs a page in to a sample site through the backoffice login form, starting a new backoffice
 * session (ADR 0020 §7). Use it for a browser context that must not share the test's session,
 * such as the share-link test's colleague.
 *
 * Logins to one site run one at a time across all workers: each login updates the admin's
 * `umbracoUser` row, and parallel logins make SQLite fail with "database table is locked".
 *
 * The login page itself logs a token-refresh 400 before the user signs in, so watch the page's
 * console only after this returns.
 *
 * @param page - A page in a context with no backoffice session.
 * @param site - The site to log in to.
 */
export async function logIn(page: Page, site: E2eSite): Promise<void> {
  await page.goto(`${site.baseURL}/umbraco`);

  // The same fields @umbraco/playwright-testhelpers' LoginUiHelper uses (ADR 0020).
  await page.locator('[name="username"]').fill(USER.email);
  await page.locator('[name="password"]').fill(USER.password);

  await withSiteLock(site, async () => {
    await page.getByLabel("Login").click();
    // Hold the lock until the code exchange has finished and the backoffice has loaded.
    await expect(page).toHaveURL(/\/umbraco\/section\//);
  });
}

/**
 * Runs `action` while holding a per-site lock shared by every Playwright worker process. The lock
 * is a directory, because creating one is atomic on every platform; a lock older than
 * {@link STALE_LOCK_MS} (a worker killed mid-login) is taken over.
 */
async function withSiteLock(site: E2eSite, action: () => Promise<void>): Promise<void> {
  const lock = join(tmpdir(), `logexplorer-e2e-login-${site.port}.lock`);
  for (;;) {
    try {
      await mkdir(lock);
      break;
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code !== "EEXIST") throw error;
      const age = await stat(lock).then(
        (info) => Date.now() - info.mtimeMs,
        () => 0,
      );
      if (age > STALE_LOCK_MS) await rm(lock, { recursive: true, force: true });
      else await new Promise((resolve) => setTimeout(resolve, 100));
    }
  }

  try {
    await action();
  } finally {
    await rm(lock, { recursive: true, force: true });
  }
}
