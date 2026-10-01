import { customElement, html } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import "../shared/empty-state.element.js";

/**
 * The Overview workspace view of the Log Explorer workspace (UI brief §4.13).
 *
 * Shows an empty state until the view is built. The workspace editor renders it when its
 * "Overview" tab is active.
 *
 * Bound by the `Umbraco.Community.LogExplorer.WorkspaceView.Overview` manifest.
 *
 * @element log-explorer-overview-view
 */
@customElement("log-explorer-overview-view")
export class LogExplorerOverviewViewElement extends UmbLitElement {
  /**
   * Renders the view's empty state.
   *
   * @returns The template.
   */
  override render() {
    return html`<log-explorer-empty-state headline=${this.localize.term("logExplorer_tabOverview")}
      ><p>${this.localize.term("logExplorer_overviewEmpty")}</p></log-explorer-empty-state
    >`;
  }
}

export { LogExplorerOverviewViewElement as element };

declare global {
  interface HTMLElementTagNameMap {
    "log-explorer-overview-view": LogExplorerOverviewViewElement;
  }
}
