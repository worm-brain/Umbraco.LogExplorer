import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { UmbControllerHostElementMixin } from "@umbraco-cms/backoffice/controller-api";
import { LogExplorerQueryContext, type LogExplorerQueryContextOptions } from "./query.context.js";

const SEARCH_PATH = "/umbraco/section/settings/workspace/log-explorer/view/search";
const PATTERNS_PATH = "/umbraco/section/settings/workspace/log-explorer/view/patterns";

/** A bare controller host element, standing in for `log-explorer-workspace`. */
class TestHostElement extends UmbControllerHostElementMixin(HTMLElement) {}
customElements.define("test-query-context-host", TestHostElement);

let host: TestHostElement;

/** Navigates the way the backoffice router does: change the URL, then announce it. */
function navigate(url: string): void {
  window.history.pushState({}, "", url);
  window.dispatchEvent(new Event("changestate"));
}

/** Creates the context on a connected host, with `/settings` answering immediately. */
async function connectContext(options: LogExplorerQueryContextOptions = {}): Promise<LogExplorerQueryContext> {
  const context = new LogExplorerQueryContext(host, {
    loadDefaultTimeRange: async () => undefined,
    ...options,
  });
  document.body.appendChild(host);
  // hostConnected and the defaults both settle in microtasks.
  await Promise.resolve();
  await Promise.resolve();
  return context;
}

beforeEach(() => {
  window.history.replaceState({}, "", SEARCH_PATH);
  host = new TestHostElement();
});

afterEach(() => {
  host.remove();
});

describe("LogExplorerQueryContext", () => {
  it("restores the state from the URL it opens on", async () => {
    window.history.replaceState({}, "", `${SEARCH_PATH}?range=24h&sort=asc`);

    const context = await connectContext();

    expect(context.getState()).toMatchObject({ range: { relative: "24h" }, sort: "asc" });
  });

  it("writes a changed range into the URL", async () => {
    const context = await connectContext();

    context.setRange({ relative: "15m" });

    expect(window.location.search).toBe("?range=15m");
  });

  it("pushes a history entry per change so the back button restores the previous view", async () => {
    const context = await connectContext();
    const before = window.history.length;

    context.setRange({ relative: "4h" });

    expect(window.history.length).toBe(before + 1);
  });

  it("does not push a history entry when the URL already holds the state", async () => {
    window.history.replaceState({}, "", `${SEARCH_PATH}?range=4h`);
    const context = await connectContext();
    const before = window.history.length;

    context.setRange({ relative: "4h" });

    expect(window.history.length).toBe(before);
  });

  it("follows the URL when the router announces a change", async () => {
    const context = await connectContext();

    navigate(`${SEARCH_PATH}?range=7d`);

    expect(context.getState().range).toEqual({ relative: "7d" });
  });

  it("keeps the state when a tab link drops the query string", async () => {
    const context = await connectContext();
    context.setRange({ relative: "30d" });

    navigate(PATTERNS_PATH);

    expect([context.getState().range, window.location.search]).toEqual([{ relative: "30d" }, "?range=30d"]);
  });

  it("uses DefaultTimeRange from the settings when the URL has no range", async () => {
    const context = await connectContext({ loadDefaultTimeRange: async () => "24h" });

    expect(context.getState().range).toEqual({ relative: "24h" });
  });

  it("ignores URL changes outside the workspace", async () => {
    const context = await connectContext();

    navigate("/umbraco/section/content?range=7d");

    expect(context.getState().range).toEqual({ relative: "1h" });
  });

  it("stops following the URL once its host disconnects", async () => {
    const context = await connectContext();
    host.remove();

    navigate(`${SEARCH_PATH}?range=7d`);

    expect(context.getState().range).toEqual({ relative: "1h" });
  });
});
