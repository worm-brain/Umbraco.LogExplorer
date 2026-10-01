import { customElement, html } from "@umbraco-cms/backoffice/external/lit";
import { FacetsService, type FacetResult, type FacetsRequest, type FacetValue } from "../api/index.js";
import { valueText } from "../facets/facets-model.js";
import { toLogQuery } from "../query/log-query.js";
import type { LogExplorerViewState } from "../query/view-state.js";
import type { RequestFn } from "../shared/request-loader.js";
import { EXCEPTION_TYPE_FIELD, exceptionFocusChip, focusInSearch, OVERVIEW_EXCEPTIONS_TOP } from "./overview-model.js";
import { LogExplorerOverviewPanelBase } from "./overview-panel-base.js";

/**
 * "Exception types", the third Overview panel (UI brief §4.13, BRIEF §6.10): the exception types
 * in the query bar's results with their counts, from `POST /facets` on `@exception.type` (both the
 * files and the sample source facet that portable field). Each row is a button that focuses its
 * type in Search with an include chip, the one the fields panel adds for that value. With no
 * exceptions it says "No exceptions in the current results."
 *
 * Type names are log text and render as text, in full: the namespace is what tells two
 * `TimeoutException`s apart.
 *
 * Not bound to a manifest; the Overview view renders it.
 *
 * @element log-explorer-overview-exceptions
 */
@customElement("log-explorer-overview-exceptions")
export class LogExplorerOverviewExceptionsElement extends LogExplorerOverviewPanelBase<FacetsRequest, FacetResult> {
  protected override readonly feature = "facets";

  protected override fetchData: RequestFn<FacetsRequest, FacetResult> = (alias, body, signal) =>
    FacetsService.getFacets({ path: { alias }, body, signal });

  protected override buildRequest(state: LogExplorerViewState): FacetsRequest {
    // Sort and paging do not change the counts; fixing them avoids needless refetches.
    return {
      query: toLogQuery({ ...state, sort: "desc" }, 1),
      fields: [EXCEPTION_TYPE_FIELD],
      top: OVERVIEW_EXCEPTIONS_TOP,
    };
  }

  #renderRow(value: FacetValue) {
    const type = valueText(value.value);
    const count = value.count.toLocaleString(this.localize.lang());
    return html`
      <li class="focus-row">
        <uui-button
          compact
          label=${this.localize.term("logExplorer_overviewFocusException", type, count)}
          title=${type}
          @click=${() => this._context && focusInSearch(this._context, exceptionFocusChip(type))}
        >
          <span class="focus-label">${type}</span>
        </uui-button>
        <span class="count" aria-hidden="true">${count}</span>
      </li>
    `;
  }

  /**
   * Renders the box with one button per exception type, or the empty text.
   *
   * @returns The template.
   */
  override render() {
    const result = this._data.result;
    const values = result?.facets.find((facet) => facet.field === EXCEPTION_TYPE_FIELD)?.topValues ?? [];
    return this.renderPanel(
      this.localize.term("logExplorer_overviewExceptionTypes"),
      (source) => this.localize.term("logExplorer_overviewExceptionsUnsupported", source),
      (message) => this.localize.term("logExplorer_overviewExceptionsError", message),
      result?.approximate ?? false,
      () =>
        values.length > 0
          ? html`<ul class="rows">
              ${values.map((value) => this.#renderRow(value))}
            </ul>`
          : html`<p class="empty">${this.localize.term("logExplorer_overviewExceptionsEmpty")}</p>`,
    );
  }
}

declare global {
  interface HTMLElementTagNameMap {
    "log-explorer-overview-exceptions": LogExplorerOverviewExceptionsElement;
  }
}
