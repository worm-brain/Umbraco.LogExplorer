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

1. **Plain `@playwright/test`, no Umbraco helpers.** A small `logIn` helper
   (`e2e/support/auth.ts`) logs in through the same form fields the helpers use; item 7 says how
   sessions are shared and why there is no saved storage state. The helpers would need one
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
4. **One project per site:** `site17` and `site18`. Every spec runs on both. Specs use the
   deterministic Fake `sample` source wherever exact counts or rows matter; the view-loading test
   also runs once on the default `files` source.
5. **Console guard:** a shared auto fixture (`e2e/support/test.ts`) fails any test that logs a
   `console.error` or throws an uncaught page error. Its allow-list is empty: the only noise seen,
   the backoffice's token-refresh 400 on the login page, happens before `logIn` returns, and the
   guard starts watching after that.
6. **CI: `.github/workflows/e2e.yml`**, one job per major (`E2E_SITES` = the matrix major), on pull
   requests and on pushes to `main` and `phase-1`. It installs Node 22 and Bun, creates the .NET
   development certificate, caches `~/.cache/ms-playwright` by Playwright version, installs
   Chromium with its OS packages, runs `bun run e2e`, and uploads the HTML report and traces on
   failure. CI retries a failed test once; locally there are no retries.
7. **One login per worker, cookies carried from test to test (issue #50, reopened).** A worker
   logs in once through the form (`newSession` in `e2e/support/auth.ts`, in a throwaway context)
   and keeps only the session cookies. Each test's context starts from those cookies
   (`storageState` fixture in `e2e/support/test.ts`), and when the test ends the context's
   cookies, which by then hold the latest rotated refresh token, replace them for the next test.
   Local storage is not carried, so it stays per test. A context a test opens itself (the
   share-link colleague) passes an empty `storageState` (`NO_SESSION`; Playwright Test would
   otherwise give it the test's) and logs in for itself with `logIn`. A worker restarts after a
   failure, so a broken session never outlives the failing test. The sites the suite starts get
   `Umbraco__CMS__Security__AllowConcurrentLogins=true` in their `webServer` env, next to the log
   generator switches, and logins to one site run one at a time across workers (a lock directory
   in the OS temp folder, keyed by port). Nothing is saved to disk (`e2e/.auth/` is gone).

### Why: a backoffice session cannot be shared (verified on 17.7, 2026-10-01)

The first version logged in once per site and gave every test that storage state. CI run
36909793215 then failed both majors: tests starting a minute or more into the run, and the
share-link test's fresh context, logged `[UmbAuthClient] Token request failed: 400`. A scratch
script that opened contexts from one saved state over time, plus the site's own log, showed why:

- The backoffice keeps its tokens in httpOnly cookies (`__Host-umbAccessToken`,
  `__Host-umbRefreshToken`) and holds the session only in memory, so **every page load in a new
  context redeems the stored refresh token** (`setInitialState` -> `/token`,
  `grant_type=refresh_token`). The access token lasts 300 s, but that is not what bites.
- OpenIddict rotates refresh tokens and accepts an already-redeemed one again only within its
  default **30-second reuse leeway** (Umbraco sets token lifetimes but no leeway). A context
  loading 28 s after the first redemption got 200; one loading 32 s after got `invalid_grant`
  "already been redeemed" (ID2012), and OpenIddict logged "115 tokens associated with the
  authorization were revoked to prevent a potential token replay attack", so later contexts got
  ID2018 "no longer valid" too. The backoffice then recovers through a silent re-authorisation,
  so the page works but the console has the 400s. The first CI run passed only because every
  test started inside the leeway. Locally the old harness passed once (35 s) and failed 44 of 80
  tests with `--repeat-each=8` (1.8 min), with the CI error.
- So no two contexts may start from the same cookies more than 30 s apart. A worker's tests run
  one after another, so carrying the newest cookies forward redeems each refresh token once.
- **With concurrent logins off (the sample sites' `Security:AllowConcurrentLogins: false`) each
  login revokes every other token of that user** (`RevokeUserAuthenticationTokensNotificationHandler`
  on `UserLoginSuccessNotification`), so one worker's login would sign the others out; parallel
  revocations also threw OpenIddict `ConcurrencyException` on the login request. Hence the
  setting for the e2e sites.
- **Many logins overwhelm SQLite.** A login per test (the first fix tried) failed on parallel
  logins with "database table is locked: umbracoUser", and with the logins serialised it still
  wedged the site after a few minutes (OpenIddict queries timing out on the locked database).
  One login per worker plus the share test's colleague keeps it to a handful per run, and the
  lock keeps them apart.

Alternatives rejected: allow-listing the 400 (hides real auth failures); a login per test (SQLite,
above); a separate backoffice user per worker (would avoid the setting, but creating and
activating users through the Management API on both majors is a lot of harness for no gain). The
setting lives only in the env of the sites Playwright starts; the sample sites' own config is
unchanged. A site you start yourself and let Playwright reuse locally needs the same env var, or
the workers sign each other out.

## Consequences

- Locally a full run over both sites, including a fresh unattended install of each and
  incremental builds, took 48 s (Playwright itself 40 s, 16 tests plus 4 placeholders). CI adds
  the restore, cold builds and browser install, so each job should take a few minutes; that does
  not justify limiting the workflow to pull requests. If it grows, run it on pull requests only.
- Tests depend on accessible names (roles and labels from the UI brief's copy deck), not on
  `data-testid`s; no product code changed for the suite. Changing a label means changing the
  locator in `e2e/support/explorer.ts`.
- The share-link test (#44) reads the copied URL through granted clipboard permissions and opens
  it in a new, logged-out browser context that logs in for itself, so the view comes from the URL
  alone. Pages a test opens outside the `page` fixture call `watchConsole` to get the same guard.
  It waits for the Patterns header before reading the tab, which it once read mid-load.
- A test that opens another context must start it with `NO_SESSION` and `logIn`, never with the
  test's cookies (item 7).
- Criterion 1 (Same request, #42) is a `test.fixme` placeholder until that issue lands.
