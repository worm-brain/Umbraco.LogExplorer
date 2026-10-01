import type { HistogramRequest, HistogramResult } from "../api/index.js";
import { describeError } from "../shared/describe-error.js";

/**
 * What the histogram panel renders.
 *
 * - `idle`: nothing requested (no active source, or it has no histogram).
 * - `loading`: a request is in flight; `result` still holds the previous bars, which the panel
 *   dims (UI brief §4.15).
 * - `loaded`: `result` is the histogram for the current query.
 * - `error`: the last request failed; `error` is a message safe to show as text, and `result`
 *   keeps the last bars that did load.
 */
export interface HistogramState {
  status: "idle" | "loading" | "loaded" | "error";
  result?: HistogramResult;
  error?: string;
}

/**
 * The `POST /sources/{alias}/histogram` call the loader depends on, in hey-api's non-throwing
 * result shape. It may reject when `signal` aborts or the network fails.
 */
export type HistogramFn = (
  alias: string,
  request: HistogramRequest,
  signal: AbortSignal,
) => Promise<{ data?: HistogramResult; error?: unknown; response?: Response }>;

/**
 * Runs histogram requests for the histogram panel and keeps their {@link HistogramState}.
 *
 * Only one request is ever in flight: a new load aborts the previous one (BRIEF §11.1, §17), and
 * a response whose request was superseded is dropped even if it arrives.
 */
export class HistogramLoader {
  #state: HistogramState = { status: "idle" };
  #controller?: AbortController;
  #alias?: string;
  #request?: HistogramRequest;

  /**
   * @param fetchHistogram - Performs the request.
   * @param onChange - Called with every new state.
   */
  constructor(
    private readonly fetchHistogram: HistogramFn,
    private readonly onChange: (state: HistogramState) => void,
  ) {}

  /** @returns The current state. */
  get state(): HistogramState {
    return this.#state;
  }

  /**
   * Requests a histogram, aborting any request in flight.
   *
   * @param alias - The source to count.
   * @param request - The query and bucket target.
   */
  load(alias: string, request: HistogramRequest): void {
    this.#alias = alias;
    this.#request = request;
    void this.#run();
  }

  /** Repeats the request that failed. */
  retry(): void {
    if (this.#state.status === "error") void this.#run();
  }

  /** Aborts any request in flight and returns to idle, dropping the bars. */
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
      const response = await this.fetchHistogram(this.#alias, this.#request, controller.signal);
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

  #set(state: HistogramState): void {
    this.#state = state;
    this.onChange(state);
  }
}
