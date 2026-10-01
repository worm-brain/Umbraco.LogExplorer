import { css, customElement, html, nothing, state, styleMap } from "@umbraco-cms/backoffice/external/lit";
import {
  HistogramService,
  MinimumLevelsService,
  type HistogramRequest,
  type HistogramResult,
  type MinimumLevelsResult,
  type SourceResponseModel,
} from "../api/index.js";
import { patternsWindow } from "../patterns/patterns-model.js";
import { toLogQuery } from "../query/log-query.js";
import type { LogExplorerViewState } from "../query/view-state.js";
import { LEVEL_BADGE_STYLES, LEVEL_TOKENS } from "../shared/level-tokens.js";
import { RequestLoader, type RequestFn, type RequestState } from "../shared/request-loader.js";
import { formatAbsoluteRange } from "../time-range/time-range.js";
import { FILES_SOURCE_TYPE, levelRows, OVERVIEW_BUCKETS, sinkRows } from "./overview-model.js";
import { LogExplorerOverviewPanelBase } from "./overview-panel-base.js";

/** `GET /sources/{alias}/minimum-levels` through the generated client; it takes no body. */
const minimumLevelsWithClient: RequestFn<undefined, MinimumLevelsResult> = (alias, _request, signal) =>
  MinimumLevelsService.getMinimumLevels({ path: { alias }, signal });

/**
 * "Entries by level · {range}", the first Overview panel (UI brief §4.13, BRIEF §6.10): one row per
 * level with its badge, a bar proportional to its share of the range and its count, then, for a
 * files source, "Minimum levels (configuration)" with each sink's configured level.
 *
 * - **Counts** are the histogram's buckets summed, with the level set removed from the request,
 *   as the histogram's toggles count (ADR 0004): a level the set hides keeps its count and is
 *   drawn muted, so the user sees what the filter leaves out. The range in the headline is the
 *   one the server resolved.
 * - **Minimum levels** come from `GET /minimum-levels` (ADR 0019), asked once per source and only
 *   of `UmbracoFiles` sources; other sources leave the section out. A failure there shows one line
 *   and does not hide the counts.
 * - The bars are plain spans: UUI has no proportional bar.
 *
 * Not bound to a manifest; the Overview view renders it.
 *
 * @element log-explorer-overview-levels
 */
@customElement("log-explorer-overview-levels")
export class LogExplorerOverviewLevelsElement extends LogExplorerOverviewPanelBase<HistogramRequest, HistogramResult> {
  protected override readonly feature = "histogram";

  protected override fetchData: RequestFn<HistogramRequest, HistogramResult> = (alias, body, signal) =>
    HistogramService.getHistogram({ path: { alias }, body, signal });

  /** The sink levels of a files source; `idle` for any other source. */
  @state()
  private _minimumLevels: RequestState<MinimumLevelsResult> = { status: "idle" };

  #minimumLevels = new RequestLoader<undefined, MinimumLevelsResult>(
    minimumLevelsWithClient,
    (data) => (this._minimumLevels = data),
  );
  #minimumLevelsAlias?: string;

  protected override buildRequest(state: LogExplorerViewState): HistogramRequest {
    // Levels, sort and paging do not change the counts; fixing them avoids needless refetches.
    return { query: toLogQuery({ ...state, levels: null, sort: "desc" }, 1), targetBuckets: OVERVIEW_BUCKETS };
  }

  protected override onSourceChanged(source: SourceResponseModel | undefined): void {
    // The sink configuration is the site's, not the query's: one request per source is enough.
    const alias = source?.type === FILES_SOURCE_TYPE ? source.alias : undefined;
    if (alias === this.#minimumLevelsAlias) return;
    this.#minimumLevelsAlias = alias;
    if (alias) this.#minimumLevels.load(alias, undefined);
    else this.#minimumLevels.reset();
  }

  override disconnectedCallback(): void {
    super.disconnectedCallback();
    this.#minimumLevels.reset();
    this.#minimumLevelsAlias = undefined;
  }

  #rangeLabel(): string {
    const lang = this.localize.lang();
    const resolved = this._data.result?.range;
    if (resolved) return formatAbsoluteRange(resolved, lang);
    if (!this._viewState) return "";
    // Before the first answer, label the range as the client resolves it.
    const { fromMs, toMs } = patternsWindow(this._viewState, Date.now());
    return formatAbsoluteRange({ from: new Date(fromMs).toISOString(), to: new Date(toMs).toISOString() }, lang);
  }

  #renderLevels(result: HistogramResult | undefined) {
    const lang = this.localize.lang();
    const rows = levelRows(result?.buckets ?? [], this._viewState?.levels ?? null);
    return html`
      <ul class="levels">
        ${rows.map((row) => {
          // The rows are display: contents, so the note goes on each cell (tooltip) and in
          // visually hidden text (screen readers), not on the row.
          const note = row.hidden ? this.localize.term("logExplorer_overviewLevelHidden", row.level.toUpperCase()) : "";
          return html`
            <li class=${row.hidden ? "level-row hidden" : "level-row"}>
              <uui-tag class="badge level-${row.level}" title=${note}>${row.level.toUpperCase()}</uui-tag>
              <span class="bar" aria-hidden="true" title=${note}>
                <span class="fill level-${row.level}" style=${styleMap({ width: `${row.percent}%` })}></span>
              </span>
              <span class="level-count" title=${note}
                >${row.count.toLocaleString(lang)}${
                  row.hidden ? html`<span class="visually-hidden">, ${note}</span>` : nothing
                }</span
              >
            </li>
          `;
        })}
      </ul>
    `;
  }

  #renderMinimumLevels() {
    const { status, result, error } = this._minimumLevels;
    if (status === "idle") return nothing;
    return html`
      <section class="minimum" aria-label=${this.localize.term("logExplorer_overviewMinimumLevels")}>
        <h6>${this.localize.term("logExplorer_overviewMinimumLevels")}</h6>
        ${
          status === "error"
            ? html`<p class="empty">${this.localize.term("logExplorer_overviewMinimumLevelsError", error ?? "")}</p>`
            : html`<dl>
                ${sinkRows(result?.sinks ?? []).map(
                  (sink) => html`
                    <dt>${sink.name}</dt>
                    <dd><uui-tag class="badge level-${sink.level ?? "none"}">${sink.label}</uui-tag></dd>
                  `,
                )}
              </dl>`
        }
      </section>
    `;
  }

  /**
   * Renders the box with the level rows and, for a files source, the sink levels.
   *
   * @returns The template.
   */
  override render() {
    const result = this._data.result;
    return this.renderPanel(
      this.localize.term("logExplorer_overviewEntriesByLevel", this.#rangeLabel()),
      (source) => this.localize.term("logExplorer_overviewLevelsUnsupported", source),
      (message) => this.localize.term("logExplorer_overviewLevelsError", message),
      result?.approximate ?? false,
      () => html`${this.#renderLevels(result)}${this.#renderMinimumLevels()}`,
    );
  }

  static override styles = [
    ...LogExplorerOverviewPanelBase.styles,
    LEVEL_TOKENS,
    LEVEL_BADGE_STYLES,
    css`
      /* One grid for every row (rows are display: contents), so the badge column is as wide as
         the widest badge and the bars line up. */
      .levels {
        display: grid;
        grid-template-columns: auto minmax(0, 1fr) auto;
        align-items: center;
        gap: var(--uui-size-space-2) var(--uui-size-space-3);
        margin: 0;
        padding: 0;
        list-style: none;
      }

      .level-row {
        display: contents;
      }

      /* A level the level set hides: muted, the count still visible (ADR 0004). */
      .level-row.hidden > * {
        opacity: 0.5;
      }

      .level-row.hidden .level-count {
        text-decoration: line-through;
      }

      .badge {
        justify-self: start;
        font-size: var(--uui-type-small-size);
      }

      .level-none {
        background-color: var(--uui-color-surface-alt);
        color: var(--uui-color-text);
      }

      .bar {
        display: block;
        height: var(--uui-size-2);
        border-radius: var(--uui-border-radius);
        background-color: var(--uui-color-surface-alt);
        overflow: hidden;
      }

      /* LEVEL_BADGE_STYLES sets the fill colour from the level-{name} class. */
      .fill {
        display: block;
        height: 100%;
      }

      .level-count {
        min-width: 6ch;
        text-align: right;
        font-weight: bold;
        font-variant-numeric: tabular-nums;
      }

      .minimum {
        padding-top: var(--uui-size-space-3);
        border-top: 1px solid var(--uui-color-divider);
      }

      .minimum h6 {
        margin: 0 0 var(--uui-size-space-2);
        color: var(--uui-color-text-alt);
        font-size: var(--uui-type-small-size);
      }

      dl {
        display: grid;
        grid-template-columns: 1fr auto;
        align-items: center;
        gap: var(--uui-size-space-2) var(--uui-size-space-3);
        margin: 0;
      }

      dd {
        margin: 0;
      }

      .visually-hidden {
        position: absolute;
        width: 1px;
        height: 1px;
        overflow: hidden;
        clip-path: inset(50%);
        white-space: nowrap;
      }
    `,
  ];
}

declare global {
  interface HTMLElementTagNameMap {
    "log-explorer-overview-levels": LogExplorerOverviewLevelsElement;
  }
}
