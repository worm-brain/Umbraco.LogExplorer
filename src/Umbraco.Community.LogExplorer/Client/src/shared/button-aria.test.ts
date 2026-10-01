import { beforeEach, describe, expect, it } from "vitest";
import { syncButtonAria } from "./button-aria.js";

// A stand-in with uui-button's shadow structure (the focusable `#button`), under its tag name so
// the helper finds it; UUI itself needs ElementInternals, which happy-dom lacks.
class FakeUuiButton extends HTMLElement {
  readonly updateComplete = Promise.resolve(true);
  constructor() {
    super();
    this.attachShadow({ mode: "open" }).innerHTML = `<button id="button"></button>`;
  }
}
customElements.define("uui-button", FakeUuiButton);

let root: HTMLElement;

function button(data: Record<string, string>): FakeUuiButton {
  const element = new FakeUuiButton();
  Object.assign(element.dataset, data);
  root.append(element);
  return element;
}

function inner(element: FakeUuiButton): HTMLElement {
  return element.shadowRoot!.querySelector("#button")!;
}

beforeEach(() => {
  root = document.createElement("div");
});

describe("syncButtonAria", () => {
  it("copies pressed, expanded and haspopup onto the inner button", async () => {
    const toggle = button({ pressed: "true", expanded: "false", haspopup: "menu" });

    await syncButtonAria(root);

    expect([
      inner(toggle).getAttribute("aria-pressed"),
      inner(toggle).getAttribute("aria-expanded"),
      inner(toggle).getAttribute("aria-haspopup"),
    ]).toEqual(["true", "false", "menu"]);
  });

  it("follows a change of state on the next sync", async () => {
    const toggle = button({ pressed: "true" });
    await syncButtonAria(root);

    toggle.dataset.pressed = "false";
    await syncButtonAria(root);

    expect(inner(toggle).getAttribute("aria-pressed")).toBe("false");
  });

  it("leaves buttons without forwarded state alone", async () => {
    const plain = button({ level: "info" });

    await syncButtonAria(root);

    expect(inner(plain).hasAttribute("aria-pressed")).toBe(false);
  });
});
