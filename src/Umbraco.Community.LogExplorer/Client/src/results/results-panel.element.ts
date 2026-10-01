import {
  classMap,
  css,
  customElement,
  html,
  nothing,
  query,
  state,
  styleMap,
  type PropertyValues,
} from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import { SearchService, type LogRecord } from "../api/index.js";
import { toLogQuery } from "../query/log-query.js";
import { LOG_EXPLORER_QUERY_CONTEXT, type LogExplorerQueryContext } from "../query/query.context.js";
import type { LogExplorerViewState } from "../query/view-state.js";
import { LEVEL_TOKENS } from "../shared/level-tokens.js";
import { tokeniseMessage } from "./message-tokens.js";
import {
  INITIAL_RESULTS_STATE,
  RESULTS_PAGE_SIZE,
  ResultsLoader,
  type ResultsState,
  type SearchFn,
} from "./results-loader.js";
import { formatRowTime, formatShowing, levelOf, shortenSource } from "./row-format.js";
import { rowWindow, type RowWindow } from "./virtual-window.js";

/** How close (in rows) to the end of the loaded rows scrolling must get to fetch the next page. */
const LOAD_MORE_THRESHOLD = 15;

/** Rows rendered beyond each edge of the viewport. */
const OVERSCAN = 8;

/** Used until the first row has been measured. */
const ESTIMATED_ROW_HEIGHT = 48;

/** The default request: the generated client, which carries the backoffice token. */
const searchWithClient: SearchFn = (alias, query, signal) =>
  SearchService.search({ path: { alias }, body: query, signal });

/**
 * Fired when the user activates a results row; the entry detail drawer (#41) listens for it.
 * Bubbles and is composed, so it crosses the Search view's shadow root.
 */
export class LogExplorerEntryOpenEvent extends Event {
  /** The event type. */
  static readonly TYPE = "log-explorer-entry-open";

  /**
   * @param record - The record behind the row.
   */
  constructor(readonly record: LogRecord) {
    super(LogExplorerEntryOpenEvent.TYPE, { bubbles: true, composed: true });
  }
}

/**
 * The results list of the Search view (UI brief §4.9, BRIEF §6.7): the latest entries for the
 * view state in `LogExplorerQueryContext`, newest first by default.
 *
 * - **Querying:** whenever the active source or anything in the view state that changes the
 *   query changes, it requests the first page again (60 rows) and aborts the request in flight.
 *   Scrolling near the end fetches the next page by cursor; the footer's "Load 60 more" does
 *   the same, for keyboard users and as a fallback.
 * - **Rows:** each row is one native `<button>` (time, level badge, message with highlighted
 *   values, short source). A native button rather than `uui-button`, whose content box centres
 *   and pads its slot for an action button and cannot lay out a four-column row; `uui-table-row`
 *   was ruled out because the list is virtualised and a table cannot be.
 * - **Virtualised** by a small fixed-row-height window (see `virtual-window.ts`): only the rows
 *   in view, plus a few either side, exist in the DOM, rendered with a plain `map` so scrolling
 *   updates the same row elements in place. Every row is two lines tall (the message clamp), so
 *   one measured height positions them all. The list element is its own scroller, which is why
 *   it is a `div` and not `uui-scroll-container`.
 *
 *   Do not switch to `@lit-labs/virtualizer` or a keyed `repeat` here: Lit's `removePart` leaves
 *   a comment node behind for every row scrolled past, and scrolling slows down as they pile up
 *   (ADR 0015).
 * - **States** (UI brief §4.15): a `uui-loader-bar` while a request runs, with the previous rows
 *   dimmed during a first-page load; the copy-deck empty text; an error banner with the
 *   ProblemDetails message and Retry.
 *
 * Log text is rendered as text nodes only.
 *
 * Not bound to a manifest; the Search view renders it.
 *
 * @element log-explorer-results
 * @fires log-explorer-entry-open - {@link LogExplorerEntryOpenEvent}, when a row is activated.
 */
@customElement("log-explorer-results")
export class LogExplorerResultsElement extends UmbLitElement {
  /** The loader's state; every change re-renders the list, footer and banners. */
  @state()
  private _results: ResultsState = INITIAL_RESULTS_STATE;

  /** The sort in force, for the Time header's arrow. */
  @state()
  private _sort: LogExplorerViewState["sort"] = "desc";

  /** Id of the row last opened, highlighted until another is opened. */
  @state()
  private _selectedId: string | undefined;

  /** The rows currently rendered; recomputed on scroll, resize and new results. */
  @state()
  private _window: RowWindow = { first: 0, end: 0 };

  @query(".list")
  private _list?: HTMLElement;

  #loader = new ResultsLoader(searchWithClient, (results) => {
    // A new first page starts at the top; otherwise the list would keep the old scroll offset
    // and, if that was near the end, immediately fetch the next page of the new query.
    if (results.status === "loaded" && this._results.status === "loading" && !this._results.appending) {
      this.#scrollToTop = true;
    }
    this._results = results;
  });
  #scrollToTop = false;
  #rowHeight = ESTIMATED_ROW_HEIGHT;
  #observedList?: HTMLElement;
  #resizeObserver = new ResizeObserver(() => this.#updateWindow());
  #context?: LogExplorerQueryContext;
  #viewState?: LogExplorerViewState;
  #alias?: string;
  #queryKey?: string;

  constructor() {
    super();
    this.consumeContext(LOG_EXPLORER_QUERY_CONTEXT, (context) => {
      this.#context = context;
      this.observe(
        context?.queryState,
        (viewState) => {
          this.#viewState = viewState;
          this._sort = viewState?.sort ?? "desc";
          this.#requery();
        },
        "_observeState",
      );
      this.observe(
        context?.activeSource,
        (source) => {
          this.#alias = source?.alias;
          this.#requery();
        },
        "_observeActiveSource",
      );
    });
  }

  protected override updated(changed: PropertyValues): void {
    super.updated(changed);
    const list = this._list;

    // The list element comes and goes with the empty and error states; watch whichever exists
    // so a resized workspace re-renders the right number of rows.
    if (list !== this.#observedList) {
      if (this.#observedList) this.#resizeObserver.unobserve(this.#observedList);
      if (list) this.#resizeObserver.observe(list);
      this.#observedList = list;
    }
    if (!list) return;

    // Every row has the same height (two clamped lines); measure it once a row exists, and
    // again if the font size changes it.
    const row = list.querySelector<HTMLElement>(".row");
    if (row && row.offsetHeight > 0 && row.offsetHeight !== this.#rowHeight) {
      this.#rowHeight = row.offsetHeight;
      this.#updateWindow();
    }
    if (this.#scrollToTop) {
      this.#scrollToTop = false;
      list.scrollTop = 0;
    }
    if (changed.has("_results")) this.#updateWindow();
  }

  override disconnectedCallback(): void {
    super.disconnectedCallback();
    this.#resizeObserver.disconnect();
    this.#observedList = undefined;
    // Nothing should land on a view the user has left; reconnecting queries again (the
    // context observers re-emit on reconnect).
    this.#loader.reset();
    this.#queryKey = undefined;
  }

  /**
   * Starts a new first page when the query actually changed. The view state also carries fields
   * that do not affect the query (none yet, but the drawer and panels will add some), so the
   * comparison is on the request itself rather than on every state emission.
   */
  #requery(): void {
    if (!this.#alias || !this.#viewState) {
      this.#loader.reset();
      this.#queryKey = undefined;
      return;
    }

    const query = toLogQuery(this.#viewState, RESULTS_PAGE_SIZE);
    const key = JSON.stringify([this.#alias, query]);
    if (key === this.#queryKey) return;
    this.#queryKey = key;
    this.#loader.load(this.#alias, query);
  }

  #toggleSort(): void {
    this.#context?.update({ sort: this._sort === "desc" ? "asc" : "desc" });
  }

  /**
   * Re-renders only when the window actually moves (most scroll events stay inside the
   * overscan), and asks for the next page once the window nears the last loaded row.
   */
  #updateWindow = (): void => {
    const list = this._list;
    const count = this._results.records.length;
    if (!list) return;

    const next = rowWindow(list.scrollTop, list.clientHeight, this.#rowHeight, count, OVERSCAN);
    if (next.first !== this._window.first || next.end !== this._window.end) this._window = next;
    if (count > 0 && next.end >= count - LOAD_MORE_THRESHOLD) this.#loader.loadMore();
  };

  #open(record: LogRecord): void {
    this._selectedId = record.id;
    this.dispatchEvent(new LogExplorerEntryOpenEvent(record));
  }

  #renderRow(record: LogRecord, index: number) {
    const time = formatRowTime(record.timestamp);
    const level = levelOf(record.severityNumber);
    const levelText = level ? level.toUpperCase() : "—";
    return html`
      <button
        type="button"
        class=${classMap({ row: true, selected: record.id === this._selectedId })}
        style=${styleMap({ transform: `translateY(${index * this.#rowHeight}px)` })}
        aria-label=${this.localize.term("logExplorer_resultsOpenEntry", time, levelText)}
        @click=${() => this.#open(record)}
      >
        <span class="time">${time}</span>
        <span class="level"><uui-tag class="level-${level ?? "none"}">${levelText}</uui-tag></span>
        <span class="message">${this.#renderMessage(record)}</span>
        <span class="source" title=${record.scope ?? ""}>${shortenSource(record.scope)}</span>
      </button>
    `;
  }

  #renderMessage(record: LogRecord) {
    return tokeniseMessage(record.body, record.messageTemplate, record.attributes).map((token) =>
      token.field ? html`<span class="value">${token.text}</span>` : token.text,
    );
  }

  #renderBody() {
    const { status, records, error, appending } = this._results;

    if (status === "error" && !appending) {
      return this.#renderError(error);
    }
    if (records.length === 0) {
      return status === "loaded"
        ? html`<p class="empty">${this.localize.term("logExplorer_resultsEmpty")}</p>`
        : nothing;
    }

    // Dim the old rows only while they are about to be replaced, not while a page is appended.
    const dimmed = status === "loading" && !appending;
    const { first, end } = this._window;
    // `map`, not `repeat`: Lit then reuses the rendered rows positionally and only updates
    // their bindings as the window slides, instead of creating and removing row parts.
    return html`
      <div
        class=${classMap({ list: true, dimmed })}
        aria-busy=${status === "loading" ? "true" : "false"}
        @scroll=${this.#updateWindow}
      >
        <div class="spacer" style=${styleMap({ height: `${records.length * this.#rowHeight}px` })}>
          ${records.slice(first, end).map((record, offset) => this.#renderRow(record, first + offset))}
        </div>
      </div>
    `;
  }

  #renderError(message: string | undefined) {
    return html`
      <div class="error" role="alert">
        <span>${this.localize.term("logExplorer_resultsError", message ?? "")}</span>
        <uui-button
          look="secondary"
          compact
          label=${this.localize.term("logExplorer_retry")}
          @click=${() => this.#loader.retry()}
        ></uui-button>
      </div>
    `;
  }

  #renderFooter() {
    const { status, records, nextCursor, totalCount, totalIsLowerBound, appending, error } = this._results;
    if (records.length === 0) return nothing;

    const showing = formatShowing(
      records.length,
      totalCount,
      totalIsLowerBound,
      nextCursor !== null,
      this.localize.lang(),
    );
    return html`
      <div class="footer">
        ${status === "error" && appending ? this.#renderError(error) : nothing}
        <span>${this.localize.term("logExplorer_resultsShowing", showing.shown, showing.total)}</span>
        ${
          nextCursor
            ? html`<uui-button
                look="outline"
                compact
                label=${this.localize.term("logExplorer_resultsLoadMore")}
                ?disabled=${status === "loading"}
                @click=${() => this.#loader.loadMore()}
              ></uui-button>`
            : nothing
        }
      </div>
    `;
  }

  /**
   * Renders the panel: header row, list (or empty/error state) and footer.
   *
   * @returns The template.
   */
  override render() {
    const loading = this._results.status === "loading";
    return html`
      <uui-box>
        <div class="panel">
          ${loading ? html`<uui-loader-bar class="loader"></uui-loader-bar>` : nothing}
          <div class="header" role="presentation">
            <uui-button
              class="sort"
              compact
              label=${this.localize.term(
                this._sort === "desc" ? "logExplorer_resultsSortNewestFirst" : "logExplorer_resultsSortOldestFirst",
              )}
              @click=${this.#toggleSort}
            >
              ${this.localize.term("logExplorer_columnTime")}
              <uui-symbol-sort active ?descending=${this._sort === "desc"}></uui-symbol-sort>
            </uui-button>
            <span>${this.localize.term("logExplorer_columnLevel")}</span>
            <span>${this.localize.term("logExplorer_columnMessage")}</span>
            <span>${this.localize.term("logExplorer_columnSource")}</span>
          </div>
          ${this.#renderBody()} ${this.#renderFooter()}
        </div>
      </uui-box>
    `;
  }

  static override styles = [
    UmbTextStyles,
    LEVEL_TOKENS,
    css`
      :host {
        display: flex;
        flex-direction: column;
        min-height: 0;
        /* Shared by the header and every row so the columns line up. */
        --log-explorer-results-columns: 12ch 10ch minmax(0, 1fr) 18ch;
      }

      /* uui-box has no parts, and its default slot is a plain block. As a one-row grid (the
         box renders no header: there is no headline) the slot is stretched to the box's
         height, which gives .panel a definite height to share between header, list and footer. */
      uui-box {
        flex: 1;
        min-height: 0;
        position: relative;
        display: grid;
        grid-template-rows: minmax(0, 1fr);
        --uui-box-default-padding: 0;
      }

      .panel {
        display: flex;
        flex-direction: column;
        height: 100%;
        min-height: 0;
      }

      .loader {
        position: absolute;
        inset: 0 0 auto 0;
      }

      .header,
      .row {
        display: grid;
        grid-template-columns: var(--log-explorer-results-columns);
        column-gap: var(--uui-size-space-4);
        align-items: start;
        padding: var(--uui-size-space-2) var(--uui-size-space-4);
      }

      .header {
        align-items: center;
        font-weight: bold;
        font-size: var(--uui-type-small-size);
        border-bottom: 1px solid var(--uui-color-border);
      }

      .sort {
        justify-self: start;
        --uui-button-font-weight: bold;
        --uui-button-padding-left-factor: 0;
      }

      .list {
        flex: 1;
        min-height: 0;
        overflow-y: auto;
        /* Keeps the scroll offset stable when rows re-render under the pointer. */
        overflow-anchor: none;
        transition: opacity 120ms ease-out;
      }

      .spacer {
        position: relative;
      }

      .list.dimmed {
        opacity: 0.5;
      }

      /* Rows are positioned by the window (translateY) and all share one height: two lines of
         message plus padding, whether or not the message needs the second line. */
      .row {
        position: absolute;
        top: 0;
        left: 0;
        width: 100%;
        height: calc(2lh + 2 * var(--uui-size-space-1) + 1px);
        padding-block: var(--uui-size-space-1);
        overflow: hidden;
        box-sizing: border-box;
        border: 0;
        border-bottom: 1px solid var(--uui-color-divider-standalone);
        /* The left bar marks the selected row; transparent keeps every row's text aligned. */
        border-left: var(--uui-size-1) solid transparent;
        background: none;
        color: var(--uui-color-text);
        font: inherit;
        font-size: var(--uui-type-small-size);
        /* A unitless, tight line height keeps two lines dense (UI brief §1.6, about half the
           core viewer's row height); the inherited backoffice line height is for prose. */
        line-height: 1.35;
        text-align: left;
        cursor: pointer;
      }

      .row:hover {
        background-color: var(--uui-color-surface-emphasis);
      }

      .row:focus-visible {
        outline: 2px solid var(--uui-color-focus);
        outline-offset: -2px;
      }

      .row.selected {
        background-color: var(--uui-color-current);
        border-left-color: var(--uui-color-interactive-emphasis);
      }

      .time {
        font-variant-numeric: tabular-nums;
        white-space: nowrap;
      }

      .message {
        display: -webkit-box;
        -webkit-box-orient: vertical;
        -webkit-line-clamp: 2;
        line-clamp: 2;
        overflow: hidden;
        overflow-wrap: anywhere;
      }

      .value {
        font-weight: bold;
        color: var(--uui-color-interactive-emphasis);
      }

      .source {
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        color: var(--uui-color-text-alt);
      }

      /* A badge one text line tall, so it never sets the row height. */
      uui-tag {
        font-size: var(--uui-type-small-size);
        line-height: inherit;
        padding-block: 0;
        border-color: transparent;
      }

      /* Badges carry the level name, never colour alone (UI brief §5.1). */
      .level-trace {
        background-color: var(--log-explorer-level-trace);
        color: var(--log-explorer-level-trace-contrast);
      }
      .level-debug {
        background-color: var(--log-explorer-level-debug);
        color: var(--log-explorer-level-debug-contrast);
      }
      .level-info {
        background-color: var(--log-explorer-level-info);
        color: var(--log-explorer-level-info-contrast);
      }
      .level-warn {
        background-color: var(--log-explorer-level-warn);
        color: var(--log-explorer-level-warn-contrast);
      }
      .level-error {
        background-color: var(--log-explorer-level-error);
        color: var(--log-explorer-level-error-contrast);
      }
      .level-fatal {
        background-color: var(--log-explorer-level-fatal);
        color: var(--log-explorer-level-fatal-contrast);
      }

      .empty {
        margin: 0;
        padding: var(--uui-size-space-5) var(--uui-size-space-4);
        color: var(--uui-color-text-alt);
      }

      .error {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: var(--uui-size-space-4);
        margin: var(--uui-size-space-4);
        padding: var(--uui-size-space-3) var(--uui-size-space-4);
        border: 1px solid var(--uui-color-danger-standalone);
        border-radius: var(--uui-border-radius);
        color: var(--uui-color-danger-standalone);
      }

      .footer {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        justify-content: flex-end;
        gap: var(--uui-size-space-4);
        padding: var(--uui-size-space-2) var(--uui-size-space-4);
        border-top: 1px solid var(--uui-color-border);
        font-size: var(--uui-type-small-size);
      }

      .footer .error {
        flex-basis: 100%;
        margin: 0;
      }
    `,
  ];
}

declare global {
  interface HTMLElementTagNameMap {
    "log-explorer-results": LogExplorerResultsElement;
  }
  interface HTMLElementEventMap {
    "log-explorer-entry-open": LogExplorerEntryOpenEvent;
  }
}
