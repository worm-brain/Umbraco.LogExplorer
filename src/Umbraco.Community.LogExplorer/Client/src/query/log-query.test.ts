import { describe, expect, it } from "vitest";
import type { FilterNode } from "./filter-node.js";
import { chipsToFilter, toLogQuery } from "./log-query.js";
import { createDefaultViewState } from "./view-state.js";

const pathApi: FilterNode = { kind: "condition", field: "RequestPath", op: "startsWith", value: "/api" };
const pathUmbraco: FilterNode = { kind: "condition", field: "RequestPath", op: "startsWith", value: "/umbraco" };
const notOk: FilterNode = { kind: "not", child: { kind: "condition", field: "StatusCode", op: "equals", value: 200 } };
const timeout: FilterNode = { kind: "text", text: "timeout" };

describe("chipsToFilter", () => {
  it("has no filter without chips", () => {
    expect(chipsToFilter([])).toBeNull();
  });

  it("uses a single chip as it is", () => {
    expect(chipsToFilter([timeout])).toEqual(timeout);
  });

  it("ORs include chips on the same field", () => {
    expect(chipsToFilter([pathApi, pathUmbraco])).toEqual({ kind: "or", children: [pathApi, pathUmbraco] });
  });

  it("ANDs chips on different fields, exclude chips and text", () => {
    expect(chipsToFilter([pathApi, notOk, timeout, pathUmbraco])).toEqual({
      kind: "and",
      children: [{ kind: "or", children: [pathApi, pathUmbraco] }, notOk, timeout],
    });
  });
});

describe("toLogQuery", () => {
  it("builds a first-page query for the default view", () => {
    expect(toLogQuery(createDefaultViewState("1h"), 60)).toEqual({
      range: { relative: "1h" },
      levels: null,
      filter: null,
      nativeQuery: null,
      take: 60,
      cursor: null,
      sort: "descending",
    });
  });

  it("carries an absolute range, the level set, the cursor and oldest-first sort", () => {
    const state = {
      ...createDefaultViewState("1h"),
      range: { from: "2026-10-01T10:00:00.000Z", to: "2026-10-01T11:00:00.000Z" },
      levels: ["warn" as const, "error" as const],
      sort: "asc" as const,
    };

    expect(toLogQuery(state, 60, "c2")).toMatchObject({
      range: { from: "2026-10-01T10:00:00.000Z", to: "2026-10-01T11:00:00.000Z" },
      levels: ["warn", "error"],
      cursor: "c2",
      sort: "ascending",
    });
  });

  it("runs over the time zoom instead of the picker's range", () => {
    const state = {
      ...createDefaultViewState("1h"),
      zoom: { from: "2026-10-01T10:40:00.000Z", to: "2026-10-01T10:45:00.000Z" },
    };

    expect(toLogQuery(state, 60).range).toEqual({ from: "2026-10-01T10:40:00.000Z", to: "2026-10-01T10:45:00.000Z" });
  });
});
