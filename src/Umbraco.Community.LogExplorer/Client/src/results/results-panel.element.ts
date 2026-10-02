import {
  classMap,
  css,
  customElement,
  html,
  nothing,
  property,
  query,
  state,
  styleMap,
  type PropertyValues,
} from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import { ContextService, SearchService, type LogRecord } from "../api/index.js";
import { AroundLoader, INITIAL_AROUND_STATE, type AroundState, type ContextFn } from "../around/around-loader.js";
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
import { nextRowIndex, rowWindow, scrollTopToReveal, type RowWindow } from "./virtual-window.js";
import { formatLocale } from "../shared/format-locale.js";

/** How close (in rows) to the end of the loaded rows scrolling must get to fetch the next page. */
const LOAD_MORE_THRESHOLD = 15;

/** Rows rendered beyond each edge of the viewport. */
const OVERSCAN = 8;

/** Used until the first row has been measured. */
const ESTIMATED_ROW_HEIGHT = 48;

/** The default request: the generated client, which carries the backoffice token. */
const searchWithClient: SearchFn = (alias, query, signal) =>
  SearchService.search({ path: { alias }, body: query, signal });

/** The default Around-this request, through the generated client. */
const contextWithClient: ContextFn = (alias, id, count, signal) =>
  ContextService.getContext({ path: { alias, id }, query: { before: count, after: count }, signal });

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
 * - **Around this** (UI brief §4.10): while the view state has `around`, the list shows the
 *   anchor and the entries either side of it from `GET /records/{id}/context`, ignoring every
 *   filter, in the list's sort direction, with the anchor highlighted and a warning-tinted banner
 *   whose "Back to filtered results" clears `around`. The filtered page stays loaded underneath,
 *   so Back shows it again without a request unless the query changed meanwhile. A stale id (an
 *   old link) shows the error banner; Back still works.
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

  /**
   * Id of the entry open in the detail drawer; its row shows the selected state (accent tint and
   * left bar). Set by the Search view, which owns the open entry; `undefined` selects nothing.
   */
  @property({ attribute: false })
  selectedId: string | undefined;

  /** The Around-this loader's state; `idle` outside Around-this mode. */
  @state()
  private _around: AroundState = INITIAL_AROUND_STATE;

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
  #aroundLoader = new AroundLoader(contextWithClient, (around) => {
    // Bring the anchor into view once its neighbours arrive (UI brief §4.10).
    if (around.status === "loaded" && this._around.status === "loading") this.#revealAnchor = true;
    this._around = around;
  });
  #scrollToTop = false;
  #revealAnchor = false;
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
    if (this.#revealAnchor) {
      this.#revealAnchor = false;
      const index = this._around.rows.findIndex((record) => record.id === this._around.anchorId);
      // Centre the anchor so the entries either side of it are both in view.
      if (index !== -1) list.scrollTop = Math.max(0, (index + 0.5) * this.#rowHeight - list.clientHeight / 2);
    }
    if (changed.has("_results") || changed.has("_around")) this.#updateWindow();
  }

  override disconnectedCallback(): void {
    super.disconnectedCallback();
    this.#resizeObserver.disconnect();
    this.#observedList = undefined;
    // Nothing should land on a view the user has left; reconnecting queries again (the
    // context observers re-emit on reconnect).
    this.#loader.reset();
    this.#aroundLoader.reset();
    this.#queryKey = undefined;
  }

  /** Whether the list shows Around-this rows rather than the filtered page. */
  get #inAround(): boolean {
    return this._around.status !== "idle";
  }

  /** The rows the list shows: the Around-this rows in that mode, otherwise the loaded pages. */
  get #rows(): ReadonlyArray<LogRecord> {
    return this.#inAround ? this._around.rows : this._results.records;
  }

  /**
   * Starts a new first page when the query actually changed. The view state also carries fields
   * that do not affect the query (none yet, but the drawer and panels will add some), so the
   * comparison is on the request itself rather than on every state emission.
   */
  #requery(): void {
    if (!this.#alias || !this.#viewState) {
      this.#loader.reset();
      this.#aroundLoader.reset();
      this.#queryKey = undefined;
      return;
    }

    // Around this replaces the list but not the filtered query: it is left as it is (not reset)
    // so Back shows it again, and a query changed meanwhile loads when Around this ends.
    const around = this.#viewState.around;
    if (around) {
      this.#aroundLoader.load(this.#alias, around, this.#viewState.sort);
      return;
    }
    this.#aroundLoader.reset();

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
    const count = this.#rows.length;
    if (!list) return;

    const next = rowWindow(list.scrollTop, list.clientHeight, this.#rowHeight, count, OVERSCAN);
    if (next.first !== this._window.first || next.end !== this._window.end) this._window = next;
    if (!this.#inAround && count > 0 && next.end >= count - LOAD_MORE_THRESHOLD) this.#loader.loadMore();
  };

  #open(record: LogRecord): void {
    this.dispatchEvent(new LogExplorerEntryOpenEvent(record));
  }

  /**
   * Focuses the row of an entry, for returning focus when the drawer closes. Rows are reused
   * positionally as the window slides (ADR 0015), so the element that was clicked may now show
   * another entry or be gone: the row is found by id, scrolled into view first when it is outside
   * the window, and focused once the window has re-rendered.
   *
   * @param id - `LogRecord.id`.
   * @returns `true` when the row was focused; `false` when the entry is no longer loaded (for
   *   example after the query changed).
   */
  async focusRow(id: string): Promise<boolean> {
    const index = this.#rows.findIndex((record) => record.id === id);
    const list = this._list;
    if (index === -1 || !list) return false;

    list.scrollTop = scrollTopToReveal(index, this.#rowHeight, list.scrollTop, list.clientHeight);
    this.#updateWindow();
    await this.updateComplete;
    const row = [...list.querySelectorAll<HTMLElement>(".row")].find((element) => element.dataset.id === id);
    // preventScroll: the row is already in view, and the list must not jump to align it.
    row?.focus({ preventScroll: true });
    return row !== undefined;
  }

  /**
   * Moves focus one row down or up, for the `j`/`k` shortcuts (BRIEF §6.15). Works across the
   * whole loaded list, not only the rendered window: the target is found by index and revealed
   * through {@link focusRow}. With no row focused, the first press focuses the open entry's row,
   * else the first fully visible row, without stepping. Nearing the end of the loaded rows
   * fetches the next page as scrolling does, so `j` carries on once it arrives.
   *
   * @param step - `1` for down, `-1` for up.
   * @returns `true` when a row was focused; `false` at either end or with no rows.
   */
  async moveRowFocus(step: 1 | -1): Promise<boolean> {
    // The rows on screen: the Around-this rows in that mode.
    const records = this.#rows;
    const list = this._list;
    if (!list) return false;

    // The focused row's data-id names the entry it shows now; rows are reused positionally
    // (ADR 0015), so the element itself says nothing about the index.
    const focused = this.shadowRoot?.activeElement;
    const focusedId =
      focused instanceof HTMLElement && focused.classList.contains("row") ? focused.dataset.id : undefined;
    const current = focusedId === undefined ? -1 : records.findIndex((record) => record.id === focusedId);
    const selected = this.selectedId === undefined ? -1 : records.findIndex((record) => record.id === this.selectedId);
    const firstVisible = Math.ceil(list.scrollTop / this.#rowHeight);

    const target = nextRowIndex(
      current === -1 ? undefined : current,
      step,
      records.length,
      selected === -1 ? firstVisible : selected,
    );
    const record = target === undefined ? undefined : records[target];
    return record ? this.focusRow(record.id) : false;
  }

  #renderRow(record: LogRecord, index: number) {
    const time = formatRowTime(record.timestamp);
    const level = levelOf(record.severityNumber);
    const levelText = level ? level.toUpperCase() : "—";
    return html`
      <button
        type="button"
        class=${classMap({
          row: true,
          selected: record.id === this.selectedId,
          anchor: this.#inAround && record.id === this._around.anchorId,
        })}
        data-id=${record.id}
        aria-current=${this.#inAround && record.id === this._around.anchorId ? "true" : "false"}
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
    if (this.#inAround) return this.#renderAroundBody();
    const { status, records, error, appending } = this._results;

    if (status === "error" && !appending) {
      return this.#renderError(this.localize.term("logExplorer_resultsError", error ?? ""), () => this.#loader.retry());
    }
    if (records.length === 0) {
      return status === "loaded"
        ? html`<p class="empty">${this.localize.term("logExplorer_resultsEmpty")}</p>`
        : nothing;
    }

    // Dim the old rows only while they are about to be replaced, not while a page is appended.
    return this.#renderList(records, status === "loading" && !appending, status === "loading");
  }

  /** The Around-this rows, or its error banner (a stale id), with no empty state: the anchor is always a row. */
  #renderAroundBody() {
    const { status, rows, error } = this._around;
    if (status === "error") {
      return this.#renderError(this.localize.term("logExplorer_aroundError", error ?? ""), () =>
        this.#aroundLoader.retry(),
      );
    }
    return rows.length === 0 ? nothing : this.#renderList(rows, false, status === "loading");
  }

  #renderList(records: ReadonlyArray<LogRecord>, dimmed: boolean, busy: boolean) {
    const { first, end } = this._window;
    // `map`, not `repeat`: Lit then reuses the rendered rows positionally and only updates
    // their bindings as the window slides, instead of creating and removing row parts.
    return html`
      <div class=${classMap({ list: true, dimmed })} aria-busy=${busy ? "true" : "false"} @scroll=${this.#updateWindow}>
        <div class="spacer" style=${styleMap({ height: `${records.length * this.#rowHeight}px` })}>
          ${records.slice(first, end).map((record, offset) => this.#renderRow(record, first + offset))}
        </div>
      </div>
    `;
  }

  #renderError(message: string, retry: () => void) {
    return html`
      <div class="error" role="alert">
        <span>${message}</span>
        <uui-button
          look="secondary"
          compact
          label=${this.localize.term("logExplorer_retry")}
          @click=${retry}
        ></uui-button>
      </div>
    `;
  }

  /**
   * The Around-this banner (UI brief §4.10): the anchor's time once it has loaded, and the way
   * back to the filtered list in every state, including a failed load.
   *
   * Back comes before the text, not after it: the entry drawer that offered Around this is
   * usually still open over the right of the results panel, and would cover a trailing button.
   */
  #renderAroundBanner() {
    const { anchor } = this._around;
    return html`
      <div class="around-banner">
        <uui-icon name="icon-navigation-vertical"></uui-icon>
        <uui-button
          look="secondary"
          compact
          label=${this.localize.term("logExplorer_aroundBack")}
          @click=${() => this.#context?.clearAround()}
        ></uui-button>
        <span class="around-text" role="status">
          ${
            anchor
              ? this.localize.term("logExplorer_aroundBanner", formatRowTime(anchor.timestamp))
              : this.localize.term("logExplorer_aroundBannerNoAnchor")
          }
        </span>
      </div>
    `;
  }

  #renderFooter() {
    const { status, records, nextCursor, totalCount, totalIsLowerBound, appending, error } = this._results;
    // Around this has no pages and no total; the banner says what is shown.
    if (this.#inAround || records.length === 0) return nothing;

    const showing = formatShowing(
      records.length,
      totalCount,
      totalIsLowerBound,
      nextCursor !== null,
      formatLocale(this.localize.lang()),
    );
    return html`
      <div class="footer">
        ${
          status === "error" && appending
            ? this.#renderError(this.localize.term("logExplorer_resultsError", error ?? ""), () => this.#loader.retry())
            : nothing
        }
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
    const loading = this.#inAround ? this._around.status === "loading" : this._results.status === "loading";
    return html`
      <uui-box>
        <div class="panel">
          ${loading ? html`<uui-loader-bar class="loader"></uui-loader-bar>` : nothing}
          ${this.#inAround ? this.#renderAroundBanner() : nothing}
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
              <!-- uui-symbol-sort draws "descending" as an up chevron (the table convention of
                   "values grow upwards"); newest first must read as the brief's "Time ↓", so the
                   attribute is set for oldest first instead. -->
              <uui-symbol-sort active ?descending=${this._sort === "asc"}></uui-symbol-sort>
            </uui-button>
            <span>${this.localize.term("logExplorer_columnLevel")}</span>
            <span>${this.localize.term("logExplorer_columnMessage")}</span>
            <span class="source-heading">${this.localize.term("logExplorer_columnSource")}</span>
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

      /* No block padding: the sort button is already a full control height (UI brief §2's
         rows at 1440 x 900 need the space). */
      .header {
        padding-block: 0;
        align-items: center;
        font-weight: bold;
        font-size: var(--uui-type-default-size);
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
        /* The row is two lines tall even when the message needs one: align-content centres the
           grid's single row track in that height, align-items centres the cells within it. */
        align-content: center;
        align-items: center;
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
        font-size: var(--uui-type-default-size);
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

      /* The accent tint of UI brief §4.9 (prototype #e8ebfa). --uui-color-current is the pink of
         the active tab and tree item, so the tint is mixed from the accent instead. */
      .row.selected {
        background-color: color-mix(in srgb, var(--uui-color-interactive-emphasis) 12%, var(--uui-color-surface));
        border-left-color: var(--uui-color-interactive-emphasis);
      }

      /* The "around" anchor's warning tint (UI brief §4.9, prototype #fff7d6), mixed from the
         warning colour like the accent tint above. Selected and anchor together keep the accent
         bar on the left. */
      .row.anchor {
        background-color: color-mix(in srgb, var(--uui-color-warning) 25%, var(--uui-color-surface));
      }

      .around-banner {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-3);
        padding: var(--uui-size-space-2) var(--uui-size-space-4);
        border-bottom: 1px solid var(--uui-color-warning-standalone);
        background-color: color-mix(in srgb, var(--uui-color-warning) 25%, var(--uui-color-surface));
        color: var(--uui-color-text);
        flex-wrap: wrap;
        font-size: var(--uui-type-small-size);
      }

      .around-text {
        flex: 1;
        min-width: 0;
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
        /* One control height, like the header, whether or not "Load 60 more" is showing. */
        min-height: var(--uui-size-11);
        /* Room around the count and "Load 60 more" so the footer does not feel cramped. */
        padding: var(--uui-size-space-3) var(--uui-size-space-5);
        border-top: 1px solid var(--uui-color-border);
        font-size: var(--uui-type-small-size);
      }

      .footer .error {
        flex-basis: 100%;
        margin: 0;
      }

      /* Medium and narrow workspaces (under about 1100 px) drop the Source column to give the
         message the width (UI brief §2.1). The nearest size container is the Search view, whose
         content box is its padding (2 x 24 px) narrower than the workspace, hence 1051 px. */
      @container (max-width: 1051px) {
        :host {
          --log-explorer-results-columns: 12ch 10ch minmax(0, 1fr);
        }
        .source,
        .source-heading {
          display: none;
        }
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
