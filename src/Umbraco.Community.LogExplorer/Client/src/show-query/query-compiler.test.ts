import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { CompileResult, LogQuery } from "../api/index.js";
import { COMPILE_DEBOUNCE_MS, QueryCompiler, type CompileFn, type CompileState } from "./query-compiler.js";

const base: LogQuery = {
  range: { relative: "1h" },
  levels: null,
  filter: { kind: "text", text: "timeout" },
  nativeQuery: null,
  take: 1,
  cursor: null,
  sort: "descending",
};

/** A compile stub whose answers the test releases one by one. */
function deferredCompile() {
  const calls: Array<{ query: LogQuery; signal: AbortSignal; resolve: (data: CompileResult) => void }> = [];
  const compile: CompileFn = (_, query, signal) =>
    new Promise((resolve) => calls.push({ query, signal, resolve: (data) => resolve({ data }) }));
  return { compile, calls };
}

let states: Array<CompileState>;

beforeEach(() => {
  vi.useFakeTimers();
  states = [];
});

afterEach(() => {
  vi.useRealTimers();
});

describe("QueryCompiler", () => {
  it("reports the compiled text once the debounce has passed and the answer arrived", async () => {
    const { compile, calls } = deferredCompile();
    const compiler = new QueryCompiler(compile, (state) => states.push(state));

    compiler.load("sample", base);
    await vi.advanceTimersByTimeAsync(COMPILE_DEBOUNCE_MS);
    calls[0]!.resolve({ native: 'text("timeout")', unsupported: [] });
    await vi.runAllTimersAsync();

    expect(compiler.state).toEqual({ status: "loaded", native: 'text("timeout")', unsupported: [] });
  });

  it("sends one request for a burst of changes inside the debounce", async () => {
    const { compile, calls } = deferredCompile();
    const compiler = new QueryCompiler(compile, (state) => states.push(state));

    compiler.load("sample", base);
    compiler.load("sample", { ...base, filter: { kind: "text", text: "slow" } });
    await vi.advanceTimersByTimeAsync(COMPILE_DEBOUNCE_MS);

    expect(calls.map((call) => call.query.filter)).toEqual([{ kind: "text", text: "slow" }]);
  });

  it("does not compile again when only the range, sort or page changed", async () => {
    const { compile, calls } = deferredCompile();
    const compiler = new QueryCompiler(compile, (state) => states.push(state));
    compiler.load("sample", base);
    await vi.advanceTimersByTimeAsync(COMPILE_DEBOUNCE_MS);

    compiler.load("sample", { ...base, range: { relative: "24h" }, sort: "ascending", take: 60 });
    await vi.advanceTimersByTimeAsync(COMPILE_DEBOUNCE_MS);

    expect(calls).toHaveLength(1);
  });

  it("aborts the request in flight when the query changes and drops its late answer", async () => {
    const { compile, calls } = deferredCompile();
    const compiler = new QueryCompiler(compile, (state) => states.push(state));
    compiler.load("sample", base);
    await vi.advanceTimersByTimeAsync(COMPILE_DEBOUNCE_MS);

    compiler.load("sample", { ...base, nativeQuery: "x" });
    calls[0]!.resolve({ native: "old", unsupported: [] });
    await vi.advanceTimersByTimeAsync(COMPILE_DEBOUNCE_MS);
    calls[1]!.resolve({ native: "new", unsupported: [] });
    await vi.runAllTimersAsync();

    expect([calls[0]!.signal.aborted, compiler.state.native]).toEqual([true, "new"]);
  });

  it("reports a failed compile as an error with nothing unsupported", async () => {
    const compile: CompileFn = async () => ({ error: { detail: "No language." } });
    const compiler = new QueryCompiler(compile, (state) => states.push(state), 0);

    compiler.load("sample", base);
    await vi.runAllTimersAsync();

    expect(compiler.state).toEqual({ status: "error", native: null, unsupported: [], error: "No language." });
  });

  it("returns to idle on reset", async () => {
    const { compile } = deferredCompile();
    const compiler = new QueryCompiler(compile, (state) => states.push(state));
    compiler.load("sample", base);

    compiler.reset();

    expect(compiler.state.status).toBe("idle");
  });
});
