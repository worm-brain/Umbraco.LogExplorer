import { css, customElement, html, nothing, state, type PropertyValues } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import type { SourceResponseModel } from "../api/index.js";
import { LOG_EXPLORER_QUERY_CONTEXT, type LogExplorerQueryContext } from "../query/query.context.js";
import { canShowQuery, canUseNativeMode, enterNativeMode, formatClauses } from "./native-mode.js";
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

  #context?: LogExplorerQueryContext;
  #unsupported: ReadonlyArray<number> = [];

  constructor() {
    super();
    this.consumeContext(LOG_EXPLORER_QUERY_CONTEXT, (context) => {
      this.#context = context;
      this.observe(context?.state, (viewState) => (this._open = viewState?.showQuery ?? false), "_observeState");
      this.observe(context?.activeSource, (source) => (this._source = source), "_observeSource");
      this.observe(
        context?.compiled,
        (compiled) => (this._compiled = compiled ?? INITIAL_COMPILE_STATE),
        "_observeCompiled",
      );
      this.observe(context?.unsupportedChips, (indices) => (this.#unsupported = indices ?? []), "_observeUnsupported");
    });
  }

  /**
   * Hides the host while there is nothing to show, so the Search view's flex gap does not leave
   * an empty band where the panel would be.
   */
  protected override willUpdate(changed: PropertyValues): void {
    super.willUpdate(changed);
    this.hidden = !this._open || !canShowQuery(this._source);
  }

  #editAsNative(): void {
    const context = this.#context;
    if (!context) return;
    context.update(enterNativeMode(context.getState(), this._compiled.native, this.#unsupported));
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
                : html`<p class="empty">${this.localize.term("logExplorer_showQueryEmpty")}</p>`
          }
        </div>
        ${
          canUseNativeMode(this._source)
            ? html`<uui-button
                look="outline"
                label=${this.localize.term("logExplorer_showQueryEditAsNative")}
                ?disabled=${loading || this._compiled.status === "error"}
                @click=${this.#editAsNative}
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
