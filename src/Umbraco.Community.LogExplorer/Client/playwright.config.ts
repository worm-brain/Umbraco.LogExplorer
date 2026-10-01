import { defineConfig, type PlaywrightTestProject } from "@playwright/test";
import { sites } from "./e2e/support/sites.js";
import type { E2eOptions } from "./e2e/support/test.js";

const ci = !!process.env.CI;

/**
 * The e2e suite (ADR 0020): runs every spec against each selected sample site (`E2E_SITES`),
 * starting the sites itself. `bun run e2e` builds the client and the sites first (`e2e/prepare.ts`).
 */
export default defineConfig<E2eOptions>({
  testDir: "e2e",
  outputDir: "e2e/test-results",
  // Specs only change URL state and per-browser storage, so they can share a site.
  fullyParallel: true,
  workers: ci ? 2 : 4,
  forbidOnly: ci,
  retries: ci ? 1 : 0,
  timeout: 60_000,
  expect: { timeout: 10_000 },
  reporter: ci
    ? [["github"], ["html", { open: "never", outputFolder: "e2e/playwright-report" }]]
    : [["list"], ["html", { open: "never", outputFolder: "e2e/playwright-report" }]],
  use: {
    // The sites use the ASP.NET Core development certificate, which CI does not trust.
    ignoreHTTPSErrors: true,
    // UI brief §11's reference window.
    viewport: { width: 1440, height: 900 },
    locale: "en-GB",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: sites.flatMap((site): Array<PlaywrightTestProject<E2eOptions>> => [
    {
      name: `setup-${site.name}`,
      testMatch: /auth\.setup\.ts/,
      use: { site, baseURL: site.baseURL },
    },
    {
      name: site.name,
      testMatch: /\.spec\.ts/,
      dependencies: [`setup-${site.name}`],
      use: { site, baseURL: site.baseURL, storageState: site.storageState },
    },
  ]),
  webServer: sites.map((site) => ({
    // --no-build: e2e/prepare.ts has built the client and the site, in that order.
    command: `dotnet run --no-build --no-launch-profile -p:UmbracoMajor=${site.major} --urls https://localhost:${site.port}`,
    cwd: site.projectDir,
    env: {
      ASPNETCORE_ENVIRONMENT: "Development",
      // Fill the files source with live events and the multi-machine/rolled-file scenarios.
      LogGenerator__Enabled: "true",
      LogGenerator__WriteFileScenarios: "true",
    },
    // Answers once Umbraco has booted; on a fresh database that includes the unattended install.
    url: `${site.baseURL}/umbraco`,
    ignoreHTTPSErrors: true,
    timeout: 300_000,
    // Locally, a site already running on the e2e port is used as is.
    reuseExistingServer: !ci,
    stdout: "ignore",
    stderr: "pipe",
  })),
});
