import { UmbContextBase } from "@umbraco-cms/backoffice/class-api";
import { UmbContextToken } from "@umbraco-cms/backoffice/context-api";
import type { UmbControllerHost } from "@umbraco-cms/backoffice/controller-api";
import { UmbBasicState, UmbObjectState, mergeObservables } from "@umbraco-cms/backoffice/observable-api";
import { NativeQueryService, type SettingsResponseModel, type SourceResponseModel } from "../api/index.js";
import { DEFAULT_CORRELATION_FIELDS } from "../entry-detail/same-request.js";
import { loadSettings } from "../settings/settings.js";
import { canShowQuery } from "../show-query/native-mode.js";
import {
  INITIAL_COMPILE_STATE,
  QueryCompiler,
  type CompileFn,
  type CompileState,
} from "../show-query/query-compiler.js";
import { findNotExpressibleChips, findUnsupportedChips, toQueryState } from "../show-query/unsupported-chips.js";
import { findSource, resolveActiveSource } from "../sources/active-source.js";
import { loadSources, type SourcesState } from "../sources/sources-loader.js";
import { LOG_EXPLORER_ENTITY_TYPE } from "../workspace/constants.js";
import { appendChips, removeChipAt, replaceChipAt } from "./chips.js";
import type { FilterNode } from "./filter-node.js";
import { toLogQuery } from "./log-query.js";
import {
  createDefaultViewState,
  decodeViewState,
  encodeViewState,
  hasViewState,
  mergeViewStateIntoSearch,
  type AbsoluteRange,
  type LogExplorerViewState,
  type ViewTimeRange,
} from "./view-state.js";

/** Options for {@link LogExplorerQueryContext}; tests replace the settings request. */
export interface LogExplorerQueryContextOptions {
  /** Supplies `DefaultTimeRange`; defaults to `GET /settings`. A failure keeps the built-in default. */
  loadDefaultTimeRange?: () => Promise<string | undefined>;
  /** Supplies `DefaultSource`; defaults to `GET /settings`. A failure means "no default". */
  loadDefaultSource?: () => Promise<string | undefined>;
  /** Supplies `CorrelationFields`; defaults to `GET /settings`. A failure keeps the BRIEF §13 defaults. */
  loadCorrelationFields?: () => Promise<ReadonlyArray<string> | undefined>;
  /** Fetches the visible sources; defaults to `GET /sources`. */
  loadSources?: () => Promise<SourcesState>;
  /** Compiles a query for "Show query"; defaults to `POST /sources/{alias}/compile`. */
  compile?: CompileFn;
  /** Wait before a compile is sent; tests pass 0. */
  compileDebounceMs?: number;
}

/** The default compile request: the generated client, which carries the backoffice token. */
const compileWithClient: CompileFn = (alias, query, signal) =>
  NativeQueryService.compile({ path: { alias }, body: query, signal });

/**
 * The explorer's shared view state (BRIEF §6.11, §11.2), two-way synced with the query string.
 *
 * - **State to URL:** every change made through the setters is written with `history.pushState`,
 *   so the back button steps through earlier views. Nothing is written when the encoded query
 *   already matches the URL, which is also what stops the URL listener and the writer looping.
 * - **URL to state:** the backoffice router's patched history fires a global `changestate` event
 *   on push, replace and popstate; the context decodes the URL on each one while its host is
 *   connected.
 * - **Tab switches:** the workspace tabs are plain links without a query string. When the route
 *   path changes and the new URL carries no view-state keys, the context keeps its state and
 *   writes it onto the new URL with `replaceState`, so changing tab never resets filters.
 *
 * Only URLs inside the Log Explorer workspace are read or written, because `changestate` also
 * fires while the user navigates away, before the workspace element disconnects.
 *
 * The context also loads the visible sources and `DefaultSource`, and resolves the
 * {@link activeSource} from them and the `src` in the view state (see `resolveActiveSource`).
 *
 * It also works out which chips the source cannot run ({@link unsupportedChips}, from its
 * declared operators) and compiles the query for "Show query" whenever it changes and the source
 * has a native language (see `QueryCompiler`); from that answer it finds the chips the source runs
 * but the compiled text cannot show ({@link notExpressibleChips}). Queries read
 * {@link queryState}, which leaves out only the chips the source cannot run (ADR 0016).
 *
 * Provided by `log-explorer-workspace`; consume it with {@link LOG_EXPLORER_QUERY_CONTEXT}.
 */
export class LogExplorerQueryContext extends UmbContextBase {
  #defaults = createDefaultViewState();
  #state = new UmbObjectState<LogExplorerViewState>(this.#defaults);
  #lastPath: string | undefined;
  #sources = new UmbObjectState<SourcesState>({ status: "loading" });
  #loadSources: () => Promise<SourcesState>;
  /** `undefined` until `/settings` has answered; `null` when it answered with no usable default. */
  #defaultSource = new UmbBasicState<string | null | undefined>(undefined);
  #compiled = new UmbObjectState<CompileState>(INITIAL_COMPILE_STATE);
  #compiler: QueryCompiler;
  #correlationFields = new UmbBasicState<ReadonlyArray<string>>(DEFAULT_CORRELATION_FIELDS);

  /** The whole view state. */
  readonly state = this.#state.asObservable();

  /** The time range alone, for controls that only care about it. */
  readonly range = this.#state.asObservablePart((state) => state.range);

  /** The histogram's time zoom alone, for the time chip; `undefined` when not zoomed. */
  readonly zoom = this.#state.asObservablePart((state) => state.zoom);

  /** The `src` alias in the view state; `undefined` means "use the default". Not validated. */
  readonly source = this.#state.asObservablePart((state) => state.source);

  /** The outcome of the visible-sources fetch, for the source picker's loading and error states. */
  readonly sources = this.#sources.asObservable();

  /**
   * The source queries should run against, resolved from `src`, `DefaultSource` and the visible
   * sources. `undefined` while either request is pending, and when no source is visible; it only
   * emits when the resolved source changes.
   */
  readonly activeSource = mergeObservables(
    [this.#sources.asObservable(), this.#defaultSource.asObservable(), this.source],
    ([sources, defaultSource, requested]) => this.#resolve(sources, defaultSource, requested),
    // Return true when unchanged: the comparator feeds rxjs `distinctUntilChanged`, whatever the
    // mergeObservables TSDoc says ("true when different").
    (previous, current) => previous?.alias === current?.alias,
  );

  /** The latest "Show query" translation of the view state for the active source. */
  readonly compiled = this.#compiled.asObservable();

  /**
   * `CorrelationFields` (BRIEF §13) in priority order, for the drawer's Same request: the BRIEF
   * defaults until `/settings` answers, and when it fails or answers with an empty list.
   */
  readonly correlationFields = this.#correlationFields.asObservable();

  /**
   * Positions (in {@link LogExplorerViewState.chips}) of the chips the active source cannot run,
   * because it does not declare their operator: the search box shows them disabled, and
   * {@link queryState} leaves them out (BRIEF §6.3).
   */
  readonly unsupportedChips = mergeObservables([this.state, this.activeSource], ([state, source]) =>
    findUnsupportedChips(state.chips, source),
  );

  /**
   * Positions of the chips the active source runs but the latest compile could not express. They
   * stay active and in {@link queryState}; the show-query panel lists them as not shown.
   */
  readonly notExpressibleChips = mergeObservables(
    [this.state, this.unsupportedChips, this.compiled],
    ([state, unsupported, compiled]) => findNotExpressibleChips(state.chips, compiled.unsupported, unsupported),
  );

  /**
   * The view state as queries should run it: {@link unsupportedChips} removed, and the native
   * query removed when the active source does not allow native mode. Panels that query the
   * source observe this rather than {@link state}.
   */
  readonly queryState = mergeObservables(
    [this.state, this.activeSource, this.unsupportedChips],
    ([state, source, unsupported]) => toQueryState(state, source, unsupported),
  );

  /**
   * @param host - The workspace element that provides the context.
   * @param options - Optional overrides; see {@link LogExplorerQueryContextOptions}.
   */
  constructor(host: UmbControllerHost, options: LogExplorerQueryContextOptions = {}) {
    super(host, LOG_EXPLORER_QUERY_CONTEXT);
    // Both defaults come from one /settings response, so share the request between them.
    let settingsRequest: Promise<SettingsResponseModel | undefined> | undefined;
    const settings = () => (settingsRequest ??= loadSettings());
    const loadDefaultTimeRange = options.loadDefaultTimeRange ?? (async () => (await settings())?.defaultTimeRange);
    const loadDefaultSource = options.loadDefaultSource ?? (async () => (await settings())?.defaultSource);
    const loadCorrelationFields = options.loadCorrelationFields ?? (async () => (await settings())?.correlationFields);
    this.#loadSources = options.loadSources ?? (() => loadSources());
    this.#compiler = new QueryCompiler(
      options.compile ?? compileWithClient,
      (compiled) => this.#compiled.setValue(compiled),
      options.compileDebounceMs,
    );
    this.observe(
      mergeObservables([this.state, this.activeSource], ([state, source]) => ({ state, source })),
      ({ state, source }) => this.#compile(state, source),
      "_observeCompile",
    );

    void loadDefaultTimeRange().then((range) => this.#applyDefaults(createDefaultViewState(range)));
    void loadDefaultSource().then(
      (alias) => this.#defaultSource.setValue(alias || null),
      () => this.#defaultSource.setValue(null),
    );
    void loadCorrelationFields().then(
      (fields) => {
        if (fields?.length) this.#correlationFields.setValue(fields);
      },
      () => {},
    );
    void this.reloadSources();
  }

  /** Starts listening to the URL and reads the current one. */
  override hostConnected(): void {
    super.hostConnected();
    window.addEventListener("changestate", this.#onChangeState);
    this.#onChangeState();
  }

  /** Stops listening to the URL. */
  override hostDisconnected(): void {
    super.hostDisconnected();
    window.removeEventListener("changestate", this.#onChangeState);
  }

  /** @returns The current view state. */
  getState(): LogExplorerViewState {
    return this.#state.getValue();
  }

  /** @returns The defaults currently in force (the built-in ones until `/settings` answers). */
  getDefaults(): LogExplorerViewState {
    return this.#defaults;
  }

  /**
   * Sets the time range and records it in the URL. Clears any time zoom, because a zoom is a
   * window inside the old range (UI brief §4.2).
   *
   * @param range - The new range.
   */
  setRange(range: ViewTimeRange): void {
    this.update({ range, zoom: undefined });
  }

  /**
   * Zooms queries into an absolute window, or clears the zoom, and records it in the URL. The
   * picker's range is kept, so clearing the zoom returns to it.
   *
   * @param zoom - The window; `undefined` clears the zoom.
   */
  setZoom(zoom: AbsoluteRange | undefined): void {
    this.update({ zoom });
  }

  /**
   * Selects a source by alias and records it in the URL as `src`. The alias is not checked here:
   * one that is not visible resolves to the default, as it would from a hand-edited URL.
   *
   * @param alias - The source alias; `undefined` returns to the configured default.
   */
  setSource(alias: string | undefined): void {
    this.update({ source: alias });
  }

  /**
   * Appends filter chips, skipping any already present (see `appendChips`), and records the
   * result in the URL. Does nothing when every chip is a duplicate.
   *
   * @param chips - The chips to add, in order.
   */
  addChips(chips: ReadonlyArray<FilterNode>): void {
    const current = this.getState().chips;
    const next = appendChips(current, chips);
    if (next !== current) this.update({ chips: next });
  }

  /**
   * Replaces one chip in place, as the chip editor does on Save (see `replaceChipAt` for what
   * happens when the result duplicates another chip).
   *
   * @param index - Position of the chip in {@link LogExplorerViewState.chips}.
   * @param chip - The replacement.
   */
  replaceChip(index: number, chip: FilterNode): void {
    this.update({ chips: replaceChipAt(this.getState().chips, index, chip) });
  }

  /**
   * Removes one chip.
   *
   * @param index - Position of the chip in {@link LogExplorerViewState.chips}.
   */
  removeChip(index: number): void {
    this.update({ chips: removeChipAt(this.getState().chips, index) });
  }

  /**
   * Same request (BRIEF §6.8, UI brief §4.11): replaces every filter (chips and the native query),
   * the level set and the time zoom with the one correlation chip, and leaves Around-this mode, so
   * the results list shows every entry of that request in the picker's range. Source, range and
   * sort are kept.
   *
   * @param chip - The correlation chip, from `sameRequestTarget`.
   */
  showSameRequest(chip: FilterNode): void {
    this.update({ chips: [chip], native: undefined, levels: null, zoom: undefined, around: undefined });
  }

  /**
   * Switches the results list to Around-this mode for one entry (UI brief §4.10). Filters, levels
   * and zoom are kept, so {@link clearAround} returns to exactly the filtered list.
   *
   * @param recordId - `LogRecord.id` of the anchor.
   */
  showAround(recordId: string): void {
    this.update({ around: recordId });
  }

  /** Leaves Around-this mode ("Back to filtered results"); the rest of the state is unchanged. */
  clearAround(): void {
    this.update({ around: undefined });
  }

  /** @returns The latest compile; see {@link compiled}. */
  getCompiled(): CompileState {
    return this.#compiled.getValue();
  }

  /** @returns The active source right now; see {@link activeSource}. */
  getActiveSource(): SourceResponseModel | undefined {
    return this.#resolve(this.#sources.getValue(), this.#defaultSource.getValue(), this.getState().source);
  }

  /**
   * Fetches the visible sources again, for the source picker's Retry button. Never rejects; a
   * failure lands in {@link sources} as an `error` state.
   */
  async reloadSources(): Promise<void> {
    this.#sources.setValue({ status: "loading" });
    this.#sources.setValue(await this.#loadSources());
  }

  /**
   * Changes part of the view state and records the result in the URL.
   *
   * @param partial - The fields to change.
   */
  update(partial: Partial<LogExplorerViewState>): void {
    this.#state.update(partial);
    this.#writeUrl("push");
  }

  /**
   * Compiles the query exactly as {@link queryState} runs it (chips the source cannot run and a
   * native query it does not allow left out), so the panel never shows a clause that does not run.
   */
  #compile(state: LogExplorerViewState, source: SourceResponseModel | undefined): void {
    if (!source || !canShowQuery(source)) {
      this.#compiler.reset();
      return;
    }
    this.#compiler.load(
      source.alias,
      toLogQuery(toQueryState(state, source, findUnsupportedChips(state.chips, source)), 1),
    );
  }

  /** Arrow function so it can be added and removed as a listener with the same identity. */
  #onChangeState = (): void => {
    if (!isInWorkspace(window.location.pathname)) return;

    const params = new URLSearchParams(window.location.search);
    const pathChanged = this.#lastPath !== undefined && this.#lastPath !== window.location.pathname;
    this.#lastPath = window.location.pathname;

    if (pathChanged && !hasViewState(params)) {
      this.#writeUrl("replace");
      return;
    }
    this.#state.setValue(decodeViewState(params, this.#defaults));
  };

  /**
   * Switches to new defaults. The URL omits fields equal to the defaults, so the state is
   * re-read from the URL against the new ones: an untouched range follows `DefaultTimeRange`.
   */
  #applyDefaults(defaults: LogExplorerViewState): void {
    this.#defaults = defaults;
    if (isInWorkspace(window.location.pathname)) {
      this.#state.setValue(decodeViewState(new URLSearchParams(window.location.search), defaults));
    }
  }

  #resolve(
    sources: SourcesState,
    defaultSource: string | null | undefined,
    requested: string | undefined,
  ): SourceResponseModel | undefined {
    if (sources.status !== "loaded") return undefined;
    // Without a visible `src`, wait for DefaultSource rather than briefly showing the first source.
    if (defaultSource === undefined && !findSource(sources.sources, requested)) return undefined;
    return resolveActiveSource(sources.sources, requested, defaultSource ?? undefined);
  }

  #writeUrl(mode: "push" | "replace"): void {
    if (!isInWorkspace(window.location.pathname)) return;
    const search = mergeViewStateIntoSearch(
      window.location.search,
      encodeViewState(this.#state.getValue(), this.#defaults),
    );
    if (search === window.location.search.replace(/^\?/, "")) return;

    const url = `${window.location.pathname}${search ? `?${search}` : ""}${window.location.hash}`;
    if (mode === "push") window.history.pushState(window.history.state, "", url);
    else window.history.replaceState(window.history.state, "", url);
  }
}

/** Whether a path is inside the Log Explorer workspace (`.../workspace/log-explorer[/...]`). */
function isInWorkspace(pathname: string): boolean {
  return new RegExp(`/workspace/${LOG_EXPLORER_ENTITY_TYPE}(/|$)`).test(pathname);
}

/** Token for {@link LogExplorerQueryContext}. */
export const LOG_EXPLORER_QUERY_CONTEXT = new UmbContextToken<LogExplorerQueryContext>("LogExplorerQueryContext");
