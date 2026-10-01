import { describe, expect, it } from "vitest";
import type { FilterOperator, SourceResponseModel } from "../api/index.js";
import type { FilterNode } from "../query/filter-node.js";
import { createDefaultViewState } from "../query/view-state.js";
import { findNotExpressibleChips, findUnsupportedChips, toQueryState } from "./unsupported-chips.js";

function source(operators: Array<FilterOperator>, allowNativeQuery = true): SourceResponseModel {
  return {
    alias: "sample",
    displayName: "Sample data",
    type: "Fake",
    sensitive: false,
    capabilities: {
      features: ["nativeQuery"],
      operators,
      nativeLanguage: "Sample",
      maxRangeSeconds: null,
      maxPageSize: 1000,
    },
    allowNativeQuery,
  };
}

const pathApi: FilterNode = { kind: "condition", field: "RequestPath", op: "startsWith", value: "/api" };
const status200: FilterNode = { kind: "condition", field: "StatusCode", op: "equals", value: "200" };
const notStatus200: FilterNode = { kind: "not", child: status200 };
const timestamp: FilterNode = { kind: "condition", field: "@timestamp", op: "greaterThan", value: 5 };
const text: FilterNode = { kind: "text", text: "timeout" };

describe("findUnsupportedChips", () => {
  it("marks a chip whose operator the source does not declare", () => {
    expect(findUnsupportedChips([pathApi, status200], source(["equals"]))).toEqual([0]);
  });

  it("marks an exclude chip around an unsupported condition", () => {
    expect(findUnsupportedChips([notStatus200], source(["startsWith"]))).toEqual([0]);
  });

  it("marks nothing when the source runs every chip", () => {
    expect(findUnsupportedChips([pathApi, notStatus200, text], source(["equals", "startsWith"]))).toEqual([]);
  });

  it("marks nothing while no source is known", () => {
    expect(findUnsupportedChips([pathApi], undefined)).toEqual([]);
  });
});

describe("findNotExpressibleChips", () => {
  it("marks a chip holding a node the compile reported unsupported", () => {
    // Compile answers carry the wire defaults, so the comparison must ignore them.
    const reported: FilterNode = { ...timestamp, caseInsensitive: true } as FilterNode;

    expect(findNotExpressibleChips([text, timestamp], [reported], [])).toEqual([1]);
  });

  it("marks an exclude chip whose condition the compile reported unsupported", () => {
    expect(findNotExpressibleChips([notStatus200], [status200], [])).toEqual([0]);
  });

  it("does not mark a chip the source cannot run, which is reported as unsupported instead", () => {
    expect(findNotExpressibleChips([timestamp], [timestamp], [0])).toEqual([]);
  });

  it("marks nothing when the compile expressed everything", () => {
    expect(findNotExpressibleChips([pathApi, text], [], [])).toEqual([]);
  });
});

describe("toQueryState", () => {
  it("leaves the unsupported chips out of the query", () => {
    const state = { ...createDefaultViewState("1h"), chips: [pathApi, status200, text] };

    expect(toQueryState(state, source(["equals"]), [0]).chips).toEqual([status200, text]);
  });

  it("returns the state itself when nothing is left out", () => {
    const state = { ...createDefaultViewState("1h"), chips: [pathApi] };

    expect(toQueryState(state, source(["startsWith"]), [])).toBe(state);
  });

  it("drops the native query for a source that does not allow native queries", () => {
    const state = { ...createDefaultViewState("1h"), native: 'text("a")' };

    expect(toQueryState(state, source([], false), []).native).toBeUndefined();
  });

  it("keeps the native query for a source that allows it", () => {
    const state = { ...createDefaultViewState("1h"), native: 'text("a")' };

    expect(toQueryState(state, source([]), []).native).toBe('text("a")');
  });
});
