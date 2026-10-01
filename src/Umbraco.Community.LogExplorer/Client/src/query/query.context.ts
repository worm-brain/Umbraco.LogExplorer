import { UmbContextBase } from "@umbraco-cms/backoffice/class-api";
import { UmbContextToken } from "@umbraco-cms/backoffice/context-api";
import type { UmbControllerHost } from "@umbraco-cms/backoffice/controller-api";
import { UmbBasicState, UmbObjectState, mergeObservables } from "@umbraco-cms/backoffice/observable-api";
import type { SettingsResponseModel, SourceResponseModel } from "../api/index.js";
import { loadSettings } from "../settings/settings.js";
import { findSource, resolveActiveSource } from "../sources/active-source.js";
import { loadSources, type SourcesState } from "../sources/sources-loader.js";
import { LOG_EXPLORER_ENTITY_TYPE } from "../workspace/constants.js";
import {
  createDefaultViewState,
  decodeViewState,
  encodeViewState,
  hasViewState,
  mergeViewStateIntoSearch,
  type LogExplorerViewState,
  type ViewTimeRange,
} from "./view-state.js";

/** Options for {@link LogExplorerQueryContext}; tests replace the settings request. */
export interface LogExplorerQueryContextOptions {
  /** Supplies `DefaultTimeRange`; defaults to `GET /settings`. A failure keeps the built-in default. */
  loadDefaultTimeRange?: () => Promise<string | undefined>;
  /** Supplies `DefaultSource`; defaults to `GET /settings`. A failure means "no default". */
  loadDefaultSource?: () => Promise<string | undefined>;
  /** Fetches the visible sources; defaults to `GET /sources`. */
  loadSources?: () => Promise<SourcesState>;
}

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

  /** The whole view state. */
  readonly state = this.#state.asObservable();

  /** The time range alone, for controls that only care about it. */
  readonly range = this.#state.asObservablePart((state) => state.range);

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
    this.#loadSources = options.loadSources ?? (() => loadSources());

    void loadDefaultTimeRange().then((range) => this.#applyDefaults(createDefaultViewState(range)));
    void loadDefaultSource().then(
      (alias) => this.#defaultSource.setValue(alias || null),
      () => this.#defaultSource.setValue(null),
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
   * Sets the time range and records it in the URL.
   *
   * @param range - The new range.
   */
  setRange(range: ViewTimeRange): void {
    this.update({ range });
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
