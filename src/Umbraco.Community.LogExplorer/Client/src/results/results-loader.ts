import type { LogPage, LogQuery, LogRecord } from "../api/index.js";
import { describeError } from "../shared/describe-error.js";

/** Rows per request: the "Load 60 more" footer's 60 (UI brief §4.9). */
export const RESULTS_PAGE_SIZE = 60;

/**
 * What the results panel renders.
 *
 * - `idle`: nothing requested yet (no active source).
 * - `loading`: a request is in flight; `records` still holds the previous rows, which the panel
 *   dims while a first page loads (UI brief §4.15) and keeps as-is while a further page loads.
 * - `loaded`: `records` is everything fetched for the current query.
 * - `error`: the last request failed; `error` is a message safe to show as text.
 */
export interface ResultsState {
  status: "idle" | "loading" | "loaded" | "error";
  records: ReadonlyArray<LogRecord>;
  /** Pass back for the next page; `null` once the last page has arrived. */
  nextCursor: string | null;
  /** Matches across all pages; `null` when the source does not count cheaply. */
  totalCount: number | null;
  /** Whether `totalCount` is "at least" rather than exact. */
  totalIsLowerBound: boolean;
  /** Whether the in-flight (or failed) request was for a further page rather than a first one. */
  appending: boolean;
  error?: string;
}

/** The transitions of {@link ResultsState}. */
export type ResultsAction =
  | { type: "start"; append: boolean }
  | { type: "success"; page: LogPage; append: boolean }
  | { type: "failure"; message: string; append: boolean };

/** The state before anything is requested. */
export const INITIAL_RESULTS_STATE: ResultsState = {
  status: "idle",
  records: [],
  nextCursor: null,
  totalCount: null,
  totalIsLowerBound: false,
  appending: false,
};

/**
 * Applies one transition. A first page replaces the rows; a further page appends to them. A
 * failure keeps the rows already shown, so a failed "load more" does not blank the list.
 *
 * @param state - The current state.
 * @param action - What happened.
 * @returns The next state.
 */
export function resultsReducer(state: ResultsState, action: ResultsAction): ResultsState {
  switch (action.type) {
    case "start":
      return { ...state, status: "loading", appending: action.append, error: undefined };
    case "success":
      return {
        status: "loaded",
        records: action.append ? [...state.records, ...action.page.records] : action.page.records,
        nextCursor: action.page.nextCursor ?? null,
        totalCount: action.page.totalCount ?? null,
        totalIsLowerBound: action.page.totalIsLowerBound,
        appending: false,
      };
    case "failure":
      return { ...state, status: "error", appending: action.append, error: action.message };
  }
}

/**
 * The `POST /sources/{alias}/search` call the loader depends on, in hey-api's non-throwing result
 * shape. It may reject when `signal` aborts or the network fails.
 */
export type SearchFn = (
  alias: string,
  query: LogQuery,
  signal: AbortSignal,
) => Promise<{ data?: LogPage; error?: unknown; response?: Response }>;

/**
 * Runs result queries for the results panel and keeps their {@link ResultsState}.
 *
 * Only one request is ever in flight: starting a first page aborts whatever was running
 * (BRIEF §11.1, §17), and a response whose request was superseded is dropped even if it
 * arrives, so a slow old query can never overwrite a newer one.
 */
export class ResultsLoader {
  #state: ResultsState = INITIAL_RESULTS_STATE;
  #controller?: AbortController;
  #alias?: string;
  #query?: LogQuery;
  #lastAppend = false;

  /**
   * @param search - Performs the request.
   * @param onChange - Called with every new state.
   */
  constructor(
    private readonly search: SearchFn,
    private readonly onChange: (state: ResultsState) => void,
  ) {}

  /** @returns The current state. */
  get state(): ResultsState {
    return this.#state;
  }

  /**
   * Starts the first page of a new query, aborting any request in flight.
   *
   * @param alias - The source to search.
   * @param query - The query, without a cursor.
   */
  load(alias: string, query: LogQuery): void {
    this.#alias = alias;
    this.#query = { ...query, cursor: null };
    void this.#run(false);
  }

  /** Fetches the next page, unless a request is already running or the last page has arrived. */
  loadMore(): void {
    if (this.#state.status === "loading" || this.#state.status === "error" || !this.#state.nextCursor) return;
    void this.#run(true);
  }

  /** Repeats the request that failed. */
  retry(): void {
    if (this.#state.status === "error") void this.#run(this.#lastAppend);
  }

  /** Aborts any request in flight and forgets the query; the state returns to idle. */
  reset(): void {
    this.#controller?.abort();
    this.#controller = undefined;
    this.#alias = undefined;
    this.#query = undefined;
    this.#set(INITIAL_RESULTS_STATE);
  }

  async #run(append: boolean): Promise<void> {
    if (this.#alias === undefined || this.#query === undefined) return;

    this.#controller?.abort();
    const controller = new AbortController();
    this.#controller = controller;
    this.#lastAppend = append;

    const query: LogQuery = append ? { ...this.#query, cursor: this.#state.nextCursor } : this.#query;
    this.#apply({ type: "start", append });

    try {
      const result = await this.search(this.#alias, query, controller.signal);
      if (controller !== this.#controller) return;
      if (result.error !== undefined || !result.data) {
        this.#apply({ type: "failure", message: describeError(result.error, result.response), append });
      } else {
        this.#apply({ type: "success", page: result.data, append });
      }
    } catch (error) {
      // An aborted request rejects here; it was superseded, so there is nothing to report.
      if (controller !== this.#controller) return;
      this.#apply({ type: "failure", message: error instanceof Error ? error.message : String(error), append });
    }
  }

  #apply(action: ResultsAction): void {
    this.#set(resultsReducer(this.#state, action));
  }

  #set(state: ResultsState): void {
    this.#state = state;
    this.onChange(state);
  }
}
