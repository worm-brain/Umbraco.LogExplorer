import { css, customElement, html } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import "../histogram/zoom-chip.element.js";
import "../search/search-box.element.js";
import "../share/share-button.element.js";
import "../show-query/show-query-button.element.js";
import "../show-query/show-query-panel.element.js";
import "../time-range/time-range-picker.element.js";
import "./overview-exceptions.element.js";
import "./overview-levels.element.js";
import "./overview-messages.element.js";

/**
 * The Overview workspace view of the Log Explorer workspace (UI brief §4.13, BRIEF §6.10): a
 * summary of the time range in three equal `uui-box` panels, entries by level (with the sink
 * minimum levels for a files source), the most frequent messages and the exception types.
 *
 * Keeps the Search view's query bar (time range picker, the search box with the time-zoom chip,
 * the show-query and share buttons) and its show-query panel, repeated here as the Patterns view does, so
 * the summary follows the same filters; every panel reads the shared `LogExplorerQueryContext`.
 * The panels sit in a row and stack when the view is narrower than about three readable columns
 * (a container query on the view, so the tree's width counts, not the window's). The view
 * scrolls only when stacked panels overflow it.
 *
 * Bound by the `Umbraco.Community.LogExplorer.WorkspaceView.Overview` manifest.
 *
 * @element log-explorer-overview-view
 */
@customElement("log-explorer-overview-view")
export class LogExplorerOverviewViewElement extends UmbLitElement {
  /**
   * Renders the query bar and the three panels.
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
      <div class="panels">
        <log-explorer-overview-levels></log-explorer-overview-levels>
        <log-explorer-overview-messages></log-explorer-overview-messages>
        <log-explorer-overview-exceptions></log-explorer-overview-exceptions>
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
        overflow-y: auto;
        container-type: inline-size;
      }

      /* The same definite height as the Search view's bar, which the square icon buttons need. */
      .query-bar {
        display: flex;
        align-items: stretch;
        gap: var(--uui-size-space-3);
        height: var(--uui-size-11);
      }

      log-explorer-search-box {
        flex: 1;
      }

      .panels {
        display: grid;
        grid-template-columns: repeat(3, minmax(0, 1fr));
        align-items: start;
        gap: var(--uui-size-space-4);
      }

      /* Below about 900 px of workspace (UI brief §2.1 "narrow"), three columns get too tight
         for the template text, so the panels stack. */
      @container (max-width: 900px) {
        .panels {
          grid-template-columns: minmax(0, 1fr);
        }
      }
    `,
  ];
}

export { LogExplorerOverviewViewElement as element };

declare global {
  interface HTMLElementTagNameMap {
    "log-explorer-overview-view": LogExplorerOverviewViewElement;
  }
}
