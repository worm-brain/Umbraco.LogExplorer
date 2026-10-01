import { describe, expect, it } from "vitest";
import { isTypingTarget, viewKeyAction, type ViewKeyEvent, type ViewKeyState } from "./view-keys.js";

const press = (key: string, extra: Partial<ViewKeyEvent> = {}): ViewKeyEvent => ({
  key,
  ctrlKey: false,
  metaKey: false,
  altKey: false,
  isComposing: false,
  defaultPrevented: false,
  ...extra,
});

const idle: ViewKeyState = {
  typing: false,
  inSearchBox: false,
  menuOpen: false,
  drawerOpen: false,
  searchHasText: false,
};

describe("viewKeyAction", () => {
  it("focuses the search box on /", () => {
    expect(viewKeyAction(press("/"), idle)).toBe("focusSearch");
  });

  it("moves down on j and up on k", () => {
    expect([viewKeyAction(press("j"), idle), viewKeyAction(press("k"), idle)]).toEqual(["nextRow", "previousRow"]);
  });

  it("lets /, j and k be typed into a text field", () => {
    const typing = { ...idle, typing: true };
    expect([
      viewKeyAction(press("/"), typing),
      viewKeyAction(press("j"), typing),
      viewKeyAction(press("k"), typing),
    ]).toEqual([undefined, undefined, undefined]);
  });

  it.each([
    ["Ctrl", { ctrlKey: true }],
    ["Meta", { metaKey: true }],
    ["Alt", { altKey: true }],
  ])("leaves j alone with %s held, for the browser's and backoffice's own shortcuts", (_, modifier) => {
    expect(viewKeyAction(press("j", modifier), idle)).toBeUndefined();
  });

  it("ignores keys during an IME composition", () => {
    expect(viewKeyAction(press("Escape", { isComposing: true }), { ...idle, drawerOpen: true })).toBeUndefined();
  });

  it("ignores other keys", () => {
    expect(viewKeyAction(press("x"), idle)).toBeUndefined();
  });

  describe("Escape", () => {
    it("does nothing when a nearer handler already used it", () => {
      const state = { ...idle, drawerOpen: true, searchHasText: true };
      expect(viewKeyAction(press("Escape", { defaultPrevented: true }), state)).toBeUndefined();
    });

    it("leaves an open menu to the browser before closing the drawer", () => {
      expect(viewKeyAction(press("Escape"), { ...idle, menuOpen: true, drawerOpen: true })).toBeUndefined();
    });

    it("closes the drawer before clearing the search text", () => {
      expect(viewKeyAction(press("Escape"), { ...idle, drawerOpen: true, searchHasText: true })).toBe("closeDrawer");
    });

    it("clears the search text when nothing else is open", () => {
      expect(viewKeyAction(press("Escape"), { ...idle, searchHasText: true })).toBe("clearSearch");
    });

    it("works from a text field", () => {
      expect(viewKeyAction(press("Escape"), { ...idle, typing: true, drawerOpen: true })).toBe("closeDrawer");
    });

    it("moves focus from an empty search box to the results", () => {
      expect(viewKeyAction(press("Escape"), { ...idle, typing: true, inSearchBox: true })).toBe("focusResults");
    });

    it("closes the drawer rather than leaving the search box", () => {
      expect(viewKeyAction(press("Escape"), { ...idle, typing: true, inSearchBox: true, drawerOpen: true })).toBe(
        "closeDrawer",
      );
    });

    it("does nothing when there is nothing to close or clear", () => {
      expect(viewKeyAction(press("Escape"), idle)).toBeUndefined();
    });
  });
});

describe("isTypingTarget", () => {
  const input = (type: string) => Object.assign(document.createElement("input"), { type });

  it.each([
    ["a text input", input("text")],
    ["a search input", input("search")],
    ["a textarea", document.createElement("textarea")],
    ["a select", document.createElement("select")],
  ])("is true for %s", (_, element) => {
    expect(isTypingTarget(element)).toBe(true);
  });

  it("is true for contenteditable content", () => {
    const element = document.createElement("div");
    element.contentEditable = "true";
    document.body.append(element);
    expect(isTypingTarget(element)).toBe(true);
    element.remove();
  });

  it.each([
    ["a checkbox", input("checkbox")],
    ["a button", document.createElement("button")],
    ["no element", null],
  ])("is false for %s", (_, element) => {
    expect(isTypingTarget(element)).toBe(false);
  });
});
