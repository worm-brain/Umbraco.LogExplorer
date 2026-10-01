import { describe, expect, it } from "vitest";
import type { PatternResult, PatternsRequest } from "../api/index.js";
import { PatternsLoader, type PatternsFn } from "./patterns-loader.js";

const request: PatternsRequest = {
  query: {
    range: { relative: "1h" },
    levels: null,
    filter: null,
    nativeQuery: null,
    take: 1,
    cursor: null,
    sort: "descending",
  },
  top: 100,
};

const result: PatternResult = { patterns: [], approximate: false };

/** A request the test resolves by hand, so it controls which response lands first. */
function deferred() {
  let resolve!: (value: Awaited<ReturnType<PatternsFn>>) => void;
  const promise = new Promise<Awaited<ReturnType<PatternsFn>>>((r) => (resolve = r));
  return { promise, resolve };
}

describe("PatternsLoader", () => {
  it("loads the patterns", async () => {
    const loader = new PatternsLoader(
      async () => ({ data: result }),
      () => {},
    );

    loader.load("sample", request);
    await Promise.resolve();

    expect(loader.state).toEqual({ status: "loaded", result });
  });

  it("reports the ProblemDetails detail when the request fails", async () => {
    const loader = new PatternsLoader(
      async () => ({ error: { detail: "Log source 'seq' does not support patterns." } }),
      () => {},
    );

    loader.load("seq", request);
    await Promise.resolve();

    expect(loader.state).toMatchObject({ status: "error", error: "Log source 'seq' does not support patterns." });
  });

  it("keeps the previous patterns while the next request runs", async () => {
    const next = deferred();
    const calls = [Promise.resolve({ data: result }), next.promise];
    const loader = new PatternsLoader(
      () => calls.shift()!,
      () => {},
    );

    loader.load("sample", request);
    await Promise.resolve();
    loader.load("sample", { ...request, top: 50 });

    expect(loader.state).toEqual({ status: "loading", result });
  });

  it("aborts the request in flight when a new one starts", () => {
    const signals: Array<AbortSignal> = [];
    const loader = new PatternsLoader(
      (_alias, _request, signal) => {
        signals.push(signal);
        return new Promise(() => {});
      },
      () => {},
    );

    loader.load("sample", request);
    loader.load("sample", { ...request, top: 50 });

    expect(signals[0]?.aborted).toBe(true);
  });

  it("drops a superseded response that arrives late", async () => {
    const first = deferred();
    const second = deferred();
    const calls = [first, second];
    const loader = new PatternsLoader(
      () => calls.shift()!.promise,
      () => {},
    );
    const newer = { ...result, approximate: true };

    loader.load("sample", request);
    loader.load("sample", { ...request, top: 50 });
    second.resolve({ data: newer });
    await Promise.resolve();
    first.resolve({ data: result });
    await Promise.resolve();

    expect(loader.state.result).toBe(newer);
  });

  it("retries the request that failed", async () => {
    const responses: Array<Awaited<ReturnType<PatternsFn>>> = [{ error: { title: "Boom" } }, { data: result }];
    const loader = new PatternsLoader(
      async () => responses.shift()!,
      () => {},
    );

    loader.load("sample", request);
    await Promise.resolve();
    loader.retry();
    await Promise.resolve();

    expect(loader.state).toEqual({ status: "loaded", result });
  });
});
