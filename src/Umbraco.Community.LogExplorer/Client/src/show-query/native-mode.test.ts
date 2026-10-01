import { describe, expect, it } from "vitest";
import type { SourceResponseModel } from "../api/index.js";
import type { FilterNode } from "../query/filter-node.js";
import { createDefaultViewState } from "../query/view-state.js";
import {
  canShowQuery,
  canUseNativeMode,
  enterNativeMode,
  formatClauses,
  leaveNativeMode,
  splitsOrGroup,
} from "./native-mode.js";

function source(features: Array<string>, allowNativeQuery: boolean): SourceResponseModel {
  return {
    alias: "sample",
    displayName: "Sample data",
    type: "Fake",
    sensitive: false,
    capabilities: { features, operators: [], nativeLanguage: "Sample", maxRangeSeconds: null, maxPageSize: 1000 },
    allowNativeQuery,
  };
}

const pathApi: FilterNode = { kind: "condition", field: "RequestPath", op: "startsWith", value: "/api" };
const matches: FilterNode = { kind: "condition", field: "RequestPath", op: "matches", value: "^/a" };

describe("canShowQuery and canUseNativeMode", () => {
  it.each([
    ["a source declaring nativeQuery that allows it", ["nativeQuery"], true, true, true],
    ["a source declaring nativeQuery configured without native queries", ["nativeQuery"], false, true, false],
    ["a source with no native language", ["histogram"], true, false, false],
  ])("for %s", (_, features, allow, showQuery, nativeMode) => {
    const active = source(features, allow);

    expect([canShowQuery(active), canUseNativeMode(active)]).toEqual([showQuery, nativeMode]);
  });

  it("offers neither while no source is known", () => {
    expect([canShowQuery(undefined), canUseNativeMode(undefined)]).toEqual([false, false]);
  });
});

describe("enterNativeMode", () => {
  it("moves the compiled query into native mode and clears the chips and levels it expresses", () => {
    const state = { ...createDefaultViewState("1h"), chips: [pathApi], levels: ["error" as const] };

    expect(enterNativeMode(state, 'RequestPath startswith "/api"', [])).toEqual({
      native: 'RequestPath startswith "/api"',
      chips: [],
      levels: null,
    });
  });

  it("keeps the chips the compiled query left out", () => {
    const state = { ...createDefaultViewState("1h"), chips: [pathApi, matches] };

    expect(enterNativeMode(state, "x", [1]).chips).toEqual([matches]);
  });

  it("puts a multi-line compiled query on one line, as the search box is a single-line input", () => {
    expect(enterNativeMode(createDefaultViewState("1h"), '@severity in [warn]\nand text("a")', []).native).toBe(
      '@severity in [warn] and text("a")',
    );
  });

  it("opens an empty native query when there was no filter", () => {
    expect(enterNativeMode(createDefaultViewState("1h"), null, []).native).toBe("");
  });
});

describe("splitsOrGroup", () => {
  const exceptionTimeout: FilterNode = {
    kind: "condition",
    field: "@exception.type",
    op: "contains",
    value: "Timeout",
  };
  const pathAdmin: FilterNode = { kind: "condition", field: "RequestPath", op: "startsWith", value: "/admin" };

  it("is true when one field has include chips both shown and not shown", () => {
    expect(splitsOrGroup([pathApi, pathAdmin], [1])).toBe(true);
  });

  it("is false when the not-shown chips are on a field of their own", () => {
    expect(splitsOrGroup([pathApi, exceptionTimeout], [1])).toBe(false);
  });

  it("ignores chips the source cannot run, which no query sees", () => {
    expect(splitsOrGroup([pathApi, matches], [1], [0])).toBe(false);
  });
});

describe("leaveNativeMode", () => {
  it("drops the native query", () => {
    expect(leaveNativeMode()).toEqual({ native: undefined });
  });
});

describe("formatClauses", () => {
  it("keeps one clause per line and drops Windows line endings and blank lines", () => {
    expect(formatClauses("@Level = 'Error'\r\n\r\nand Has(RequestPath)  \n")).toBe(
      "@Level = 'Error'\nand Has(RequestPath)",
    );
  });

  it.each([null, undefined, "", "  \n "])("is null for no filter (%j)", (native) => {
    expect(formatClauses(native)).toBeNull();
  });
});
