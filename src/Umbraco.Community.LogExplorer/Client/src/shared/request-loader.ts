import { describeError } from "./describe-error.js";

/**
 * What a panel fed by a {@link RequestLoader} renders.
 *
 * - `idle`: nothing requested (no active source, or it lacks the feature).
 * - `loading`: a request is in flight; `result` still holds the previous answer, which the panel
 *   dims (UI brief §4.15).
 * - `loaded`: `result` answers the current request.
 * - `error`: the last request failed; `error` is a message safe to show as text, and `result`
 *   keeps the last answer that did load.
 */
export interface RequestState<TResult> {
  status: "idle" | "loading" | "loaded" | "error";
  result?: TResult;
  error?: string;
}

/**
 * A package API call in hey-api's non-throwing result shape. It may reject when `signal` aborts
 * or the network fails.
 */
export type RequestFn<TRequest, TResult> = (
  alias: string,
  request: TRequest,
  signal: AbortSignal,
) => Promise<{ data?: TResult; error?: unknown; response?: Response }>;

/**
 * Runs one kind of request against a source and keeps its {@link RequestState}: the
 * histogram and patterns loaders' behaviour for any endpoint, so a view with several
 * small panels does not need one loader class per call.
 *
 * Only one request is ever in flight: a new load aborts the previous one (BRIEF §11.1, §17), and
 * a response whose request was superseded is dropped even if it arrives.
 */
export class RequestLoader<TRequest, TResult> {
  #state: RequestState<TResult> = { status: "idle" };
  #controller?: AbortController;
  #alias?: string;
  #request?: TRequest;

  /**
   * @param fetch - Performs the request.
   * @param onChange - Called with every new state.
   */
  constructor(
    private readonly fetch: RequestFn<TRequest, TResult>,
    private readonly onChange: (state: RequestState<TResult>) => void,
  ) {}

  /** @returns The current state. */
  get state(): RequestState<TResult> {
    return this.#state;
  }

  /**
   * Sends a request, aborting any request in flight.
   *
   * @param alias - The source to ask.
   * @param request - The request; endpoints without a body take `undefined`.
   */
  load(alias: string, request: TRequest): void {
    this.#alias = alias;
    this.#request = request;
    void this.#run();
  }

  /** Repeats the request that failed. */
  retry(): void {
    if (this.#state.status === "error") void this.#run();
  }

  /** Aborts any request in flight and returns to idle, dropping the result. */
  reset(): void {
    this.#controller?.abort();
    this.#controller = undefined;
    this.#alias = undefined;
    this.#request = undefined;
    this.#set({ status: "idle" });
  }

  async #run(): Promise<void> {
    if (this.#alias === undefined) return;

    this.#controller?.abort();
    const controller = new AbortController();
    this.#controller = controller;
    this.#set({ status: "loading", result: this.#state.result });

    try {
      const response = await this.fetch(this.#alias, this.#request as TRequest, controller.signal);
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

  #set(state: RequestState<TResult>): void {
    this.#state = state;
    this.onChange(state);
  }
}
