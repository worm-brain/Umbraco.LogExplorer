import { css, customElement, html } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import "../histogram/zoom-chip.element.js";
import "../search/search-box.element.js";
import "../share/share-button.element.js";
import "../show-query/show-query-button.element.js";
import "../show-query/show-query-panel.element.js";
import "../time-range/time-range-picker.element.js";
import "./patterns-list.element.js";

/**
 * The Patterns workspace view of the Log Explorer workspace (UI brief §4.12).
 *
 * Keeps the Search view's query bar (time range picker, the search box with the time-zoom chip,
 * the show-query and share buttons) and its show-query panel, which still filter and show what
 * runs, and replaces the histogram and results with the patterns list. The bar is repeated here
 * rather than shared as an element: it is four children of a flex row,
 * and both views read and write the same `LogExplorerQueryContext`, so the two stay in step
 * without a wrapper. Only the list scrolls; the view itself never does (UI brief §2).
 *
 * Bound by the `Umbraco.Community.LogExplorer.WorkspaceView.Patterns` manifest.
 *
 * @element log-explorer-patterns-view
 */
@customElement("log-explorer-patterns-view")
export class LogExplorerPatternsViewElement extends UmbLitElement {
  /**
   * Renders the query bar and the patterns list.
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
      <log-explorer-patterns></log-explorer-patterns>
    `;
  }

  static override styles = [
    css`
      :host {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-4);
        height: 100%;
        box-sizing: border-box;
        padding: var(--uui-size-layout-1);
      }

      .query-bar {
        display: flex;
        align-items: flex-start;
        gap: var(--uui-size-space-3);
        min-height: var(--uui-size-11);
      }

      /* Every control is one control tall and keeps it when the search box grows with wrapped
         chips (ADR 0024); the square icon buttons fill that height. */
      .query-bar > * {
        height: var(--uui-size-11);
      }

      /* The one control that grows (wrapped chips); this selector outranks .query-bar > *. */
      .query-bar > log-explorer-search-box {
        flex: 1;
        height: auto;
      }

      log-explorer-patterns {
        flex: 1;
        min-height: 0;
        overflow-y: auto;
      }
    `,
  ];
}

export { LogExplorerPatternsViewElement as element };

declare global {
  interface HTMLElementTagNameMap {
    "log-explorer-patterns-view": LogExplorerPatternsViewElement;
  }
}
