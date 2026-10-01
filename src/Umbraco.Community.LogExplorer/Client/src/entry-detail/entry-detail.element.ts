import {
  css,
  customElement,
  html,
  nothing,
  property,
  query,
  state,
  styleMap,
  type PropertyValues,
} from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UMB_NOTIFICATION_CONTEXT, type UmbNotificationContext } from "@umbraco-cms/backoffice/notification";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import type { LogRecord } from "../api/index.js";
import type { FilterNode } from "../query/filter-node.js";
import { LOG_EXPLORER_QUERY_CONTEXT, type LogExplorerQueryContext } from "../query/query.context.js";
import { tokeniseMessage } from "../results/message-tokens.js";
import { levelOf } from "../results/row-format.js";
import { LEVEL_BADGE_STYLES, LEVEL_TOKENS } from "../shared/level-tokens.js";
import { formatDetailTime, machineOf, messageValueChip, recordJson, samePatternChip } from "./entry-format.js";
import {
  buildPropertyTree,
  formatScalar,
  isFilterable,
  propertyChip,
  visibleRows,
  type PropertyNode,
} from "./property-tree.js";
import { renderStackTrace, splitStackTrace } from "./stack-trace.js";

/**
 * Fired when the drawer asks to be closed (its close button, or Escape inside it). Bubbles and is
 * composed; the Search view, which owns the open record, listens for it.
 */
export class LogExplorerEntryCloseEvent extends Event {
  /** The event type. */
  static readonly TYPE = "log-explorer-entry-close";

  constructor() {
    super(LogExplorerEntryCloseEvent.TYPE, { bubbles: true, composed: true });
  }
}

/**
 * The entry detail drawer (UI brief §4.11, BRIEF §6.8): one log entry in full, over the right of
 * the Search view, leaving the results usable on the left.
 *
 * A custom element rather than the backoffice sidebar modal (`UMB_MODAL_MANAGER_CONTEXT`,
 * `type: 'sidebar'`): `uui-modal-sidebar` opens a native `<dialog>` with `showModal()`, which
 * makes the rest of the page inert behind a backdrop, so the results could not be clicked to
 * switch entry; it also covers the backoffice header. The drawer is composed of `uui-box`
 * (headline and header-actions slots), `uui-scroll-container`, `uui-table`, `umb-code-block`,
 * `uui-tag` and `uui-button`s.
 *
 * Sections, top to bottom: header (level badge, local date and time, machine, close), the
 * rendered message whose property values add include chips, the actions (Same pattern, Copy as
 * JSON; Same request and Around this arrive with #42), the message template, the properties as a
 * typed tree with `+`/`-` buttons, and the exception with framework frames collapsed. Chips are
 * added through `LogExplorerQueryContext`. Log text is rendered as text nodes only.
 *
 * Positioning is the host's job: the Search view places it.
 *
 * Not bound to a manifest; the Search view renders it.
 *
 * @element log-explorer-entry-detail
 * @fires log-explorer-entry-close - {@link LogExplorerEntryCloseEvent}, on the close button or Escape.
 */
@customElement("log-explorer-entry-detail")
export class LogExplorerEntryDetailElement extends UmbLitElement {
  /**
   * The entry to show. Setting a different record (by id) collapses the property tree and the
   * framework frames again and scrolls back to the top; `undefined` renders nothing.
   */
  @property({ attribute: false })
  record: LogRecord | undefined;

  /** {@link PropertyNode.key}s of the expanded objects and arrays. */
  @state()
  private _expanded: ReadonlySet<string> = new Set();

  /** Whether the stack trace shows framework frames. */
  @state()
  private _showFramework = false;

  @query("#heading")
  private _heading?: HTMLElement;

  @query(".content")
  private _content?: HTMLElement;

  #context?: LogExplorerQueryContext;
  #notifications?: UmbNotificationContext;
  #tree: Array<PropertyNode> = [];

  constructor() {
    super();
    this.consumeContext(LOG_EXPLORER_QUERY_CONTEXT, (context) => (this.#context = context));
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (context) => (this.#notifications = context));
    // Escape anywhere inside the drawer closes it. Listening on the host catches keydowns from
    // the shadow tree too (they are composed), and a control that handles Escape itself can
    // still stop it with preventDefault.
    this.addEventListener("keydown", (event) => {
      if (event.key === "Escape" && !event.defaultPrevented && this.record) {
        event.preventDefault();
        this.#close();
      }
    });
  }

  /**
   * Moves focus to the drawer's heading (level, time and machine), so a screen reader announces
   * the entry and Tab continues to the close button. Waits for a pending render first.
   */
  async focusHeading(): Promise<void> {
    await this.updateComplete;
    // uui-box renders its header `display: none` until a `slotchange` tells it the headline slot
    // has content, which lands a frame or so after the drawer first renders; focusing a hidden
    // element silently does nothing. So wait (a few frames at most) until the heading has a box.
    for (let frame = 0; frame < 5 && !this._heading?.getClientRects().length; frame++) {
      await new Promise((resolve) => requestAnimationFrame(resolve));
    }
    this._heading?.focus();
  }

  protected override willUpdate(changed: PropertyValues<this>): void {
    super.willUpdate(changed);
    if (changed.has("record") && changed.get("record")?.id !== this.record?.id) {
      this._expanded = new Set();
      this._showFramework = false;
      this.#tree = this.record ? buildPropertyTree(this.record.attributes) : [];
    }
  }

  protected override updated(changed: PropertyValues<this>): void {
    super.updated(changed);
    if (changed.has("record") && changed.get("record")?.id !== this.record?.id && this._content) {
      this._content.scrollTop = 0;
    }
  }

  #close(): void {
    this.dispatchEvent(new LogExplorerEntryCloseEvent());
  }

  #addChip(chip: FilterNode | undefined): void {
    if (chip) this.#context?.addChips([chip]);
  }

  #samePattern(record: LogRecord): void {
    const chip = samePatternChip(record);
    if (!chip) return;
    this.#addChip(chip);
    this.#notifications?.peek("positive", {
      data: { message: this.localize.term("logExplorer_detailSamePatternApplied", record.messageTemplate ?? "") },
    });
  }

  /**
   * Not `uui-button-copy-text`: it reports a failed write only with `console.error`, and the
   * failure needs a notification like every other copy in the backoffice (`umb-code-block` does
   * the same).
   */
  async #copyJson(record: LogRecord): Promise<void> {
    try {
      await navigator.clipboard.writeText(recordJson(record));
      this.#notifications?.peek("positive", { data: { message: this.localize.term("logExplorer_detailCopied") } });
    } catch {
      this.#notifications?.peek("danger", { data: { message: this.localize.term("logExplorer_detailCopyFailed") } });
    }
  }

  #toggle(key: string): void {
    const next = new Set(this._expanded);
    if (!next.delete(key)) next.add(key);
    this._expanded = next;
  }

  /**
   * Renders the drawer for {@link record}, or nothing without one.
   *
   * @returns The template.
   */
  override render() {
    const record = this.record;
    if (!record) return nothing;

    const level = levelOf(record.severityNumber);
    const machine = machineOf(record);
    return html`
      <section class="drawer" role="region" aria-labelledby="heading">
        <uui-box headline-variant="h2">
          <span slot="headline" id="heading" class="heading" tabindex="-1">
            <uui-tag class="level level-${level ?? "none"}">${level ? level.toUpperCase() : "—"}</uui-tag>
            <span class="time">${formatDetailTime(record.timestamp)}</span>
            ${machine ? html`<span class="machine">${machine}</span>` : nothing}
          </span>
          <uui-button
            slot="header-actions"
            compact
            label=${this.localize.term("logExplorer_detailClose")}
            title=${this.localize.term("logExplorer_detailClose")}
            @click=${this.#close}
          >
            <uui-icon name="icon-wrong"></uui-icon>
          </uui-button>
          <uui-scroll-container class="content">
            ${this.#renderMessage(record)} ${this.#renderActions(record)} ${this.#renderTemplate(record)}
            ${this.#renderProperties()} ${this.#renderException(record)}
          </uui-scroll-container>
        </uui-box>
      </section>
    `;
  }

  #renderMessage(record: LogRecord) {
    const tokens = tokeniseMessage(record.body, record.messageTemplate, record.attributes);
    if (tokens.length === 0) return nothing;
    // Native buttons rather than uui-button: these sit inside running text and must wrap with it;
    // uui-button is an inline-flex action button with its own padding and minimum height.
    return html`<p class="message">
      ${tokens.map((token) => {
        if (!token.field) return token.text;
        const chip = messageValueChip(record, token.field);
        if (!chip) return html`<span class="value">${token.text}</span>`;
        const label = this.localize.term("logExplorer_detailFilterOn", token.field);
        return html`<button
          type="button"
          class="value"
          title=${label}
          aria-label=${`${label}: ${token.text}`}
          @click=${() => this.#addChip(chip)}
        >
          ${token.text}
        </button>`;
      })}
    </p>`;
  }

  #renderActions(record: LogRecord) {
    // Same request and Around this (UI brief §4.11 section 3) come with #42 and take the first
    // row of this grid then.
    return html`
      <div class="actions">
        <uui-button
          look="secondary"
          label=${this.localize.term("logExplorer_actionSamePattern")}
          ?disabled=${!record.messageTemplate}
          @click=${() => this.#samePattern(record)}
        >
          <uui-icon name="icon-layers"></uui-icon>
          ${this.localize.term("logExplorer_actionSamePattern")}
        </uui-button>
        <uui-button
          look="secondary"
          label=${this.localize.term("logExplorer_actionCopyJson")}
          @click=${() => this.#copyJson(record)}
        >
          <uui-icon name="icon-documents"></uui-icon>
          ${this.localize.term("logExplorer_actionCopyJson")}
        </uui-button>
      </div>
    `;
  }

  #renderTemplate(record: LogRecord) {
    if (!record.messageTemplate) return nothing;
    return html`
      <section>
        <h3>${this.localize.term("logExplorer_detailMessageTemplate")}</h3>
        <umb-code-block copy>${record.messageTemplate}</umb-code-block>
      </section>
    `;
  }

  #renderProperties() {
    return html`
      <section>
        <h3>${this.localize.term("logExplorer_detailProperties")}</h3>
        ${
          this.#tree.length === 0
            ? html`<p class="none">${this.localize.term("logExplorer_detailNoProperties")}</p>`
            : html`<uui-table class="properties">
                <uui-table-head>
                  <uui-table-head-cell>${this.localize.term("logExplorer_detailPropertyName")}</uui-table-head-cell>
                  <uui-table-head-cell>${this.localize.term("logExplorer_detailPropertyValue")}</uui-table-head-cell>
                  <uui-table-head-cell
                    ><uui-visually-hidden
                      >${this.localize.term("logExplorer_detailPropertyFilter")}</uui-visually-hidden
                    ></uui-table-head-cell
                  >
                </uui-table-head>
                ${visibleRows(this.#tree, this._expanded).map(({ node, depth }) => this.#renderProperty(node, depth))}
              </uui-table>`
        }
      </section>
    `;
  }

  #renderProperty(node: PropertyNode, depth: number) {
    const container = node.kind === "object" || node.kind === "array";
    const open = this._expanded.has(node.key);
    const name = node.path ?? node.name;
    const text = container ? "" : formatScalar(node.value);
    return html`
      <uui-table-row>
        <uui-table-cell>
          <div class="name" style=${styleMap({ "--depth": String(depth) })}>
            ${
              container && node.children.length > 0
                ? html`<uui-button
                    compact
                    class="expand"
                    label=${this.localize.term(open ? "logExplorer_detailCollapse" : "logExplorer_detailExpand", name)}
                    aria-expanded=${open ? "true" : "false"}
                    @click=${() => this.#toggle(node.key)}
                  >
                    <uui-symbol-expand ?open=${open}></uui-symbol-expand>
                  </uui-button>`
                : html`<span class="expand-spacer"></span>`
            }
            <span title=${name}>${node.name}</span>
          </div>
        </uui-table-cell>
        <uui-table-cell>
          ${
            container
              ? html`<span class="summary">${this.#summary(node)}</span>`
              : // pre-wrap lives on this span only, so the template's own whitespace around it
                // is not rendered.
                html`<span class="scalar kind-${node.kind}">${text}</span>`
          }
        </uui-table-cell>
        <uui-table-cell class="filter">
          ${node.path && isFilterable(node.value) ? this.#renderFilterButtons(node.path, node.value, text) : nothing}
        </uui-table-cell>
      </uui-table-row>
    `;
  }

  #summary(node: PropertyNode): string {
    const key = node.kind === "array" ? "logExplorer_detailArraySummary" : "logExplorer_detailObjectSummary";
    return this.localize.term(key, node.children.length);
  }

  #renderFilterButtons(path: string, value: string | number | boolean, text: string) {
    const include = this.localize.term("logExplorer_detailInclude", path, text);
    const exclude = this.localize.term("logExplorer_detailExclude", path, text);
    return html`
      <uui-button
        compact
        label=${include}
        title=${include}
        @click=${() => this.#addChip(propertyChip(path, value, false))}
      >
        <uui-icon name="icon-badge-add"></uui-icon>
      </uui-button>
      <uui-button
        compact
        label=${exclude}
        title=${exclude}
        @click=${() => this.#addChip(propertyChip(path, value, true))}
      >
        <uui-icon name="icon-badge-remove"></uui-icon>
      </uui-button>
    `;
  }

  #renderException(record: LogRecord) {
    const exception = record.exception;
    if (!exception || (!exception.type && !exception.message && !exception.stackTrace)) return nothing;

    const runs = splitStackTrace(exception.stackTrace);
    const hasFramework = runs.some((run) => run.framework);
    const stack = renderStackTrace(runs, this._showFramework, (count) =>
      this.localize.term("logExplorer_detailFrameworkFramesHidden", count),
    );
    return html`
      <section class="exception">
        <h3>${this.localize.term("logExplorer_detailException")}</h3>
        ${exception.type ? html`<p class="exception-type">${exception.type}</p>` : nothing}
        ${exception.message ? html`<p class="exception-message">${exception.message}</p>` : nothing}
        ${
          exception.stackTrace
            ? html`
                ${
                  hasFramework
                    ? html`<uui-button
                        compact
                        look="outline"
                        class="frames-toggle"
                        label=${this.localize.term(
                          this._showFramework
                            ? "logExplorer_detailHideFrameworkFrames"
                            : "logExplorer_detailShowFrameworkFrames",
                        )}
                        aria-expanded=${this._showFramework ? "true" : "false"}
                        @click=${() => (this._showFramework = !this._showFramework)}
                      ></uui-button>`
                    : nothing
                }
                <umb-code-block copy>${stack}</umb-code-block>
              `
            : nothing
        }
      </section>
    `;
  }

  static override styles = [
    UmbTextStyles,
    LEVEL_TOKENS,
    LEVEL_BADGE_STYLES,
    css`
      :host {
        display: flex;
        flex-direction: column;
        min-height: 0;
        background-color: var(--uui-color-surface);
        border-left: 1px solid var(--uui-color-border);
        box-shadow: var(--uui-shadow-depth-3);
      }

      .drawer {
        flex: 1;
        min-height: 0;
        display: flex;
        flex-direction: column;
      }

      /* As a two-row grid, uui-box's header (shadow #header) takes its height and the default
         slot the rest, which gives the scroll container below a definite height to scroll in. */
      uui-box {
        flex: 1;
        min-height: 0;
        display: grid;
        grid-template-rows: auto minmax(0, 1fr);
        /* Without this the column grows to the longest unbreakable line (a stack frame in its
           pre) and the drawer spills out of the workspace; the code blocks scroll instead. */
        grid-template-columns: minmax(0, 1fr);
        border: 0;
        --uui-box-border-radius: 0;
        --uui-box-default-padding: 0;
      }

      .heading {
        display: inline-flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-3);
      }

      .heading:focus-visible {
        outline: 2px solid var(--uui-color-focus);
        outline-offset: 2px;
      }

      .time {
        font-weight: bold;
        font-variant-numeric: tabular-nums;
      }

      .machine {
        font-weight: normal;
        color: var(--uui-color-text-alt);
      }

      .level {
        border-color: transparent;
      }

      .content {
        height: 100%;
        box-sizing: border-box;
        padding: var(--uui-size-space-4) var(--uui-size-space-5);
      }

      section + section,
      .actions + section {
        margin-top: var(--uui-size-space-5);
      }

      h3 {
        margin: 0 0 var(--uui-size-space-2);
        font-size: var(--uui-type-small-size);
        font-weight: bold;
        color: var(--uui-color-text-alt);
      }

      .message {
        margin: 0;
        font-size: var(--uui-type-h5-size);
        line-height: 1.5;
        overflow-wrap: anywhere;
      }

      .message .value {
        font: inherit;
        font-weight: bold;
        color: var(--uui-color-interactive-emphasis);
      }

      /* The accent tint of UI brief §4.11 section 2; no UUI token is a tint of the accent, so it
         is mixed from the accent and the surface. */
      .message button.value {
        display: inline;
        padding: 0 var(--uui-size-space-1);
        margin: 0;
        border: 0;
        border-radius: var(--uui-border-radius);
        background-color: color-mix(in srgb, var(--uui-color-interactive-emphasis) 12%, var(--uui-color-surface));
        cursor: pointer;
        text-align: inherit;
      }

      .message button.value:hover {
        background-color: color-mix(in srgb, var(--uui-color-interactive-emphasis) 22%, var(--uui-color-surface));
      }

      .message button.value:focus-visible {
        outline: 2px solid var(--uui-color-focus);
        outline-offset: 1px;
      }

      .actions {
        display: grid;
        grid-template-columns: repeat(2, minmax(0, 1fr));
        gap: var(--uui-size-space-3);
        margin-top: var(--uui-size-space-4);
      }

      .actions uui-icon {
        margin-right: var(--uui-size-space-2);
      }

      .none {
        margin: 0;
        color: var(--uui-color-text-alt);
      }

      .properties {
        font-size: var(--uui-type-small-size);
      }

      /* Names are short and values long; give the values the room. */
      uui-table-head-cell:first-child {
        width: 35%;
      }

      /* Compact rows (UI brief §1.6): uui-table-cell defaults to a 48 px minimum height. */
      uui-table-head-cell,
      uui-table-cell {
        --uui-table-cell-height: var(--uui-size-8);
        padding: var(--uui-size-space-1) var(--uui-size-space-2);
        vertical-align: top;
      }

      .name {
        display: flex;
        align-items: flex-start;
        gap: var(--uui-size-space-1);
        padding-left: calc(var(--depth, 0) * var(--uui-size-space-4));
        color: var(--uui-color-text-alt);
        overflow-wrap: anywhere;
      }

      .expand,
      .expand-spacer {
        flex: none;
        width: var(--uui-size-6);
      }

      .expand {
        --uui-button-height: var(--uui-size-6);
        --uui-button-padding-left-factor: 0;
        --uui-button-padding-right-factor: 0;
      }

      .scalar {
        font-family: var(--uui-font-monospace, monospace);
        white-space: pre-wrap;
        overflow-wrap: anywhere;
      }

      .kind-number,
      .kind-boolean,
      .kind-null {
        color: var(--uui-color-interactive-emphasis);
      }

      .summary {
        font-family: var(--uui-font-family);
        color: var(--uui-color-text-alt);
      }

      .filter {
        white-space: nowrap;
        text-align: right;
        width: 1%;
      }

      .filter uui-button {
        --uui-button-height: var(--uui-size-6);
      }

      /* Danger tint (UI brief §4.11 section 6), mixed like the accent tint above. */
      .exception {
        padding: var(--uui-size-space-3) var(--uui-size-space-4);
        border-left: var(--uui-size-1) solid var(--uui-color-danger);
        border-radius: var(--uui-border-radius);
        background-color: color-mix(in srgb, var(--uui-color-danger) 8%, var(--uui-color-surface));
      }

      .exception-type {
        margin: 0;
        font-weight: bold;
        overflow-wrap: anywhere;
      }

      .exception-message {
        margin: var(--uui-size-space-1) 0 0;
        overflow-wrap: anywhere;
      }

      .frames-toggle {
        margin: var(--uui-size-space-3) 0 var(--uui-size-space-2);
      }

      .exception umb-code-block {
        margin-top: var(--uui-size-space-2);
        font-size: var(--uui-type-small-size);
      }
    `,
  ];
}

declare global {
  interface HTMLElementTagNameMap {
    "log-explorer-entry-detail": LogExplorerEntryDetailElement;
  }
  interface HTMLElementEventMap {
    "log-explorer-entry-close": LogExplorerEntryCloseEvent;
  }
}
