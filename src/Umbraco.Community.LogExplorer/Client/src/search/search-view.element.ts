import { css, customElement, html, nothing, query, state } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import "../facets/fields-panel.element.js";
import type { LogRecord } from "../api/index.js";
import "../entry-detail/entry-detail.element.js";
import type { LogExplorerEntryDetailElement } from "../entry-detail/entry-detail.element.js";
import "../histogram/histogram-panel.element.js";
import "../histogram/zoom-chip.element.js";
import "../results/results-panel.element.js";
import type { LogExplorerEntryOpenEvent, LogExplorerResultsElement } from "../results/results-panel.element.js";
import "../share/share-button.element.js";
import "../show-query/show-query-button.element.js";
import "../show-query/show-query-panel.element.js";
import "../time-range/time-range-picker.element.js";
import "./search-box.element.js";
import type { LogExplorerSearchBoxElement } from "./search-box.element.js";
import { SearchViewKeyboard } from "./view-keyboard.js";

/**
 * The Search workspace view of the Log Explorer workspace (UI brief §3).
 *
 * Stacks the query bar (the time range picker, then the search box with the time-zoom chip and the
 * filter chips, then the icon buttons of UI brief §4.5: "Show generated query" and "Copy link to
 * this view" so far), the
 * show-query panel when open, the histogram, then the fields panel beside the results list. The
 * view fills the workspace body and never scrolls itself; only the fields panel and the results
 * list do (UI brief §2). Opening a row shows the entry detail drawer over the right of the view
 * (UI brief §4.11); the view owns which entry is open and moves focus between the row and the
 * drawer. It also owns the keyboard shortcuts (`/`, `j`/`k`, Escape; {@link SearchViewKeyboard}).
 * The workspace editor renders it when its "Search" tab is active; every child reads and
 * writes `LogExplorerQueryContext`, which `log-explorer-workspace` provides.
 *
 * Bound by the `Umbraco.Community.LogExplorer.WorkspaceView.Search` manifest.
 *
 * @element log-explorer-search-view
 */
@customElement("log-explorer-search-view")
export class LogExplorerSearchViewElement extends UmbLitElement {
  /** The entry open in the drawer; `undefined` when the drawer is closed. */
  @state()
  private _openRecord: LogRecord | undefined;

  @query("log-explorer-results")
  private _results?: LogExplorerResultsElement;

  @query("log-explorer-entry-detail")
  private _drawer?: LogExplorerEntryDetailElement;

  @query("log-explorer-search-box")
  private _searchBox?: LogExplorerSearchBoxElement;

  constructor() {
    super();
    // `/`, `j`/`k` and Escape across the view (BRIEF §6.15); Enter on a row needs nothing, rows
    // being native buttons.
    new SearchViewKeyboard(this, {
      focusSearch: () => this._searchBox?.focus(),
      searchHasText: () => this._searchBox?.hasText ?? false,
      clearSearch: () => this._searchBox?.clearText(),
      moveRowFocus: (step) => void this._results?.moveRowFocus(step),
      drawerOpen: () => this._openRecord !== undefined,
      closeDrawer: (restoreFocus) => void this.#close(restoreFocus),
    });
  }

  /** Opening another row while the drawer is open replaces its content. */
  async #onOpen(event: LogExplorerEntryOpenEvent): Promise<void> {
    this._openRecord = event.record;
    await this.updateComplete;
    await this._drawer?.focusHeading();
  }

  /**
   * Closes the drawer and, by default, returns focus to the row that was open (UI brief §7).
   * Escape pressed in the search box or a facet closes it without pulling focus away from there.
   */
  #close = async (restoreFocus = true): Promise<void> => {
    const id = this._openRecord?.id;
    this._openRecord = undefined;
    if (id && restoreFocus) await this._results?.focusRow(id);
  };

  /**
   * Renders the query bar, the show-query panel, the histogram, the fields panel beside the
   * results list and, when an entry is open, the drawer.
   *
   * @returns The template.
   */
  override render() {
    return html`
      <div class="query-bar">
        <log-explorer-time-range-picker></log-explorer-time-range-picker>
        <log-explorer-search-box>
          <log-explorer-zoom-chip slot="before-chips"></log-explorer-zoom-chip>
        </log-explorer-search-box>
        <log-explorer-show-query-button></log-explorer-show-query-button>
        <log-explorer-share-button></log-explorer-share-button>
      </div>
      <log-explorer-show-query-panel></log-explorer-show-query-panel>
      <log-explorer-histogram></log-explorer-histogram>
      <div class="body">
        <log-explorer-fields-panel></log-explorer-fields-panel>
        <log-explorer-results
          .selectedId=${this._openRecord?.id}
          @log-explorer-entry-open=${this.#onOpen}
        ></log-explorer-results>
      </div>
      ${
        this._openRecord
          ? html`<log-explorer-entry-detail
              .record=${this._openRecord}
              @log-explorer-entry-close=${() => this.#close()}
            ></log-explorer-entry-detail>`
          : nothing
      }
    `;
  }

  static override styles = [
    css`
      :host {
        /* The drawer is positioned against the view, which fills the workspace body below the
           workspace header (UI brief §4.11). Inline-size containment lets the drawer's width
           follow the view's width rather than the window's. */
        position: relative;
        container-type: inline-size;
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-4);
        height: 100%;
        box-sizing: border-box;
        /* Tighter top and bottom than the backoffice's usual layout-1, which is what fits 12
           result rows at 1440 x 900 (UI brief §2). The sides keep layout-1: the breakpoints in
           the results and histogram subtract it. */
        padding: var(--uui-size-space-5) var(--uui-size-layout-1);
      }

      /* About a third of the view, but never too narrow to read; the whole width below the
         narrow breakpoint (UI brief §2.1). */
      log-explorer-entry-detail {
        position: absolute;
        inset: 0 0 0 auto;
        z-index: 1;
        box-sizing: border-box;
        width: max(33%, min(100%, 30rem));
        animation: log-explorer-drawer-in 160ms ease-out;
      }

      @container (max-width: 900px) {
        log-explorer-entry-detail {
          width: 100%;
        }
      }

      @keyframes log-explorer-drawer-in {
        from {
          transform: translateX(var(--uui-size-space-6));
          opacity: 0;
        }
      }

      @media (prefers-reduced-motion: reduce) {
        log-explorer-entry-detail {
          animation: none;
        }
      }

      /* A definite height (UUI's standard control height): the square icon buttons size
         themselves from the bar's height (height: 100%; aspect-ratio: 1), which without one
         resolves against their own content and stretches the whole bar to about 54 px. */
      .query-bar {
        display: flex;
        align-items: stretch;
        gap: var(--uui-size-space-3);
        height: var(--uui-size-11);
      }

      log-explorer-search-box {
        flex: 1;
      }

      /* The fields panel and results share the remaining height; the panel keeps its own width
         (or collapses to a strip) and the results take the rest. */
      .body {
        flex: 1;
        min-height: 0;
        display: flex;
        gap: var(--uui-size-space-4);
      }

      log-explorer-results {
        flex: 1;
        min-width: 0;
        min-height: 0;
      }
    `,
  ];
}

export { LogExplorerSearchViewElement as element };

declare global {
  interface HTMLElementTagNameMap {
    "log-explorer-search-view": LogExplorerSearchViewElement;
  }
}
