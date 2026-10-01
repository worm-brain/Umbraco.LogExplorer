import { css, customElement, html, property, query, state } from "@umbraco-cms/backoffice/external/lit";
import type { UUIPopoverContainerElement } from "@umbraco-cms/backoffice/external/uui";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import type { FilterNode } from "../query/filter-node.js";
import type { LogExplorerChipEditorElement } from "./chip-editor.element.js";
import { draftFromChip } from "./chip-edit.js";
import {
  LogExplorerChipChangeEvent,
  LogExplorerChipEditCancelEvent,
  LogExplorerChipRemoveEvent,
} from "./chip-events.js";
import { describeChip, type ChipKind } from "./chip-format.js";
import "./chip-editor.element.js";

/** Tag look and colour per chip kind (UI brief §4.4: accent, danger and neutral tints). */
const TAG_STYLE: Readonly<Record<ChipKind, { look: string; color: string }>> = {
  include: { look: "outline", color: "default" },
  exclude: { look: "outline", color: "danger" },
  text: { look: "secondary", color: "default" },
};

/**
 * One filter chip in the search box (UI brief §4.4): a pill `uui-tag` holding the chip's label
 * and a compact remove button. The full description is the tooltip and the accessible names.
 *
 * Pressing the label opens `log-explorer-chip-editor` in a `uui-popover-container`, the same
 * popover the time range picker uses. The label is a native `<button>` styled to inherit the
 * tag's text: a `uui-button` brings its own height and padding, which would break the pill.
 * Chips the editor cannot represent (and/or groups, `in`, `matches`) show their label as text.
 *
 * A chip the active source cannot run ({@link unsupported}) stays in place but is drawn disabled
 * (reduced opacity, label struck through) with the tooltip "Not supported by {source}" (UI brief
 * §4.4); it can still be edited into something the source supports, or removed. A chip the source
 * runs but "Show query" cannot express ({@link notShownIn}) looks like any other chip; only its
 * tooltip says it is missing from the generated query (ADR 0016).
 *
 * The chip does not write the context. The search box listens for its events and knows which
 * chip, by position, fired them.
 *
 * Not bound to a manifest; `log-explorer-search-box` renders one per chip.
 *
 * @element log-explorer-filter-chip
 * @fires log-explorer-chip-change - {@link LogExplorerChipChangeEvent}, from the editor's Save.
 * @fires log-explorer-chip-remove - {@link LogExplorerChipRemoveEvent}, from the remove button.
 */
@customElement("log-explorer-filter-chip")
export class LogExplorerFilterChipElement extends UmbLitElement {
  /** The chip to show; a new chip re-renders the label and resets the editor. */
  @property({ attribute: false })
  chip?: FilterNode;

  /** Whether the active source cannot run this chip; draws it disabled. */
  @property({ type: Boolean, reflect: true })
  unsupported = false;

  /**
   * The source's query language when the generated query leaves this chip out although the
   * source runs it; adds a note to the tooltip. Empty when the query shows the chip.
   */
  @property({ attribute: false })
  notShownIn = "";

  /** The active source's display name, for the unsupported tooltip. */
  @property({ attribute: false })
  sourceName = "";

  /** Whether the editor popover is open; drives `aria-expanded`. */
  @state()
  private _open = false;

  @query("#editor")
  private _popover?: UUIPopoverContainerElement;

  @query("log-explorer-chip-editor")
  private _editor?: LogExplorerChipEditorElement;

  @query(".label")
  private _label?: HTMLElement;

  /** Opens the editor, for keyboard and programmatic use. Does nothing for a non-editable chip. */
  openEditor(): void {
    if (this.chip && draftFromChip(this.chip)) this._popover?.showPopover();
  }

  #onToggle(event: Event): void {
    this._open = (event as ToggleEvent).newState === "open";
    if (this._open) {
      this._editor?.reset();
      void this._editor?.focusFirstField();
    }
  }

  #close(): void {
    this._popover?.hidePopover();
    this._label?.focus();
  }

  #onChange(): void {
    // Let the change event carry on to the search box; only close the popover here.
    this.#close();
  }

  #onCancel(event: LogExplorerChipEditCancelEvent): void {
    event.stopPropagation();
    this.#close();
  }

  #remove(): void {
    this.dispatchEvent(new LogExplorerChipRemoveEvent());
  }

  /**
   * Renders the pill, and the editor popover for an editable chip.
   *
   * @returns The template.
   */
  override render() {
    if (!this.chip) return html``;
    const term = (key: string, ...args: Array<string>) => this.localize.term(key, ...args);
    const display = describeChip(this.chip, term);
    const style = TAG_STYLE[display.kind];
    const editable = draftFromChip(this.chip) !== undefined;
    // The accessible names say why the chip is disabled, so screen readers hear it too.
    const description = this.unsupported
      ? this.localize.term("logExplorer_chipUnsupportedDescription", display.description, this.sourceName)
      : display.description;
    const title = this.unsupported
      ? this.localize.term("logExplorer_chipUnsupported", this.sourceName)
      : this.notShownIn
        ? this.localize.term("logExplorer_chipNotShown", description, this.notShownIn)
        : description;

    return html`
      <uui-tag
        class="${display.kind}${this.unsupported ? " unsupported" : ""}"
        look=${style.look}
        color=${style.color}
        title=${title}
      >
        <span class="content">
          ${
            editable
              ? html`<button
                  class="label"
                  type="button"
                  popovertarget="editor"
                  aria-haspopup="dialog"
                  aria-expanded=${this._open ? "true" : "false"}
                  aria-label=${this.localize.term("logExplorer_chipEdit", description)}
                >
                  ${display.label}
                </button>`
              : html`<span class="label">${display.label}</span>`
          }
          <uui-button
            compact
            look="default"
            label=${this.localize.term("logExplorer_chipRemove", description)}
            @click=${this.#remove}
          >
            <umb-icon name="icon-wrong"></umb-icon>
          </uui-button>
        </span>
      </uui-tag>
      ${
        editable
          ? html`<uui-popover-container id="editor" placement="bottom-start" @toggle=${this.#onToggle}>
              <umb-popover-layout>
                <log-explorer-chip-editor
                  .chip=${this.chip}
                  @log-explorer-chip-change=${this.#onChange}
                  @log-explorer-chip-edit-cancel=${this.#onCancel}
                ></log-explorer-chip-editor>
              </umb-popover-layout>
            </uui-popover-container>`
          : ""
      }
    `;
  }

  static override styles = [
    css`
      :host {
        display: inline-flex;
        flex: none;
        max-width: var(--uui-size-100);
      }

      uui-tag {
        max-width: 100%;
        min-width: 0;
        --uui-tag-padding: 0 0 0 var(--uui-size-space-3);
        /* A pill, as the level toggles are (UI brief §5.3). */
        --uui-tag-border-radius: var(--uui-size-layout-1);
      }

      /* Not supported by the active source: still shown, visibly not part of the query. */
      uui-tag.unsupported {
        opacity: 0.6;
      }

      uui-tag.unsupported .label {
        text-decoration: line-through;
      }

      /* Text chips are neutral: the secondary look's surface and border, body text colour. */
      uui-tag.text {
        color: var(--uui-color-text);
      }

      .content {
        display: inline-flex;
        align-items: center;
        min-width: 0;
        max-width: 100%;
      }

      .label {
        min-width: 0;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        font: inherit;
        color: inherit;
        background: none;
        border: none;
        padding: 0;
        cursor: pointer;
      }

      span.label {
        cursor: default;
      }

      .label:focus-visible {
        outline: 2px solid var(--uui-color-focus);
        border-radius: var(--uui-border-radius);
      }

      uui-button {
        flex: none;
        --uui-button-height: var(--uui-size-8);
        --uui-button-padding-left-factor: 0.5;
        --uui-button-padding-right-factor: 0.5;
      }
    `,
  ];
}

declare global {
  interface HTMLElementTagNameMap {
    "log-explorer-filter-chip": LogExplorerFilterChipElement;
  }
}
