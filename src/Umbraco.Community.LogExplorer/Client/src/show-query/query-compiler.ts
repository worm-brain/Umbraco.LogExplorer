import type { CompileResult, LogQuery } from "../api/index.js";
import type { FilterNode } from "../query/filter-node.js";
import { describeError } from "../shared/describe-error.js";

/**
 * The `POST /sources/{alias}/compile` call the compiler depends on, in hey-api's non-throwing
 * result shape. It may reject when `signal` aborts or the network fails.
 */
export type CompileFn = (
  alias: string,
  query: LogQuery,
  signal: AbortSignal,
) => Promise<{ data?: CompileResult; error?: unknown; response?: Response }>;

/**
 * The latest "Show query" translation.
 *
 * - `idle`: nothing to compile (no source, or the source has no native language).
 * - `loading`: a compile is pending; `native` and `unsupported` still hold the previous answer,
 *   so the panel and its not-shown list do not flicker while it runs.
 * - `loaded`: `native` is the compiled text (`null` for no filter at all).
 * - `error`: the last compile failed; `error` is a message safe to show as text, and nothing is
 *   reported unsupported.
 */
export interface CompileState {
  status: "idle" | "loading" | "loaded" | "error";
  native: string | null;
  /**
   * Nodes the source's language cannot express (`CompileResult.unsupported`), as the server
   * returned them. The source still runs them; the compiled text leaves them out.
   */
  unsupported: ReadonlyArray<FilterNode>;
  error?: string;
}

/** The state before anything is compiled. */
export const INITIAL_COMPILE_STATE: CompileState = { status: "idle", native: null, unsupported: [] };

/** Default wait before a compile is sent, so a burst of view-state changes costs one request. */
export const COMPILE_DEBOUNCE_MS = 150;

/**
 * Compiles the current query for the show-query panel and its not-shown list.
 *
 * Compiling is cheap (no store is touched), so it runs on every change that affects the output,
 * whether the panel is open or not, so the panel opens on a current answer and the chips can say
 * whether the query shows them. Requests are debounced by {@link COMPILE_DEBOUNCE_MS}, only one is
 * ever in flight (a newer one aborts it), and a query whose output cannot differ from the last
 * one (only the range, sort, page or cursor changed) is not sent again.
 */
export class QueryCompiler {
  #state: CompileState = INITIAL_COMPILE_STATE;
  #controller?: AbortController;
  #timer?: ReturnType<typeof setTimeout>;
  #key?: string;

  /**
   * @param compile - Performs the request.
   * @param onChange - Called with every new state.
   * @param debounceMs - Wait before sending; tests pass 0.
   */
  constructor(
    private readonly compile: CompileFn,
    private readonly onChange: (state: CompileState) => void,
    private readonly debounceMs = COMPILE_DEBOUNCE_MS,
  ) {}

  /** @returns The current state. */
  get state(): CompileState {
    return this.#state;
  }

  /**
   * Schedules a compile of `query` on `alias`, unless the last one already covers it.
   *
   * @param alias - The source.
   * @param query - The query as `/search` would get it.
   */
  load(alias: string, query: LogQuery): void {
    // The range, sort, page size and cursor never change the compiled text (the compilers leave
    // the range out, ADR 0013), so they are not part of the key.
    const key = JSON.stringify([alias, query.levels, query.filter, query.nativeQuery]);
    if (key === this.#key) return;
    this.#key = key;

    this.#cancel();
    this.#set({ ...this.#state, status: "loading", error: undefined });
    this.#timer = setTimeout(() => void this.#run(alias, query), this.debounceMs);
  }

  /** Cancels anything pending and returns to idle, for a source with no native language. */
  reset(): void {
    this.#cancel();
    this.#key = undefined;
    if (this.#state !== INITIAL_COMPILE_STATE) this.#set(INITIAL_COMPILE_STATE);
  }

  #cancel(): void {
    clearTimeout(this.#timer);
    this.#timer = undefined;
    this.#controller?.abort();
    this.#controller = undefined;
  }

  async #run(alias: string, query: LogQuery): Promise<void> {
    const controller = new AbortController();
    this.#controller = controller;
    try {
      const result = await this.compile(alias, query, controller.signal);
      if (controller !== this.#controller) return;
      this.#controller = undefined;
      if (result.error !== undefined || !result.data) {
        // Forget the key so the next change, even back to this query, tries again.
        this.#key = undefined;
        this.#set({
          status: "error",
          native: null,
          unsupported: [],
          error: describeError(result.error, result.response),
        });
      } else {
        this.#set({ status: "loaded", native: result.data.native ?? null, unsupported: result.data.unsupported });
      }
    } catch (error) {
      // An aborted request rejects here; it was superseded, so there is nothing to report.
      if (controller !== this.#controller) return;
      this.#controller = undefined;
      this.#key = undefined;
      this.#set({
        status: "error",
        native: null,
        unsupported: [],
        error: error instanceof Error ? error.message : String(error),
      });
    }
  }

  #set(state: CompileState): void {
    this.#state = state;
    this.onChange(state);
  }
}
