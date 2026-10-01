import { describe, expect, it } from "vitest";
import { RequestLoader, type RequestFn } from "./request-loader.js";

type Fn = RequestFn<{ top: number }, { value: string }>;

/** A request the test resolves by hand, so it controls which response lands first. */
function deferred() {
  let resolve!: (value: Awaited<ReturnType<Fn>>) => void;
  const promise = new Promise<Awaited<ReturnType<Fn>>>((r) => (resolve = r));
  return { promise, resolve };
}

describe("RequestLoader", () => {
  it("loads the result", async () => {
    const loader = new RequestLoader<{ top: number }, { value: string }>(
      async () => ({ data: { value: "a" } }),
      () => {},
    );

    loader.load("sample", { top: 6 });
    await Promise.resolve();

    expect(loader.state).toEqual({ status: "loaded", result: { value: "a" } });
  });

  it("reports the ProblemDetails detail when the request fails", async () => {
    const loader = new RequestLoader<{ top: number }, { value: string }>(
      async () => ({ error: { detail: "Source 'seq' is a Seq source." } }),
      () => {},
    );

    loader.load("seq", { top: 6 });
    await Promise.resolve();

    expect(loader.state).toMatchObject({ status: "error", error: "Source 'seq' is a Seq source." });
  });

  it("aborts the request in flight when a new one starts", () => {
    const signals: Array<AbortSignal> = [];
    const loader = new RequestLoader<{ top: number }, { value: string }>(
      (_alias, _request, signal) => {
        signals.push(signal);
        return new Promise(() => {});
      },
      () => {},
    );

    loader.load("sample", { top: 6 });
    loader.load("sample", { top: 10 });

    expect(signals[0]?.aborted).toBe(true);
  });

  it("drops a superseded response that arrives after the newer one", async () => {
    const first = deferred();
    const calls = [first.promise, Promise.resolve({ data: { value: "new" } })];
    const loader = new RequestLoader<{ top: number }, { value: string }>(
      () => calls.shift()!,
      () => {},
    );

    loader.load("sample", { top: 6 });
    loader.load("sample", { top: 10 });
    await Promise.resolve();
    first.resolve({ data: { value: "old" } });
    await Promise.resolve();

    expect(loader.state.result).toEqual({ value: "new" });
  });

  it("retries the request that failed", async () => {
    const calls: Array<Awaited<ReturnType<Fn>>> = [{ error: { detail: "Busy" } }, { data: { value: "a" } }];
    const loader = new RequestLoader<{ top: number }, { value: string }>(
      async () => calls.shift()!,
      () => {},
    );

    loader.load("sample", { top: 6 });
    await Promise.resolve();
    loader.retry();
    await Promise.resolve();

    expect(loader.state).toEqual({ status: "loaded", result: { value: "a" } });
  });

  it("returns to idle on reset", async () => {
    const loader = new RequestLoader<{ top: number }, { value: string }>(
      async () => ({ data: { value: "a" } }),
      () => {},
    );

    loader.load("sample", { top: 6 });
    await Promise.resolve();
    loader.reset();

    expect(loader.state).toEqual({ status: "idle" });
  });
});
