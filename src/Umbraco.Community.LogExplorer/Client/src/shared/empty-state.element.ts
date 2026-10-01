import { css, customElement, html, property } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";

/**
 * A panel that says why there is nothing to show, used for empty views and empty results.
 *
 * UI brief §4.0 asks for `umb-empty-state` "if available, else `uui-box` with text". The
 * installed `@umbraco-cms/backoffice` (17.0 to 17.7, and 18.2) registers no `umb-empty-state`
 * element, so this composes `uui-box` with its headline and default content instead.
 *
 * Not bound to a manifest; views render it directly.
 *
 * @element log-explorer-empty-state
 * @slot - The explanation shown under the headline.
 */
@customElement("log-explorer-empty-state")
export class LogExplorerEmptyStateElement extends UmbLitElement {
  /** The panel headline, for example the view's name. */
  @property()
  headline = "";

  /**
   * Renders the box; the explanation comes from the default slot so callers keep control of
   * its markup (and it stays text, never HTML from data).
   *
   * @returns The template.
   */
  override render() {
    return html`<uui-box headline=${this.headline}><slot></slot></uui-box>`;
  }

  static override styles = [
    UmbTextStyles,
    css`
      :host {
        display: block;
        padding: var(--uui-size-layout-1);
      }

      ::slotted(*) {
        margin: 0;
        color: var(--uui-color-text-alt);
      }
    `,
  ];
}

declare global {
  interface HTMLElementTagNameMap {
    "log-explorer-empty-state": LogExplorerEmptyStateElement;
  }
}
