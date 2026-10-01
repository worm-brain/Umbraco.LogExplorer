import {
  css,
  customElement,
  html,
  nothing,
  property,
  state,
  type PropertyValues,
} from "@umbraco-cms/backoffice/external/lit";
import type { UUISelectOption } from "@umbraco-cms/backoffice/external/uui";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import type { FilterNode } from "../query/filter-node.js";
import { LogExplorerChipChangeEvent, LogExplorerChipEditCancelEvent } from "./chip-events.js";
import { OPERATOR_KEYS } from "./chip-format.js";
import {
  chipFromDraft,
  draftFromChip,
  isDraftValid,
  operatorsFor,
  type ChipDraft,
  type ConditionDraft,
  type EditOperator,
} from "./chip-edit.js";

/**
 * The small form a filter chip opens to change its operator or value (UI brief §4.4, BRIEF §6.3).
 *
 * - Field chips: the field name (fixed), an operator `uui-select` (see `operatorsFor`), a value
 *   `uui-input` (hidden for "exists") and an "Exclude matching entries" `uui-toggle`.
 * - Text chips: the text and an "Exact phrase" toggle.
 *
 * Enter in a text field saves, like Save. The editor only reports; the chip that hosts it closes
 * its popover and the search box writes the context.
 *
 * Not bound to a manifest; `log-explorer-filter-chip` renders it inside its popover.
 *
 * @element log-explorer-chip-editor
 * @fires log-explorer-chip-change - {@link LogExplorerChipChangeEvent} on Save, with the new chip.
 * @fires log-explorer-chip-edit-cancel - {@link LogExplorerChipEditCancelEvent} on Cancel.
 */
@customElement("log-explorer-chip-editor")
export class LogExplorerChipEditorElement extends UmbLitElement {
  /** The chip being edited. Setting it resets the form to that chip. */
  @property({ attribute: false })
  chip?: FilterNode;

  /** The form's current values; `undefined` when the chip cannot be edited. */
  @state()
  private _draft?: ChipDraft;

  /** Resets the form when a different chip arrives (also after a Save re-renders the chip). */
  override willUpdate(changed: PropertyValues<this>): void {
    if (changed.has("chip")) this._draft = this.chip ? draftFromChip(this.chip) : undefined;
  }

  /** Puts the form back to the chip's current values, for a popover that is reopened. */
  reset(): void {
    this._draft = this.chip ? draftFromChip(this.chip) : undefined;
  }

  /** Moves focus to the first field, for when the popover opens. */
  async focusFirstField(): Promise<void> {
    await this.updateComplete;
    (this.shadowRoot?.querySelector("[data-first]") as HTMLElement | null)?.focus();
  }

  #save(): void {
    if (!this._draft || !isDraftValid(this._draft)) return;
    this.dispatchEvent(new LogExplorerChipChangeEvent(chipFromDraft(this._draft)));
  }

  #cancel(): void {
    this.dispatchEvent(new LogExplorerChipEditCancelEvent());
  }

  #patch(partial: Partial<ConditionDraft>): void {
    if (this._draft?.kind === "condition") this._draft = { ...this._draft, ...partial };
  }

  #onKeydown(event: KeyboardEvent): void {
    if (event.key !== "Enter" || event.isComposing) return;
    event.preventDefault();
    this.#save();
  }

  /**
   * Renders the form, or nothing for a chip the editor cannot represent.
   *
   * @returns The template.
   */
  override render() {
    const draft = this._draft;
    if (!draft) return nothing;
    return html`
      <div class="form">
        ${draft.kind === "text" ? this.#renderText(draft) : this.#renderCondition(draft)}
        <div class="actions">
          <uui-button
            look="secondary"
            label=${this.localize.term("logExplorer_chipEditorCancel")}
            @click=${this.#cancel}
          ></uui-button>
          <uui-button
            look="primary"
            label=${this.localize.term("logExplorer_chipEditorSave")}
            ?disabled=${!isDraftValid(draft)}
            @click=${this.#save}
          ></uui-button>
        </div>
      </div>
    `;
  }

  #renderCondition(draft: ConditionDraft) {
    const options: Array<UUISelectOption> = operatorsFor(draft).map((op) => ({
      name: this.localize.term(OPERATOR_KEYS[op]),
      value: op,
      selected: op === draft.op,
    }));
    return html`
      <strong class="field">${draft.field}</strong>
      <uui-label for="op">${this.localize.term("logExplorer_chipEditorOperator")}</uui-label>
      <uui-select
        id="op"
        data-first
        label=${this.localize.term("logExplorer_chipEditorOperator")}
        .options=${options}
        @change=${(event: Event) => this.#patch({ op: (event.target as HTMLSelectElement).value as EditOperator })}
      ></uui-select>
      ${
        draft.op === "exists"
          ? nothing
          : html`
              <uui-label for="value">${this.localize.term("logExplorer_chipEditorValue")}</uui-label>
              <uui-input
                id="value"
                label=${this.localize.term("logExplorer_chipEditorValue")}
                .value=${draft.value}
                @input=${(event: Event) => this.#patch({ value: String((event.target as HTMLInputElement).value) })}
                @keydown=${this.#onKeydown}
              ></uui-input>
            `
      }
      <uui-toggle
        label=${this.localize.term("logExplorer_chipEditorExclude")}
        .checked=${draft.exclude}
        @change=${(event: Event) => this.#patch({ exclude: (event.target as HTMLInputElement).checked })}
      ></uui-toggle>
    `;
  }

  #renderText(draft: Extract<ChipDraft, { kind: "text" }>) {
    return html`
      <uui-label for="text">${this.localize.term("logExplorer_chipEditorText")}</uui-label>
      <uui-input
        id="text"
        data-first
        label=${this.localize.term("logExplorer_chipEditorText")}
        .value=${draft.text}
        @input=${(event: Event) => (this._draft = { ...draft, text: String((event.target as HTMLInputElement).value) })}
        @keydown=${this.#onKeydown}
      ></uui-input>
      <uui-toggle
        label=${this.localize.term("logExplorer_chipEditorPhrase")}
        .checked=${draft.phrase}
        @change=${(event: Event) => (this._draft = { ...draft, phrase: (event.target as HTMLInputElement).checked })}
      ></uui-toggle>
    `;
  }

  static override styles = [
    UmbTextStyles,
    css`
      .form {
        display: grid;
        gap: var(--uui-size-space-2);
        padding: var(--uui-size-space-4);
        min-width: var(--uui-size-80);
      }

      .field {
        word-break: break-all;
      }

      .actions {
        display: flex;
        justify-content: flex-end;
        gap: var(--uui-size-space-2);
        margin-top: var(--uui-size-space-2);
      }
    `,
  ];
}

declare global {
  interface HTMLElementTagNameMap {
    "log-explorer-chip-editor": LogExplorerChipEditorElement;
  }
}
