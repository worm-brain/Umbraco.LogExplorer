import { describe, expect, it } from "vitest";
import type { FilterNode } from "./filter-node.js";
import {
  createDefaultViewState,
  decodeViewState,
  encodeViewState,
  mergeViewStateIntoSearch,
  type LogExplorerViewState,
} from "./view-state.js";

const defaults = createDefaultViewState("1h");

const chips: Array<FilterNode> = [
  { kind: "condition", field: "RequestPath", op: "startsWith", value: "/api" },
  { kind: "not", child: { kind: "condition", field: "StatusCode", op: "equals", value: 200 } },
  { kind: "text", text: "délai dépassé", phrase: true },
];

/** Base64url-encodes test input the way the URL carries chips, so only the payload is invalid. */
function b64url(text: string): string {
  return btoa(text).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

/** Encodes then decodes through a real query string, as a reload does. */
function roundTrip(state: LogExplorerViewState): LogExplorerViewState {
  const search = encodeViewState(state, defaults).toString();
  return decodeViewState(new URLSearchParams(search), defaults);
}

describe("encodeViewState / decodeViewState", () => {
  it("round-trips a relative range, chips, levels, native query, source and sort", () => {
    const state: LogExplorerViewState = {
      source: "sample",
      range: { relative: "24h" },
      chips,
      levels: ["warn", "error"],
      native: "@Level = 'Error'",
      sort: "asc",
    };

    expect(roundTrip(state)).toEqual(state);
  });

  it("round-trips an absolute range", () => {
    const state = { ...defaults, range: { from: "2026-09-02T00:40:00.000Z", to: "2026-09-02T00:45:00.000Z" } };

    expect(roundTrip(state)).toEqual(state);
  });

  it("round-trips an empty level set (every level hidden) distinctly from no filter", () => {
    const state = { ...defaults, levels: [] };

    expect(roundTrip(state).levels).toEqual([]);
  });

  it("writes nothing for the defaults", () => {
    expect(encodeViewState(defaults, defaults).toString()).toBe("");
  });

  it("keeps relative ranges relative in the URL", () => {
    const params = encodeViewState({ ...defaults, range: { relative: "15m" } }, defaults);

    expect(params.toString()).toBe("range=15m");
  });

  it("round-trips a time zoom alongside the relative range it narrows", () => {
    const state = {
      ...defaults,
      range: { relative: "24h" as const },
      zoom: { from: "2026-09-02T00:40:00.000Z", to: "2026-09-02T00:45:00.000Z" },
    };

    expect(roundTrip(state)).toEqual(state);
  });

  it("round-trips the open show-query panel as sq=1", () => {
    const state = { ...defaults, showQuery: true };

    expect([encodeViewState(state, defaults).toString(), roundTrip(state).showQuery]).toEqual(["sq=1", true]);
  });

  it("keeps native mode with an empty native query across a reload", () => {
    expect(roundTrip({ ...defaults, native: "" }).native).toBe("");
  });

  it("writes the zoom as zf and zt", () => {
    const params = encodeViewState(
      { ...defaults, zoom: { from: "2026-09-02T00:40:00.000Z", to: "2026-09-02T00:45:00.000Z" } },
      defaults,
    );

    expect(params.toString()).toBe("zf=2026-09-02T00%3A40%3A00.000Z&zt=2026-09-02T00%3A45%3A00.000Z");
  });

  it("treats all six levels as no level filter", () => {
    const params = encodeViewState(
      { ...defaults, levels: ["trace", "debug", "info", "warn", "error", "fatal"] },
      defaults,
    );

    expect(params.has("levels")).toBe(false);
  });
});

describe("decodeViewState with invalid input", () => {
  it.each([
    ["an unknown relative range", "range=2y"],
    ["an unparsable from", "from=yesterday&to=2026-09-02T00:45:00Z"],
    ["a from after the to", "from=2026-09-02T01:00:00Z&to=2026-09-02T00:00:00Z"],
    ["an absolute range missing its end", "from=2026-09-02T00:40:00Z"],
  ])("falls back to the default range for %s", (_name, search) => {
    expect(decodeViewState(new URLSearchParams(search), defaults).range).toEqual({ relative: "1h" });
  });

  it.each([
    ["chips that are not base64url", "f=***"],
    ["chips that are not JSON", `f=${b64url("not json")}`],
    ["chips with an unknown kind", `f=${b64url(JSON.stringify([{ kind: "regex", text: "x" }]))}`],
    ["chips with an unknown operator", `f=${b64url(JSON.stringify([{ kind: "condition", field: "a", op: "like" }]))}`],
  ])("falls back to no chips for %s", (_name, search) => {
    expect(decodeViewState(new URLSearchParams(search), defaults).chips).toEqual([]);
  });

  it.each([
    ["a zoom missing its end", "zf=2026-09-02T00:40:00Z"],
    ["a zoom that ends before it starts", "zf=2026-09-02T00:45:00Z&zt=2026-09-02T00:40:00Z"],
    ["an unparsable zoom", "zf=soon&zt=later"],
  ])("ignores %s", (_name, search) => {
    expect(decodeViewState(new URLSearchParams(search), defaults).zoom).toBeUndefined();
  });

  it("falls back to no level filter for an unknown level name", () => {
    expect(decodeViewState(new URLSearchParams("levels=warn,loud"), defaults).levels).toBeNull();
  });

  it("falls back to newest first for an unknown sort", () => {
    expect(decodeViewState(new URLSearchParams("sort=sideways"), defaults).sort).toBe("desc");
  });

  it("keeps the valid fields when another one is invalid", () => {
    const state = decodeViewState(new URLSearchParams("range=2y&levels=error&sort=asc"), defaults);

    expect(state).toEqual({ ...defaults, levels: ["error"], sort: "asc" });
  });
});

describe("createDefaultViewState", () => {
  it("uses the configured default time range", () => {
    expect(createDefaultViewState("7d").range).toEqual({ relative: "7d" });
  });

  it("falls back to the last hour when the configured range is not a preset", () => {
    expect(createDefaultViewState("90m").range).toEqual({ relative: "1h" });
  });
});

describe("mergeViewStateIntoSearch", () => {
  it("replaces the view-state keys and keeps unrelated ones", () => {
    const merged = mergeViewStateIntoSearch("?other=1&range=4h&sort=asc", new URLSearchParams("range=15m"));

    expect(merged).toBe("other=1&range=15m");
  });
});
