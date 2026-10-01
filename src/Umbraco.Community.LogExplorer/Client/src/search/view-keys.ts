/**
 * What a key press anywhere in the Search view does (BRIEF §6.15, UI brief §6):
 *
 * - `focusSearch`: `/` moves focus to the search box.
 * - `nextRow` / `previousRow`: `j` / `k` move focus down / up the results.
 * - `closeDrawer`, `clearSearch`, `focusResults`: the Escape outcomes the view owns (see
 *   {@link viewKeyAction} for the precedence).
 *
 * Enter on a focused row needs no action: rows are native buttons, so the browser activates them.
 */
export type ViewKeyAction = "focusSearch" | "nextRow" | "previousRow" | "closeDrawer" | "clearSearch" | "focusResults";

/** The parts of a `KeyboardEvent` the dispatch reads, so tests can pass plain objects. */
export type ViewKeyEvent = Pick<
  KeyboardEvent,
  "key" | "ctrlKey" | "metaKey" | "altKey" | "isComposing" | "defaultPrevented"
>;

/** What the view knows at the moment of the key press. */
export interface ViewKeyState {
  /** Focus is in a text field (input, textarea, select or contenteditable); see {@link isTypingTarget}. */
  typing: boolean;
  /** Focus is in the search box (its input or a chip). */
  inSearchBox: boolean;
  /** A menu or popover in the view is open; the browser closes it on Escape. */
  menuOpen: boolean;
  /** The entry detail drawer is open. */
  drawerOpen: boolean;
  /** The search box holds text that has not been turned into chips yet. */
  searchHasText: boolean;
}

/**
 * Maps a key press to its {@link ViewKeyAction}.
 *
 * `/`, `j` and `k` act only when focus is not in a text field, so they can still be typed, and
 * never with Ctrl, Meta or Alt held, so the browser's and the backoffice's own shortcuts keep
 * working. Shift is allowed for `/` because some layouts (German, Nordic) need it to type one.
 *
 * Escape works from text fields too, innermost thing first:
 *
 * 1. A handler nearer the focus already used it (`defaultPrevented`): the search box clearing its
 *    own text, the drawer closing itself, a chip editor. Nothing more happens.
 * 2. A menu is open: left to the browser, whose popover light dismiss closes it. Cancelling the
 *    event would stop that.
 * 3. The drawer is open: close it.
 * 4. The search box holds text: clear it. (With focus in the box, the box already did.)
 * 5. Focus is in the search box with nothing left to clear: move focus to the results, so `j`/`k`
 *    work next without tabbing through every histogram bar. Not in the briefs; it gives the
 *    keyboard a way back from `/` to the rows, as Escape leaves a mail client's search field.
 *
 * @param event - The key press.
 * @param state - The view at the time of the press.
 * @returns The action, or `undefined` to leave the key alone (and not cancel it).
 */
export function viewKeyAction(event: ViewKeyEvent, state: ViewKeyState): ViewKeyAction | undefined {
  if (event.isComposing || event.defaultPrevented) return undefined;
  if (event.ctrlKey || event.metaKey || event.altKey) return undefined;

  if (event.key === "Escape") {
    if (state.menuOpen) return undefined;
    if (state.drawerOpen) return "closeDrawer";
    if (state.searchHasText) return "clearSearch";
    return state.inSearchBox ? "focusResults" : undefined;
  }

  if (state.typing) return undefined;
  switch (event.key) {
    case "/":
      return "focusSearch";
    case "j":
      return "nextRow";
    case "k":
      return "previousRow";
    default:
      return undefined;
  }
}

/** Input types that take no typed text, so shortcuts still apply while one is focused. */
const NON_TEXT_INPUT_TYPES = new Set([
  "button",
  "checkbox",
  "radio",
  "range",
  "submit",
  "reset",
  "color",
  "file",
  "image",
]);

/**
 * Whether the element a key press started on takes typed text. Pass the event's
 * `composedPath()[0]`, not `target`: a key typed into `uui-input` is retargeted to the
 * `uui-input` host by the time it leaves its shadow root, and only the inner `<input>` says
 * that it is a text field.
 *
 * @param element - The innermost target, or `null`/anything else when there is none.
 * @returns `true` for text inputs, textareas, selects and contenteditable content.
 */
export function isTypingTarget(element: EventTarget | null | undefined): boolean {
  if (!(element instanceof HTMLElement)) return false;
  if (element.isContentEditable) return true;
  if (element instanceof HTMLTextAreaElement || element instanceof HTMLSelectElement) return true;
  return element instanceof HTMLInputElement && !NON_TEXT_INPUT_TYPES.has(element.type);
}
