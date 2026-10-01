import { css, customElement, html, query, state, type PropertyValues } from "@umbraco-cms/backoffice/external/lit";
import type { UUIInputElement } from "@umbraco-cms/backoffice/external/uui";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UMB_NOTIFICATION_CONTEXT, type UmbNotificationContext } from "@umbraco-cms/backoffice/notification";
import { NativeQueryService, ParseService, type SourceResponseModel } from "../api/index.js";
import type { FilterNode } from "../query/filter-node.js";
import { LOG_EXPLORER_QUERY_CONTEXT, type LogExplorerQueryContext } from "../query/query.context.js";
import { canUseNativeMode, leaveNativeMode } from "../show-query/native-mode.js";
import { NativeValidator, type NativeValidation, type ValidateFn } from "../show-query/native-validator.js";
import { LogExplorerChipChangeEvent, LogExplorerChipRemoveEvent } from "./chip-events.js";
import { searchKeyAction } from "./search-keys.js";
import { SearchSubmitter, UNBALANCED_QUOTE, type ParseFn, type SubmitOutcome } from "./search-submit.js";
import "./filter-chip.element.js";

/** The default request: the generated client, which carries the backoffice token. */
const parseWithClient: ParseFn = (input, signal) => ParseService.parse({ body: { input }, signal });

/** The default native-query check, through the generated client. */
const validateWithClient: ValidateFn = (alias, native, signal) =>
  NativeQueryService.validate({ path: { alias }, body: { native }, signal });

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
 *   Chips the active source cannot run (`unsupportedChips` in the context) are drawn disabled;
 *   chips it runs but "Show query" cannot express (`notExpressibleChips`) only get a tooltip note.
 * - **Native mode** (BRIEF §6.2) is on while the view state holds a native query (`native`, even
 *   `""`) and the source allows native queries. A tag with the language name sits before the
 *   chips, with a button back to simple mode; the input holds the native text, checked with
 *   `POST /validate` 400 ms after the last keystroke. An invalid query marks the input invalid
 *   and shows the source's message with the error position under the box. **Enter** stores the
 *   text as the native query, which the panels run ANDed with the chips; it does not parse.
 *   The committed query is checked too when native mode opens (from "Edit as native" or a
 *   link), so a query `/search` would reject with `invalid_native_query` shows up here as well.
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

  /** Positions of the chips the active source cannot run. */
  @state()
  private _unsupported: ReadonlyArray<number> = [];

  @state()
  private _notExpressible: ReadonlyArray<number> = [];

  /** The active source, for its language, its name and whether native mode is allowed. */
  @state()
  private _source?: SourceResponseModel;

  /** The view state's native query; `undefined` is simple mode. */
  @state()
  private _native: string | undefined;

  /** The verdict on the native text in the input. */
  @state()
  private _validation: NativeValidation = { status: "idle" };

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
  #validator = new NativeValidator(validateWithClient, (validation) => (this._validation = validation));
  #scrollChipsToEnd = false;
  #focusAfterRender = false;
  /** The native text last put in the input; `undefined` while the box is in simple mode. */
  #shownNative: string | undefined;

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
          this._native = viewState?.native;
          this.#syncNative();
        },
        "_observeState",
      );
      this.observe(context?.unsupportedChips, (indices) => (this._unsupported = indices ?? []), "_observeUnsupported");
      this.observe(
        context?.notExpressibleChips,
        (indices) => (this._notExpressible = indices ?? []),
        "_observeNotExpressible",
      );
      this.observe(
        context?.activeSource,
        (source) => {
          const changed = source?.alias !== this._source?.alias;
          this._source = source;
          this.#syncNative();
          // A verdict for another source says nothing about this one.
          if (changed && this.#nativeMode) this.#validator.check(source!.alias, this._value);
        },
        "_observeSource",
      );
    });
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (context) => (this.#notifications = context));
  }

  /** Moves focus to the text input, for the `/` shortcut (BRIEF §6.15). */
  override focus(): void {
    void this._input?.focus();
  }

  /** Aborts a parse or native check still running when the Search view goes away. */
  override disconnectedCallback(): void {
    super.disconnectedCallback();
    this.#submitter.abort();
    this.#validator.cancel();
  }

  /** Native mode needs both a native query in the view state and a source that allows it. */
  get #nativeMode(): boolean {
    return this._native !== undefined && canUseNativeMode(this._source);
  }

  /**
   * Follows the view state's native query and the source. Entering native mode (or a new native
   * query from elsewhere, such as "Edit as native") puts its text in the input and checks it;
   * leaving it, or switching to a source without native mode, empties the input. The view state
   * echoing back what Enter just stored changes nothing.
   */
  #syncNative(): void {
    const shown = this.#nativeMode ? this._native : undefined;
    if (shown === this.#shownNative) return;
    const entering = this.#shownNative === undefined;
    this.#shownNative = shown;

    if (shown === undefined) {
      this.#clearInput();
      this.#resetValidation();
      return;
    }
    if (shown !== this._value) {
      this._value = shown;
      if (this._input) this._input.value = shown;
    }
    this.#validator.check(this._source!.alias, shown);
    if (entering) this.#focusAfterRender = true;
  }

  #resetValidation(): void {
    this.#validator.cancel();
    this._validation = { status: "idle" };
  }

  /**
   * Scrolls the chip list to its end after chips were added, mirrors the native verdict onto the
   * input's validity, and focuses the input when native mode opens.
   */
  protected override updated(changed: PropertyValues): void {
    super.updated(changed);
    if (changed.has("_validation") && this._input) {
      // `pristine = false` makes uui-input draw its invalid border straight away.
      const invalid = this._validation.status === "invalid";
      this._input.setCustomValidity(invalid ? this.#validationText() : "");
      if (invalid) this._input.pristine = false;
    }
    if (this.#focusAfterRender) {
      this.#focusAfterRender = false;
      this.focus();
    }
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
    if (this.#nativeMode) this.#validator.check(this._source!.alias, this._value);
  }

  #onKeydown(event: KeyboardEvent): void {
    // Keys typed into the text input reach here retargeted to uui-input. Keys from the chips
    // (their buttons and the chip editor's fields sit in uui-input's prepend slot) arrive with a
    // chip as the target and must not remove chips or parse.
    if (event.target !== this._input) return;
    switch (searchKeyAction(event, this._value, this._chips.length)) {
      case "submit":
        event.preventDefault();
        // Native text runs as typed; only simple-syntax input goes through /parse.
        if (this.#nativeMode) this.#context?.update({ native: this._value });
        else void this.#submit(this._value);
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
        if (this.#nativeMode) this.#resetValidation();
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

  /** Returns to simple mode; the chips stay, the native text goes. */
  #leaveNative(): void {
    this.#context?.update(leaveNativeMode());
    this.focus();
  }

  /** The invalid verdict as one sentence, with the 1-based character position when known. */
  #validationText(): string {
    const validation = this._validation;
    if (validation.status !== "invalid") return "";
    return validation.position === undefined
      ? this.localize.term("logExplorer_nativeInvalid", validation.message)
      : this.localize.term("logExplorer_nativeInvalidAt", validation.message, String(validation.position + 1));
  }

  /**
   * Renders the verdict under the box: the message, and for a known position the input with
   * the offending character marked (an end-of-input error marks the space after it).
   */
  #renderValidation() {
    const validation = this._validation;
    if (validation.status === "error") {
      return html`<p class="native-message">
        ${this.localize.term("logExplorer_nativeValidateError", validation.message)}
      </p>`;
    }
    if (validation.status !== "invalid") return "";

    const position = validation.position;
    const text = this._value;
    return html`
      <p class="native-message invalid" role="alert">
        <span>${this.#validationText()}</span>
        ${
          position === undefined
            ? ""
            : html`<code class="snippet" aria-hidden="true"
                >${text.slice(0, position)}<mark>${text.slice(position, position + 1) || " "}</mark>${text.slice(
                  position + 1,
                )}</code
              >`
        }
      </p>
    `;
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
    const native = this.#nativeMode;
    const language = this._source?.capabilities.nativeLanguage ?? "";
    const placeholder = native
      ? this.localize.term("logExplorer_nativeModePlaceholder", language)
      : this.localize.term(
          this._chips.length > 0 ? "logExplorer_searchPlaceholderMore" : "logExplorer_searchPlaceholder",
        );
    const sourceName = this._source?.displayName ?? "";
    return html`
      <uui-input
        id="input"
        class=${native ? "native" : ""}
        label=${
          native
            ? this.localize.term("logExplorer_nativeModeLabel", language)
            : this.localize.term("logExplorer_searchLabel")
        }
        placeholder=${placeholder}
        autocomplete="off"
        spellcheck="false"
        .value=${this._value}
        @input=${this.#onInput}
        @keydown=${this.#onKeydown}
      >
        <div slot="prepend" class="prepend">
          <umb-icon name="icon-search" aria-hidden="true"></umb-icon>
          ${native ? this.#renderLanguageTag(language) : ""}
          <div class="chips" @wheel=${this.#onChipWheel}>
            <slot name="before-chips"></slot>
            ${this._chips.map(
              (chip, index) => html`
                <log-explorer-filter-chip
                  .chip=${chip}
                  ?unsupported=${this._unsupported.includes(index)}
                  .notShownIn=${this._notExpressible.includes(index) ? language : ""}
                  .sourceName=${sourceName}
                  @log-explorer-chip-change=${(event: LogExplorerChipChangeEvent) => this.#onChipChange(index, event)}
                  @log-explorer-chip-remove=${(event: LogExplorerChipRemoveEvent) => this.#onChipRemove(index, event)}
                ></log-explorer-filter-chip>
              `,
            )}
          </div>
        </div>
      </uui-input>
      ${native ? this.#renderValidation() : ""}
    `;
  }

  /** The native-mode marker: the language name and a button back to simple search. */
  #renderLanguageTag(language: string) {
    const leave = this.localize.term("logExplorer_nativeModeLeave", language);
    return html`
      <uui-tag class="language" look="primary">
        <span class="content">
          <span>${language}</span>
          <uui-button compact look="primary" label=${leave} title=${leave} @click=${this.#leaveNative}>
            <umb-icon name="icon-wrong"></umb-icon>
          </uui-button>
        </span>
      </uui-tag>
    `;
  }

  static override styles = [
    css`
      :host {
        display: block;
        min-width: 0;
        /* The chip area is capped in container units of the whole box (see .prepend). */
        container-type: inline-size;
        /* Anchors the native verdict, which floats under the box (see .native-message). */
        position: relative;
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

      uui-input.native {
        /* No UUI monospace token exists; umb-code-block uses the generic family too. */
        font-family: monospace;
      }

      uui-tag.language {
        flex: none;
        --uui-tag-padding: 0 0 0 var(--uui-size-space-3);
        --uui-tag-border-radius: var(--uui-size-layout-1);
      }

      uui-tag.language .content {
        display: inline-flex;
        align-items: center;
      }

      uui-tag.language uui-button {
        --uui-button-height: var(--uui-size-8);
        --uui-button-padding-left-factor: 0.5;
        --uui-button-padding-right-factor: 0.5;
      }

      /*
       * Floats over the top of the histogram rather than pushing it down, so the bar keeps its
       * height (UI brief §4.3) while the query is invalid.
       */
      .native-message {
        position: absolute;
        top: 100%;
        left: 0;
        right: 0;
        z-index: 1;
        margin: var(--uui-size-space-1) 0 0;
        padding: var(--uui-size-space-2) var(--uui-size-space-3);
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-1);
        font-size: var(--uui-type-small-size);
        color: var(--uui-color-text);
        background: var(--uui-color-surface);
        border: 1px solid var(--uui-color-border);
        border-radius: var(--uui-border-radius);
        box-shadow: var(--uui-shadow-depth-1);
      }

      .native-message.invalid {
        border-color: var(--uui-color-invalid);
        color: var(--uui-color-invalid-standalone);
      }

      .snippet {
        font-family: monospace;
        white-space: pre;
        overflow: hidden;
        text-overflow: ellipsis;
        color: var(--uui-color-text);
      }

      .snippet mark {
        background: var(--uui-color-invalid);
        color: var(--uui-color-invalid-contrast);
      }
    `,
  ];
}

declare global {
  interface HTMLElementTagNameMap {
    "log-explorer-search-box": LogExplorerSearchBoxElement;
  }
}
