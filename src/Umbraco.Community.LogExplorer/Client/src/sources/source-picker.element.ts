import { css, customElement, html, nothing, query, state } from "@umbraco-cms/backoffice/external/lit";
import type { UUIButtonElement, UUIPopoverContainerElement } from "@umbraco-cms/backoffice/external/uui";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import type { SourceResponseModel } from "../api/index.js";
import { LOG_EXPLORER_QUERY_CONTEXT, type LogExplorerQueryContext } from "../query/query.context.js";
import type { SourcesState } from "./sources-loader.js";

/**
 * The source picker in the Log Explorer workspace header (UI brief §4.1).
 *
 * A button shows the active source (database icon, display name, a lock when sensitive, a tag
 * with the native query language when the source has one, chevron) and opens a menu captioned
 * "Log sources (from appsettings)" listing every visible source. Choosing one writes its alias
 * to `LogExplorerQueryContext`, which records it in the URL as `src`. What else changes on a
 * switch (keeping compatible chips, disabling the rest) is #54.
 *
 * The sources, their loading and error states and the resolved active source all come from the
 * query context; this element only renders them. Retry asks the context to fetch again.
 *
 * Built from `uui-button` + `uui-popover-container` + `umb-popover-layout` + `uui-menu-item`, like
 * the time range picker, because `umb-dropdown` closes on any click inside its popover. Escape
 * and outside clicks close the menu through the native popover API.
 *
 * Rendered by `log-explorer-workspace` into `umb-workspace-editor`'s `header` slot; no manifest
 * binds to it directly.
 *
 * @element log-explorer-source-picker
 */
@customElement("log-explorer-source-picker")
export class LogExplorerSourcePickerElement extends UmbLitElement {
  /** The sources fetch, mirrored from the query context. */
  @state()
  private _sources: SourcesState = { status: "loading" };

  /** The resolved active source; `undefined` while the context is still resolving it. */
  @state()
  private _active: SourceResponseModel | undefined;

  /** Whether the menu is open; drives the chevron and `aria-expanded`. */
  @state()
  private _open = false;

  @query("#menu")
  private _menu?: UUIPopoverContainerElement;

  @query("#trigger")
  private _trigger?: UUIButtonElement;

  #context?: LogExplorerQueryContext;

  constructor() {
    super();
    this.consumeContext(LOG_EXPLORER_QUERY_CONTEXT, (context) => {
      this.#context = context;
      this.observe(
        context?.sources,
        (sources) => (this._sources = sources ?? { status: "loading" }),
        "_observeSources",
      );
      this.observe(context?.activeSource, (active) => (this._active = active), "_observeActive");
    });
  }

  #select(alias: string): void {
    this.#context?.setSource(alias);
    this._menu?.hidePopover();
  }

  #onToggle(event: Event): void {
    this._open = (event as ToggleEvent).newState === "open";
  }

  /** Keeps the menu state on the focusable button after each render. */
  override updated(): void {
    void this.#syncTriggerAria();
  }

  /**
   * `uui-button` forwards only `aria-label`/`aria-labelledby` to the `<button>` in its shadow
   * root, and that inner button is what takes focus, so `aria-haspopup` and `aria-expanded` on the
   * host would never reach assistive technology. Set them on the inner button directly.
   */
  async #syncTriggerAria(): Promise<void> {
    const trigger = this._trigger;
    if (!trigger) return;
    await trigger.updateComplete;
    const button = trigger.shadowRoot?.querySelector("#button");
    button?.setAttribute("aria-haspopup", "menu");
    button?.setAttribute("aria-expanded", String(this._open));
  }

  /**
   * Renders the picker, or the state of the sources fetch.
   *
   * @returns The template.
   */
  override render() {
    switch (this._sources.status) {
      case "loading":
        return this.#renderLoading();
      case "empty":
        return html`<span class="muted">${this.localize.term("logExplorer_sourcesEmpty")}</span>`;
      case "error":
        return html`
          <span class="error" role="alert">
            <umb-icon name="icon-alert"></umb-icon>
            ${this.localize.term("logExplorer_sourcesError", this._sources.message)}
          </span>
          <uui-button
            look="secondary"
            compact
            label=${this.localize.term("logExplorer_retry")}
            @click=${() => this.#context?.reloadSources()}
          ></uui-button>
        `;
      case "loaded":
        // Sources can arrive before /settings has said which one is the default.
        return this._active ? this.#renderPicker(this._sources.sources, this._active) : this.#renderLoading();
    }
  }

  #renderLoading() {
    return html`<span class="muted">${this.localize.term("logExplorer_sourcesLoading")}</span>`;
  }

  #renderPicker(sources: Array<SourceResponseModel>, active: SourceResponseModel) {
    const label = this.localize.term("logExplorer_sourcePickerLabel", active.displayName);
    return html`
      <uui-button id="trigger" popovertarget="menu" look="outline" label=${label} title=${label}>
        <span class="button-content">
          <umb-icon name="icon-database"></umb-icon>
          <strong class="name">${active.displayName}</strong>
          ${this.#renderLock(active)}
          ${
            active.capabilities.nativeLanguage
              ? html`<uui-tag look="secondary">${active.capabilities.nativeLanguage}</uui-tag>`
              : nothing
          }
          <uui-symbol-expand .open=${this._open}></uui-symbol-expand>
        </span>
      </uui-button>
      <uui-popover-container id="menu" placement="bottom-start" @toggle=${this.#onToggle}>
        <umb-popover-layout>
          <p class="caption">${this.localize.term("logExplorer_sourcesMenuCaption")}</p>
          ${sources.map((source) => this.#renderItem(source, source.alias === active.alias))}
        </umb-popover-layout>
      </uui-popover-container>
    `;
  }

  #renderItem(source: SourceResponseModel, selected: boolean) {
    const language = source.capabilities.nativeLanguage;
    const note = language ? this.localize.term("logExplorer_sourceNote", language, source.type) : source.type;
    // The menu item puts `label` on its inner button as aria-label, which replaces the slotted
    // content for screen readers, so the label carries the lock and the note too.
    const label = this.localize.term(
      source.sensitive ? "logExplorer_sourceItemLabelSensitive" : "logExplorer_sourceItemLabel",
      source.displayName,
      note,
    );
    // `selected` without `selectable` gives the menu item's selected look without its own
    // toggle-on-click behaviour; the label click is handled through `click-label`.
    return html`
      <uui-menu-item label=${label} ?selected=${selected} @click-label=${() => this.#select(source.alias)}>
        <span slot="label" class="item">
          <span class="item-name">
            <strong>${source.displayName}</strong>
            ${this.#renderLock(source)}
          </span>
          <span class="note">${note}</span>
        </span>
      </uui-menu-item>
    `;
  }

  #renderLock(source: SourceResponseModel) {
    if (!source.sensitive) return nothing;
    const label = this.localize.term("logExplorer_sensitiveSource");
    return html`<span class="lock" role="img" aria-label=${label} title=${label}>
      <umb-icon name="icon-lock"></umb-icon>
    </span>`;
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

      uui-button {
        min-width: 0;
      }

      .button-content {
        display: inline-flex;
        align-items: center;
        gap: var(--uui-size-space-2);
        min-width: 0;
        white-space: nowrap;
      }

      .name {
        overflow: hidden;
        text-overflow: ellipsis;
      }

      .lock {
        display: inline-flex;
      }

      .caption {
        margin: 0;
        padding: var(--uui-size-space-3) var(--uui-size-space-4) var(--uui-size-space-2);
        color: var(--uui-color-text-alt);
        font-size: var(--uui-type-small-size);
      }

      uui-menu-item {
        /* A flat list: no indent reserved for a chevron. */
        --uui-menu-item-flat-structure: 1;
      }

      .item {
        display: flex;
        flex-direction: column;
        padding-block: var(--uui-size-space-2);
        line-height: normal;
      }

      .item-name {
        display: inline-flex;
        align-items: center;
        gap: var(--uui-size-space-2);
      }

      .note {
        color: var(--uui-color-text-alt);
        font-size: var(--uui-type-small-size);
      }

      /* The selected item is accent-filled; the alt text colour would fail contrast on it. */
      uui-menu-item[selected] .note {
        color: inherit;
      }

      /* The selected item is accent-filled; the alt text colour would fail contrast on it. */
      uui-menu-item[selected] .note {
        color: inherit;
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
