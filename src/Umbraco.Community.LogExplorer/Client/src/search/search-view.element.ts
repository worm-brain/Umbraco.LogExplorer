import { css, customElement, html } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import "../shared/empty-state.element.js";
import "../time-range/time-range-picker.element.js";

/**
 * The Search workspace view of the Log Explorer workspace (UI brief §3).
 *
 * Holds the query bar, which so far has only the time range picker, above an empty state until
 * the results, histogram and fields panel are built. The workspace editor renders it when its
 * "Search" tab is active; the picker reads and writes `LogExplorerQueryContext`, which
 * `log-explorer-workspace` provides.
 *
 * Bound by the `Umbraco.Community.LogExplorer.WorkspaceView.Search` manifest.
 *
 * @element log-explorer-search-view
 */
@customElement("log-explorer-search-view")
export class LogExplorerSearchViewElement extends UmbLitElement {
  /**
   * Renders the query bar and the view's empty state.
   *
   * @returns The template.
   */
  override render() {
    return html`
      <div class="query-bar">
        <log-explorer-time-range-picker></log-explorer-time-range-picker>
      </div>
      <log-explorer-empty-state headline=${this.localize.term("logExplorer_tabSearch")}
        ><p>${this.localize.term("logExplorer_searchEmpty")}</p></log-explorer-empty-state
      >
    `;
  }

  static override styles = [
    css`
      :host {
        display: block;
      }

      .query-bar {
        display: flex;
        align-items: stretch;
        gap: var(--uui-size-space-3);
        padding: var(--uui-size-layout-1) var(--uui-size-layout-1) 0;
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
