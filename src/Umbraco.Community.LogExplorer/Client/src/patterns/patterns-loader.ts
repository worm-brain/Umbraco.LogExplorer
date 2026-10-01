import type { PatternResult, PatternsRequest } from "../api/index.js";
import { describeError } from "../shared/describe-error.js";

/**
 * What the patterns list renders.
 *
 * - `idle`: nothing requested (no active source, or it has no patterns).
 * - `loading`: a request is in flight; `result` still holds the previous patterns, which the
 *   list dims (UI brief §4.15).
 * - `loaded`: `result` is the patterns for the current query.
 * - `error`: the last request failed; `error` is a message safe to show as text, and `result`
 *   keeps the last patterns that did load.
 */
export interface PatternsState {
  status: "idle" | "loading" | "loaded" | "error";
  result?: PatternResult;
  error?: string;
}

/**
 * The `POST /sources/{alias}/patterns` call the loader depends on, in hey-api's non-throwing
 * result shape. It may reject when `signal` aborts or the network fails.
 */
export type PatternsFn = (
  alias: string,
  request: PatternsRequest,
  signal: AbortSignal,
) => Promise<{ data?: PatternResult; error?: unknown; response?: Response }>;

/**
 * Runs patterns requests for the Patterns view and keeps their {@link PatternsState}.
 *
 * Only one request is ever in flight: a new load aborts the previous one (BRIEF §11.1, §17), and
 * a response whose request was superseded is dropped even if it arrives.
 */
export class PatternsLoader {
  #state: PatternsState = { status: "idle" };
  #controller?: AbortController;
  #alias?: string;
  #request?: PatternsRequest;

  /**
   * @param fetchPatterns - Performs the request.
   * @param onChange - Called with every new state.
   */
  constructor(
    private readonly fetchPatterns: PatternsFn,
    private readonly onChange: (state: PatternsState) => void,
  ) {}

  /** @returns The current state. */
  get state(): PatternsState {
    return this.#state;
  }

  /**
   * Requests patterns, aborting any request in flight.
   *
   * @param alias - The source to group.
   * @param request - The query and pattern limit.
   */
  load(alias: string, request: PatternsRequest): void {
    this.#alias = alias;
    this.#request = request;
    void this.#run();
  }

  /** Repeats the request that failed. */
  retry(): void {
    if (this.#state.status === "error") void this.#run();
  }

  /** Aborts any request in flight and returns to idle, dropping the patterns. */
  reset(): void {
    this.#controller?.abort();
    this.#controller = undefined;
    this.#alias = undefined;
    this.#request = undefined;
    this.#set({ status: "idle" });
  }

  async #run(): Promise<void> {
    if (this.#alias === undefined || this.#request === undefined) return;

    this.#controller?.abort();
    const controller = new AbortController();
    this.#controller = controller;
    this.#set({ status: "loading", result: this.#state.result });

    try {
      const response = await this.fetchPatterns(this.#alias, this.#request, controller.signal);
      if (controller !== this.#controller) return;
      if (response.error !== undefined || !response.data) {
        this.#fail(describeError(response.error, response.response));
      } else {
        this.#set({ status: "loaded", result: response.data });
      }
    } catch (error) {
      // An aborted request rejects here; it was superseded, so there is nothing to report.
      if (controller !== this.#controller) return;
      this.#fail(error instanceof Error ? error.message : String(error));
    }
  }

  #fail(message: string): void {
    this.#set({ status: "error", result: this.#state.result, error: message });
  }

  #set(state: PatternsState): void {
    this.#state = state;
    this.onChange(state);
  }
}
