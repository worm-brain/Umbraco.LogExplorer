import { describe, expect, it } from "vitest";
import {
  KEY_STEP,
  MIN_PANEL_WIDTH,
  PANEL_WIDTH_KEY,
  SHIFT_FACTOR,
  clampPanelWidth,
  keyPanelWidth,
  maxPanelWidth,
  readPanelWidth,
  writePanelWidth,
} from "./panel-width.js";

/** An in-memory storage, as localStorage behaves when it works. */
function memoryStorage() {
  const items = new Map<string, string>();
  return {
    items,
    getItem: (key: string) => items.get(key) ?? null,
    setItem: (key: string, value: string) => void items.set(key, value),
    removeItem: (key: string) => void items.delete(key),
  };
}

/** Storage that throws on every call, as it can in a private window or with site data blocked. */
const failingStorage = {
  getItem: (): string | null => {
    throw new DOMException("denied", "SecurityError");
  },
  setItem: () => {
    throw new DOMException("denied", "SecurityError");
  },
  removeItem: () => {
    throw new DOMException("denied", "SecurityError");
  },
};

describe("panel width", () => {
  it("leaves the results 40% of the body", () => {
    expect(maxPanelWidth(1000)).toBe(600);
  });

  it("never lets the maximum fall below the minimum on a narrow body", () => {
    expect(maxPanelWidth(300)).toBe(MIN_PANEL_WIDTH);
  });

  it("keeps a dragged width within the minimum and maximum", () => {
    expect([clampPanelWidth(50, 1000), clampPanelWidth(450.4, 1000), clampPanelWidth(900, 1000)]).toEqual([
      MIN_PANEL_WIDTH,
      450,
      600,
    ]);
  });

  it("moves by one step per arrow key and four with Shift", () => {
    expect([keyPanelWidth("ArrowRight", false, 300, 1000), keyPanelWidth("ArrowLeft", true, 300, 1000)]).toEqual([
      300 + KEY_STEP,
      300 - KEY_STEP * SHIFT_FACTOR,
    ]);
  });

  it("jumps to the minimum and maximum with Home and End", () => {
    expect([keyPanelWidth("Home", false, 300, 1000), keyPanelWidth("End", false, 300, 1000)]).toEqual([
      MIN_PANEL_WIDTH,
      600,
    ]);
  });

  it("ignores keys that do not resize", () => {
    expect(keyPanelWidth("Enter", false, 300, 1000)).toBeUndefined();
  });

  it("round-trips a stored width", () => {
    const storage = memoryStorage();
    writePanelWidth(321.6, storage);

    expect([storage.items.get(PANEL_WIDTH_KEY), readPanelWidth(storage)]).toEqual(["322", 322]);
  });

  it("forgets the width when asked to, back to the default", () => {
    const storage = memoryStorage();
    writePanelWidth(300, storage);
    writePanelWidth(undefined, storage);

    expect(readPanelWidth(storage)).toBeUndefined();
  });

  it("treats a stored value that is not a positive number as no width", () => {
    const storage = memoryStorage();
    storage.setItem(PANEL_WIDTH_KEY, "wide");

    expect(readPanelWidth(storage)).toBeUndefined();
  });

  it("reads no width and writes nothing when storage throws", () => {
    expect([readPanelWidth(failingStorage), writePanelWidth(300, failingStorage)]).toEqual([undefined, undefined]);
  });
});
