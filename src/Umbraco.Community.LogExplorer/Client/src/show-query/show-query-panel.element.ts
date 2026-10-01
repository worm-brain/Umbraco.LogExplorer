import { css, customElement, html, nothing, state, type PropertyValues } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UMB_NOTIFICATION_CONTEXT, type UmbNotificationContext } from "@umbraco-cms/backoffice/notification";
import type { SourceResponseModel } from "../api/index.js";
import type { FilterNode } from "../query/filter-node.js";
import { LOG_EXPLORER_QUERY_CONTEXT, type LogExplorerQueryContext } from "../query/query.context.js";
import { describeChip } from "../search/chip-format.js";
import { canShowQuery, canUseNativeMode, enterNativeMode, formatClauses, splitsOrGroup } from "./native-mode.js";
import { INITIAL_COMPILE_STATE, type CompileState } from "./query-compiler.js";

/**
 * The show-query panel (UI brief §4.6), directly below the query bar while `showQuery` is set:
 * the source's language name, the compiled query in an `umb-code-block` (one clause per line,
 * with its own copy button), and "Edit as native", which moves the query into the search box in
 * native mode (see `enterNativeMode`).
 *
 * The compile itself lives in `LogExplorerQueryContext`, because the unsupported-chip check needs
 * it with the panel closed too; the panel only renders `compiled`. While a recompile runs the
 * previous text stays, dimmed, under a `uui-loader-bar` (UI brief §4.15).
 *
 * Chips the source runs but the compiled text leaves out (`notExpressibleChips`) are listed under
 * the query as "Not shown in {language}: {chip}", so the user knows the text is not the whole
 * filter (ADR 0016). "Edit as native" keeps them as chips, ANDed with the native query, and says
 * so in a notification; it is disabled, with the reason in that list, when keeping them would
 * split an OR group (see `splitsOrGroup`).
 *
 * Renders nothing when closed or when the source has no native language; "Edit as native" is
 * left out when the source does not allow native queries.
 *
 * Not bound to a manifest; the Search view renders it.
 *
 * @element log-explorer-show-query-panel
 */
@customElement("log-explorer-show-query-panel")
export class LogExplorerShowQueryPanelElement extends UmbLitElement {
  @state()
  private _open = false;

  @state()
  private _source?: SourceResponseModel;

  @state()
  private _compiled: CompileState = INITIAL_COMPILE_STATE;

  @state()
  private _chips: ReadonlyArray<FilterNode> = [];

  /** Positions of the chips the source runs but the compiled text leaves out. */
  @state()
  private _notExpressible: ReadonlyArray<number> = [];

  #context?: LogExplorerQueryContext;
  #notifications?: UmbNotificationContext;
  #unsupported: ReadonlyArray<number> = [];

  constructor() {
    super();
    this.consumeContext(LOG_EXPLORER_QUERY_CONTEXT, (context) => {
      this.#context = context;
      this.observe(
        context?.state,
        (viewState) => {
          this._open = viewState?.showQuery ?? false;
          this._chips = viewState?.chips ?? [];
        },
        "_observeState",
      );
      this.observe(context?.activeSource, (source) => (this._source = source), "_observeSource");
      this.observe(
        context?.compiled,
        (compiled) => (this._compiled = compiled ?? INITIAL_COMPILE_STATE),
        "_observeCompiled",
      );
      this.observe(context?.unsupportedChips, (indices) => (this.#unsupported = indices ?? []), "_observeUnsupported");
      this.observe(
        context?.notExpressibleChips,
        (indices) => (this._notExpressible = indices ?? []),
        "_observeNotExpressible",
      );
    });
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (context) => (this.#notifications = context));
  }

  /**
   * Hides the host while there is nothing to show, so the Search view's flex gap does not leave
   * an empty band where the panel would be.
   */
  protected override willUpdate(changed: PropertyValues): void {
    super.willUpdate(changed);
    this.hidden = !this._open || !canShowQuery(this._source);
  }

  #editAsNative(language: string): void {
    const context = this.#context;
    if (!context) return;
    context.update(
      enterNativeMode(context.getState(), this._compiled.native, [...this.#unsupported, ...this._notExpressible]),
    );
    if (this._notExpressible.length > 0) {
      this.#notifications?.peek("default", {
        data: { message: this.localize.term("logExplorer_showQueryKeptAsChips", language) },
      });
    }
  }

  /** The not-shown list, plus why "Edit as native" is disabled when it would split an OR group. */
  #renderNotShown(language: string, splits: boolean) {
    if (this._notExpressible.length === 0) return nothing;
    const term = (key: string, ...args: Array<string>) => this.localize.term(key, ...args);
    return html`<ul class="not-shown">
      ${this._notExpressible.map((index) => {
        const chip = this._chips[index];
        return chip
          ? html`<li>
              ${this.localize.term("logExplorer_showQueryNotShown", language, describeChip(chip, term).description)}
            </li>`
          : nothing;
      })}
      ${splits ? html`<li>${this.localize.term("logExplorer_showQuerySplitsGroup", language)}</li>` : nothing}
    </ul>`;
  }

  /**
   * Renders the panel, or nothing when it is closed or unavailable.
   *
   * @returns The template.
   */
  override render() {
    if (!this._open || !canShowQuery(this._source)) return nothing;
    const language = this._source!.capabilities.nativeLanguage ?? "";
    const text = formatClauses(this._compiled.native);
    const loading = this._compiled.status === "loading";
    const splits = splitsOrGroup(this._chips, this._notExpressible, this.#unsupported);
    // With every chip left out of the text, "no filter" would be untrue: the chips still apply.
    const empty =
      this._notExpressible.length > 0
        ? this.localize.term("logExplorer_showQueryNothingShown", language)
        : this.localize.term("logExplorer_showQueryEmpty");

    return html`
      <section
        class="panel"
        aria-label=${this.localize.term("logExplorer_showQueryLabel", language)}
        aria-busy=${loading}
      >
        ${loading ? html`<uui-loader-bar></uui-loader-bar>` : nothing}
        <span class="language">${language}</span>
        <div class="query ${loading ? "dimmed" : ""}">
          ${
            this._compiled.status === "error"
              ? html`<p class="error" role="alert">
                  ${this.localize.term("logExplorer_showQueryError", this._compiled.error ?? "")}
                </p>`
              : text
                ? // No `language`: the panel already names it on the left, and the code block
                  // would repeat it in its own header row.
                  html`<umb-code-block copy>${text}</umb-code-block>`
                : html`<p class="empty">${empty}</p>`
          }
          ${this.#renderNotShown(language, splits)}
        </div>
        ${
          canUseNativeMode(this._source)
            ? html`<uui-button
                look="outline"
                label=${this.localize.term("logExplorer_showQueryEditAsNative")}
                ?disabled=${loading || this._compiled.status === "error" || splits}
                @click=${() => this.#editAsNative(language)}
              ></uui-button>`
            : nothing
        }
      </section>
    `;
  }

  static override styles = [
    css`
      :host {
        display: block;
      }

      :host([hidden]) {
        display: none;
      }

      .panel {
        position: relative;
        display: flex;
        align-items: flex-start;
        gap: var(--uui-size-space-4);
        padding: var(--uui-size-space-3) var(--uui-size-space-4);
        background: var(--uui-color-surface);
        border: 1px solid var(--uui-color-border);
        border-radius: var(--uui-border-radius);
      }

      uui-loader-bar {
        position: absolute;
        inset: 0 0 auto 0;
      }

      .language {
        flex: none;
        font-size: var(--uui-type-small-size);
        font-weight: 700;
        color: var(--uui-color-text-alt);
        padding-top: var(--uui-size-space-2);
      }

      .query {
        flex: 1;
        min-width: 0;
      }

      .query.dimmed {
        opacity: 0.6;
      }

      .empty,
      .error {
        margin: 0;
        padding-top: var(--uui-size-space-2);
        /* No UUI monospace token exists; umb-code-block uses the generic family too. */
        font-family: monospace;
        color: var(--uui-color-text-alt);
      }

      .error {
        color: var(--uui-color-danger-standalone);
      }

      .not-shown {
        margin: var(--uui-size-space-2) 0 0;
        padding: 0;
        list-style: none;
        font-size: var(--uui-type-small-size);
        color: var(--uui-color-text-alt);
      }

      uui-button {
        flex: none;
      }
    `,
  ];
}

declare global {
  interface HTMLElementTagNameMap {
    "log-explorer-show-query-panel": LogExplorerShowQueryPanelElement;
  }
}
