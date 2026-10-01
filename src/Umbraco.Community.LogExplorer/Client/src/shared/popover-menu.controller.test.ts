import { beforeEach, describe, expect, it } from "vitest";
import type { ReactiveController, ReactiveControllerHost } from "@umbraco-cms/backoffice/external/lit";
import { PopoverMenuController } from "./popover-menu.controller.js";

// Stand-ins with the shadow DOM UUI renders (uui-button's `#button`; uui-menu-item's
// `#menu-item` wrapper and `#label-button`), so the controller runs against the same structure
// without loading UUI, whose controls need ElementInternals that happy-dom lacks.

class FakeButton extends HTMLElement {
  readonly updateComplete = Promise.resolve(true);
  constructor() {
    super();
    this.attachShadow({ mode: "open" }).innerHTML = `<button id="button"></button>`;
  }
  override focus(): void {
    this.shadowRoot!.querySelector("button")!.focus();
  }
}
customElements.define("fake-uui-button", FakeButton);

class FakeMenuItem extends HTMLElement {
  readonly updateComplete = Promise.resolve(true);
  selected = false;
  active = false;
  constructor() {
    super();
    this.attachShadow({ mode: "open" }).innerHTML =
      `<div id="menu-item" role="menuitem" aria-label="menuitem"><button id="label-button"></button></div>`;
  }
  override focus(): void {
    this.shadowRoot!.querySelector("button")!.focus();
  }
}
// The controller finds items by the UUI tag name.
customElements.define("uui-menu-item", FakeMenuItem);

/** A controller host that records re-render requests. */
class FakeHost implements ReactiveControllerHost {
  controllers: Array<ReactiveController> = [];
  updates = 0;
  readonly updateComplete = Promise.resolve(true);
  addController(controller: ReactiveController): void {
    this.controllers.push(controller);
  }
  removeController(): void {}
  requestUpdate(): void {
    this.updates++;
  }
}

let trigger: FakeButton;
let popover: HTMLElement;
let menu: HTMLElement;
let items: Array<FakeMenuItem>;
let outside: HTMLButtonElement;
let controller: PopoverMenuController;

function toggle(newState: "open" | "closed"): Event {
  return Object.assign(new Event("toggle"), { newState });
}

function key(name: string): KeyboardEvent {
  return new KeyboardEvent("keydown", { key: name, cancelable: true, bubbles: true, composed: true });
}

function labelButton(item: FakeMenuItem): HTMLElement {
  return item.shadowRoot!.querySelector("#label-button")!;
}

/** Lets the controller's awaited `updateComplete`s resolve. */
async function settle(): Promise<void> {
  for (let i = 0; i < 5; i++) await Promise.resolve();
}

beforeEach(() => {
  document.body.innerHTML = "";
  trigger = new FakeButton();
  popover = document.createElement("div");
  popover.hidePopover = () => popover.dispatchEvent(toggle("closed"));
  menu = document.createElement("div");
  items = [new FakeMenuItem(), new FakeMenuItem(), new FakeMenuItem()];
  items[1]!.active = true;
  menu.append(...items);
  popover.append(menu);
  outside = document.createElement("button");
  document.body.append(trigger, popover, outside);

  controller = new PopoverMenuController(new FakeHost(), {
    trigger: () => trigger,
    popover: () => popover,
    menu: () => menu,
  });
  popover.addEventListener("toggle", controller.onToggle);
  popover.addEventListener("keydown", controller.onKeydown);
});

describe("PopoverMenuController", () => {
  it("exposes a closed menu on the trigger's focusable button", async () => {
    controller.hostUpdated();
    await settle();

    const button = trigger.shadowRoot!.querySelector("#button")!;
    expect([button.getAttribute("aria-haspopup"), button.getAttribute("aria-expanded")]).toEqual(["menu", "false"]);
  });

  it("reports the menu as expanded once it opens", async () => {
    popover.dispatchEvent(toggle("open"));
    controller.hostUpdated();
    await settle();

    expect(controller.open, "open").toBe(true);
    expect(trigger.shadowRoot!.querySelector("#button")!.getAttribute("aria-expanded")).toBe("true");
  });

  it("turns each item into a radio item of one menu, checked by active or selected", async () => {
    controller.hostUpdated();
    await settle();

    expect(
      items.map((item) => ({
        host: item.getAttribute("role"),
        wrapper: item.shadowRoot!.querySelector("#menu-item")!.getAttribute("role"),
        wrapperLabel: item.shadowRoot!.querySelector("#menu-item")!.getAttribute("aria-label"),
        button: labelButton(item).getAttribute("role"),
        checked: labelButton(item).getAttribute("aria-checked"),
        tabindex: labelButton(item).getAttribute("tabindex"),
      })),
    ).toEqual([
      { host: "none", wrapper: "none", wrapperLabel: null, button: "menuitemradio", checked: "false", tabindex: "-1" },
      { host: "none", wrapper: "none", wrapperLabel: null, button: "menuitemradio", checked: "true", tabindex: "-1" },
      { host: "none", wrapper: "none", wrapperLabel: null, button: "menuitemradio", checked: "false", tabindex: "-1" },
    ]);
  });

  it("focuses the checked item when the menu opens", async () => {
    popover.dispatchEvent(toggle("open"));
    await settle();

    expect(document.activeElement).toBe(items[1]);
  });

  it("moves focus to the next item on ArrowDown and cancels the key", async () => {
    items[1]!.focus();
    const event = key("ArrowDown");

    labelButton(items[1]!).dispatchEvent(event);

    expect(document.activeElement, "next item focused").toBe(items[2]);
    expect(event.defaultPrevented, "cancelled").toBe(true);
  });

  it("leaves keys that do not move focus alone", () => {
    items[1]!.focus();
    const event = key("a");

    labelButton(items[1]!).dispatchEvent(event);

    expect(event.defaultPrevented).toBe(false);
  });

  it("leaves arrow keys in other popover content (an input) to that content", () => {
    const input = document.createElement("input");
    popover.append(input);
    input.focus();
    const event = key("ArrowDown");

    input.dispatchEvent(event);

    expect(event.defaultPrevented, "not cancelled").toBe(false);
    expect(document.activeElement, "focus stays").toBe(input);
  });

  it("returns focus to the trigger when Escape in other popover content closes it", () => {
    const input = document.createElement("input");
    popover.append(input);
    popover.dispatchEvent(toggle("open"));
    input.focus();

    input.dispatchEvent(key("Escape"));
    popover.dispatchEvent(toggle("closed"));

    expect(document.activeElement).toBe(trigger);
  });

  it("returns focus to the trigger after a choice closes the menu", () => {
    popover.dispatchEvent(toggle("open"));

    controller.close();

    expect(document.activeElement).toBe(trigger);
  });

  it("returns focus to the trigger when Escape closes the menu", () => {
    popover.dispatchEvent(toggle("open"));
    items[0]!.focus();

    labelButton(items[0]!).dispatchEvent(key("Escape"));
    popover.dispatchEvent(toggle("closed"));

    expect(document.activeElement).toBe(trigger);
  });

  it("leaves focus where it went when the menu closes for an outside click", () => {
    popover.dispatchEvent(toggle("open"));
    outside.focus();

    popover.dispatchEvent(toggle("closed"));

    expect(document.activeElement).toBe(outside);
  });

  it("closes when focus moves to a control outside the menu", () => {
    popover.dispatchEvent(toggle("open"));

    controller.onFocusOut(new FocusEvent("focusout", { relatedTarget: outside }));

    expect(controller.open).toBe(false);
  });

  it("stays open when focus moves to the trigger, whose own toggle closes it", () => {
    popover.dispatchEvent(toggle("open"));

    controller.onFocusOut(new FocusEvent("focusout", { relatedTarget: trigger }));

    expect(controller.open).toBe(true);
  });
});
