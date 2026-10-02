import { css, customElement, html, nothing, query, state } from "@umbraco-cms/backoffice/external/lit";
import type { UUIButtonElement, UUIPopoverContainerElement } from "@umbraco-cms/backoffice/external/uui";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import { PopoverMenuController } from "../shared/popover-menu.controller.js";
import { LOG_EXPLORER_QUERY_CONTEXT, type LogExplorerQueryContext } from "../query/query.context.js";
import { RELATIVE_RANGES, type RelativeRange, type ViewTimeRange } from "../query/view-state.js";
import {
  PRESET_LABEL_KEYS,
  customRangeFromInputs,
  formatAbsoluteRange,
  localTimeZone,
  toLocalInputValue,
} from "./time-range.js";
import { formatLocale } from "../shared/format-locale.js";

/**
 * The time range picker at the start of the query bar (UI brief §4.2).
 *
 * A button (clock icon, current range, chevron) opens a menu of the relative presets and
 * "Custom range…", which reveals two `umb-input-date type="datetime-local"` inputs, the browser's
 * time zone and an Apply button. Choosing a preset or applying a custom range writes
 * `LogExplorerQueryContext`, which records it in the URL.
 *
 * Built from `uui-button` + `uui-popover-container` + `umb-popover-layout` + `uui-menu-item`
 * rather than `umb-dropdown`: `umb-dropdown` closes its popover on any click inside it (a
 * capture-phase handler, there for a WebKit bug), so the custom-range inputs could never be
 * focused. Escape and outside clicks close the menu through the native popover API.
 * {@link PopoverMenuController} supplies what UUI does not: menu roles, `aria-expanded` on the
 * button, arrow keys between the presets, and focus back on the button after a choice (#51).
 *
 * Not bound to a manifest; the Search view renders it.
 *
 * @element log-explorer-time-range-picker
 */
@customElement("log-explorer-time-range-picker")
export class LogExplorerTimeRangePickerElement extends UmbLitElement {
  /** The range in force, mirrored from the query context; re-renders the button label. */
  @state()
  private _range: ViewTimeRange | undefined;

  /** Whether the custom-range inputs are shown inside the menu. */
  @state()
  private _customOpen = false;

  /** Draft values of the custom-range inputs (`datetime-local` format, local time). */
  @state()
  private _draft = { from: "", to: "" };

  /** Set when Apply was pressed with an invalid draft, to show the error. */
  @state()
  private _draftInvalid = false;

  @query("#popover")
  private _popover?: UUIPopoverContainerElement;

  @query("#trigger")
  private _trigger?: UUIButtonElement;

  @query("#menu")
  private _menuList?: HTMLElement;

  @query("#from")
  private _fromInput?: HTMLElement;

  #menu = new PopoverMenuController(this, {
    trigger: () => this._trigger,
    popover: () => this._popover,
    menu: () => this._menuList,
  });

  #context?: LogExplorerQueryContext;

  constructor() {
    super();
    this.consumeContext(LOG_EXPLORER_QUERY_CONTEXT, (context) => {
      this.#context = context;
      this.observe(context?.range, (range) => (this._range = range), "_observeRange");
    });
  }

  #select(relative: RelativeRange): void {
    this.#context?.setRange({ relative });
    this._customOpen = false;
    this.#menu.close();
  }

  async #openCustom(): Promise<void> {
    // Seed the inputs with the range in force; for a relative range, compute it from now so the
    // user starts from what they are looking at.
    const range = this._range;
    const to = range && "to" in range ? range.to : new Date().toISOString();
    const from =
      range && "from" in range ? range.from : new Date(Date.now() - durationMs(range?.relative ?? "1h")).toISOString();
    this._draft = { from: toLocalInputValue(from), to: toLocalInputValue(to) };
    this._draftInvalid = false;
    this._customOpen = true;
    // The inputs are what the user came for; Tab from them reaches Apply.
    await this.updateComplete;
    this._fromInput?.focus();
  }

  #applyCustom(): void {
    const range = customRangeFromInputs(this._draft.from, this._draft.to);
    if (!range) {
      this._draftInvalid = true;
      return;
    }
    this.#context?.setRange(range);
    this._customOpen = false;
    this.#menu.close();
  }

  #onToggle = (event: Event): void => {
    this.#menu.onToggle(event);
    if (!this.#menu.open) this._customOpen = false;
  };

  #label(): string {
    const range = this._range;
    if (!range) return "";
    if ("relative" in range) return this.localize.term(PRESET_LABEL_KEYS[range.relative]);
    return formatAbsoluteRange(range, formatLocale(this.localize.lang()));
  }

  /**
   * Renders the button and its menu.
   *
   * @returns The template.
   */
  override render() {
    const label = this.#label();
    const active = this._range && "relative" in this._range ? this._range.relative : undefined;
    return html`
      <uui-button
        id="trigger"
        popovertarget="popover"
        look="outline"
        label=${this.localize.term("logExplorer_timeRangePickerLabel", label)}
      >
        <span class="button-content">
          <umb-icon name="icon-time"></umb-icon>
          <span class="label">${label}</span>
          <uui-symbol-expand .open=${this.#menu.open}></uui-symbol-expand>
        </span>
      </uui-button>
      <uui-popover-container
        id="popover"
        placement="bottom-start"
        no-scroll
        @toggle=${this.#onToggle}
        @focusout=${this.#menu.onFocusOut}
        @keydown=${this.#menu.onKeydown}
      >
        <umb-popover-layout>
          <!-- role="none" on each item before it connects, or UUI gives it role="menu". -->
          <div id="menu" role="menu" aria-label=${this.localize.term("logExplorer_timeRangeMenuLabel")}>
            ${RELATIVE_RANGES.map(
              (preset) => html`
                <uui-menu-item
                  role="none"
                  label=${this.localize.term(PRESET_LABEL_KEYS[preset])}
                  ?active=${preset === active}
                  @click-label=${() => this.#select(preset)}
                ></uui-menu-item>
              `,
            )}
            <uui-menu-item
              role="none"
              label=${this.localize.term("logExplorer_timeRangeCustom")}
              ?active=${active === undefined}
              @click-label=${() => void this.#openCustom()}
            ></uui-menu-item>
          </div>
          ${this._customOpen ? this.#renderCustom() : nothing}
        </umb-popover-layout>
      </uui-popover-container>
    `;
  }

  #renderCustom() {
    return html`
      <div class="custom">
        <uui-label for="from">${this.localize.term("logExplorer_timeRangeFrom")}</uui-label>
        <umb-input-date
          id="from"
          type="datetime-local"
          label=${this.localize.term("logExplorer_timeRangeFrom")}
          .value=${this._draft.from}
          @change=${(event: Event) => (this._draft = { ...this._draft, from: inputValue(event) })}
        ></umb-input-date>
        <uui-label for="to">${this.localize.term("logExplorer_timeRangeTo")}</uui-label>
        <umb-input-date
          id="to"
          type="datetime-local"
          label=${this.localize.term("logExplorer_timeRangeTo")}
          .value=${this._draft.to}
          @change=${(event: Event) => (this._draft = { ...this._draft, to: inputValue(event) })}
        ></umb-input-date>
        <p class="timezone">${this.localize.term("logExplorer_timeRangeTimezone", localTimeZone())}</p>
        ${
          this._draftInvalid
            ? html`<p class="error" role="alert">${this.localize.term("logExplorer_timeRangeInvalid")}</p>`
            : nothing
        }
        <uui-button
          look="primary"
          label=${this.localize.term("logExplorer_timeRangeApply")}
          @click=${() => this.#applyCustom()}
        ></uui-button>
      </div>
    `;
  }

  static override styles = [
    UmbTextStyles,
    css`
      :host {
        display: inline-block;
      }

      uui-button {
        height: 100%;
      }

      .button-content {
        display: inline-flex;
        align-items: center;
        gap: var(--uui-size-space-2);
        white-space: nowrap;
      }

      .custom {
        display: grid;
        gap: var(--uui-size-space-2);
        padding: var(--uui-size-space-4);
      }

      .timezone {
        margin: 0;
        color: var(--uui-color-text-alt);
      }

      .error {
        margin: 0;
        color: var(--uui-color-danger-standalone);
      }
    `,
  ];
}

/** Length of a relative preset, used only to seed the custom-range inputs. */
function durationMs(relative: RelativeRange): number {
  const hour = 3_600_000;
  return { "15m": hour / 4, "1h": hour, "4h": 4 * hour, "24h": 24 * hour, "7d": 168 * hour, "30d": 720 * hour }[
    relative
  ];
}

/** Reads the value from a `umb-input-date` change event (it extends `uui-input`). */
function inputValue(event: Event): string {
  return String((event.target as HTMLInputElement).value ?? "");
}

declare global {
  interface HTMLElementTagNameMap {
    "log-explorer-time-range-picker": LogExplorerTimeRangePickerElement;
  }
}
