import { test as base, expect, type ConsoleMessage, type Page } from "@playwright/test";
import type { E2eSite } from "./sites.js";

/**
 * Console errors that are not failures, each with the reason it is safe to ignore. Empty on
 * purpose: every entry hides a class of real errors too. The one known noise, the backoffice's
 * token-refresh 400 on the login page, happens only in `auth.setup.ts`, which has no guard.
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

/** Playwright's `test` with the console guard and the site option. Import this, not `@playwright/test`. */
export const test = base.extend<E2eFixtures, E2eOptions>({
  site: [undefined as unknown as E2eSite, { option: true, scope: "worker" }],

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
