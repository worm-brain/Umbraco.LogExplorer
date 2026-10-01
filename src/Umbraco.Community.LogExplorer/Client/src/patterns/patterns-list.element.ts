import { classMap, css, customElement, html, nothing, state, styleMap } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UMB_NOTIFICATION_CONTEXT } from "@umbraco-cms/backoffice/notification";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import { PatternsService, type Pattern, type PatternsRequest } from "../api/index.js";
import { toLogQuery } from "../query/log-query.js";
import { LOG_EXPLORER_QUERY_CONTEXT, type LogExplorerQueryContext } from "../query/query.context.js";
import type { Level, LogExplorerViewState } from "../query/view-state.js";
import { tokeniseTemplate } from "../results/message-tokens.js";
import { shortenSource } from "../results/row-format.js";
import { LEVEL_TOKENS } from "../shared/level-tokens.js";
import { PatternsLoader, type PatternsFn, type PatternsState } from "./patterns-loader.js";
import {
  focusChip,
  levelMix,
  muteChip,
  PATTERNS_TOP,
  patternsWindow,
  searchTabPath,
  sparkBars,
  windowLabels,
} from "./patterns-model.js";

/** The default request: the generated client, which carries the backoffice token. */
const patternsWithClient: PatternsFn = (alias, body, signal) =>
  PatternsService.getPatterns({ path: { alias }, body, signal });

/**
 * The patterns list of the Patterns view (UI brief §4.12, BRIEF §6.9): the entries matching the
 * query bar, grouped by message template, highest count first (the server's order is kept).
 *
 * - **Table:** a `uui-table` with one row per pattern. The template shows its `{Placeholders}`
 *   in accent monospace, with the sample's source and rendered message under it; then the level
 *   mix, the sparkline, the count and the Focus and Mute buttons. Everything from the log is
 *   rendered as text.
 * - **Level mix and sparkline** are small custom bars, because nothing in UUI draws a chart.
 *   Both are hidden from assistive technology and replaced by a text alternative (the mix as a
 *   labelled image, the sparkline as visually hidden text).
 * - **Focus** adds an include chip on `@template` and opens the Search tab with the same query
 *   string; **Mute** adds an exclude chip and confirms it with a notification.
 * - **Levels:** the level toggles live on the histogram, which this view does not show, so a
 *   level set (from `level:error` in the search box, or the Search tab) is named above the table
 *   with a button that clears it.
 * - **States** (UI brief §4.15): `uui-loader-bar` with the previous rows dimmed; empty text;
 *   an error banner with Retry; an Approximate tag; a message when the source lacks Patterns.
 *
 * Not bound to a manifest; the Patterns view renders it.
 *
 * @element log-explorer-patterns
 */
@customElement("log-explorer-patterns")
export class LogExplorerPatternsElement extends UmbLitElement {
  /** The loader's state; every change re-renders the table. */
  @state()
  private _patterns: PatternsState = { status: "idle" };

  /** The level set in force, for the levels notice; `null` means every level. */
  @state()
  private _levels: ReadonlyArray<Level> | null = null;

  /** Display name of the active source when it does not declare Patterns; `undefined` otherwise. */
  @state()
  private _unsupportedSource: string | undefined;

  /** The window the patterns cover, for the volume header, refreshed with each request. */
  @state()
  private _window: { fromMs: number; toMs: number } | undefined;

  #loader = new PatternsLoader(patternsWithClient, (patterns) => (this._patterns = patterns));
  #context?: LogExplorerQueryContext;
  #notifications?: typeof UMB_NOTIFICATION_CONTEXT.TYPE;
  #viewState?: LogExplorerViewState;
  #alias?: string;
  #requestKey?: string;

  constructor() {
    super();
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (context) => (this.#notifications = context));
    this.consumeContext(LOG_EXPLORER_QUERY_CONTEXT, (context) => {
      this.#context = context;
      this.observe(
        // queryState, not state: chips the source cannot run and a disallowed native query
        // never reach the server, as on the Search view.
        context?.queryState,
        (viewState) => {
          this.#viewState = viewState;
          this._levels = viewState?.levels ?? null;
          this.#requery();
        },
        "_observeState",
      );
      this.observe(
        context?.activeSource,
        (source) => {
          const supported = source ? source.capabilities.features.includes("patterns") : true;
          this.#alias = supported ? source?.alias : undefined;
          this._unsupportedSource = supported ? undefined : source?.displayName;
          this.#requery();
        },
        "_observeActiveSource",
      );
    });
  }

  override disconnectedCallback(): void {
    super.disconnectedCallback();
    // Nothing should land on a view the user has left; the context observers re-emit on
    // reconnect, which queries again.
    this.#loader.reset();
    this.#requestKey = undefined;
  }

  /** Requests patterns again only when the request itself changed (not on sort changes). */
  #requery(): void {
    if (!this.#alias || !this.#viewState) {
      this.#loader.reset();
      this.#requestKey = undefined;
      return;
    }

    // Sort, take and cursor do not change patterns; fix them so they never cause a refetch.
    const request: PatternsRequest = { query: toLogQuery({ ...this.#viewState, sort: "desc" }, 1), top: PATTERNS_TOP };
    const key = JSON.stringify([this.#alias, request]);
    if (key === this.#requestKey) return;
    this.#requestKey = key;
    this._window = patternsWindow(this.#viewState, Date.now());
    this.#loader.load(this.#alias, request);
  }

  #focus(pattern: Pattern): void {
    this.#context?.addChips([focusChip(pattern.template)]);
    // The tabs are router links; pushing the Search route with the query string the context has
    // just written switches tab with the new chip in place. The router listens to the patched
    // history's `changestate`, as the context does.
    window.history.pushState(
      window.history.state,
      "",
      `${searchTabPath(window.location.pathname)}${window.location.search}`,
    );
  }

  #mute(pattern: Pattern): void {
    this.#context?.addChips([muteChip(pattern.template)]);
    this.#notifications?.peek("default", {
      data: { message: this.localize.term("logExplorer_patternsMuted", pattern.template) },
    });
  }

  #renderLevelsNotice() {
    if (this._levels === null) return nothing;
    const names = this._levels.map((level) => level.toUpperCase()).join(", ");
    return html`
      <div class="levels-notice">
        <span>${this.localize.term("logExplorer_patternsLevelsFiltered", names)}</span>
        <uui-button
          look="secondary"
          compact
          label=${this.localize.term("logExplorer_patternsShowAllLevels")}
          @click=${() => this.#context?.update({ levels: null })}
        ></uui-button>
      </div>
    `;
  }

  #renderTemplate(pattern: Pattern) {
    const sample = pattern.sample.body ?? pattern.template;
    const source = shortenSource(pattern.sample.scope);
    return html`
      <div class="template">
        ${tokeniseTemplate(pattern.template).map((token) =>
          token.field ? html`<span class="placeholder">${token.text}</span>` : token.text,
        )}
      </div>
      <div class="sample" title=${sample}>
        ${
          source
            ? this.localize.term("logExplorer_patternsSample", source, sample)
            : this.localize.term("logExplorer_patternsSampleNoSource", sample)
        }
      </div>
    `;
  }

  #renderLevelMix(pattern: Pattern) {
    const lang = this.localize.lang();
    const shares = levelMix(pattern.countsBySeverityShortName);
    const text = shares.map((share) => `${share.level.toUpperCase()} ${share.count.toLocaleString(lang)}`).join(", ");
    const label = this.localize.term("logExplorer_patternsLevelMix", text);
    return html`
      <div class="mix" role="img" aria-label=${label} title=${label}>
        ${shares.map(
          (share) =>
            html`<span
              class="mix-segment level-${share.level}"
              style=${styleMap({ width: `${share.percent}%` })}
            ></span>`,
        )}
      </div>
    `;
  }

  #renderSparkline(pattern: Pattern) {
    const lang = this.localize.lang();
    const peak = Math.max(0, ...pattern.sparkline);
    const summary =
      pattern.count === 1
        ? this.localize.term("logExplorer_patternsVolumeOne")
        : this.localize.term(
            "logExplorer_patternsVolume",
            pattern.count.toLocaleString(lang),
            peak.toLocaleString(lang),
          );
    return html`
      <div class="spark" aria-hidden="true" title=${summary}>
        ${sparkBars(pattern.sparkline).map((bar) =>
          bar.empty
            ? html`<span class="spark-bar empty"></span>`
            : html`<span class="spark-bar" style=${styleMap({ height: `${bar.heightPercent}%` })}></span>`,
        )}
      </div>
      <span class="visually-hidden">${summary}</span>
    `;
  }

  #renderRow(pattern: Pattern) {
    return html`
      <uui-table-row>
        <uui-table-cell class="col-template">${this.#renderTemplate(pattern)}</uui-table-cell>
        <uui-table-cell class="col-mix">${this.#renderLevelMix(pattern)}</uui-table-cell>
        <uui-table-cell class="col-volume">${this.#renderSparkline(pattern)}</uui-table-cell>
        <uui-table-cell class="col-count">${pattern.count.toLocaleString(this.localize.lang())}</uui-table-cell>
        <uui-table-cell class="col-actions">
          <div class="actions">
            <uui-button
              look="outline"
              compact
              label=${this.localize.term("logExplorer_patternsFocusLabel", pattern.template)}
              @click=${() => this.#focus(pattern)}
              >${this.localize.term("logExplorer_patternsFocus")}</uui-button
            >
            <uui-button
              look="outline"
              compact
              label=${this.localize.term("logExplorer_patternsMuteLabel", pattern.template)}
              @click=${() => this.#mute(pattern)}
              >${this.localize.term("logExplorer_patternsMute")}</uui-button
            >
          </div>
        </uui-table-cell>
      </uui-table-row>
    `;
  }

  #renderTable(patterns: ReadonlyArray<Pattern>) {
    const lang = this.localize.lang();
    const edges = this._window ? windowLabels(this._window.fromMs, this._window.toMs, lang) : undefined;
    return html`
      <uui-table
        class=${classMap({ dimmed: this._patterns.status === "loading" })}
        aria-label=${this.localize.term("logExplorer_patternsLabel")}
      >
        <uui-table-head>
          <uui-table-head-cell class="col-template">
            ${
              patterns.length === 1
                ? this.localize.term("logExplorer_patternsHeaderOne")
                : this.localize.term("logExplorer_patternsHeader", patterns.length.toLocaleString(lang))
            }
          </uui-table-head-cell>
          <uui-table-head-cell class="col-mix"
            >${this.localize.term("logExplorer_patternsColumnLevelMix")}</uui-table-head-cell
          >
          <uui-table-head-cell class="col-volume">
            ${edges ? this.localize.term("logExplorer_patternsColumnVolume", edges.from, edges.to) : nothing}
          </uui-table-head-cell>
          <uui-table-head-cell class="col-count"
            >${this.localize.term("logExplorer_patternsColumnCount")}</uui-table-head-cell
          >
          <uui-table-head-cell class="col-actions">
            <span class="visually-hidden">${this.localize.term("logExplorer_patternsColumnActions")}</span>
          </uui-table-head-cell>
        </uui-table-head>
        ${patterns.map((pattern) => this.#renderRow(pattern))}
      </uui-table>
    `;
  }

  #renderBody() {
    if (this._unsupportedSource !== undefined) {
      return html`<p class="empty">
        ${this.localize.term("logExplorer_patternsUnsupported", this._unsupportedSource)}
      </p>`;
    }

    const { status, result, error } = this._patterns;
    const patterns = result?.patterns ?? [];
    return html`
      ${
        status === "error"
          ? html`<div class="error" role="alert">
              <span>${this.localize.term("logExplorer_patternsError", error ?? "")}</span>
              <uui-button
                look="secondary"
                compact
                label=${this.localize.term("logExplorer_retry")}
                @click=${() => this.#loader.retry()}
              ></uui-button>
            </div>`
          : nothing
      }
      ${
        patterns.length > 0
          ? this.#renderTable(patterns)
          : status === "loaded"
            ? html`<p class="empty">${this.localize.term("logExplorer_patternsEmpty")}</p>`
            : nothing
      }
    `;
  }

  /**
   * Renders the box: loader, levels notice and Approximate tag, then the table or a state.
   *
   * @returns The template.
   */
  override render() {
    const { status, result } = this._patterns;
    return html`
      <uui-box>
        <section
          class="panel"
          aria-label=${this.localize.term("logExplorer_patternsLabel")}
          aria-busy=${status === "loading" ? "true" : "false"}
        >
          ${status === "loading" ? html`<uui-loader-bar class="loader"></uui-loader-bar>` : nothing}
          ${
            this._levels !== null || result?.approximate
              ? html`<div class="top">
                  ${this.#renderLevelsNotice()}
                  ${
                    result?.approximate
                      ? html`<uui-tag
                          look="secondary"
                          title=${this.localize.term("logExplorer_histogramApproximateHint")}
                        >
                          ${this.localize.term("logExplorer_histogramApproximate")}
                        </uui-tag>`
                      : nothing
                  }
                </div>`
              : nothing
          }
          ${this.#renderBody()}
        </section>
      </uui-box>
    `;
  }

  static override styles = [
    UmbTextStyles,
    LEVEL_TOKENS,
    css`
      :host {
        display: block;
      }

      uui-box {
        position: relative;
        --uui-box-default-padding: var(--uui-size-space-3) var(--uui-size-space-4);
      }

      .panel {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-3);
      }

      .loader {
        position: absolute;
        inset: 0 0 auto 0;
      }

      .top,
      .levels-notice {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-3);
      }

      .levels-notice {
        color: var(--uui-color-text-alt);
        font-size: var(--uui-type-small-size);
      }

      .top uui-tag {
        margin-inline-start: auto;
        font-size: var(--uui-type-small-size);
      }

      .error {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: var(--uui-size-space-4);
        padding: var(--uui-size-space-2) var(--uui-size-space-4);
        border: 1px solid var(--uui-color-danger-standalone);
        border-radius: var(--uui-border-radius);
        color: var(--uui-color-danger-standalone);
      }

      .empty {
        margin: 0;
        color: var(--uui-color-text-alt);
      }

      uui-table {
        transition: opacity 120ms ease-out;
      }

      uui-table.dimmed {
        opacity: 0.5;
      }

      uui-table-head-cell {
        font-size: var(--uui-type-small-size);
      }

      /* The template takes the remaining width; the other columns are fixed and narrow. */
      .col-mix {
        width: var(--uui-size-24);
      }

      .col-volume {
        width: calc(var(--uui-size-24) * 1.5);
      }

      .col-count {
        width: var(--uui-size-16);
        text-align: right;
        font-weight: bold;
        font-variant-numeric: tabular-nums;
      }

      .col-actions {
        width: 1%;
        white-space: nowrap;
      }

      uui-table-cell.col-template {
        /* Long templates and samples wrap inside the cell rather than widening the table. */
        word-break: break-word;
      }

      .template {
        font-family: var(--uui-font-monospace, monospace);
      }

      .placeholder {
        color: var(--uui-color-interactive-emphasis);
        font-weight: bold;
      }

      .sample {
        margin-top: var(--uui-size-space-1);
        color: var(--uui-color-text-alt);
        font-size: var(--uui-type-small-size);
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        max-width: 0;
        min-width: 100%;
      }

      .mix {
        display: flex;
        height: var(--uui-size-2);
        border-radius: var(--uui-border-radius);
        overflow: hidden;
        background-color: var(--uui-color-surface-alt);
      }

      .mix-segment {
        display: block;
        height: 100%;
        background-color: var(--swatch);
      }

      .spark {
        display: flex;
        align-items: flex-end;
        gap: 1px;
        height: var(--uui-size-8);
      }

      .spark-bar {
        flex: 1 1 0;
        min-width: 0;
        background-color: var(--uui-color-interactive-emphasis);
      }

      /* An empty bucket is a faint baseline, so the strip still shows the whole window. */
      .spark-bar.empty {
        height: 1px;
        background-color: var(--uui-color-border);
      }

      .actions {
        display: flex;
        gap: var(--uui-size-space-2);
      }

      .visually-hidden {
        position: absolute;
        width: 1px;
        height: 1px;
        overflow: hidden;
        clip-path: inset(50%);
        white-space: nowrap;
      }

      .level-trace {
        --swatch: var(--log-explorer-level-trace);
      }
      .level-debug {
        --swatch: var(--log-explorer-level-debug);
      }
      .level-info {
        --swatch: var(--log-explorer-level-info);
      }
      .level-warn {
        --swatch: var(--log-explorer-level-warn);
      }
      .level-error {
        --swatch: var(--log-explorer-level-error);
      }
      .level-fatal {
        --swatch: var(--log-explorer-level-fatal);
      }
    `,
  ];
}

declare global {
  interface HTMLElementTagNameMap {
    "log-explorer-patterns": LogExplorerPatternsElement;
  }
}
