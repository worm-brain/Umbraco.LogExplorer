import { customElement, html } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import "../shared/empty-state.element.js";

/**
 * The Patterns workspace view of the Log Explorer workspace (UI brief §4.12).
 *
 * Shows an empty state until the view is built. The workspace editor renders it when its
 * "Patterns" tab is active.
 *
 * Bound by the `Umbraco.Community.LogExplorer.WorkspaceView.Patterns` manifest.
 *
 * @element log-explorer-patterns-view
 */
@customElement("log-explorer-patterns-view")
export class LogExplorerPatternsViewElement extends UmbLitElement {
  /**
   * Renders the view's empty state.
   *
   * @returns The template.
   */
  override render() {
    // TODO: localise (UI brief §8) once the package registers a localization manifest.
    return html`<log-explorer-empty-state headline="Patterns"
      ><p>No patterns to show yet. Message templates grouped by count arrive here.</p></log-explorer-empty-state
    >`;
  }
}

export { LogExplorerPatternsViewElement as element };

declare global {
  interface HTMLElementTagNameMap {
    "log-explorer-patterns-view": LogExplorerPatternsViewElement;
  }
}
