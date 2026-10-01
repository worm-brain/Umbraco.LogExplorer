import type { ReactiveController, ReactiveControllerHost } from "@umbraco-cms/backoffice/external/lit";
import { rovingIndex } from "./roving-focus.js";

/** What the controller needs of a `uui-button`: its focus and its rendered shadow root. */
type TriggerElement = HTMLElement & { updateComplete?: Promise<unknown> };

/** What the controller needs of a `uui-menu-item`. */
type MenuItemElement = HTMLElement & {
  updateComplete?: Promise<unknown>;
  selected?: boolean;
  active?: boolean;
};

/** What the controller needs of a `uui-popover-container`: the native popover API. */
type PopoverElement = HTMLElement & Pick<HTMLElement, "hidePopover" | "matches">;

/** Looks up the parts of the menu in the host's shadow root after each render. */
export interface PopoverMenuParts {
  /** The `uui-button` with `popovertarget` that opens the menu. */
  trigger(): TriggerElement | null | undefined;
  /** The `uui-popover-container` holding the menu. */
  popover(): PopoverElement | null | undefined;
  /** The element with `role="menu"` whose `uui-menu-item` children are the choices. */
  menu(): HTMLElement | null | undefined;
}

/**
 * Makes a `uui-button` + `uui-popover-container` + `uui-menu-item` menu behave as a WAI-ARIA menu
 * button (issue #51): one menu with one radio item per choice, arrow keys between items, focus
 * moved into the menu on open and back to the button after a choice or Escape.
 *
 * Why the patching: UUI builds `uui-menu-item` for navigation trees, not for menus. Each item
 * gives its host `role="menu"` (unless it already has a role) and wraps a `<button>` in a
 * `div role="menuitem" aria-label="menuitem"` inside its shadow root, so a list of them reads
 * as several one-item menus whose items are all called "menuitem". `uui-button` forwards only
 * `aria-label` to the `<button>` in its shadow root, which is what takes focus, so
 * `aria-expanded` and `aria-haspopup` on its host never reach assistive technology. UUI offers
 * no attribute for either, so after each render this controller:
 *
 * - sets `aria-haspopup="menu"` and `aria-expanded` on the trigger's inner button;
 * - turns each item's wrapper `div` into `role="none"` (dropping its "menuitem" label) and its
 *   inner button into `role="menuitemradio"` with `aria-checked` from `selected` or `active`,
 *   and `tabindex="-1"`, so the whole menu is reached through the arrow keys, not Tab.
 *
 * The host renders the items with `role="none"` themselves (before they connect, so UUI does not
 * add `role="menu"`) inside an element with `role="menu"`, and wires {@link onToggle},
 * {@link onKeydown} and {@link onFocusOut} on the popover. The popover should carry `no-scroll`, so that UUI
 * does not wrap the menu in a `uui-scroll-container`, which takes a Tab stop of its own.
 *
 * Escape and outside clicks still close the menu through the native popover's light dismiss.
 */
export class PopoverMenuController implements ReactiveController {
  #host: ReactiveControllerHost;
  #parts: PopoverMenuParts;
  #open = false;
  /** Set when the next close should return focus to the trigger (a choice, or Escape). */
  #restoreFocus = false;

  /**
   * @param host - The element rendering the menu; re-rendered when the menu opens or closes.
   * @param parts - Look-ups for the trigger, popover and menu in the host's shadow root.
   */
  constructor(host: ReactiveControllerHost, parts: PopoverMenuParts) {
    this.#host = host;
    this.#parts = parts;
    host.addController(this);
  }

  /** Whether the menu is open, from the popover's last `toggle` event; drives the chevron. */
  get open(): boolean {
    return this.#open;
  }

  /** Re-applies the ARIA the host's render cannot reach after every update. */
  hostUpdated(): void {
    void this.#syncAria();
  }

  /**
   * Closes the menu.
   *
   * @param restoreFocus - Whether focus returns to the trigger, as it should after a choice.
   */
  close(restoreFocus = true): void {
    this.#restoreFocus = restoreFocus;
    this.#parts.popover()?.hidePopover();
  }

  /**
   * The popover's `toggle` handler: tracks the open state, moves focus to the checked item (or
   * the first) on open, and back to the trigger on a close that asked for it.
   *
   * @param event - The popover's `ToggleEvent`.
   */
  onToggle = (event: Event): void => {
    this.#open = (event as ToggleEvent).newState === "open";
    this.#host.requestUpdate();
    if (this.#open) {
      this.#restoreFocus = false;
      void this.#focusInitialItem();
      return;
    }
    if (this.#restoreFocus) {
      this.#restoreFocus = false;
      void this.#parts.trigger()?.focus();
    }
  };

  /**
   * The popover's `keydown` handler: Up/Down/Home/End move between items (wrapping), and Escape
   * marks the close the browser is about to do as one that returns focus to the trigger. Other
   * keys are left alone; Enter and Space activate the focused item as a native button.
   *
   * Wired on the popover rather than the menu so Escape in other content of the popover (the
   * time range picker's custom inputs) also returns focus; the arrow keys act only when a menu
   * item has focus, so they still change the value of an input.
   *
   * @param event - The key press, from anywhere inside the popover.
   */
  onKeydown = (event: KeyboardEvent): void => {
    if (event.key === "Escape") {
      // Not cancelled: the popover's light dismiss closes the menu.
      this.#restoreFocus = true;
      return;
    }
    const items = this.#items();
    // The key comes from the focused item's label button, so that item is on the event's path.
    const path = event.composedPath();
    const current = items.findIndex((item) => path.includes(item));
    if (current < 0) return;
    const next = rovingIndex(current, event.key, items.length, { orientation: "vertical", wrap: true });
    if (next === undefined) return;
    event.preventDefault();
    void items[next]?.focus();
  };

  /**
   * The popover's `focusout` handler: closes the menu when Tab (or anything else) moves focus to
   * a control outside it, as a menu does. Moving to the trigger is left to the trigger's own
   * toggle, and a `null` target (a click on nothing focusable, or leaving the window) to the
   * light dismiss.
   *
   * @param event - The focus change; `relatedTarget` is retargeted into the host's shadow root.
   */
  onFocusOut = (event: FocusEvent): void => {
    const popover = this.#parts.popover();
    const target = event.relatedTarget as Node | null;
    if (!popover || !this.#open || !target) return;
    if (popover.contains(target) || target === this.#parts.trigger()) return;
    this.close(false);
  };

  #items(): Array<MenuItemElement> {
    return Array.from(this.#parts.menu()?.querySelectorAll<MenuItemElement>("uui-menu-item") ?? []);
  }

  async #focusInitialItem(): Promise<void> {
    const items = this.#items();
    const item = items.find((candidate) => candidate.selected || candidate.active) ?? items[0];
    await item?.updateComplete;
    void item?.focus();
  }

  async #syncAria(): Promise<void> {
    const trigger = this.#parts.trigger();
    if (trigger) {
      await trigger.updateComplete;
      const button = trigger.shadowRoot?.querySelector("#button");
      button?.setAttribute("aria-haspopup", "menu");
      button?.setAttribute("aria-expanded", String(this.#open));
    }

    for (const item of this.#items()) {
      await item.updateComplete;
      if (item.getAttribute("role") !== "none") item.setAttribute("role", "none");
      const wrapper = item.shadowRoot?.querySelector("#menu-item");
      wrapper?.setAttribute("role", "none");
      wrapper?.removeAttribute("aria-label");
      const button = item.shadowRoot?.querySelector("#label-button");
      button?.setAttribute("role", "menuitemradio");
      button?.setAttribute("aria-checked", String(Boolean(item.selected || item.active)));
      button?.setAttribute("tabindex", "-1");
    }
  }
}
