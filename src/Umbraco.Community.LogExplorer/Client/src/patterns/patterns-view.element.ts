import { css, customElement, html } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import "../histogram/zoom-chip.element.js";
import "../search/search-box.element.js";
import "../time-range/time-range-picker.element.js";
import "./patterns-list.element.js";

/**
 * The Patterns workspace view of the Log Explorer workspace (UI brief §4.12).
 *
 * Keeps the Search view's query bar (time range picker, then the search box with the time-zoom
 * chip), which still filters, and replaces the histogram and results with the patterns list. The
 * bar is repeated here rather than shared as an element: it is three children of a flex row,
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
      </div>
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
        align-items: stretch;
        gap: var(--uui-size-space-3);
      }

      log-explorer-search-box {
        flex: 1;
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
