import {
  classMap,
  css,
  customElement,
  html,
  nothing,
  query,
  state,
  styleMap,
} from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UMB_NOTIFICATION_CONTEXT } from "@umbraco-cms/backoffice/notification";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import { HistogramService, type HistogramBucket, type HistogramRequest } from "../api/index.js";
import { toLogQuery } from "../query/log-query.js";
import { LOG_EXPLORER_QUERY_CONTEXT, type LogExplorerQueryContext } from "../query/query.context.js";
import { LEVELS, type Level, type LogExplorerViewState } from "../query/view-state.js";
import { syncButtonAria } from "../shared/button-aria.js";
import { LEVEL_TOKENS } from "../shared/level-tokens.js";
import { rovingIndex } from "../shared/roving-focus.js";
import { HistogramLoader, type HistogramFn, type HistogramState } from "./histogram-loader.js";
import {
  barIndexAt,
  bucketTotal,
  formatClock,
  isLevelOn,
  layoutBar,
  levelTotals,
  parseTimeSpanMs,
  rangeFromDrag,
  targetBucketsFor,
  ticks,
  toggleLevel,
  zoomFromBucket,
} from "./histogram-model.js";
import { formatLocale } from "../shared/format-locale.js";

/** Smallest height of a non-zero segment, as a percentage of the bar strip (about 2 px). */
const MIN_SEGMENT_PERCENT = 4;

/** Range lengths for the relative presets, to choose a bucket target before the server answers. */
const RELATIVE_MS: Record<string, number> = {
  "15m": 15 * 60_000,
  "1h": 3_600_000,
  "4h": 4 * 3_600_000,
  "24h": 24 * 3_600_000,
  "7d": 7 * 24 * 3_600_000,
  "30d": 30 * 24 * 3_600_000,
};

/** The default request: the generated client, which carries the backoffice token. */
const histogramWithClient: HistogramFn = (alias, body, signal) =>
  HistogramService.getHistogram({ path: { alias }, body, signal });

/**
 * The histogram panel of the Search view (UI brief §4.7, BRIEF §6.4): entry volume over time,
 * stacked by level, with the level toggles that are the level filter (ADR 0004).
 *
 * - **Level toggles:** one `uui-button` per OTel level (swatch, name, count). On is `outline`,
 *   off is `secondary` with a hollow swatch and struck-through text. Pressing one writes the
 *   context's level set (`null` when all six are on). Counts come from the histogram, which
 *   ignores the level set, so a hidden level still shows how much it hides. The request is sent
 *   without levels so toggling never refetches the bars.
 * - **Bars:** one native `<button>` per bucket, segments stacked INFO first (see `layoutBar`).
 *   Custom because nothing in UUI or the backoffice draws a chart; native buttons rather than
 *   `uui-button` because each needs a column layout of coloured segments, which `uui-button`'s
 *   centred, padded content box cannot hold. Clicking a bar zooms to five minutes around it;
 *   dragging across bars selects any range. Both write the context's `zoom`, which the time chip
 *   (`log-explorer-zoom-chip`) shows and the URL records. The bars are one Tab stop (a roving
 *   tabindex): Left/Right/Home/End move between them and Enter or Space zooms (#51).
 * - **States** (UI brief §4.15): `uui-loader-bar` with the previous bars dimmed while a request
 *   runs; an error banner with Retry; an Approximate tag beside the summary. A source without
 *   the Histogram feature shows the level toggles only, without counts, because they are still
 *   the level filter.
 *
 * Not bound to a manifest; the Search view renders it.
 *
 * @element log-explorer-histogram
 * @cssprop --log-explorer-histogram-height - Height of the bar strip; defaults to `--uui-size-20`.
 */
@customElement("log-explorer-histogram")
export class LogExplorerHistogramElement extends UmbLitElement {
  /** The loader's state; every change re-renders bars, counts and summary. */
  @state()
  private _histogram: HistogramState = { status: "idle" };

  /** The level set in force (`null` = all on), for the toggles' pressed state. */
  @state()
  private _levels: ReadonlyArray<Level> | null = null;

  /** Whether the view is zoomed: picks the bucket target, tick format and click behaviour. */
  @state()
  private _zoomed = false;

  /** Whether the active source declares the Histogram feature. */
  @state()
  private _supported = true;

  /** The bars a drag currently covers, for the selection overlay; `undefined` when not dragging. */
  @state()
  private _drag: { start: number; end: number } | undefined;

  /**
   * Index of the bar that holds the bars' single Tab stop; moved by the arrow keys and by focusing
   * a bar. Clamped to the bars on screen, so a shorter histogram keeps a reachable stop.
   */
  @state()
  private _activeBar = 0;

  @query(".bars")
  private _bars?: HTMLElement;

  #loader = new HistogramLoader(histogramWithClient, (histogram) => (this._histogram = histogram));
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
        context?.queryState,
        (viewState) => {
          this.#viewState = viewState;
          this._levels = viewState?.levels ?? null;
          this._zoomed = viewState?.zoom !== undefined;
          this.#requery();
        },
        "_observeState",
      );
      this.observe(
        context?.activeSource,
        (source) => {
          this.#alias = source?.alias;
          this._supported = source ? source.capabilities.features.includes("histogram") : true;
          this.#requery();
        },
        "_observeActiveSource",
      );
    });
  }

  override disconnectedCallback(): void {
    super.disconnectedCallback();
    // Reconnecting queries again: the context observers re-emit on reconnect.
    this.#loader.reset();
    this.#requestKey = undefined;
  }

  /** Copies the level toggles' `data-pressed` onto their focusable inner buttons. */
  protected override updated(): void {
    void syncButtonAria(this.renderRoot);
  }

  /** Requests the bars again only when the request itself changed (not on level or sort changes). */
  #requery(): void {
    if (!this.#alias || !this.#viewState || !this._supported) {
      this.#loader.reset();
      this.#requestKey = undefined;
      return;
    }

    const state = this.#viewState;
    // Levels and sort do not change the counts (ADR 0004), so they are left out of the request.
    const query = toLogQuery({ ...state, levels: null, sort: "desc" }, 1);
    const range = state.zoom ?? state.range;
    const length =
      "relative" in range
        ? (RELATIVE_MS[range.relative] ?? RELATIVE_MS["1h"]!)
        : new Date(range.to).getTime() - new Date(range.from).getTime();
    const request: HistogramRequest = { query, targetBuckets: targetBucketsFor(length, state.zoom !== undefined) };

    const key = JSON.stringify([this.#alias, request]);
    if (key === this.#requestKey) return;
    this.#requestKey = key;
    this.#loader.load(this.#alias, request);
  }

  #toggle(level: Level): void {
    this.#context?.update({ levels: toggleLevel(this._levels, level) });
  }

  #bucketSizeMs(): number {
    const result = this._histogram.result;
    if (!result) return 0;
    const parsed = parseTimeSpanMs(result.bucketSize);
    if (parsed !== undefined) return parsed;
    // Fall back to the spacing of the first two buckets if the span arrives in another format.
    const [first, second] = result.buckets;
    return first && second ? new Date(second.start).getTime() - new Date(first.start).getTime() : 0;
  }

  /** Keyboard activation (Enter/Space) of a bar; pointer clicks are handled in `#onPointerUp`. */
  #onBarClick(event: MouseEvent, bucket: HistogramBucket): void {
    // `detail` is 0 for clicks synthesised from the keyboard; pointer clicks already zoomed.
    if (event.detail === 0) this.#zoomToBucket(bucket);
  }

  #zoomToBucket(bucket: HistogramBucket): void {
    if (this._zoomed) {
      this.#notifications?.peek("default", {
        data: { message: this.localize.term("logExplorer_histogramAlreadyZoomed") },
      });
      return;
    }
    this.#context?.setZoom(zoomFromBucket(bucket.start));
  }

  /** Moves the roving focus between bars with Left/Right/Home/End; other keys pass through. */
  #onBarsKeydown = async (event: KeyboardEvent): Promise<void> => {
    const count = this._histogram.result?.buckets.length ?? 0;
    const next = rovingIndex(this.#clampedActiveBar(count), event.key, count, {
      orientation: "horizontal",
      wrap: false,
    });
    if (next === undefined) return;
    event.preventDefault();
    this._activeBar = next;
    await this.updateComplete;
    this.renderRoot.querySelectorAll<HTMLButtonElement>("button.bar")[next]?.focus();
  };

  #clampedActiveBar(count: number): number {
    return Math.min(Math.max(this._activeBar, 0), Math.max(count - 1, 0));
  }

  #indexAt(event: PointerEvent): number {
    const bars = this._bars;
    const count = this._histogram.result?.buckets.length ?? 0;
    if (!bars) return 0;
    const rect = bars.getBoundingClientRect();
    return barIndexAt(event.clientX, rect.left, rect.width, count);
  }

  #onPointerDown = (event: PointerEvent): void => {
    if (event.button !== 0 || !this._histogram.result?.buckets.length) return;
    // Capture keeps the drag going when the pointer leaves the strip. Because it also retargets
    // the click to the strip, pointer clicks on a bar are handled here rather than by the bar.
    (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
    const index = this.#indexAt(event);
    this._drag = { start: index, end: index };
  };

  #onPointerMove = (event: PointerEvent): void => {
    if (!this._drag) return;
    const end = this.#indexAt(event);
    if (end !== this._drag.end) this._drag = { ...this._drag, end };
  };

  #onPointerUp = (event: PointerEvent): void => {
    const drag = this._drag;
    this._drag = undefined;
    const buckets = this._histogram.result?.buckets;
    if (!drag || !buckets) return;
    const end = this.#indexAt(event);

    if (end === drag.start) {
      const bucket = buckets[end];
      if (bucket) this.#zoomToBucket(bucket);
      return;
    }
    const zoom = rangeFromDrag(buckets, this.#bucketSizeMs(), drag.start, end);
    if (zoom) this.#context?.setZoom(zoom);
  };

  #onPointerCancel = (): void => {
    this._drag = undefined;
  };

  #renderToggles(totals: Record<Level, number> | undefined) {
    const lang = formatLocale(this.localize.lang());
    return html`
      <div class="levels" role="group" aria-label=${this.localize.term("logExplorer_histogramLevelsLabel")}>
        ${LEVELS.map((level) => {
          const on = isLevelOn(this._levels, level);
          const name = level.toUpperCase();
          const count = totals?.[level];
          const countText = count === undefined ? "" : count.toLocaleString(lang);
          return html`
            <uui-button
              class=${classMap({ level: true, off: !on, zero: count === 0 })}
              data-level=${level}
              data-pressed=${String(on)}
              compact
              look=${on ? "outline" : "secondary"}
              label=${count === undefined ? name : this.localize.term("logExplorer_histogramLevelLabel", name, count, countText)}
              title=${this.localize.term(on ? "logExplorer_histogramLevelHide" : "logExplorer_histogramLevelShow", name)}
              @click=${() => this.#toggle(level)}
            >
              <span class="toggle-content">
                <span class="swatch level-${level}"></span>
                <span class="name">${name}</span>
                ${count === undefined ? nothing : html`<span class="count">${countText}</span>`}
              </span>
            </uui-button>
          `;
        })}
      </div>
    `;
  }

  #renderSummary() {
    const result = this._histogram.result;
    if (!result) return nothing;
    const lang = formatLocale(this.localize.lang());
    const total = result.buckets.reduce((sum, bucket) => sum + bucketTotal(bucket), 0);
    const format = new Intl.DateTimeFormat(lang, { dateStyle: "short", timeStyle: "short" });
    return html`
      <div class="summary">
        <span>
          ${this.localize.term(
            "logExplorer_histogramSummary",
            total,
            total.toLocaleString(lang),
            format.format(new Date(result.range.from)),
            format.format(new Date(result.range.to)),
          )}
        </span>
        ${
          result.approximate
            ? html`<uui-tag look="secondary" title=${this.localize.term("logExplorer_histogramApproximateHint")}>
                ${this.localize.term("logExplorer_histogramApproximate")}
              </uui-tag>`
            : nothing
        }
      </div>
    `;
  }

  #renderBars() {
    const result = this._histogram.result;
    if (!result || result.buckets.length === 0) return nothing;

    const lang = formatLocale(this.localize.lang());
    const buckets = result.buckets;
    const sizeMs = this.#bucketSizeMs();
    const withSeconds = sizeMs > 0 && sizeMs < 60_000;
    const maxTotal = Math.max(0, ...buckets.map(bucketTotal));
    const firstMs = new Date(buckets[0]!.start).getTime();
    const lastEndMs = new Date(buckets[buckets.length - 1]!.start).getTime() + sizeMs;
    const dimmed = this._histogram.status === "loading";
    const drag = this._drag;
    const activeBar = this.#clampedActiveBar(buckets.length);

    return html`
      <div class=${classMap({ chart: true, dimmed })}>
        <div
          class="bars"
          role="group"
          aria-label=${this.localize.term("logExplorer_histogramBarsLabel")}
          @keydown=${this.#onBarsKeydown}
          @pointerdown=${this.#onPointerDown}
          @pointermove=${this.#onPointerMove}
          @pointerup=${this.#onPointerUp}
          @pointercancel=${this.#onPointerCancel}
        >
          ${buckets.map((bucket, index) =>
            this.#renderBar(bucket, index, index === activeBar, maxTotal, withSeconds, lang),
          )}
          ${
            drag && drag.start !== drag.end
              ? html`<div
                  class="selection"
                  style=${styleMap({
                    left: `${(Math.min(drag.start, drag.end) / buckets.length) * 100}%`,
                    width: `${((Math.abs(drag.end - drag.start) + 1) / buckets.length) * 100}%`,
                  })}
                ></div>`
              : nothing
          }
        </div>
        <div class="ticks" aria-hidden="true">
          ${ticks(firstMs, lastEndMs, this._zoomed || withSeconds, lang).map(
            (tick) => html`<span style=${styleMap({ left: `${tick.offsetPercent}%` })}>${tick.label}</span>`,
          )}
        </div>
      </div>
    `;
  }

  #renderBar(
    bucket: HistogramBucket,
    index: number,
    tabStop: boolean,
    maxTotal: number,
    withSeconds: boolean,
    lang: string,
  ) {
    const time = formatClock(new Date(bucket.start).getTime(), withSeconds, lang);
    const total = bucketTotal(bucket);
    const segments = layoutBar(bucket.countsBySeverityShortName, maxTotal, MIN_SEGMENT_PERCENT);
    // The tooltip lists every non-zero level, top of the stack first, as people read it.
    const breakdown = [...segments]
      .reverse()
      .map((segment) => `${segment.level.toUpperCase()} ${segment.count.toLocaleString(lang)}`)
      .join(" · ");
    const label = this.localize.term("logExplorer_histogramBarLabel", time, total, total.toLocaleString(lang));
    return html`
      <button
        type="button"
        class="bar"
        aria-label=${label}
        title=${breakdown ? `${label}\n${breakdown}` : label}
        tabindex=${tabStop ? 0 : -1}
        @focus=${() => (this._activeBar = index)}
        @click=${(event: MouseEvent) => this.#onBarClick(event, bucket)}
      >
        ${segments.map(
          (segment) =>
            html`<span
              class="segment level-${segment.level}"
              style=${styleMap({ height: `${segment.heightPercent}%` })}
            ></span>`,
        )}
      </button>
    `;
  }

  /**
   * Renders the panel: toggles and summary, any error, then bars and ticks.
   *
   * @returns The template.
   */
  override render() {
    const { status, result, error } = this._histogram;
    const totals = this._supported && result ? levelTotals(result.buckets) : undefined;
    return html`
      <uui-box>
        <section class="panel" aria-label=${this.localize.term("logExplorer_histogramLabel")}>
          ${status === "loading" ? html`<uui-loader-bar class="loader"></uui-loader-bar>` : nothing}
          <div class="top">${this.#renderToggles(totals)} ${this._supported ? this.#renderSummary() : nothing}</div>
          ${
            status === "error"
              ? html`<div class="error" role="alert">
                  <span>${this.localize.term("logExplorer_histogramError", error ?? "")}</span>
                  <uui-button
                    look="secondary"
                    compact
                    label=${this.localize.term("logExplorer_retry")}
                    @click=${() => this.#loader.retry()}
                  ></uui-button>
                </div>`
              : nothing
          }
          ${this._supported ? this.#renderBars() : nothing}
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
        /* Lets the toggles react to the panel's own width (UI brief §2.1, narrow workspaces). */
        container-type: inline-size;
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

      .top {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-3);
      }

      .levels {
        display: flex;
        flex-wrap: wrap;
        gap: var(--uui-size-space-2);
      }

      /* Pills (UI brief §5.3). */
      uui-button.level {
        --uui-button-border-radius: var(--uui-size-layout-1);
        --uui-button-height: var(--uui-size-8);
        font-size: var(--uui-type-small-size);
      }

      .toggle-content {
        display: inline-flex;
        align-items: center;
        gap: var(--uui-size-space-2);
        white-space: nowrap;
      }

      .swatch {
        width: var(--uui-size-3);
        height: var(--uui-size-3);
        border-radius: 50%;
        box-sizing: border-box;
        border: 2px solid transparent;
        background-color: var(--swatch);
      }

      .name {
        font-weight: bold;
      }

      .count {
        color: var(--uui-color-text-alt);
        font-variant-numeric: tabular-nums;
      }

      /* Off: hollow swatch, struck-through muted text. */
      uui-button.level.off .swatch {
        background-color: transparent;
        border-color: var(--swatch);
      }

      uui-button.level.off .name,
      uui-button.level.off .count {
        text-decoration: line-through;
        /* Secondary text, not the disabled colour: an off toggle is still a live control, and
           the disabled colour fails 4.5:1 on the secondary look (axe, #51). The strike-through,
           hollow swatch and aria-pressed carry the off state. */
        color: var(--uui-color-text-alt);
      }

      /* Zero-count levels stay visible but recede: a faded swatch and secondary text. Not
         opacity on the whole toggle, which took the text below 4.5:1 contrast (axe, #51). */
      uui-button.level.zero:not(.off) .swatch {
        opacity: 0.5;
      }

      uui-button.level.zero:not(.off) .name {
        color: var(--uui-color-text-alt);
      }

      .summary {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
        margin-inline-start: auto;
        color: var(--uui-color-text-alt);
        font-size: var(--uui-type-small-size);
      }

      .summary uui-tag {
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

      .chart {
        transition: opacity 120ms ease-out;
      }

      .chart.dimmed {
        opacity: 0.5;
      }

      .bars {
        position: relative;
        display: flex;
        align-items: stretch;
        gap: calc(var(--uui-size-1) / 3);
        height: var(--log-explorer-histogram-height, var(--uui-size-20));
        border-bottom: 1px solid var(--uui-color-border);
        /* Horizontal drags select a range; vertical swipes still scroll the page on touch. */
        touch-action: pan-y;
        user-select: none;
      }

      .bar {
        flex: 1 1 0;
        min-width: 0;
        display: flex;
        /* Segments stack from the baseline up, in DOM order (INFO first). */
        flex-direction: column-reverse;
        padding: 0;
        border: 0;
        background: none;
        cursor: pointer;
      }

      .bar:hover {
        background-color: var(--uui-color-surface-emphasis);
      }

      .bar:focus-visible {
        outline: 2px solid var(--uui-color-focus);
        outline-offset: 1px;
      }

      .segment {
        display: block;
        flex: none;
        width: 100%;
        background-color: var(--swatch);
      }

      .selection {
        position: absolute;
        inset-block: 0;
        background-color: var(--uui-color-selected);
        opacity: 0.25;
        pointer-events: none;
      }

      .ticks {
        position: relative;
        height: 1lh;
        margin-top: var(--uui-size-space-1);
        color: var(--uui-color-text-alt);
        font-size: var(--uui-type-small-size);
        font-variant-numeric: tabular-nums;
      }

      /* Each tick is centred on its position, except the two ends, which stay inside the panel. */
      .ticks span {
        position: absolute;
        transform: translateX(-50%);
        white-space: nowrap;
      }

      .ticks span:first-child {
        transform: none;
      }

      .ticks span:last-child {
        transform: translateX(-100%);
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

      /* Narrow workspace (under about 900 px, so this panel under about 852 px once the view's
         padding is taken off): swatch and count only; the name stays in the label and tooltip
         (UI brief §2.1). */
      @container (max-width: 851px) {
        .name {
          display: none;
        }
      }
    `,
  ];
}

declare global {
  interface HTMLElementTagNameMap {
    "log-explorer-histogram": LogExplorerHistogramElement;
  }
}
