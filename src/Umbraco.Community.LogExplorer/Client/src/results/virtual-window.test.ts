import { describe, expect, it } from "vitest";
import { rowWindow } from "./virtual-window.js";

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
