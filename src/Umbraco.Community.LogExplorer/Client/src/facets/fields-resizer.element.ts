import { css, customElement, html, state } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { MIN_PANEL_WIDTH, clampPanelWidth, keyPanelWidth, maxPanelWidth } from "./panel-width.js";

/**
 * Detail of `log-explorer-fields-resize`: the width the fields panel should take, or `undefined`
 * to go back to its default width.
 */
export interface LogExplorerFieldsResizeDetail {
  /** The new width in CSS pixels, already clamped; `undefined` resets to the default. */
  width: number | undefined;
  /** `true` once the change is final (pointer released, key pressed, reset), so it can be stored. */
  done: boolean;
}

/**
 * The draggable divider between the fields panel and the results (ADR 0025). It fills the gap
 * between them with a `col-resize` cursor; dragging it, or Left/Right (Shift for bigger steps),
 * Home and End while it has focus, asks for a new panel width; double-click resets it.
 *
 * It measures the fields panel (its previous sibling) and its parent (the Search body) itself
 * and fires `log-explorer-fields-resize` with a clamped width; the Search view applies and
 * stores it. A `role="separator"` with its value in pixels, labelled and focusable.
 *
 * @element log-explorer-fields-resizer
 * @fires log-explorer-fields-resize - With a {@link LogExplorerFieldsResizeDetail}.
 */
@customElement("log-explorer-fields-resizer")
export class LogExplorerFieldsResizerElement extends UmbLitElement {
  /** The panel's current width, for `aria-valuenow`. */
  @state()
  private _width = MIN_PANEL_WIDTH;

  /** The widest the panel may be in the current body, for `aria-valuemax`. */
  @state()
  private _max = MIN_PANEL_WIDTH;

  /** Whether a pointer drag is in progress (shows the divider line while dragging). */
  @state()
  private _dragging = false;

  #drag?: { pointerId: number; startX: number; startWidth: number };

  #observer = new ResizeObserver(() => this.#measure());

  override connectedCallback(): void {
    super.connectedCallback();
    this.tabIndex = 0;
    this.setAttribute("role", "separator");
    this.setAttribute("aria-orientation", "vertical");
    this.addEventListener("pointerdown", this.#onPointerDown);
    this.addEventListener("pointermove", this.#onPointerMove);
    this.addEventListener("pointerup", this.#onPointerUp);
    this.addEventListener("pointercancel", this.#onPointerUp);
    this.addEventListener("keydown", this.#onKeydown);
    this.addEventListener("dblclick", this.#onDoubleClick);
    // Observe once the siblings exist; the Search view renders them in the same template.
    void this.updateComplete.then(() => {
      if (this.#panel) this.#observer.observe(this.#panel);
      if (this.parentElement) this.#observer.observe(this.parentElement);
    });
  }

  override disconnectedCallback(): void {
    super.disconnectedCallback();
    this.#observer.disconnect();
  }

  /** The fields panel, which sits immediately before the divider. */
  get #panel(): HTMLElement | null {
    return this.previousElementSibling as HTMLElement | null;
  }

  get #bodyWidth(): number {
    return this.parentElement?.getBoundingClientRect().width ?? 0;
  }

  get #panelWidth(): number {
    return this.#panel?.getBoundingClientRect().width ?? MIN_PANEL_WIDTH;
  }

  #measure(): void {
    this._width = Math.round(this.#panelWidth);
    this._max = maxPanelWidth(this.#bodyWidth);
  }

  #emit(width: number | undefined, done: boolean): void {
    this.dispatchEvent(
      new CustomEvent<LogExplorerFieldsResizeDetail>("log-explorer-fields-resize", {
        detail: { width, done },
        bubbles: true,
        composed: true,
      }),
    );
  }

  #onPointerDown = (event: PointerEvent): void => {
    if (event.button !== 0) return;
    event.preventDefault();
    this.focus();
    // Capture keeps the drag going when the pointer leaves the thin divider.
    this.setPointerCapture(event.pointerId);
    this.#drag = { pointerId: event.pointerId, startX: event.clientX, startWidth: this.#panelWidth };
    this._dragging = true;
  };

  #onPointerMove = (event: PointerEvent): void => {
    if (!this.#drag || event.pointerId !== this.#drag.pointerId) return;
    const width = clampPanelWidth(this.#drag.startWidth + event.clientX - this.#drag.startX, this.#bodyWidth);
    this._width = width;
    this.#emit(width, false);
  };

  #onPointerUp = (event: PointerEvent): void => {
    if (!this.#drag || event.pointerId !== this.#drag.pointerId) return;
    this.#drag = undefined;
    this._dragging = false;
    if (this.hasPointerCapture(event.pointerId)) this.releasePointerCapture(event.pointerId);
    this.#emit(this._width, true);
  };

  #onKeydown = (event: KeyboardEvent): void => {
    if (event.ctrlKey || event.metaKey || event.altKey) return;
    const width = keyPanelWidth(event.key, event.shiftKey, this.#panelWidth, this.#bodyWidth);
    if (width === undefined) return;
    // Stops the view's j/k/Escape handling and page scrolling from also acting on the key.
    event.preventDefault();
    event.stopPropagation();
    this._width = width;
    this.#emit(width, true);
  };

  #onDoubleClick = (): void => {
    this.#emit(undefined, true);
  };

  /** Keeps the separator's label, value range and drag state on the host, which takes focus. */
  protected override updated(): void {
    this.setAttribute("aria-label", this.localize.term("logExplorer_fieldsResize"));
    this.setAttribute("aria-valuenow", String(this._width));
    this.setAttribute("aria-valuemin", String(MIN_PANEL_WIDTH));
    this.setAttribute("aria-valuemax", String(this._max));
    this.title = this.localize.term("logExplorer_fieldsResizeHint");
    this.toggleAttribute("dragging", this._dragging);
  }

  override render() {
    return html`<span class="line" aria-hidden="true"></span>`;
  }

  static override styles = [
    css`
      /* Fills the gap between the panels (the Search body has no gap of its own while the
         divider shows), so the whole gap is the drag target. */
      :host {
        flex: none;
        position: relative;
        width: var(--uui-size-space-4);
        cursor: col-resize;
        touch-action: none;
        outline: none;
      }

      /* A line down the middle of the gap while hovered, focused or dragged. */
      .line {
        position: absolute;
        top: 0;
        bottom: 0;
        left: 50%;
        width: 2px;
        transform: translateX(-50%);
        border-radius: 1px;
        background-color: transparent;
        transition: background-color 120ms ease-out;
      }

      :host(:hover) .line,
      :host([dragging]) .line {
        background-color: var(--uui-color-border-emphasis);
      }

      :host(:focus-visible) .line {
        background-color: var(--uui-color-focus);
      }

      @media (prefers-reduced-motion: reduce) {
        .line {
          transition: none;
        }
      }
    `,
  ];
}

declare global {
  interface HTMLElementTagNameMap {
    "log-explorer-fields-resizer": LogExplorerFieldsResizerElement;
  }
  interface HTMLElementEventMap {
    "log-explorer-fields-resize": CustomEvent<LogExplorerFieldsResizeDetail>;
  }
}
