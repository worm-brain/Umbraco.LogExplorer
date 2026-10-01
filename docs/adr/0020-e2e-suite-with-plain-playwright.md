# ADR 0020: The e2e suite runs plain Playwright from the client, against both sample sites

- Status: Accepted
- Date: 2026-10-01
- Issue: #50

## Context

Issue #50 covers the Phase 1 acceptance criteria (BRIEF §14) with Playwright on both sample sites
and fails on any console error. BRIEF §16 and ADR 0006 asked to **Verify** whether Umbraco's own
Playwright helpers fit 17 and 18.

What the helpers are (checked on npm, 2026-10-01):

- `@umbraco/playwright-testhelpers` ships one line per CMS major: `17.0.x` (latest 17.0.36,
  depending on `@umbraco/json-models-builders` 2.0.45) and `18.0.x` (latest 18.0.5, depending on
  `@umbraco/json-models-builders` 18.0.2). They are CommonJS, depend on `node-fetch` 2, and
  expect `@playwright/test` ^1.56.
- They read one site from the environment: `URL` (base URL), `UMBRACO_USER_LOGIN` /
  `UMBRACO_USER_PASSWORD`, and `STORAGE_STAGE_PATH` for the saved login. Their `test` fixture
  adds `umbracoApi` (content, document types, users, ... through the Management API) and
  `umbracoUi`; its console listener writes errors to a file (`CONSOLE_ERRORS_PATH`) rather than
  failing the test.
- Their login is the backoffice login form (`[name="username"]`, `[name="password"]`, the
  "Login" button) plus token handling for their API helpers.

What this suite needs: log in to two sites in one run, open our own workspace, and fail on console
errors. It creates no CMS content.

## Decision

1. **Plain `@playwright/test`, no Umbraco helpers.** A small `auth.setup.ts` logs in through the
   same form fields the helpers use and saves a storage state per site. The helpers would need one
   package version per major in one lockfile, and their single-site environment variables do not
   fit one run over both sites; nothing else they offer is used. Revisit if a test needs CMS
   content (deep links, Phase 2): `@umbraco/json-models-builders` or the Management API directly.
2. **Location: `src/Umbraco.Community.LogExplorer/Client/e2e/`**, with `@playwright/test` as a
   Client devDependency and `playwright.config.ts` beside `vite.config.ts`. One `bun install`,
   one lockfile and the Client's Prettier config cover it; `bun run typecheck` also checks
   `e2e/tsconfig.json`. The Playwright runner itself runs on Node (its bin's shebang), which
   `bun run e2e` starts; Bun stays the package manager and script runner (ADR 0006).
3. **`bun run e2e`** runs `e2e/prepare.ts` (builds the client, then each selected site, so a site
   always starts after the client build it serves) and then `playwright test`. Playwright starts
   the sites itself (`webServer`, `dotnet run --no-build --no-launch-profile`) over HTTPS,
   because the backoffice's OpenIddict endpoints refuse plain HTTP (ID2083), with the log
   generator and its file scenarios on. Ports default to 44374 (17) and 44384 (18), clear of the
   launch-profile ports, and are overridden with `E2E_SITE17_PORT` / `E2E_SITE18_PORT`.
   `E2E_SITES=17` or `18` runs one site; `E2E_SKIP_BUILD=1` skips the builds (needed when a site
   is already running on its e2e port, which Playwright then reuses locally).
4. **Projects per site:** `setup-site17` and `site17`, `setup-site18` and `site18`. Every spec runs
   on both. Specs use the deterministic Fake `sample` source wherever exact counts or rows
   matter; the view-loading test also runs once on the default `files` source.
5. **Console guard:** a shared auto fixture (`e2e/support/test.ts`) fails any test that logs a
   `console.error` or throws an uncaught page error. Its allow-list is empty: the only noise seen,
   the backoffice's token-refresh 400 on the login page, happens in the setup project, which
   imports plain Playwright and has no guard.
6. **CI: `.github/workflows/e2e.yml`**, one job per major (`E2E_SITES` = the matrix major), on pull
   requests and on pushes to `main` and `phase-1`. It installs Node 22 and Bun, creates the .NET
   development certificate, caches `~/.cache/ms-playwright` by Playwright version, installs
   Chromium with its OS packages, runs `bun run e2e`, and uploads the HTML report and traces on
   failure. CI retries a failed test once; locally there are no retries.

## Consequences

- Locally a full run over both sites, including a fresh unattended install of each and
  incremental builds, took 48 s (Playwright itself 40 s, 16 tests plus 4 placeholders). CI adds
  the restore, cold builds and browser install, so each job should take a few minutes; that does
  not justify limiting the workflow to pull requests. If it grows, run it on pull requests only.
- Tests depend on accessible names (roles and labels from the UI brief's copy deck), not on
  `data-testid`s; no product code changed for the suite. Changing a label means changing the
  locator in `e2e/support/explorer.ts`.
- The share-link test (#44) reads the copied URL through granted clipboard permissions and opens
  it in a new browser context that shares only the saved login, so the view comes from the URL
  alone. Pages a test opens outside the `page` fixture call `watchConsole` to get the same guard.
- Criterion 1 (Same request, #42) is a `test.fixme` placeholder until that issue lands.
