import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { UmbControllerHostElementMixin } from "@umbraco-cms/backoffice/controller-api";
import type { SourceResponseModel } from "../api/index.js";
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
    loadDefaultSource: async () => undefined,
    loadSources: async () => ({ status: "empty" }),
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

  it("writes a time zoom into the URL and keeps the range it narrows", async () => {
    const context = await connectContext();

    context.setZoom({ from: "2026-09-02T00:40:00.000Z", to: "2026-09-02T00:45:00.000Z" });

    expect(new URLSearchParams(window.location.search).get("zf")).toBe("2026-09-02T00:40:00.000Z");
  });

  it("clears the time zoom when a new range is chosen", async () => {
    const context = await connectContext();
    context.setZoom({ from: "2026-09-02T00:40:00.000Z", to: "2026-09-02T00:45:00.000Z" });

    context.setRange({ relative: "4h" });

    expect([context.getState().zoom, window.location.search]).toEqual([undefined, "?range=4h"]);
  });

  it("restores a time zoom from the URL it opens on", async () => {
    window.history.replaceState({}, "", `${SEARCH_PATH}?zf=2026-09-02T00:40:00Z&zt=2026-09-02T00:45:00Z`);

    const context = await connectContext();

    expect(context.getState().zoom).toEqual({ from: "2026-09-02T00:40:00.000Z", to: "2026-09-02T00:45:00.000Z" });
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

function source(alias: string): SourceResponseModel {
  return {
    alias,
    displayName: alias,
    type: "Fake",
    sensitive: false,
    capabilities: { features: [], operators: [], nativeLanguage: null, maxRangeSeconds: null, maxPageSize: 1000 },
  };
}

const twoSources: LogExplorerQueryContextOptions = {
  loadSources: async () => ({ status: "loaded", sources: [source("files"), source("sample")] }),
  loadDefaultSource: async () => "sample",
};

describe("LogExplorerQueryContext active source", () => {
  it("uses DefaultSource when the URL names no source", async () => {
    const context = await connectContext(twoSources);

    expect(context.getActiveSource()?.alias).toBe("sample");
  });

  it("uses the source the URL names when it is visible", async () => {
    window.history.replaceState({}, "", `${SEARCH_PATH}?src=files`);

    const context = await connectContext(twoSources);

    expect(context.getActiveSource()?.alias).toBe("files");
  });

  it("falls back to DefaultSource when the URL names a source the user cannot see", async () => {
    window.history.replaceState({}, "", `${SEARCH_PATH}?src=prod-ai`);

    const context = await connectContext(twoSources);

    expect(context.getActiveSource()?.alias).toBe("sample");
  });

  it("writes the selected source into the URL", async () => {
    const context = await connectContext(twoSources);

    context.setSource("files");

    expect([context.getActiveSource()?.alias, window.location.search]).toEqual(["files", "?src=files"]);
  });

  it("publishes the resolved source to observers", async () => {
    const context = await connectContext(twoSources);
    const seen: Array<string | undefined> = [];
    const subscription = context.activeSource.subscribe((active) => seen.push(active?.alias));

    context.setSource("files");
    subscription.unsubscribe();

    expect(seen).toEqual(["sample", "files"]);
  });

  it("has no active source while DefaultSource is still loading", async () => {
    const context = await connectContext({ ...twoSources, loadDefaultSource: () => new Promise(() => {}) });

    expect(context.getActiveSource()).toBeUndefined();
  });

  it("has no active source when no source is visible", async () => {
    const context = await connectContext({ loadDefaultSource: async () => "sample" });

    expect(context.getActiveSource()).toBeUndefined();
  });

  it("loads the sources again on reload", async () => {
    let calls = 0;
    const context = await connectContext({
      loadSources: async () =>
        ++calls === 1
          ? { status: "error", message: "Failed to fetch" }
          : { status: "loaded", sources: [source("files")] },
    });

    await context.reloadSources();

    expect(context.getActiveSource()?.alias).toBe("files");
  });
});
