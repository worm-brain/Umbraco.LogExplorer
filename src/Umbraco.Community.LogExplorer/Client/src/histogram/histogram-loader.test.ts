import { describe, expect, it } from "vitest";
import type { HistogramRequest, HistogramResult } from "../api/index.js";
import { HistogramLoader, type HistogramFn } from "./histogram-loader.js";

const request: HistogramRequest = {
  query: {
    range: { relative: "1h" },
    levels: null,
    filter: null,
    nativeQuery: null,
    take: 1,
    cursor: null,
    sort: "descending",
  },
  targetBuckets: 60,
};

const result: HistogramResult = {
  range: { from: "2026-09-02T00:00:00Z", to: "2026-09-02T01:00:00Z" },
  bucketSize: "00:01:00",
  buckets: [{ start: "2026-09-02T00:00:00Z", countsBySeverityShortName: { info: 3 } }],
  approximate: false,
};

/** A request the test resolves by hand, so it controls which response lands first. */
function deferred() {
  let resolve!: (value: Awaited<ReturnType<HistogramFn>>) => void;
  const promise = new Promise<Awaited<ReturnType<HistogramFn>>>((r) => (resolve = r));
  return { promise, resolve };
}

describe("HistogramLoader", () => {
  it("loads the histogram", async () => {
    const loader = new HistogramLoader(
      async () => ({ data: result }),
      () => {},
    );

    loader.load("sample", request);
    await Promise.resolve();

    expect(loader.state).toEqual({ status: "loaded", result });
  });

  it("reports the ProblemDetails detail when the request fails", async () => {
    const loader = new HistogramLoader(
      async () => ({ error: { detail: "Log source 'files' does not support histograms." } }),
      () => {},
    );

    loader.load("files", request);
    await Promise.resolve();

    expect(loader.state).toMatchObject({
      status: "error",
      error: "Log source 'files' does not support histograms.",
    });
  });

  it("aborts the request in flight when a new one starts", () => {
    const signals: Array<AbortSignal> = [];
    const loader = new HistogramLoader(
      (_alias, _request, signal) => {
        signals.push(signal);
        return new Promise(() => {});
      },
      () => {},
    );

    loader.load("sample", request);
    loader.load("sample", { ...request, targetBuckets: 30 });

    expect(signals[0]?.aborted).toBe(true);
  });

  it("drops a superseded response that arrives late", async () => {
    const first = deferred();
    const second = deferred();
    const calls = [first, second];
    const loader = new HistogramLoader(
      () => calls.shift()!.promise,
      () => {},
    );
    const newer = { ...result, approximate: true };

    loader.load("sample", request);
    loader.load("sample", { ...request, targetBuckets: 30 });
    second.resolve({ data: newer });
    await Promise.resolve();
    first.resolve({ data: result });
    await Promise.resolve();

    expect(loader.state.result).toBe(newer);
  });
});
