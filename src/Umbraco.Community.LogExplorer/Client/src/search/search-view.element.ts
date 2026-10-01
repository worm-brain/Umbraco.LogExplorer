import { customElement, html } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import "../shared/empty-state.element.js";

/**
 * The Search workspace view of the Log Explorer workspace (UI brief §3).
 *
 * Shows an empty state until the view is built. The workspace editor renders it when its
 * "Search" tab is active.
 *
 * Bound by the `Umbraco.Community.LogExplorer.WorkspaceView.Search` manifest.
 *
 * @element log-explorer-search-view
 */
@customElement("log-explorer-search-view")
export class LogExplorerSearchViewElement extends UmbLitElement {
  /**
   * Renders the view's empty state.
   *
   * @returns The template.
   */
  override render() {
    return html`<log-explorer-empty-state headline=${this.localize.term("logExplorer_tabSearch")}
      ><p>${this.localize.term("logExplorer_searchEmpty")}</p></log-explorer-empty-state
    >`;
  }
}

export { LogExplorerSearchViewElement as element };

declare global {
  interface HTMLElementTagNameMap {
    "log-explorer-search-view": LogExplorerSearchViewElement;
  }
}
