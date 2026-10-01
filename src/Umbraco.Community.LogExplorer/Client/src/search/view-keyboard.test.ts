import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { ReactiveController } from "@umbraco-cms/backoffice/external/lit";
import { SearchViewKeyboard, type ViewKeyboardTarget } from "./view-keyboard.js";

/** A minimal Lit controller host: connects its controllers when added to the document. */
class TestViewElement extends HTMLElement {
  readonly controllers: ReactiveController[] = [];
  addController(controller: ReactiveController): void {
    this.controllers.push(controller);
  }
  removeController(): void {}
  requestUpdate(): void {}
  readonly updateComplete = Promise.resolve(true);
  connectedCallback(): void {
    this.controllers.forEach((controller) => controller.hostConnected?.());
  }
  disconnectedCallback(): void {
    this.controllers.forEach((controller) => controller.hostDisconnected?.());
  }
}
customElements.define("test-keyboard-view", TestViewElement);

let view: TestViewElement;
let row: HTMLButtonElement;
let target: { [K in keyof ViewKeyboardTarget]: ReturnType<typeof vi.fn> };

const keydown = (on: EventTarget, key: string) => {
  const event = new KeyboardEvent("keydown", { key, bubbles: true, composed: true, cancelable: true });
  on.dispatchEvent(event);
  return event;
};

beforeEach(() => {
  view = document.createElement("test-keyboard-view") as TestViewElement;
  // The results sit in the view's shadow root, as in the real Search view.
  const root = view.attachShadow({ mode: "open" });
  const results = document.createElement("log-explorer-results");
  row = document.createElement("button");
  results.append(row);
  root.append(results);

  target = {
    focusSearch: vi.fn(),
    searchHasText: vi.fn(() => false),
    clearSearch: vi.fn(),
    moveRowFocus: vi.fn(),
    drawerOpen: vi.fn(() => false),
    closeDrawer: vi.fn(),
  };
  new SearchViewKeyboard(view, target as unknown as ViewKeyboardTarget);
  document.body.append(view);
});

afterEach(() => {
  view.remove();
  document.body.replaceChildren();
});

describe("SearchViewKeyboard", () => {
  it("moves row focus on j pressed inside the view", () => {
    keydown(row, "j");
    expect(target.moveRowFocus).toHaveBeenCalledWith(1);
  });

  it("cancels / so the slash is not typed into the search box", () => {
    const event = keydown(row, "/");
    expect({ focused: target.focusSearch.mock.calls.length, cancelled: event.defaultPrevented }).toEqual({
      focused: 1,
      cancelled: true,
    });
  });

  it("ignores keys pressed elsewhere in the backoffice and leaves them uncancelled", () => {
    const outside = document.createElement("button");
    document.body.append(outside);
    const event = keydown(outside, "j");
    expect({ moved: target.moveRowFocus.mock.calls.length, cancelled: event.defaultPrevented }).toEqual({
      moved: 0,
      cancelled: false,
    });
  });

  it("takes a key pressed with nothing focused, as after a menu closes", () => {
    keydown(document.body, "k");
    expect(target.moveRowFocus).toHaveBeenCalledWith(-1);
  });

  it("closes the drawer on Escape from a row and asks for focus back on the row", () => {
    target.drawerOpen.mockReturnValue(true);
    keydown(row, "Escape");
    expect(target.closeDrawer).toHaveBeenCalledWith(true);
  });

  it("stops taking keys once the view is removed (another tab is open)", () => {
    view.remove();
    keydown(document.body, "j");
    expect(target.moveRowFocus).not.toHaveBeenCalled();
  });
});
