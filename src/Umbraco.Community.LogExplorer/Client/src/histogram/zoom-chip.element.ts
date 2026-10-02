import { css, customElement, html, nothing, state } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { LOG_EXPLORER_QUERY_CONTEXT, type LogExplorerQueryContext } from "../query/query.context.js";
import type { AbsoluteRange } from "../query/view-state.js";
import { zoomLabelParts } from "./histogram-model.js";

/**
 * The time-zoom chip (UI brief §4.4): `Time: 00:40 to 00:45` in the warning tint, with a remove
 * button that clears the zoom and returns queries to the picker's range. Renders nothing while
 * the view is not zoomed.
 *
 * A `uui-tag` (`color="warning"`, `look="primary"`: the warning tint) holding a compact `uui-button`, as the
 * chips in the search box will be, so it can sit first in their list (#39). Until then the Search
 * view puts it in the query bar beside the time picker.
 *
 * Reads and clears `LogExplorerQueryContext.zoom`. Not bound to a manifest.
 *
 * @element log-explorer-zoom-chip
 */
@customElement("log-explorer-zoom-chip")
export class LogExplorerZoomChipElement extends UmbLitElement {
  /** The zoom in force, mirrored from the query context; `undefined` hides the chip. */
  @state()
  private _zoom: AbsoluteRange | undefined;

  #context?: LogExplorerQueryContext;

  constructor() {
    super();
    this.consumeContext(LOG_EXPLORER_QUERY_CONTEXT, (context) => {
      this.#context = context;
      this.observe(context?.zoom, (zoom) => (this._zoom = zoom), "_observeZoom");
    });
  }

  protected override willUpdate(): void {
    // Hide the host too, so an empty chip takes no gap in the query bar's flex row.
    this.hidden = !this._zoom;
  }

  /**
   * Renders the chip, or nothing when not zoomed.
   *
   * @returns The template.
   */
  override render() {
    if (!this._zoom) return nothing;

    const { from, to } = zoomLabelParts(this._zoom, this.localize.lang());
    const label = this.localize.term("logExplorer_zoomChip", from, to);
    return html`
      <uui-tag color="warning" look="primary" title=${label}>
        <span class="content">
          <span class="label">${label}</span>
          <uui-button
            compact
            label=${this.localize.term("logExplorer_zoomChipRemove", label)}
            @click=${() => this.#context?.setZoom(undefined)}
          >
            <umb-icon name="icon-wrong"></umb-icon>
          </uui-button>
        </span>
      </uui-tag>
    `;
  }

  static override styles = [
    css`
      :host {
        display: inline-flex;
        align-items: center;
      }

      :host([hidden]) {
        display: none;
      }

      uui-tag {
        max-width: 100%;
        min-width: 0;
        /* The same padding and pill shape as the filter chips (filter-chip.element.ts, UI brief §5.3). */
        --uui-tag-padding: 0 0 0 var(--uui-size-space-3);
        --uui-tag-border-radius: var(--uui-size-layout-1);
        white-space: nowrap;
      }

      /* uui-tag is an inline-block with a block slot, so its children align on their text
         baselines and the taller remove button pushes the label down. A flex row centres them. */
      .content {
        display: inline-flex;
        align-items: center;
        min-width: 0;
        max-width: 100%;
      }

      .label {
        min-width: 0;
        overflow: hidden;
        text-overflow: ellipsis;
      }

      /* The filter chips' remove button, so every chip in the search box is the same height. */
      uui-button {
        flex: none;
        --uui-button-height: var(--uui-size-6);
        --uui-button-padding-left-factor: 0.5;
        --uui-button-padding-right-factor: 0.5;
        --uui-button-border-radius: var(--uui-size-layout-1);
      }
    `,
  ];
}

declare global {
  interface HTMLElementTagNameMap {
    "log-explorer-zoom-chip": LogExplorerZoomChipElement;
  }
}
