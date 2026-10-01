import { describe, expect, it } from "vitest";
import { nextRowIndex, rowWindow, scrollTopToReveal } from "./virtual-window.js";

describe("rowWindow", () => {
  it("renders the visible rows plus the overscan below at the top of the list", () => {
    expect(rowWindow(0, 400, 40, 1000, 5)).toEqual({ first: 0, end: 15 });
  });

  it("adds the overscan on both sides in the middle of the list", () => {
    expect(rowWindow(4000, 400, 40, 1000, 5)).toEqual({ first: 95, end: 115 });
  });

  it("includes a partly visible row at either edge", () => {
    expect(rowWindow(20, 400, 40, 1000, 0)).toEqual({ first: 0, end: 11 });
  });

  it("stops at the last row", () => {
    expect(rowWindow(39_600, 400, 40, 1000, 5)).toEqual({ first: 985, end: 1000 });
  });

  it("is empty without rows", () => {
    expect(rowWindow(0, 400, 40, 0, 5)).toEqual({ first: 0, end: 0 });
  });

  it("is empty for a row height that is not positive", () => {
    expect(rowWindow(0, 400, 0, 1000, 5)).toEqual({ first: 0, end: 0 });
  });

  it("treats overscroll above the top as the top", () => {
    expect(rowWindow(-200, 400, 40, 1000, 0)).toEqual({ first: 0, end: 10 });
  });
});

describe("nextRowIndex", () => {
  it("moves down one row", () => {
    expect(nextRowIndex(4, 1, 100, 0)).toBe(5);
  });

  it("moves up one row", () => {
    expect(nextRowIndex(4, -1, 100, 0)).toBe(3);
  });

  it("stops at the last row", () => {
    expect(nextRowIndex(99, 1, 100, 0)).toBeUndefined();
  });

  it("stops at the first row", () => {
    expect(nextRowIndex(0, -1, 100, 0)).toBeUndefined();
  });

  it("focuses the start row without stepping when no row has focus", () => {
    expect(nextRowIndex(undefined, -1, 100, 40)).toBe(40);
  });

  it("clamps a start row past the end to the last row", () => {
    expect(nextRowIndex(undefined, 1, 10, 25)).toBe(9);
  });

  it("does nothing without rows", () => {
    expect(nextRowIndex(undefined, 1, 0, 0)).toBeUndefined();
  });
});

describe("scrollTopToReveal", () => {
  // Rows 40 px tall in a 400 px viewport.
  it("leaves the offset alone when the row is fully visible", () => {
    expect(scrollTopToReveal(5, 40, 100, 400)).toBe(100);
  });

  it("aligns a row above the viewport to the top", () => {
    expect(scrollTopToReveal(2, 40, 400, 400)).toBe(80);
  });

  it("aligns a row below the viewport to the bottom", () => {
    expect(scrollTopToReveal(30, 40, 0, 400)).toBe(840);
  });

  it("scrolls a partly hidden row at the bottom just enough", () => {
    expect(scrollTopToReveal(10, 40, 20, 400)).toBe(40);
  });
});
