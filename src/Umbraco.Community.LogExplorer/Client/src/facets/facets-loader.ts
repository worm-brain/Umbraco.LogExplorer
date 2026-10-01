import type { FacetResult, FacetsRequest, FieldInfo } from "../api/index.js";
import type { LogExplorerViewState } from "../query/view-state.js";
import { describeError } from "../shared/describe-error.js";
import { buildFacetsRequest, fieldsRange, selectDiscoveredFields } from "./facets-model.js";

/**
 * What the fields panel renders.
 *
 * - `idle`: nothing requested (no active source, or it has no facets).
 * - `loading`: requests are in flight; `result` still holds the previous facets, which the panel
 *   dims (UI brief §4.15).
 * - `loaded`: `result` is the facets for the current query.
 * - `error`: the facets request failed; `error` is a message safe to show as text, and `result`
 *   keeps the last facets that did load.
 */
export interface FacetsState {
  status: "idle" | "loading" | "loaded" | "error";
  result?: FacetResult;
  error?: string;
}

/** A hey-api non-throwing result; the call may still reject on abort or a network failure. */
type ApiResult<T> = Promise<{ data?: T; error?: unknown; response?: Response }>;

/** `POST /sources/{alias}/facets`. */
export type FacetsFn = (alias: string, request: FacetsRequest, signal: AbortSignal) => ApiResult<FacetResult>;

/** `GET /sources/{alias}/fields`. */
export type FieldsFn = (
  alias: string,
  range: ReturnType<typeof fieldsRange>,
  signal: AbortSignal,
) => ApiResult<Array<FieldInfo>>;

/** What one load needs besides the source alias. */
export interface FacetsLoadInput {
  /** The view state the facets count. */
  state: LogExplorerViewState;
  /** Pinned fields, in configured order. */
  pinned: ReadonlyArray<string>;
  /** Whether the source declares field discovery; without it only pinned fields are faceted. */
  discover: boolean;
}

/**
 * Runs the fields panel's requests and keeps their {@link FacetsState}.
 *
 * A load first finds the discovered fields for the source and range (`GET /fields`), then counts
 * pinned plus discovered fields in one `POST /facets`. Discovered fields are cached per source
 * and range for the loader's lifetime, because they describe the range rather than the filters:
 * adding a chip refetches only the facets. A failed `/fields` call is not fatal (the pinned fields
 * are still worth showing) and is not cached, so the next load tries again.
 *
 * Only one load is ever in flight: a new one aborts the previous one (BRIEF §11.1, §17), and a
 * response whose load was superseded is dropped even if it arrives.
 */
export class FacetsLoader {
  #state: FacetsState = { status: "idle" };
  #controller?: AbortController;
  #alias?: string;
  #input?: FacetsLoadInput;
  #fieldsCache = new Map<string, Array<FieldInfo>>();

  /**
   * @param fetchFacets - Performs `POST /facets`.
   * @param fetchFields - Performs `GET /fields`.
   * @param onChange - Called with every new state.
   */
  constructor(
    private readonly fetchFacets: FacetsFn,
    private readonly fetchFields: FieldsFn,
    private readonly onChange: (state: FacetsState) => void,
  ) {}

  /** @returns The current state. */
  get state(): FacetsState {
    return this.#state;
  }

  /**
   * Loads facets for the source and view state, aborting any load in flight.
   *
   * @param alias - The source.
   * @param input - The view state, pinned fields and whether to discover more.
   */
  load(alias: string, input: FacetsLoadInput): void {
    this.#alias = alias;
    this.#input = input;
    void this.#run();
  }

  /** Repeats the load that failed. */
  retry(): void {
    if (this.#state.status === "error") void this.#run();
  }

  /** Aborts any load in flight and returns to idle, dropping the facets (the field cache stays). */
  reset(): void {
    this.#controller?.abort();
    this.#controller = undefined;
    this.#alias = undefined;
    this.#input = undefined;
    this.#set({ status: "idle" });
  }

  async #run(): Promise<void> {
    const alias = this.#alias;
    const input = this.#input;
    if (alias === undefined || input === undefined) return;

    this.#controller?.abort();
    const controller = new AbortController();
    this.#controller = controller;
    this.#set({ status: "loading", result: this.#state.result });

    try {
      const discovered = input.discover ? await this.#discover(alias, input.state, controller.signal) : [];
      if (controller !== this.#controller) return;

      const request = buildFacetsRequest(input.state, input.pinned, selectDiscoveredFields(discovered, input.pinned));
      const response = await this.fetchFacets(alias, request, controller.signal);
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

  /** The discovered fields for the range, from the cache or `GET /fields`; `[]` when that fails. */
  async #discover(alias: string, state: LogExplorerViewState, signal: AbortSignal): Promise<Array<FieldInfo>> {
    const range = fieldsRange(state);
    const key = JSON.stringify([alias, range]);
    const cached = this.#fieldsCache.get(key);
    if (cached) return cached;

    try {
      const response = await this.fetchFields(alias, range, signal);
      if (response.error !== undefined || !response.data) return [];
      this.#fieldsCache.set(key, response.data);
      return response.data;
    } catch (error) {
      // Let an abort propagate so #run drops this load; any other failure only loses discovery.
      if (signal.aborted) throw error;
      return [];
    }
  }

  #fail(message: string): void {
    this.#set({ status: "error", result: this.#state.result, error: message });
  }

  #set(state: FacetsState): void {
    this.#state = state;
    this.onChange(state);
  }
}
