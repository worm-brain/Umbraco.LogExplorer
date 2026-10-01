import { describe, expect, it } from "vitest";
import type { LogPage, LogQuery, LogRecord } from "../api/index.js";
import {
  INITIAL_RESULTS_STATE,
  ResultsLoader,
  resultsReducer,
  type ResultsState,
  type SearchFn,
} from "./results-loader.js";

const query: LogQuery = { range: { relative: "1h" }, take: 2, sort: "descending" };

function record(id: string): LogRecord {
  return {
    id,
    timestamp: "2026-10-01T12:00:00Z",
    severityNumber: 9,
    attributes: {},
    resource: {},
    sourceAlias: "sample",
  };
}

function page(ids: Array<string>, nextCursor: string | null, totalCount: number | null = 4): LogPage {
  return {
    records: ids.map(record),
    nextCursor,
    range: { from: "2026-10-01T11:00:00Z", to: "2026-10-01T12:00:00Z" },
    totalCount,
    totalIsLowerBound: false,
    warnings: [],
  };
}

/** A search whose responses the test releases by hand, recording each request. */
function controllableSearch() {
  const calls: Array<{ alias: string; query: LogQuery; signal: AbortSignal; resolve: (page: LogPage) => void }> = [];
  const search: SearchFn = (alias, query, signal) =>
    new Promise((resolve, reject) => {
      signal.addEventListener("abort", () => reject(new DOMException("Aborted", "AbortError")));
      calls.push({ alias, query, signal, resolve: (data) => resolve({ data }) });
    });
  return { search, calls };
}

/** Lets the loader's awaited promise settle. */
const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

describe("resultsReducer", () => {
  const loaded: ResultsState = resultsReducer(INITIAL_RESULTS_STATE, {
    type: "success",
    page: page(["a", "b"], "c2"),
    append: false,
  });

  it("appends a further page to the rows", () => {
    const next = resultsReducer(loaded, { type: "success", page: page(["c", "d"], null), append: true });

    expect(next.records.map((row) => row.id)).toEqual(["a", "b", "c", "d"]);
  });

  it("replaces the rows with a first page", () => {
    const next = resultsReducer(loaded, { type: "success", page: page(["x"], null), append: false });

    expect(next.records.map((row) => row.id)).toEqual(["x"]);
  });

  it("keeps the previous rows while a first page loads", () => {
    const next = resultsReducer(loaded, { type: "start", append: false });

    expect([next.status, next.records.length]).toEqual(["loading", 2]);
  });

  it("keeps the rows when a further page fails", () => {
    const next = resultsReducer(loaded, { type: "failure", message: "Boom", append: true });

    expect([next.status, next.error, next.records.length]).toEqual(["error", "Boom", 2]);
  });
});

describe("ResultsLoader", () => {
  it("requests the first page without a cursor", async () => {
    const { search, calls } = controllableSearch();
    const loader = new ResultsLoader(search, () => {});

    loader.load("sample", { ...query, cursor: "stale" });

    expect([calls[0]?.alias, calls[0]?.query.cursor]).toEqual(["sample", null]);
  });

  it("requests the next page with the previous page's cursor", async () => {
    const { search, calls } = controllableSearch();
    const loader = new ResultsLoader(search, () => {});
    loader.load("sample", query);
    calls[0]!.resolve(page(["a", "b"], "cursor-2"));
    await settle();

    loader.loadMore();

    expect(calls[1]?.query.cursor).toBe("cursor-2");
  });

  it("does not request past the last page", async () => {
    const { search, calls } = controllableSearch();
    const loader = new ResultsLoader(search, () => {});
    loader.load("sample", query);
    calls[0]!.resolve(page(["a"], null));
    await settle();

    loader.loadMore();

    expect(calls).toHaveLength(1);
  });

  it("aborts the request in flight when the query changes", () => {
    const { search, calls } = controllableSearch();
    const loader = new ResultsLoader(search, () => {});
    loader.load("sample", query);

    loader.load("sample", { ...query, sort: "ascending" });

    expect(calls[0]?.signal.aborted).toBe(true);
  });

  it("ignores the response of a superseded request", async () => {
    const { search, calls } = controllableSearch();
    const loader = new ResultsLoader(search, () => {});
    loader.load("sample", query);
    loader.load("sample", { ...query, sort: "ascending" });

    calls[1]!.resolve(page(["new"], null));
    calls[0]!.resolve(page(["old"], null));
    await settle();

    expect(loader.state.records.map((row) => row.id)).toEqual(["new"]);
  });

  it("reports a ProblemDetails failure as an error state", async () => {
    const loader = new ResultsLoader(
      async () => ({
        error: { title: "Bad Request", detail: "Unknown relative range '3w'.", code: "invalid_query" },
        response: new Response(null, { status: 400 }),
      }),
      () => {},
    );

    loader.load("sample", query);
    await settle();

    expect([loader.state.status, loader.state.error]).toEqual(["error", "Unknown relative range '3w'."]);
  });

  it("retries the request that failed", async () => {
    let attempts = 0;
    const loader = new ResultsLoader(
      async () => {
        attempts += 1;
        return attempts === 1 ? { error: { title: "Busy" } } : { data: page(["a"], null) };
      },
      () => {},
    );
    loader.load("sample", query);
    await settle();

    loader.retry();
    await settle();

    expect([loader.state.status, loader.state.records.length]).toEqual(["loaded", 1]);
  });

  it("returns to idle and aborts on reset", () => {
    const { search, calls } = controllableSearch();
    const loader = new ResultsLoader(search, () => {});
    loader.load("sample", query);

    loader.reset();

    expect([loader.state.status, calls[0]?.signal.aborted]).toEqual(["idle", true]);
  });
});
