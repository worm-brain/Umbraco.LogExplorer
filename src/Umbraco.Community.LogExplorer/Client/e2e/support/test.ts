import { test as base, expect, type ConsoleMessage, type Cookie, type Page } from "@playwright/test";
import { newSession } from "./auth.js";
import type { E2eSite } from "./sites.js";

/**
 * Console errors that are not failures, each with the reason it is safe to ignore. Empty on
 * purpose: every entry hides a class of real errors too. The one known noise, the backoffice's
 * token-refresh 400 on the login page, happens before `logIn` returns, so no guard sees it.
 */
const ALLOWED_CONSOLE_ERRORS: ReadonlyArray<{ pattern: RegExp; reason: string }> = [];

/** Fixtures every e2e test gets. */
export interface E2eFixtures {
  /**
   * Fails the test on any browser console error or uncaught page error (issue #50), apart from
   * the allow-listed noise above. Automatic: tests do not ask for it.
   */
  consoleGuard: void;
}

/** Per-project options, set in `playwright.config.ts`. */
export interface E2eOptions {
  /** The sample site this project runs against. */
  site: E2eSite;
}

function isAllowed(text: string): boolean {
  return ALLOWED_CONSOLE_ERRORS.some(({ pattern }) => pattern.test(text));
}

/**
 * Starts recording a page's console errors and uncaught page errors, minus the allow-list. The
 * automatic guard does this for the test's `page`; call it for any other page a test opens, and
 * assert the returned list is empty at the end.
 *
 * @param page - The page to watch.
 * @returns A live list of problems, one line each, appended to as they happen.
 */
export function watchConsole(page: Page): Array<string> {
  const problems: Array<string> = [];
  page.on("console", (message: ConsoleMessage) => {
    if (message.type() !== "error" || isAllowed(message.text())) return;
    const { url, lineNumber } = message.location();
    problems.push(`console.error: ${message.text()}${url ? ` (${url}:${lineNumber})` : ""}`);
  });
  page.on("pageerror", (error) => problems.push(`page error: ${error.message}`));
  return problems;
}

/** The worker's backoffice session: the latest cookies, handed from each test to the next. */
interface WorkerSession {
  cookies?: Array<Cookie>;
}

/**
 * Playwright's `test` with a logged-in `page`, the console guard and the site option. Import this,
 * not `@playwright/test`.
 *
 * Each worker logs in once and passes the session's cookies from one test's context to the next
 * (issue #50, ADR 0020 §7). The backoffice redeems its refresh token on every page load in a new
 * context, and OpenIddict rotates refresh tokens: a redeemed one is accepted again for only 30
 * seconds, and reusing it later revokes every token of that login. So no two contexts may start
 * from the same saved cookies; carrying the newest cookies forward means each refresh token is
 * redeemed once. Only cookies are carried, so local storage stays per test.
 */
export const test = base.extend<E2eFixtures & { carrySession: void }, E2eOptions & { workerSession: WorkerSession }>({
  site: [undefined as unknown as E2eSite, { option: true, scope: "worker" }],

  workerSession: [async ({}, use) => use({}), { scope: "worker" }],

  storageState: async ({ workerSession, browser, site }, use) => {
    workerSession.cookies ??= await newSession(browser, site);
    await use({ cookies: workerSession.cookies, origins: [] });
  },

  carrySession: [
    async ({ context, workerSession }, use) => {
      await use();

      // The context's cookie jar holds the latest rotated refresh token; the next test starts
      // from it. If the context is unusable, the next test logs in again.
      workerSession.cookies = await context.storageState().then(
        (state) => state.cookies,
        () => undefined,
      );
    },
    { auto: true },
  ],

  consoleGuard: [
    async ({ page }, use) => {
      const problems = watchConsole(page);

      await use();

      expect(problems, "browser console errors during the test").toEqual([]);
    },
    { auto: true },
  ],
});

export { expect };
