import { classMap, css, customElement, html, nothing, state, styleMap } from "@umbraco-cms/backoffice/external/lit";
import type { UUIInputElement } from "@umbraco-cms/backoffice/external/uui";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import { FacetsService, FieldsService, type Facet, type FacetValue } from "../api/index.js";
import { sameChip } from "../query/chips.js";
import { syncButtonAria } from "../shared/button-aria.js";
import type { FilterNode } from "../query/filter-node.js";
import { LOG_EXPLORER_QUERY_CONTEXT, type LogExplorerQueryContext } from "../query/query.context.js";
import type { LogExplorerViewState } from "../query/view-state.js";
import { shortFieldName } from "../search/chip-format.js";
import { loadSettings } from "../settings/settings.js";
import { FacetsLoader, type FacetsFn, type FacetsState, type FieldsFn } from "./facets-loader.js";
import {
  excludeChip,
  includeChip,
  isValueSelected,
  matchesFieldFilter,
  pinnedFacetsFrom,
  shareRatio,
  valueLabel,
  valueText,
} from "./facets-model.js";
import {
  isPanelCollapsed,
  readPanelPreference,
  writePanelPreference,
  type PanelPreference,
} from "./panel-preference.js";
import { formatLocale } from "../shared/format-locale.js";

/** The default requests: the generated client, which carries the backoffice token. */
const facetsWithClient: FacetsFn = (alias, body, signal) => FacetsService.getFacets({ path: { alias }, body, signal });
const fieldsWithClient: FieldsFn = (alias, query, signal) =>
  FieldsService.getFields({ path: { alias }, query, signal });

/**
 * The fields panel of the Search view (UI brief §4.8, BRIEF §6.6): the top values of the pinned
 * fields, then of the most common discovered fields, for the entries the current query matches.
 *
 * - **Filtering by click:** each value row is a full-width `uui-button` whose background bar
 *   shows the value's share relative to the field's top value. Clicking it adds an include chip
 *   (two on one field OR together); clicking it again while it is an active chip removes that
 *   chip, so the row is a toggle and exposes `aria-pressed`. The compact minus button beside it
 *   adds an exclude chip. Chips go through `LogExplorerQueryContext`, which records them in the
 *   URL; the panel then refetches with them like every other panel.
 * - **Collapse:** the header's toggle collapses the panel to a thin strip with one "Show fields
 *   panel" button. An explicit choice is remembered in `localStorage`; without one the panel
 *   starts collapsed on medium and narrow workspaces (UI brief §2.1). The width is the Search
 *   view's, measured with a `ResizeObserver` on the shadow host this element renders in, because
 *   the panel's own width is what collapsing changes.
 * - **States** (UI brief §4.15): `uui-loader-bar` with the previous facets dimmed while loading;
 *   an inline error with Retry; an Approximate tag in the header when the source sampled or
 *   stopped at its scan budget. A source without the Facets feature hides the panel entirely.
 *
 * Built from `uui-box`, `uui-button`, `uui-input` and `uui-tag`; the value row is the one custom
 * composition, because no UUI element pairs a button with a proportional bar.
 *
 * Not bound to a manifest; the Search view renders it.
 *
 * @element log-explorer-fields-panel
 * @cssprop --log-explorer-fields-panel-width - Width of the expanded panel; defaults to `28ch`.
 */
@customElement("log-explorer-fields-panel")
export class LogExplorerFieldsPanelElement extends UmbLitElement {
  /** The loader's state; every change re-renders the field blocks. */
  @state()
  private _facets: FacetsState = { status: "idle" };

  /** The chips in force, for each value row's selected state. */
  @state()
  private _chips: ReadonlyArray<FilterNode> = [];

  /** Whether the active source declares the Facets feature; the panel hides without it. */
  @state()
  private _supported = false;

  /** The stored open/collapsed choice; `undefined` until the user toggles the panel. */
  @state()
  private _preference: PanelPreference = readPanelPreference();

  /** The Search view's width, for the medium-workspace default; `undefined` until measured. */
  @state()
  private _workspaceWidth: number | undefined;

  /** The filter box text; narrows the field blocks by name. */
  @state()
  private _filter = "";

  #loader = new FacetsLoader(facetsWithClient, fieldsWithClient, (facets) => (this._facets = facets));
  #context?: LogExplorerQueryContext;
  #viewState?: LogExplorerViewState;
  #alias?: string;
  #discover = false;
  #pinned?: Array<string>;
  #requestKey?: string;
  #resizeObserver?: ResizeObserver;

  constructor() {
    super();
    void loadSettings().then((settings) => {
      this.#pinned = pinnedFacetsFrom(settings);
      this.#requery();
    });
    this.consumeContext(LOG_EXPLORER_QUERY_CONTEXT, (context) => {
      this.#context = context;
      // The chips, by position, for selection and toggling; the query runs from queryState,
      // which leaves out chips the source cannot run (ADR 0016).
      this.observe(context?.state, (viewState) => (this._chips = viewState?.chips ?? []), "_observeState");
      this.observe(
        context?.queryState,
        (viewState) => {
          this.#viewState = viewState;
          this.#requery();
        },
        "_observeQueryState",
      );
      this.observe(
        context?.activeSource,
        (source) => {
          this.#alias = source?.alias;
          const features = source?.capabilities.features ?? [];
          this._supported = features.includes("facets");
          this.#discover = features.includes("fieldDiscovery");
          this.#requery();
        },
        "_observeActiveSource",
      );
    });
  }

  override connectedCallback(): void {
    super.connectedCallback();
    // The host of the shadow root this panel renders in is the Search view, whose width is the
    // workspace body's. Falls back to the parent element when rendered outside a shadow root.
    const root = this.getRootNode();
    const target = root instanceof ShadowRoot ? root.host : this.parentElement;
    if (!target) return;
    this.#resizeObserver = new ResizeObserver(([entry]) => {
      // The border box: the view's padding is part of the workspace width the breakpoints mean.
      if (entry) this._workspaceWidth = entry.borderBoxSize[0]?.inlineSize ?? entry.contentRect.width;
    });
    this.#resizeObserver.observe(target);
  }

  override disconnectedCallback(): void {
    super.disconnectedCallback();
    this.#resizeObserver?.disconnect();
    this.#resizeObserver = undefined;
    // Reconnecting queries again: the context observers re-emit on reconnect.
    this.#loader.reset();
    this.#requestKey = undefined;
  }

  /**
   * Copies the buttons' `data-pressed`/`data-expanded` onto their focusable inner buttons, and
   * reflects the collapsed state as a `collapsed` attribute so the Search view can hide the
   * resize divider while the panel is a strip (ADR 0025).
   */
  protected override updated(): void {
    void syncButtonAria(this.renderRoot);
    this.toggleAttribute("collapsed", this.#collapsed);
  }

  get #collapsed(): boolean {
    return isPanelCollapsed(this._preference, this._workspaceWidth);
  }

  /** Requests facets again only when the request inputs changed. */
  #requery(): void {
    // Hidden from layout as well as from rendering, so the results list takes the full width.
    this.hidden = !this._supported;
    if (!this.#alias || !this.#viewState || !this._supported || !this.#pinned) {
      this.#loader.reset();
      this.#requestKey = undefined;
      return;
    }

    // Sort does not change the counts; leave it out of the key so toggling it does not refetch.
    const key = JSON.stringify([this.#alias, this.#discover, this.#pinned, { ...this.#viewState, sort: undefined }]);
    if (key === this.#requestKey) return;
    this.#requestKey = key;
    this.#loader.load(this.#alias, { state: this.#viewState, pinned: this.#pinned, discover: this.#discover });
  }

  #setCollapsed(collapsed: boolean): void {
    this._preference = collapsed ? "collapsed" : "open";
    writePanelPreference(this._preference);
  }

  #onValueClick(field: string, value: unknown): void {
    const context = this.#context;
    if (!context) return;
    const chip = includeChip(field, value);
    const index = this._chips.findIndex((existing) => sameChip(existing, chip));
    if (index === -1) context.addChips([chip]);
    else context.removeChip(index);
  }

  #onExclude(field: string, value: unknown): void {
    this.#context?.addChips([excludeChip(field, value)]);
  }

  #renderStrip() {
    return html`
      <uui-box class="strip">
        <uui-button
          compact
          look="secondary"
          data-expanded="false"
          label=${this.localize.term("logExplorer_fieldsShow")}
          title=${this.localize.term("logExplorer_fieldsShow")}
          @click=${() => this.#setCollapsed(false)}
        >
          <uui-icon name="icon-navigation-right"></uui-icon>
        </uui-button>
      </uui-box>
    `;
  }

  #renderHeader() {
    const result = this._facets.result;
    let approximate: unknown = nothing;
    if (result?.approximate) {
      const format = new Intl.DateTimeFormat(formatLocale(this.localize.lang()), {
        dateStyle: "short",
        timeStyle: "short",
      });
      const hint = this.localize.term(
        "logExplorer_fieldsApproximateHint",
        format.format(new Date(result.scannedRange.from)),
        format.format(new Date(result.scannedRange.to)),
      );
      approximate = html`<uui-tag look="secondary" title=${hint}
        >${this.localize.term("logExplorer_histogramApproximate")}</uui-tag
      >`;
    }
    return html`
      <div class="header">
        <div class="titles">
          <strong>${this.localize.term("logExplorer_fieldsHeader")}</strong>
          <span class="subheader">${this.localize.term("logExplorer_fieldsSubheader")}</span>
        </div>
        ${approximate}
        <uui-button
          compact
          data-expanded="true"
          label=${this.localize.term("logExplorer_fieldsHide")}
          title=${this.localize.term("logExplorer_fieldsHide")}
          @click=${() => this.#setCollapsed(true)}
        >
          <uui-icon name="icon-navigation-left"></uui-icon>
        </uui-button>
      </div>
    `;
  }

  #renderFacet(facet: Facet) {
    const label = shortFieldName(facet.field, (key, ...args) => this.localize.term(key, ...args));
    if (!matchesFieldFilter(facet.field, label, this._filter)) return nothing;

    const lang = formatLocale(this.localize.lang());
    const values = facet.topValues;
    const top = Math.max(0, ...values.map((value) => value.count));
    const presence = Math.round(facet.presenceRatio * 100).toLocaleString(lang);
    return html`
      <section class="facet" aria-label=${label}>
        <div class="facet-header">
          <strong class="field" title=${facet.field}>${label}</strong>
          <span class="presence">${this.localize.term("logExplorer_fieldsPresence", presence)}</span>
        </div>
        <ul>
          ${values.map((value) => this.#renderValue(facet.field, label, value, top, lang))}
        </ul>
      </section>
    `;
  }

  #renderValue(field: string, fieldLabel: string, value: FacetValue, top: number, lang: string) {
    const full = valueText(value.value);
    const count = value.count.toLocaleString(lang);
    const selected = isValueSelected(this._chips, field, value.value);
    return html`
      <li class=${classMap({ value: true, selected })}>
        <!-- uui-button sizes its label slot to the content, so a bar or count inside it cannot
             span the row. The share bar and count sit behind and over the button instead, inside
             a wrapper the button fills; both ignore the pointer, so the whole row still clicks. -->
        <div class="include-wrap">
          <span class="share" style=${styleMap({ width: `${shareRatio(value.count, top) * 100}%` })}></span>
          <uui-button
            class="include"
            compact
            data-pressed=${String(selected)}
            label=${this.localize.term("logExplorer_fieldsInclude", fieldLabel, full, count)}
            title=${full}
            @click=${() => this.#onValueClick(field, value.value)}
          >
            <span class="value-label">${valueLabel(field, value.value)}</span>
          </uui-button>
          <span class="count" aria-hidden="true">${count}</span>
        </div>
        <uui-button
          class="exclude"
          compact
          label=${this.localize.term("logExplorer_fieldsExclude", fieldLabel, full)}
          title=${this.localize.term("logExplorer_fieldsExclude", fieldLabel, full)}
          @click=${() => this.#onExclude(field, value.value)}
        >
          <uui-icon name="icon-badge-remove"></uui-icon>
        </uui-button>
      </li>
    `;
  }

  #renderBody() {
    const { status, result, error } = this._facets;
    // Only fields with values get a block (UI brief §4.8).
    const facets = (result?.facets ?? []).filter((facet) => facet.topValues.length > 0);
    const blocks = facets.map((facet) => this.#renderFacet(facet)).filter((block) => block !== nothing);

    let empty: unknown = nothing;
    if (status === "loaded" && facets.length === 0) {
      empty = html`<p class="message">${this.localize.term("logExplorer_fieldsEmpty")}</p>`;
    } else if (facets.length > 0 && blocks.length === 0) {
      empty = html`<p class="message">${this.localize.term("logExplorer_fieldsNoMatch")}</p>`;
    }

    return html`
      ${
        status === "error"
          ? html`<div class="error" role="alert">
              <span>${this.localize.term("logExplorer_fieldsError", error ?? "")}</span>
              <uui-button
                look="secondary"
                compact
                label=${this.localize.term("logExplorer_retry")}
                @click=${() => this.#loader.retry()}
              ></uui-button>
            </div>`
          : nothing
      }
      <div class=${classMap({ list: true, dimmed: status === "loading" })}>${blocks} ${empty}</div>
    `;
  }

  /**
   * Renders the strip when collapsed, otherwise the header, filter box and field blocks.
   *
   * @returns The template.
   */
  override render() {
    if (!this._supported) return nothing;
    if (this.#collapsed) return this.#renderStrip();
    return html`
      <uui-box>
        <section class="panel" aria-label=${this.localize.term("logExplorer_fieldsHeader")}>
          ${this._facets.status === "loading" ? html`<uui-loader-bar class="loader"></uui-loader-bar>` : nothing}
          ${this.#renderHeader()}
          <uui-input
            class="filter"
            type="search"
            label=${this.localize.term("logExplorer_fieldsFilterLabel")}
            placeholder=${this.localize.term("logExplorer_fieldsFilterLabel")}
            .value=${this._filter}
            @input=${(event: Event) => (this._filter = String((event.target as UUIInputElement).value ?? ""))}
          ></uui-input>
          ${this.#renderBody()}
        </section>
      </uui-box>
    `;
  }

  static override styles = [
    UmbTextStyles,
    css`
      :host {
        display: flex;
        flex-direction: column;
        min-height: 0;
      }

      :host([hidden]) {
        display: none;
      }

      uui-box {
        flex: 1;
        min-height: 0;
        position: relative;
        /* As in the results panel: a one-row grid gives the slot, and so .panel, a definite height.
           The explicit column stops long values widening the implicit (auto) column past the box. */
        display: grid;
        grid-template-rows: minmax(0, 1fr);
        grid-template-columns: minmax(0, 1fr);
        --uui-box-default-padding: var(--uui-size-space-3);
      }

      /* The width the divider sets (ADR 0025), capped at the host, which the Search view keeps
         from squeezing the results. */
      uui-box:not(.strip) {
        width: var(--log-explorer-fields-panel-width, 28ch);
        max-width: 100%;
        /* The divider measures the panel's outer width, so the width includes the border. */
        box-sizing: border-box;
      }

      .panel {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-3);
        height: 100%;
        min-height: 0;
      }

      .loader {
        position: absolute;
        inset: 0 0 auto 0;
      }

      .header {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
      }

      .titles {
        display: flex;
        flex-direction: column;
        flex: 1;
        min-width: 0;
      }

      .subheader,
      .presence {
        color: var(--uui-color-text-alt);
        font-size: var(--uui-type-small-size);
      }

      .header uui-tag {
        font-size: var(--uui-type-small-size);
      }

      .filter {
        width: 100%;
      }

      .error {
        display: flex;
        flex-direction: column;
        align-items: flex-start;
        gap: var(--uui-size-space-2);
        padding: var(--uui-size-space-2) var(--uui-size-space-3);
        border: 1px solid var(--uui-color-danger-standalone);
        border-radius: var(--uui-border-radius);
        color: var(--uui-color-danger-standalone);
      }

      .list {
        flex: 1;
        min-height: 0;
        overflow-y: auto;
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-4);
        transition: opacity 120ms ease-out;
      }

      .list.dimmed {
        opacity: 0.5;
      }

      .message {
        margin: 0;
        color: var(--uui-color-text-alt);
      }

      .facet-header {
        display: flex;
        align-items: baseline;
        justify-content: space-between;
        gap: var(--uui-size-space-2);
        margin-bottom: var(--uui-size-space-1);
      }

      .field {
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }

      .presence {
        flex: none;
      }

      ul {
        list-style: none;
        margin: 0;
        padding: 0;
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-1);
      }

      .value {
        display: flex;
        align-items: stretch;
        gap: var(--uui-size-space-1);
      }

      /* The include button fills this; the share bar sits behind it and the count over it. A
         size container so the label can leave room for the count (cqi below). */
      .include-wrap {
        position: relative;
        flex: 1;
        min-width: 0;
        container-type: inline-size;
        /* Room kept clear at the right of the label for counts up to six digits. */
        --log-explorer-fields-count-space: 8ch;
      }

      uui-button.include {
        width: 100%;
        --uui-button-content-align: left;
        font-size: var(--uui-type-small-size);
      }

      .share {
        position: absolute;
        inset-block: 0;
        left: 0;
        border-radius: var(--uui-border-radius);
        background-color: var(--uui-color-selected);
        opacity: 0.12;
        pointer-events: none;
      }

      .value.selected .share {
        opacity: 0.3;
      }

      .value-label {
        display: block;
        max-width: calc(100cqi - var(--log-explorer-fields-count-space));
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        text-align: start;
      }

      .value.selected .value-label {
        font-weight: bold;
      }

      .count {
        position: absolute;
        inset-block: 0;
        right: var(--uui-size-space-2);
        display: flex;
        align-items: center;
        color: var(--uui-color-text-alt);
        font-size: var(--uui-type-small-size);
        font-variant-numeric: tabular-nums;
        pointer-events: none;
      }
    `,
  ];
}

declare global {
  interface HTMLElementTagNameMap {
    "log-explorer-fields-panel": LogExplorerFieldsPanelElement;
  }
}
