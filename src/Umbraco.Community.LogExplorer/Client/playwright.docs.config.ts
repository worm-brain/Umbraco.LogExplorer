import { defineConfig } from "@playwright/test";
import { sites } from "./e2e/support/sites.js";
import type { E2eOptions } from "./e2e/support/test.js";

/**
 * Takes the documentation screenshots in `docs/images/` (`bun run docs:screenshots`), from the
 * Umbraco 17 sample site. It starts its own site on port 44376 (`DOCS_SITE_PORT`), clear of the
 * sample sites' launch ports and the e2e ports, because that site needs one setting the others do
 * not have: its "This server's log files" source serves the deterministic sample data, so every
 * screenshot shows the same entries a reader can reproduce from the sample source.
 *
 * The sample data declares its own native query language ("Sample"), not Serilog Expressions, so
 * the source badge and the show-query and native-mode shots name that language. The published
 * images were taken from real log files and are kept until the shots can match them.
 */
const port = Number(process.env.DOCS_SITE_PORT ?? 44376);
const site17 = sites.find((site) => site.major === 17);
if (!site17) throw new Error("E2E_SITES must include 17 for the documentation screenshots.");
const site = { ...site17, port, baseURL: `https://localhost:${port}` };

export default defineConfig<E2eOptions>({
  testDir: "e2e/docs",
  testMatch: /\.docs\.ts$/,
  outputDir: "e2e/test-results",
  // One worker, one session: the shots run in order.
  workers: 1,
  timeout: 120_000,
  expect: { timeout: 30_000 },
  reporter: [["list"]],
  use: {
    ignoreHTTPSErrors: true,
    // The size the screenshots are published at; scale 2 keeps text sharp on high-density screens.
    viewport: { width: 1440, height: 900 },
    deviceScaleFactor: 2,
    locale: "en-GB",
    colorScheme: "light",
    site,
    baseURL: site.baseURL,
  },
  webServer: {
    // --no-build: the docs:screenshots script has built the client and the site, in that order.
    command: `dotnet run --no-build --no-launch-profile -p:UmbracoMajor=17 --urls https://localhost:${port}`,
    cwd: site.projectDir,
    env: {
      ASPNETCORE_ENVIRONMENT: "Development",
      // The first source ("files", labelled "This server's log files") becomes the Fake source.
      LogExplorer__Sources__0__Type: "Fake",
    },
    url: `${site.baseURL}/umbraco`,
    ignoreHTTPSErrors: true,
    timeout: 300_000,
    // Never reuse a site started some other way: it would show its real log files instead.
    reuseExistingServer: false,
    stdout: "ignore",
    stderr: "pipe",
  },
});
