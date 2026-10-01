import { describe, expect, it } from "vitest";
import {
  PANEL_PREFERENCE_KEY,
  isPanelCollapsed,
  readPanelPreference,
  writePanelPreference,
  type PreferenceStorage,
} from "./panel-preference.js";

/** An in-memory storage, as localStorage behaves when it works. */
function memoryStorage(): PreferenceStorage & { items: Map<string, string> } {
  const items = new Map<string, string>();
  return {
    items,
    getItem: (key) => items.get(key) ?? null,
    setItem: (key, value) => void items.set(key, value),
  };
}

/** Storage that throws on every call, as it can in a private window or with site data blocked. */
const failingStorage: PreferenceStorage = {
  getItem: () => {
    throw new DOMException("denied", "SecurityError");
  },
  setItem: () => {
    throw new DOMException("denied", "SecurityError");
  },
};

describe("panel preference", () => {
  it("reads back what was written", () => {
    const storage = memoryStorage();

    writePanelPreference("collapsed", storage);

    expect(readPanelPreference(storage)).toBe("collapsed");
  });

  it("treats an unrecognised stored value as no choice", () => {
    const storage = memoryStorage();
    storage.items.set(PANEL_PREFERENCE_KEY, "sideways");

    expect(readPanelPreference(storage)).toBeUndefined();
  });

  it("reads no choice when storage throws", () => {
    expect(readPanelPreference(failingStorage)).toBeUndefined();
  });

  it("does not throw when storage refuses a write", () => {
    expect(() => writePanelPreference("open", failingStorage)).not.toThrow();
  });
});

describe("isPanelCollapsed", () => {
  it("starts collapsed on a medium workspace without a choice", () => {
    expect(isPanelCollapsed(undefined, 1000)).toBe(true);
  });

  it("starts open on a wide workspace without a choice", () => {
    expect(isPanelCollapsed(undefined, 1400)).toBe(false);
  });

  it("stays open before the workspace is measured", () => {
    expect(isPanelCollapsed(undefined, undefined)).toBe(false);
  });

  it("follows the user's choice over the width", () => {
    expect(isPanelCollapsed("open", 950)).toBe(false);
  });
});
