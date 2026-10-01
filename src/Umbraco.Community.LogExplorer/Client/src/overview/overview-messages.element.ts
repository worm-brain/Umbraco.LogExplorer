import { customElement, html } from "@umbraco-cms/backoffice/external/lit";
import { PatternsService, type Pattern, type PatternResult, type PatternsRequest } from "../api/index.js";
import { focusChip } from "../patterns/patterns-model.js";
import { toLogQuery } from "../query/log-query.js";
import type { LogExplorerViewState } from "../query/view-state.js";
import type { RequestFn } from "../shared/request-loader.js";
import { focusInSearch, OVERVIEW_TEMPLATES_TOP, truncateTemplate } from "./overview-model.js";
import { LogExplorerOverviewPanelBase } from "./overview-panel-base.js";

/**
 * "Most frequent messages", the second Overview panel (UI brief §4.13, BRIEF §6.10): the top six
 * message templates for the query bar, from `POST /patterns`, each truncated to about 72
 * characters with its count. Each row is a button that focuses its template in Search: the
 * include chip the Patterns view's Focus adds, then the Search tab.
 *
 * Templates are log text and render as text; the full template is the row's tooltip and part of
 * its accessible name.
 *
 * Not bound to a manifest; the Overview view renders it.
 *
 * @element log-explorer-overview-messages
 */
@customElement("log-explorer-overview-messages")
export class LogExplorerOverviewMessagesElement extends LogExplorerOverviewPanelBase<PatternsRequest, PatternResult> {
  protected override readonly feature = "patterns";

  protected override fetchData: RequestFn<PatternsRequest, PatternResult> = (alias, body, signal) =>
    PatternsService.getPatterns({ path: { alias }, body, signal });

  protected override buildRequest(state: LogExplorerViewState): PatternsRequest {
    // Sort and paging do not change patterns; fixing them avoids needless refetches.
    return { query: toLogQuery({ ...state, sort: "desc" }, 1), top: OVERVIEW_TEMPLATES_TOP };
  }

  #renderRow(pattern: Pattern) {
    const count = pattern.count.toLocaleString(this.localize.lang());
    return html`
      <li class="focus-row">
        <uui-button
          compact
          label=${this.localize.term("logExplorer_overviewFocusTemplate", pattern.template, count)}
          title=${pattern.template}
          @click=${() => this._context && focusInSearch(this._context, focusChip(pattern.template))}
        >
          <span class="focus-label">${truncateTemplate(pattern.template)}</span>
        </uui-button>
        <span class="count" aria-hidden="true">${count}</span>
      </li>
    `;
  }

  /**
   * Renders the box with one button per template, or the empty text.
   *
   * @returns The template.
   */
  override render() {
    const result = this._data.result;
    const patterns = result?.patterns ?? [];
    return this.renderPanel(
      this.localize.term("logExplorer_overviewFrequentMessages"),
      (source) => this.localize.term("logExplorer_patternsUnsupported", source),
      (message) => this.localize.term("logExplorer_patternsError", message),
      result?.approximate ?? false,
      () =>
        patterns.length > 0
          ? html`<ul class="rows">
              ${patterns.map((pattern) => this.#renderRow(pattern))}
            </ul>`
          : html`<p class="empty">${this.localize.term("logExplorer_overviewMessagesEmpty")}</p>`,
    );
  }
}

declare global {
  interface HTMLElementTagNameMap {
    "log-explorer-overview-messages": LogExplorerOverviewMessagesElement;
  }
}
