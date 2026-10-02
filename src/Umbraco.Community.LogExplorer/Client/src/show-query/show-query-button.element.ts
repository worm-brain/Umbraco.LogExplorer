import { css, customElement, html, state } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { LOG_EXPLORER_QUERY_CONTEXT, type LogExplorerQueryContext } from "../query/query.context.js";
import { syncButtonAria } from "../shared/button-aria.js";
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

  #context?: LogExplorerQueryContext;

  constructor() {
    super();
    this.consumeContext(LOG_EXPLORER_QUERY_CONTEXT, (context) => {
      this.#context = context;
      this.observe(context?.state, (viewState) => (this._pressed = viewState?.showQuery ?? false), "_observeState");
      this.observe(context?.activeSource, (source) => (this._available = canShowQuery(source)), "_observeSource");
    });
  }

  /** Copies `data-pressed` onto the focusable inner button (see syncButtonAria). */
  protected override updated(): void {
    void syncButtonAria(this.renderRoot);
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
      <uui-button
        look=${this._pressed ? "primary" : "outline"}
        data-pressed=${String(this._pressed)}
        label=${label}
        title=${label}
        @click=${this.#toggle}
      >
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
        /* Square: as wide as the query bar is tall (both var(--uui-size-11)). Not aspect-ratio:
           a flex item's automatic minimum width is its content width, and uui-button's
           horizontal padding made that 54 px against the bar's 33 px (#51). */
        /* A fixed height, not 100%: the host is display: contents and the query bar grows when
           the search box's chips wrap (ADR 0024). */
        height: var(--uui-size-11);
        width: var(--uui-size-11);
        flex: none;
        --uui-button-padding-left-factor: 0;
        --uui-button-padding-right-factor: 0;
      }
    `,
  ];
}

declare global {
  interface HTMLElementTagNameMap {
    "log-explorer-show-query-button": LogExplorerShowQueryButtonElement;
  }
}
