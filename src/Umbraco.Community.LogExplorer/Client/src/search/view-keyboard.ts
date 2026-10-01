import type { ReactiveController, ReactiveControllerHost } from "@umbraco-cms/backoffice/external/lit";
import { isTypingTarget, viewKeyAction, type ViewKeyAction } from "./view-keys.js";

/** What the shortcuts act on; the Search view implements it over its children. */
export interface ViewKeyboardTarget {
  /** Moves focus to the search box input. */
  focusSearch(): void;
  /** Whether the search box holds uncommitted text. */
  searchHasText(): boolean;
  /** Empties the search box input. */
  clearSearch(): void;
  /**
   * Moves focus one results row down (`1`) or up (`-1`). With no row focused, focuses the open
   * entry's row or the first visible one instead of stepping.
   */
  moveRowFocus(step: 1 | -1): void;
  /** Whether the entry detail drawer is open. */
  drawerOpen(): boolean;
  /**
   * Closes the drawer.
   *
   * @param restoreFocus - `true` when the key came from the results, so focus goes back to the
   *   open entry's row; `false` leaves focus where the user put it (the search box, a facet).
   */
  closeDrawer(restoreFocus: boolean): void;
}

/** The workspace element the shortcuts cover, header (source picker) included. */
const WORKSPACE_TAG = "log-explorer-workspace";

/** Whether an event path passes through an element with this tag name. */
const pathHas = (path: EventTarget[], tag: string): boolean =>
  path.some((node) => node instanceof Element && node.localName === tag);

/**
 * The Search view's keyboard shortcuts (BRIEF §6.15): `/`, `j`, `k` and Escape, decided by
 * {@link viewKeyAction}.
 *
 * Listens on `window` rather than on the view, so a key pressed with nothing focused still counts:
 * focus falls back to `<body>` after clicking a blank part of a panel or when a menu closes on
 * selection, and a keyboard user must still be able to press `/` or `j` then. While the view is
 * connected it is the Search tab on screen, so a key with nothing focused can only be meant for
 * it. Keys from anywhere else in the backoffice (the tree, a modal, another section) are ignored
 * and never cancelled, so its own shortcuts keep working. Listening in the bubble phase lets the
 * children that handle a key themselves (the search box clearing its text, the drawer closing on
 * Escape) go first and stop or cancel it.
 *
 * Active between the host's connect and disconnect, so it ends when the user switches to the
 * Patterns or Overview tab (the workspace editor removes the view).
 */
export class SearchViewKeyboard implements ReactiveController {
  #host: ReactiveControllerHost & HTMLElement;
  #target: ViewKeyboardTarget;

  /**
   * @param host - The Search view; the controller registers itself on it.
   * @param target - What the shortcuts act on.
   */
  constructor(host: ReactiveControllerHost & HTMLElement, target: ViewKeyboardTarget) {
    this.#host = host;
    this.#target = target;
    host.addController(this);
  }

  /** Starts listening; called by Lit when the view connects. */
  hostConnected(): void {
    window.addEventListener("keydown", this.#onKeydown);
  }

  /** Stops listening; called by Lit when the view disconnects. */
  hostDisconnected(): void {
    window.removeEventListener("keydown", this.#onKeydown);
  }

  #onKeydown = (event: KeyboardEvent): void => {
    const path = event.composedPath();
    if (!this.#inScope(path)) return;

    const action = viewKeyAction(event, {
      typing: isTypingTarget(path[0]),
      inSearchBox: pathHas(path, "log-explorer-search-box"),
      menuOpen: event.key === "Escape" && hasOpenPopover(this.#scope()),
      drawerOpen: this.#target.drawerOpen(),
      searchHasText: this.#target.searchHasText(),
    });
    if (!action) return;
    // Cancelled so `/` is not typed into the search box focus has just moved to, and so nothing
    // further up (the browser's quick find on `/`) acts on the same key.
    event.preventDefault();
    this.#run(action, path);
  };

  #run(action: ViewKeyAction, path: EventTarget[]): void {
    switch (action) {
      case "focusSearch":
        this.#target.focusSearch();
        break;
      case "nextRow":
      case "focusResults":
        this.#target.moveRowFocus(1);
        break;
      case "previousRow":
        this.#target.moveRowFocus(-1);
        break;
      case "closeDrawer":
        this.#target.closeDrawer(pathHas(path, "log-explorer-results"));
        break;
      case "clearSearch":
        this.#target.clearSearch();
        break;
    }
  }

  /** A key belongs to the view when it was pressed inside the Log Explorer workspace, or with nothing focused. */
  #inScope(path: EventTarget[]): boolean {
    const origin = path[0];
    if (origin === document.body || origin === document.documentElement || origin === window) return true;
    return path.includes(this.#scope());
  }

  /** The enclosing `log-explorer-workspace`, found across shadow roots; the view when there is none. */
  #scope(): Element {
    let node: Node | null = this.#host;
    while (node) {
      if (node instanceof Element && node.localName === WORKSPACE_TAG) return node;
      node = node instanceof ShadowRoot ? node.host : node.parentNode;
    }
    return this.#host;
  }
}

/**
 * Whether any popover under `root`, in its shadow trees included, is open. The menus (time
 * range, source picker, chip editor) are `uui-popover-container`s, native popovers, in nested
 * shadow roots that `querySelector` alone does not reach. Only light-dismiss (`auto`) popovers
 * count: a `manual` one, such as the backoffice's always-open toast container, does not close on
 * Escape and must not swallow it.
 *
 * @param root - Where to look.
 * @returns `true` when one is open; `false` too where the browser does not support popovers.
 */
export function hasOpenPopover(root: Element): boolean {
  const selector = ':popover-open:not([popover="manual"])';
  const stack: ParentNode[] = [root];
  try {
    if (root.matches(selector)) return true;
    while (stack.length > 0) {
      const current = stack.pop()!;
      if (current.querySelector(selector)) return true;
      for (const element of current.querySelectorAll("*")) {
        if (element.shadowRoot) stack.push(element.shadowRoot);
      }
      if (current instanceof Element && current.shadowRoot) stack.push(current.shadowRoot);
    }
  } catch {
    // `:popover-open` is an invalid selector where popovers are unsupported: nothing can be open.
    return false;
  }
  return false;
}
