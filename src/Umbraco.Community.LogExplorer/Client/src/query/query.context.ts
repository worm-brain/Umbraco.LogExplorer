import { UmbContextBase } from "@umbraco-cms/backoffice/class-api";
import { UmbContextToken } from "@umbraco-cms/backoffice/context-api";
import type { UmbControllerHost } from "@umbraco-cms/backoffice/controller-api";
import { UmbObjectState } from "@umbraco-cms/backoffice/observable-api";
import { loadSettings } from "../settings/settings.js";
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
 * Provided by `log-explorer-workspace`; consume it with {@link LOG_EXPLORER_QUERY_CONTEXT}.
 */
export class LogExplorerQueryContext extends UmbContextBase {
  #defaults = createDefaultViewState();
  #state = new UmbObjectState<LogExplorerViewState>(this.#defaults);
  #lastPath: string | undefined;

  /** The whole view state. */
  readonly state = this.#state.asObservable();

  /** The time range alone, for controls that only care about it. */
  readonly range = this.#state.asObservablePart((state) => state.range);

  /**
   * @param host - The workspace element that provides the context.
   * @param options - Optional overrides; see {@link LogExplorerQueryContextOptions}.
   */
  constructor(host: UmbControllerHost, options: LogExplorerQueryContextOptions = {}) {
    super(host, LOG_EXPLORER_QUERY_CONTEXT);
    const loadDefaultTimeRange = options.loadDefaultTimeRange ?? (async () => (await loadSettings())?.defaultTimeRange);
    void loadDefaultTimeRange().then((range) => this.#applyDefaults(createDefaultViewState(range)));
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
