import { describe, expect, it } from "vitest";
import { createDefaultViewState, type LogExplorerViewState } from "../query/view-state.js";
import { buildShareUrl, type ShareLocation } from "./share-link.js";

const defaults = createDefaultViewState("1h");

const PATTERNS_TAB: ShareLocation = {
  origin: "https://localhost:44371",
  pathname: "/umbraco/section/settings/workspace/log-explorer/view/patterns",
  search: "",
  hash: "",
};

describe("buildShareUrl", () => {
  it("is the absolute URL of the current tab with the view state in its query string", () => {
    const state: LogExplorerViewState = { ...defaults, range: { relative: "15m" }, sort: "asc" };

    expect(buildShareUrl(PATTERNS_TAB, state, defaults)).toBe(
      "https://localhost:44371/umbraco/section/settings/workspace/log-explorer/view/patterns?range=15m&sort=asc",
    );
  });

  it("is the bare tab URL when the view is the defaults", () => {
    expect(buildShareUrl(PATTERNS_TAB, defaults, defaults)).toBe(
      "https://localhost:44371/umbraco/section/settings/workspace/log-explorer/view/patterns",
    );
  });

  it("writes the state on screen over stale view-state keys and keeps other keys", () => {
    const location = { ...PATTERNS_TAB, search: "?other=1&range=4h&sq=1" };

    expect(buildShareUrl(location, { ...defaults, range: { relative: "15m" } }, defaults)).toBe(
      "https://localhost:44371/umbraco/section/settings/workspace/log-explorer/view/patterns?other=1&range=15m",
    );
  });
});
