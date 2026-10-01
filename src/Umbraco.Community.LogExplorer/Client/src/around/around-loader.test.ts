import { describe, expect, it } from "vitest";
import type { ContextResult, LogRecord } from "../api/index.js";
import { AROUND_COUNT, AroundLoader, aroundRows, type AroundState, type ContextFn } from "./around-loader.js";

function record(id: string): LogRecord {
  return {
    id,
    timestamp: "2026-09-02T00:41:10.000Z",
    severityNumber: 9,
    attributes: {},
    resource: {},
    sourceAlias: "sample",
  };
}

const context: ContextResult = {
  before: [record("b1"), record("b2")],
  anchor: record("a"),
  after: [record("c1")],
};

/** A context request whose responses the test releases by hand, recording each call. */
function controllableContext() {
  const calls: Array<{
    alias: string;
    id: string;
    count: number;
    signal: AbortSignal;
    resolve: (result: Awaited<ReturnType<ContextFn>>) => void;
  }> = [];
  const fetchContext: ContextFn = (alias, id, count, signal) =>
    new Promise((resolve, reject) => {
      signal.addEventListener("abort", () => reject(new DOMException("Aborted", "AbortError")));
      calls.push({ alias, id, count, signal, resolve });
    });
  return { fetchContext, calls };
}

const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

describe("aroundRows", () => {
  it("lists newest first for the default sort, anchor in the middle", () => {
    expect(aroundRows(context, "desc").map((row) => row.id)).toEqual(["c1", "a", "b2", "b1"]);
  });

  it("lists oldest first when the list is sorted oldest first", () => {
    expect(aroundRows(context, "asc").map((row) => row.id)).toEqual(["b1", "b2", "a", "c1"]);
  });
});

describe("AroundLoader", () => {
  it("asks for seven entries either side and loads the rows in the requested order", async () => {
    const { fetchContext, calls } = controllableContext();
    let state: AroundState | undefined;
    const loader = new AroundLoader(fetchContext, (next) => (state = next));

    loader.load("sample", "a", "asc");
    calls[0]!.resolve({ data: context });
    await settle();

    expect([calls[0]!.count, state?.status, state?.anchor?.id, state?.rows.map((row) => row.id)]).toEqual([
      AROUND_COUNT,
      "loaded",
      "a",
      ["b1", "b2", "a", "c1"],
    ]);
  });

  it("reports a stale record id as an error with the server's message", async () => {
    const { fetchContext, calls } = controllableContext();
    const loader = new AroundLoader(fetchContext, () => {});

    loader.load("sample", "gone", "desc");
    calls[0]!.resolve({ error: { detail: "Log source 'sample' has no record 'gone'.", code: "record_not_found" } });
    await settle();

    expect([loader.state.status, loader.state.anchorId, loader.state.error]).toEqual([
      "error",
      "gone",
      "Log source 'sample' has no record 'gone'.",
    ]);
  });

  it("drops the response of a superseded anchor", async () => {
    const { fetchContext, calls } = controllableContext();
    const loader = new AroundLoader(fetchContext, () => {});

    loader.load("sample", "old", "desc");
    loader.load("sample", "a", "desc");
    calls[1]!.resolve({ data: context });
    await settle();

    expect([calls[0]!.signal.aborted, loader.state.anchorId]).toEqual([true, "a"]);
  });

  it("re-sorts the loaded rows without a new request when only the sort changes", async () => {
    const { fetchContext, calls } = controllableContext();
    const loader = new AroundLoader(fetchContext, () => {});
    loader.load("sample", "a", "desc");
    calls[0]!.resolve({ data: context });
    await settle();

    loader.load("sample", "a", "asc");

    expect([calls.length, loader.state.rows[0]?.id]).toEqual([1, "b1"]);
  });

  it("returns to idle and aborts the request on reset", () => {
    const { fetchContext, calls } = controllableContext();
    const loader = new AroundLoader(fetchContext, () => {});
    loader.load("sample", "a", "desc");

    loader.reset();

    expect([loader.state.status, calls[0]!.signal.aborted]).toEqual(["idle", true]);
  });
});
