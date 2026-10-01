import { describe, expect, it } from "vitest";
import { searchKeyAction } from "./search-keys.js";

const key = (name: string, isComposing = false) => ({ key: name, isComposing });

describe("searchKeyAction", () => {
  it("parses on Enter with text", () => {
    expect(searchKeyAction(key("Enter"), "level:error", 0)).toBe("submit");
  });

  it("ignores Enter in a blank input", () => {
    expect(searchKeyAction(key("Enter"), "  ", 2)).toBeUndefined();
  });

  it("ignores Enter that confirms an IME composition", () => {
    expect(searchKeyAction(key("Enter", true), "ログ", 0)).toBeUndefined();
  });

  it("removes the last chip on Backspace in an empty input", () => {
    expect(searchKeyAction(key("Backspace"), "", 2)).toBe("removeLastChip");
  });

  it("lets Backspace delete text when the input has some", () => {
    expect(searchKeyAction(key("Backspace"), "a", 2)).toBeUndefined();
  });

  it("does nothing on Backspace with no chips", () => {
    expect(searchKeyAction(key("Backspace"), "", 0)).toBeUndefined();
  });

  it("clears the input on Escape", () => {
    expect(searchKeyAction(key("Escape"), "timeout", 0)).toBe("clear");
  });

  it("lets Escape through when the input is empty, for the drawer and menus", () => {
    expect(searchKeyAction(key("Escape"), "", 3)).toBeUndefined();
  });
});
