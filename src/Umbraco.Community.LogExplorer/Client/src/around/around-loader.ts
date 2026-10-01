import type { ContextResult, LogRecord } from "../api/index.js";
import type { LogExplorerViewState } from "../query/view-state.js";
import { describeError } from "../shared/describe-error.js";

/** Entries either side of the anchor; the copy deck's "Showing 7 entries either side" (UI brief §4.10). */
export const AROUND_COUNT = 7;

/**
 * What the results panel renders in Around-this mode.
 *
 * - `idle`: not in Around-this mode.
 * - `loading`: the context request for {@link anchorId} is in flight.
 * - `loaded`: `rows` holds the anchor and its neighbours in the panel's sort order.
 * - `error`: the request failed (for example a stale id from an old link); `error` is safe to
 *   show as text.
 */
export interface AroundState {
  status: "idle" | "loading" | "loaded" | "error";
  /** The record whose neighbours are shown; set in every status but `idle`. */
  anchorId?: string;
  /** The anchor as the server returned it, once loaded, for the banner's time. */
  anchor?: LogRecord;
  rows: ReadonlyArray<LogRecord>;
  error?: string;
}

/** The state outside Around-this mode. */
export const INITIAL_AROUND_STATE: AroundState = { status: "idle", rows: [] };

/**
 * The `GET /sources/{alias}/records/{id}/context` call the loader depends on, in hey-api's
 * non-throwing result shape. It may reject when `signal` aborts or the network fails.
 */
export type ContextFn = (
  alias: string,
  id: string,
  count: number,
  signal: AbortSignal,
) => Promise<{ data?: ContextResult; error?: unknown; response?: Response }>;

/**
 * Orders a context response as the results list shows it: the same direction as the normal list
 * (newest first unless the user chose oldest first), so switching into Around this keeps the
 * reading direction and the anchor sits in the middle.
 *
 * @param result - The context response; `before` and `after` are each oldest first.
 * @param sort - The view state's sort.
 * @returns Before, anchor and after as one list, reversed for `desc`.
 */
export function aroundRows(result: ContextResult, sort: LogExplorerViewState["sort"]): Array<LogRecord> {
  const oldestFirst = [...result.before, result.anchor, ...result.after];
  return sort === "asc" ? oldestFirst : oldestFirst.reverse();
}

/**
 * Runs the Around-this request for the results panel and keeps its {@link AroundState}. Only
 * one request is in flight: a new anchor aborts the previous one, and a superseded response is
 * dropped even if it arrives.
 */
export class AroundLoader {
  #state: AroundState = INITIAL_AROUND_STATE;
  #controller?: AbortController;
  #alias?: string;
  #result?: ContextResult;
  #sort: LogExplorerViewState["sort"] = "desc";

  /**
   * @param fetchContext - Performs the request.
   * @param onChange - Called with every new state.
   */
  constructor(
    private readonly fetchContext: ContextFn,
    private readonly onChange: (state: AroundState) => void,
  ) {}

  /** @returns The current state. */
  get state(): AroundState {
    return this.#state;
  }

  /**
   * Loads the neighbours of a record, unless that record on that source is already loaded or
   * loading, in which case only the sort is applied.
   *
   * @param alias - The source.
   * @param anchorId - `LogRecord.id` of the anchor.
   * @param sort - The order to show the rows in.
   */
  load(alias: string, anchorId: string, sort: LogExplorerViewState["sort"]): void {
    this.#sort = sort;
    if (alias === this.#alias && anchorId === this.#state.anchorId && this.#state.status !== "error") {
      if (this.#result) this.#set({ ...this.#state, rows: aroundRows(this.#result, sort) });
      return;
    }
    this.#alias = alias;
    this.#result = undefined;
    void this.#run(anchorId);
  }

  /** Repeats a failed request. */
  retry(): void {
    if (this.#state.status === "error" && this.#state.anchorId) void this.#run(this.#state.anchorId);
  }

  /** Aborts any request in flight and leaves Around-this mode; the state returns to idle. */
  reset(): void {
    this.#controller?.abort();
    this.#controller = undefined;
    this.#alias = undefined;
    this.#result = undefined;
    if (this.#state.status !== "idle") this.#set(INITIAL_AROUND_STATE);
  }

  async #run(anchorId: string): Promise<void> {
    if (this.#alias === undefined) return;
    this.#controller?.abort();
    const controller = new AbortController();
    this.#controller = controller;
    this.#set({ status: "loading", anchorId, rows: [] });

    try {
      const result = await this.fetchContext(this.#alias, anchorId, AROUND_COUNT, controller.signal);
      if (controller !== this.#controller) return;
      if (result.error !== undefined || !result.data) {
        this.#set({ status: "error", anchorId, rows: [], error: describeError(result.error, result.response) });
        return;
      }
      this.#result = result.data;
      this.#set({ status: "loaded", anchorId, anchor: result.data.anchor, rows: aroundRows(result.data, this.#sort) });
    } catch (error) {
      // An aborted request rejects here; it was superseded, so there is nothing to report.
      if (controller !== this.#controller) return;
      this.#set({ status: "error", anchorId, rows: [], error: error instanceof Error ? error.message : String(error) });
    }
  }

  #set(state: AroundState): void {
    this.#state = state;
    this.onChange(state);
  }
}
