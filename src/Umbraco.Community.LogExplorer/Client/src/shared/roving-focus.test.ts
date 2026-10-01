import { describe, expect, it } from "vitest";
import { rovingIndex, type RovingOptions } from "./roving-focus.js";

const MENU: RovingOptions = { orientation: "vertical", wrap: true };
const BARS: RovingOptions = { orientation: "horizontal", wrap: false };

describe("rovingIndex", () => {
  it.each([
    ["down moves to the next item", 1, "ArrowDown", 2],
    ["up moves to the previous item", 1, "ArrowUp", 0],
    ["down from the last item wraps to the first", 4, "ArrowDown", 0],
    ["up from the first item wraps to the last", 0, "ArrowUp", 4],
    ["Home goes to the first item", 3, "Home", 0],
    ["End goes to the last item", 1, "End", 4],
  ])("in a wrapping vertical menu of five, %s", (_name, current, key, expected) => {
    expect(rovingIndex(current, key, 5, MENU)).toBe(expected);
  });

  it.each([
    ["right moves to the next bar", 2, "ArrowRight", 3],
    ["left moves to the previous bar", 2, "ArrowLeft", 1],
    ["right on the last bar stays there", 59, "ArrowRight", 59],
    ["left on the first bar stays there", 0, "ArrowLeft", 0],
  ])("in a non-wrapping horizontal strip of sixty, %s", (_name, current, key, expected) => {
    expect(rovingIndex(current, key, 60, BARS)).toBe(expected);
  });

  it.each([
    ["the other orientation's arrows", "ArrowLeft", MENU],
    ["the other orientation's arrows (horizontal)", "ArrowDown", BARS],
    ["a letter", "a", MENU],
    ["Tab", "Tab", BARS],
  ])("leaves %s alone", (_name, key, options) => {
    expect(rovingIndex(1, key, 5, options)).toBeUndefined();
  });

  it("does nothing in an empty group", () => {
    expect(rovingIndex(0, "Home", 0, MENU)).toBeUndefined();
  });

  it("clamps a stale index before moving, so a shrunken group still works", () => {
    expect(rovingIndex(40, "ArrowLeft", 10, BARS)).toBe(8);
  });

  it.each([
    ["down starts at the first item", "ArrowDown", 0],
    ["up starts at the last item", "ArrowUp", 4],
  ])("with no item focused yet, %s", (_name, key, expected) => {
    expect(rovingIndex(-1, key, 5, MENU)).toBe(expected);
  });
});
