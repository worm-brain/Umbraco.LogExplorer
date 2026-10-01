import { css, customElement, html } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UMB_NOTIFICATION_CONTEXT, type UmbNotificationContext } from "@umbraco-cms/backoffice/notification";
import { LOG_EXPLORER_QUERY_CONTEXT, type LogExplorerQueryContext } from "../query/query.context.js";
import { buildShareUrl } from "./share-link.js";

/**
 * The query bar's "Copy link to this view" icon button (UI brief §4.5, BRIEF §6.11): a square,
 * icon-only `uui-button` that copies an absolute URL reproducing the view on screen (see
 * `buildShareUrl`) and confirms with a notification (UI brief §4.14), or a danger notification
 * when the clipboard refuses the write. It is an action, not a toggle, so it has no pressed state.
 *
 * Not `uui-button-copy-text`: that reports a failed write only with `console.error`, and the
 * failure needs a notification (as the drawer's Copy as JSON does).
 *
 * Not bound to a manifest; the Search, Patterns and Overview views render it at the end of the
 * query bar.
 *
 * @element log-explorer-share-button
 */
@customElement("log-explorer-share-button")
export class LogExplorerShareButtonElement extends UmbLitElement {
  #context?: LogExplorerQueryContext;
  #notifications?: UmbNotificationContext;

  constructor() {
    super();
    this.consumeContext(LOG_EXPLORER_QUERY_CONTEXT, (context) => (this.#context = context));
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (context) => (this.#notifications = context));
  }

  async #copy(): Promise<void> {
    const context = this.#context;
    if (!context) return;
    try {
      await navigator.clipboard.writeText(buildShareUrl(window.location, context.getState(), context.getDefaults()));
      this.#notifications?.peek("positive", { data: { message: this.localize.term("logExplorer_shareCopied") } });
    } catch {
      // No clipboard API (an insecure origin) or the write was refused.
      this.#notifications?.peek("danger", { data: { message: this.localize.term("logExplorer_shareCopyFailed") } });
    }
  }

  /**
   * Renders the button.
   *
   * @returns The template.
   */
  override render() {
    const label = this.localize.term("logExplorer_shareButton");
    return html`
      <uui-button look="outline" label=${label} title=${label} @click=${this.#copy}>
        <umb-icon name="icon-link"></umb-icon>
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
        /* Square: as wide as the bar is tall, like the show-query button beside it. */
        aspect-ratio: 1;
      }
    `,
  ];
}

declare global {
  interface HTMLElementTagNameMap {
    "log-explorer-share-button": LogExplorerShareButtonElement;
  }
}
