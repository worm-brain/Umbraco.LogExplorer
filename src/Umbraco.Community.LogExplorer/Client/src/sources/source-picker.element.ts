import { css, customElement, html, nothing, state } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import type { SourceResponseModel } from "../api/index.js";
import { loadSources, type SourcesState } from "./sources-loader.js";

/**
 * The source picker in the Log Explorer workspace header (UI brief §4.1).
 *
 * For now it only displays the active source: database icon, display name, a lock when the
 * source is sensitive and a tag with the source's native query language. The active source is
 * the first one `/sources` returns. The menu for switching sources, honouring `DefaultSource`
 * and moving this state into `LogExplorerQueryContext` come with #46.
 *
 * It fetches `GET /sources` itself on connect and renders the loading, empty and error states
 * inline, with a Retry button on error. Because it is not interactive yet it renders plain
 * content rather than a `uui-button` with no action, which would be a dead control.
 *
 * Rendered by `log-explorer-workspace` into `umb-workspace-editor`'s `header` slot; no manifest
 * binds to it directly.
 *
 * @element log-explorer-source-picker
 */
@customElement("log-explorer-source-picker")
export class LogExplorerSourcePickerElement extends UmbLitElement {
  /** The outcome of the sources fetch; starts as loading until the first response. */
  @state()
  private _state: SourcesState = { status: "loading" };

  /** Starts the sources fetch when the element joins the DOM. */
  override connectedCallback(): void {
    super.connectedCallback();
    void this.#load();
  }

  /** Fetches the visible sources and stores the result for rendering. */
  async #load(): Promise<void> {
    this._state = { status: "loading" };
    this._state = await loadSources();
  }

  /**
   * Renders the active source or the state of the fetch.
   *
   * @returns The template.
   */
  override render() {
    switch (this._state.status) {
      case "loading":
        return html`<span class="muted">${this.localize.term("logExplorer_sourcesLoading")}</span>`;
      case "empty":
        return html`<span class="muted">${this.localize.term("logExplorer_sourcesEmpty")}</span>`;
      case "error":
        return html`
          <span class="error" role="alert">
            <umb-icon name="icon-alert"></umb-icon>
            ${this.localize.term("logExplorer_sourcesError", this._state.message)}
          </span>
          <uui-button
            look="secondary"
            compact
            label=${this.localize.term("logExplorer_retry")}
            @click=${() => this.#load()}
          ></uui-button>
        `;
      case "loaded":
        return this.#renderSource(this._state.sources[0]!);
    }
  }

  /**
   * Renders one source as the picker's button content will look once it becomes a menu.
   *
   * @param source - The source to show.
   * @returns The template.
   */
  #renderSource(source: SourceResponseModel) {
    return html`
      <umb-icon name="icon-database"></umb-icon>
      <strong class="name">${source.displayName}</strong>
      ${
        source.sensitive
          ? html`<span
              role="img"
              aria-label=${this.localize.term("logExplorer_sensitiveSource")}
              title=${this.localize.term("logExplorer_sensitiveSource")}
            >
              <umb-icon name="icon-lock"></umb-icon>
            </span>`
          : nothing
      }
      ${
        source.capabilities.nativeLanguage
          ? html`<uui-tag look="secondary">${source.capabilities.nativeLanguage}</uui-tag>`
          : nothing
      }
    `;
  }

  static override styles = [
    UmbTextStyles,
    css`
      :host {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
        min-width: 0;
        margin-inline-start: var(--uui-size-space-5);
      }

      .name {
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
      }

      .muted {
        color: var(--uui-color-text-alt);
      }

      .error {
        display: inline-flex;
        align-items: center;
        gap: var(--uui-size-space-2);
        color: var(--uui-color-danger-standalone);
      }
    `,
  ];
}

declare global {
  interface HTMLElementTagNameMap {
    "log-explorer-source-picker": LogExplorerSourcePickerElement;
  }
}
