import { css, customElement, html, query, state, type PropertyValues } from "@umbraco-cms/backoffice/external/lit";
import type { UUIInputElement } from "@umbraco-cms/backoffice/external/uui";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UMB_NOTIFICATION_CONTEXT, type UmbNotificationContext } from "@umbraco-cms/backoffice/notification";
import { ParseService } from "../api/index.js";
import type { FilterNode } from "../query/filter-node.js";
import { LOG_EXPLORER_QUERY_CONTEXT, type LogExplorerQueryContext } from "../query/query.context.js";
import { LogExplorerChipChangeEvent, LogExplorerChipRemoveEvent } from "./chip-events.js";
import { searchKeyAction } from "./search-keys.js";
import { SearchSubmitter, UNBALANCED_QUOTE, type ParseFn, type SubmitOutcome } from "./search-submit.js";
import "./filter-chip.element.js";

/** The default request: the generated client, which carries the backoffice token. */
const parseWithClient: ParseFn = (input, signal) => ParseService.parse({ body: { input }, signal });

/**
 * The query bar's search box (UI brief §4.3): one `uui-input` holding every filter chip in its
 * `prepend` slot, after the search icon, with the text input after them.
 *
 * - **Enter** sends the input to `POST /parse` (ADR 0005), appends the returned chips to
 *   `LogExplorerQueryContext` (exact duplicates skipped), applies a returned level set to the
 *   level toggles (`level:` and `level=` never make chips, ADR 0004) and clears the input. A new
 *   Enter aborts a parse still in flight. When an unbalanced quote made the input plain text, the
 *   text chip is still added and a backoffice notification says why (UI brief §4.14).
 * - **Backspace** in an empty input removes the last chip; **Escape** clears the input.
 * - **Chips** are `log-explorer-filter-chip`s; their edit and remove events write the context.
 *
 * The bar stays one line: the chips scroll horizontally inside the prepend area, which is capped
 * so the text input keeps a usable width. The context re-runs the query on every chip change
 * through the results panel, which observes it.
 *
 * Not bound to a manifest; the Search view renders it.
 *
 * @element log-explorer-search-box
 * @slot before-chips - Rendered after the search icon and before the filter chips: the place for
 *   the histogram's time-zoom chip (#40), which owns its own state and element.
 */
@customElement("log-explorer-search-box")
export class LogExplorerSearchBoxElement extends UmbLitElement {
  /** The chips in the view state; each change re-renders the chip list. */
  @state()
  private _chips: ReadonlyArray<FilterNode> = [];

  /** The text in the input, mirrored so a successful parse can clear only what it parsed. */
  @state()
  private _value = "";

  @query("#input")
  private _input?: UUIInputElement;

  @query(".chips")
  private _chipList?: HTMLElement;

  #context?: LogExplorerQueryContext;
  #notifications?: UmbNotificationContext;
  #submitter = new SearchSubmitter(parseWithClient, {
    getState: () => this.#context!.getState(),
    update: (partial) => this.#context?.update(partial),
  });
  #scrollChipsToEnd = false;

  constructor() {
    super();
    this.consumeContext(LOG_EXPLORER_QUERY_CONTEXT, (context) => {
      this.#context = context;
      this.observe(
        context?.state,
        (viewState) => {
          const chips = viewState?.chips ?? [];
          // Keep the newest chip in view when chips were added.
          if (chips.length > this._chips.length) this.#scrollChipsToEnd = true;
          this._chips = chips;
        },
        "_observeState",
      );
    });
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (context) => (this.#notifications = context));
  }

  /** Moves focus to the text input, for the `/` shortcut (BRIEF §6.15). */
  override focus(): void {
    void this._input?.focus();
  }

  /** Aborts a parse still running when the Search view goes away. */
  override disconnectedCallback(): void {
    super.disconnectedCallback();
    this.#submitter.abort();
  }

  /** Scrolls the chip list to its end after chips were added. */
  protected override updated(changed: PropertyValues): void {
    super.updated(changed);
    const list = this._chipList;
    if (this.#scrollChipsToEnd && list) {
      this.#scrollChipsToEnd = false;
      // The new chips render their own shadow content after this update, so their width is not
      // known yet; wait for the last one before measuring.
      const last = list.lastElementChild as (Element & { updateComplete?: Promise<unknown> }) | null;
      void Promise.resolve(last?.updateComplete).then(() => (list.scrollLeft = list.scrollWidth));
    }
  }

  /** Turns a vertical wheel over the chips into horizontal scrolling, since there is no scrollbar. */
  #onChipWheel(event: WheelEvent): void {
    const list = event.currentTarget as HTMLElement;
    if (Math.abs(event.deltaY) <= Math.abs(event.deltaX) || list.scrollWidth <= list.clientWidth) return;
    event.preventDefault();
    list.scrollLeft += event.deltaY;
  }

  #onInput(event: Event): void {
    this._value = String((event.target as UUIInputElement).value ?? "");
  }

  #onKeydown(event: KeyboardEvent): void {
    // Keys typed into the text input reach here retargeted to uui-input. Keys from the chips
    // (their buttons and the chip editor's fields sit in uui-input's prepend slot) arrive with a
    // chip as the target and must not remove chips or parse.
    if (event.target !== this._input) return;
    switch (searchKeyAction(event, this._value, this._chips.length)) {
      case "submit":
        event.preventDefault();
        void this.#submit(this._value);
        break;
      case "removeLastChip":
        event.preventDefault();
        this.#context?.removeChip(this._chips.length - 1);
        break;
      case "clear":
        event.preventDefault();
        event.stopPropagation();
        this.#submitter.abort();
        this.#clearInput();
        break;
    }
  }

  async #submit(input: string): Promise<void> {
    if (!this.#context) return;
    const outcome = await this.#submitter.submit(input);
    // Clear only what was parsed: the user may have kept typing while the request ran.
    if (outcome.status === "applied" && this._value === input) this.#clearInput();
    this.#report(outcome);
  }

  /**
   * Empties the input. The element is written directly as well as the mirror: when the clear
   * comes before Lit re-rendered the typed text, the binding's last value is already "" and Lit
   * would see no change. (`live()` would cover this, but `@umbraco-cms/backoffice` 17.0.0 does
   * not export it.)
   */
  #clearInput(): void {
    this._value = "";
    if (this._input) this._input.value = "";
  }

  #report(outcome: SubmitOutcome): void {
    if (outcome.status === "applied" && outcome.fallback) {
      // The server's sentence is English; known codes use the localised copy-deck string.
      const message =
        outcome.fallback.code === UNBALANCED_QUOTE
          ? this.localize.term("logExplorer_searchUnbalancedQuote")
          : outcome.fallback.message;
      this.#notifications?.peek("warning", { data: { message } });
    } else if (outcome.status === "error") {
      this.#notifications?.peek("danger", {
        data: { message: this.localize.term("logExplorer_searchParseError", outcome.message) },
      });
    }
  }

  #onChipChange(index: number, event: LogExplorerChipChangeEvent): void {
    event.stopPropagation();
    this.#context?.replaceChip(index, event.chip);
  }

  #onChipRemove(index: number, event: LogExplorerChipRemoveEvent): void {
    event.stopPropagation();
    this.#context?.removeChip(index);
    // The removed chip's button had focus; give it back to the input rather than the body.
    this.focus();
  }

  /**
   * Renders the input with the icon, the time-zoom slot and the chips in its prepend area.
   *
   * @returns The template.
   */
  override render() {
    const placeholder = this.localize.term(
      this._chips.length > 0 ? "logExplorer_searchPlaceholderMore" : "logExplorer_searchPlaceholder",
    );
    return html`
      <uui-input
        id="input"
        label=${this.localize.term("logExplorer_searchLabel")}
        placeholder=${placeholder}
        autocomplete="off"
        .value=${this._value}
        @input=${this.#onInput}
        @keydown=${this.#onKeydown}
      >
        <div slot="prepend" class="prepend">
          <umb-icon name="icon-search" aria-hidden="true"></umb-icon>
          <div class="chips" @wheel=${this.#onChipWheel}>
            <slot name="before-chips"></slot>
            ${this._chips.map(
              (chip, index) => html`
                <log-explorer-filter-chip
                  .chip=${chip}
                  @log-explorer-chip-change=${(event: LogExplorerChipChangeEvent) => this.#onChipChange(index, event)}
                  @log-explorer-chip-remove=${(event: LogExplorerChipRemoveEvent) => this.#onChipRemove(index, event)}
                ></log-explorer-filter-chip>
              `,
            )}
          </div>
        </div>
      </uui-input>
    `;
  }

  static override styles = [
    css`
      :host {
        display: block;
        min-width: 0;
        /* The chip area is capped in container units of the whole box (see .prepend). */
        container-type: inline-size;
      }

      uui-input {
        width: 100%;
        height: 100%;
      }

      .prepend {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
        min-width: 0;
        /*
         * Leaves the text input a usable share of the bar however many chips there are. A
         * percentage would resolve against uui-input's prepend slot, whose width comes from this
         * element, and clip the chips; cqi measures the search box instead.
         */
        max-width: 70cqi;
        padding-left: var(--uui-size-space-3);
      }

      umb-icon {
        flex: none;
        color: var(--uui-color-text-alt);
      }

      .chips {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-1);
        min-width: 0;
        overflow-x: auto;
        /* One line: chips scroll rather than wrap, so the bar never grows (UI brief §4.3). */
        flex-wrap: nowrap;
        /*
         * No scrollbar: it would add height to the bar. The list scrolls with the wheel (see
         * #onChipWheel), with a trackpad, and by focus as Tab moves through the chips.
         */
        scrollbar-width: none;
      }
    `,
  ];
}

declare global {
  interface HTMLElementTagNameMap {
    "log-explorer-search-box": LogExplorerSearchBoxElement;
  }
}
