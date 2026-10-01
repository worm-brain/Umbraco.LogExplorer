import { css, customElement, html, query, state } from "@umbraco-cms/backoffice/external/lit";
import type { UUIButtonElement } from "@umbraco-cms/backoffice/external/uui";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { LOG_EXPLORER_QUERY_CONTEXT, type LogExplorerQueryContext } from "../query/query.context.js";
import { canShowQuery } from "./native-mode.js";

/**
 * The query bar's "Show generated query" icon button (UI brief §4.5): a square, icon-only
 * `uui-button` that toggles the show-query panel through `showQuery` in the view state (so the
 * open panel survives a reload and travels in shared links). Off it uses the `outline` look, like
 * the time range picker beside it; pressed it uses `primary`.
 *
 * Renders nothing while the active source has no native language (`nativeQuery` not declared),
 * since there would be nothing to show.
 *
 * Not bound to a manifest; the Search view renders it after the search box.
 *
 * @element log-explorer-show-query-button
 */
@customElement("log-explorer-show-query-button")
export class LogExplorerShowQueryButtonElement extends UmbLitElement {
  /** Whether the panel is open; drives the look and `aria-pressed`. */
  @state()
  private _pressed = false;

  /** Whether the active source can compile; the button is hidden otherwise. */
  @state()
  private _available = false;

  @query("uui-button")
  private _button?: UUIButtonElement;

  #context?: LogExplorerQueryContext;

  constructor() {
    super();
    this.consumeContext(LOG_EXPLORER_QUERY_CONTEXT, (context) => {
      this.#context = context;
      this.observe(context?.state, (viewState) => (this._pressed = viewState?.showQuery ?? false), "_observeState");
      this.observe(context?.activeSource, (source) => (this._available = canShowQuery(source)), "_observeSource");
    });
  }

  protected override updated(): void {
    void this.#syncPressed();
  }

  /**
   * `uui-button` forwards only `aria-label`/`aria-labelledby` to the `<button>` in its shadow
   * root, which is what takes focus, so `aria-pressed` goes on that inner button (as the
   * histogram's level toggles do).
   */
  async #syncPressed(): Promise<void> {
    const button = this._button;
    if (!button) return;
    await button.updateComplete;
    button.shadowRoot?.querySelector("#button")?.setAttribute("aria-pressed", String(this._pressed));
  }

  #toggle(): void {
    this.#context?.update({ showQuery: this._pressed ? undefined : true });
  }

  /**
   * Renders the toggle, or nothing when the source has no native language.
   *
   * @returns The template.
   */
  override render() {
    if (!this._available) return html``;
    const label = this.localize.term("logExplorer_showQueryButton");
    return html`
      <uui-button look=${this._pressed ? "primary" : "outline"} label=${label} title=${label} @click=${this.#toggle}>
        <umb-icon name="icon-code"></umb-icon>
      </uui-button>
    `;
  }

  static override styles = [
    css`
      :host {
        display: contents;
      }

      uui-button {
        height: 100%;
        /* Square: as wide as the bar is tall. */
        aspect-ratio: 1;
      }
    `,
  ];
}

declare global {
  interface HTMLElementTagNameMap {
    "log-explorer-show-query-button": LogExplorerShowQueryButtonElement;
  }
}
