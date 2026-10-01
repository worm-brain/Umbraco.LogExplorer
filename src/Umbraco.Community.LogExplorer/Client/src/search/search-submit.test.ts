import { describe, expect, it } from "vitest";
import type { ParseResult } from "../api/index.js";
import type { FilterNode } from "../query/filter-node.js";
import { createDefaultViewState, type LogExplorerViewState } from "../query/view-state.js";
import { mergeParseResult, SearchSubmitter, UNBALANCED_QUOTE, type ParseFn } from "./search-submit.js";

const pathApi: FilterNode = {
  kind: "condition",
  field: "RequestPath",
  op: "startsWith",
  value: "/api",
  caseInsensitive: true,
};
const timeout: FilterNode = { kind: "text", text: "timeout", phrase: false };

/** Stands in for the query context: holds a state and records each update. */
function target(initial: Partial<LogExplorerViewState> = {}) {
  let state: LogExplorerViewState = { ...createDefaultViewState(), ...initial };
  const updates: Array<Partial<LogExplorerViewState>> = [];
  return {
    updates,
    getState: () => state,
    update: (partial: Partial<LogExplorerViewState>) => {
      updates.push(partial);
      state = { ...state, ...partial };
    },
  };
}

const answer =
  (data: ParseResult): ParseFn =>
  async () => ({ data });

describe("mergeParseResult", () => {
  it("appends the chips and sets the levels", () => {
    const state = { ...createDefaultViewState(), chips: [timeout] };

    expect(mergeParseResult(state, { chips: [pathApi], levels: ["error", "fatal"] })).toEqual({
      chips: [timeout, pathApi],
      levels: ["error", "fatal"],
    });
  });

  it("skips chips that are already there and leaves levels alone without a level set", () => {
    const state = { ...createDefaultViewState(), chips: [pathApi], levels: ["warn" as const] };

    expect(mergeParseResult(state, { chips: [{ ...pathApi }], levels: null })).toEqual({});
  });
});

describe("SearchSubmitter", () => {
  it("applies chips and levels in one update", async () => {
    const context = target();
    const submitter = new SearchSubmitter(answer({ chips: [pathApi], levels: ["error", "fatal"] }), context);

    const outcome = await submitter.submit("level:error path:/api*");

    expect([outcome, context.updates]).toEqual([
      { status: "applied" },
      [{ chips: [pathApi], levels: ["error", "fatal"] }],
    ]);
  });

  it("applies the text chip and reports the fallback for an unbalanced quote", async () => {
    const context = target();
    const fallback = { code: UNBALANCED_QUOTE, message: "Unbalanced quote, so this was searched as plain text" };
    const submitter = new SearchSubmitter(
      answer({ chips: [{ kind: "text", text: "connection" }], levels: null, fallback }),
      context,
    );

    const outcome = await submitter.submit('"connection');

    expect([outcome, context.getState().chips]).toEqual([
      { status: "applied", fallback },
      [{ kind: "text", text: "connection" }],
    ]);
  });

  it("writes nothing when every chip is a duplicate", async () => {
    const context = target({ chips: [timeout] });
    const submitter = new SearchSubmitter(answer({ chips: [timeout], levels: null }), context);

    await submitter.submit("timeout");

    expect(context.updates).toEqual([]);
  });

  it("reports the ProblemDetails detail of a failed parse and writes nothing", async () => {
    const context = target();
    const submitter = new SearchSubmitter(
      async () => ({
        error: { detail: "The search input is too long." },
        response: new Response(null, { status: 400 }),
      }),
      context,
    );

    const outcome = await submitter.submit("x");

    expect([outcome, context.updates]).toEqual([{ status: "error", message: "The search input is too long." }, []]);
  });

  it("does not send a blank input", async () => {
    let calls = 0;
    const submitter = new SearchSubmitter(async () => {
      calls++;
      return { data: { chips: [] } };
    }, target());

    const outcome = await submitter.submit("   ");

    expect([outcome, calls]).toEqual([{ status: "empty" }, 0]);
  });

  it("aborts the parse in flight when a new one starts, and drops its answer", async () => {
    const context = target();
    const signals: Array<AbortSignal> = [];
    let releaseFirst!: () => void;
    const parse: ParseFn = (input, signal) => {
      signals.push(signal);
      if (input === "first") {
        // Resolves late, as a slow request would, even though it was aborted.
        return new Promise((resolve) => (releaseFirst = () => resolve({ data: { chips: [pathApi] } })));
      }
      return Promise.resolve({ data: { chips: [timeout] } });
    };
    const submitter = new SearchSubmitter(parse, context);

    const first = submitter.submit("first");
    const second = await submitter.submit("timeout");
    releaseFirst();

    expect([signals[0]!.aborted, await first, second, context.getState().chips]).toEqual([
      true,
      { status: "superseded" },
      { status: "applied" },
      [timeout],
    ]);
  });
});
