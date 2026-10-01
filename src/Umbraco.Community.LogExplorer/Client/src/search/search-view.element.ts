import { css, customElement, html } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import "../facets/fields-panel.element.js";
import "../histogram/histogram-panel.element.js";
import "../histogram/zoom-chip.element.js";
import "../results/results-panel.element.js";
import "../show-query/show-query-button.element.js";
import "../show-query/show-query-panel.element.js";
import "../time-range/time-range-picker.element.js";
import "./search-box.element.js";

/**
 * The Search workspace view of the Log Explorer workspace (UI brief §3).
 *
 * Stacks the query bar (the time range picker, then the search box with the time-zoom chip and the
 * filter chips, then the icon buttons of UI brief §4.5: "Show generated query" so far), the
 * show-query panel when open, the histogram, then the fields panel beside the results list.
 * The view fills the workspace body and never scrolls itself; only the fields panel and the
 * results list do (UI brief §2). The workspace editor renders it when its "Search" tab is active; every child
 * reads and writes `LogExplorerQueryContext`, which `log-explorer-workspace` provides.
 *
 * Bound by the `Umbraco.Community.LogExplorer.WorkspaceView.Search` manifest.
 *
 * @element log-explorer-search-view
 */
@customElement("log-explorer-search-view")
export class LogExplorerSearchViewElement extends UmbLitElement {
  /**
   * Renders the query bar, the show-query panel, the histogram, and the fields panel beside the
   * results list.
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
      </div>
      <log-explorer-show-query-panel></log-explorer-show-query-panel>
      <log-explorer-histogram></log-explorer-histogram>
      <div class="body">
        <log-explorer-fields-panel></log-explorer-fields-panel>
        <log-explorer-results></log-explorer-results>
      </div>
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
        align-items: stretch;
        gap: var(--uui-size-space-3);
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
