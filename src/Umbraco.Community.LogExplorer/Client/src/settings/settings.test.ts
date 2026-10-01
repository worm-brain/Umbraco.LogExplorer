import { describe, expect, it } from "vitest";
import type { SettingsResponseModel } from "../api/index.js";
import { hideCoreLogViewerIfConfigured, loadSettings, type ExtensionExcluder } from "./settings.js";

const defaults: SettingsResponseModel = {
  hideCoreLogViewer: true,
  defaultSource: "files",
  defaultTimeRange: "1h",
  pinnedFacets: ["SourceContext"],
};

/** A registry fake that records which aliases were excluded. */
function fakeRegistry(): ExtensionExcluder & { excluded: Array<string> } {
  const excluded: Array<string> = [];
  return { excluded, exclude: (alias) => void excluded.push(alias) };
}

describe("loadSettings", () => {
  it("returns the settings when the request succeeds", async () => {
    expect(await loadSettings(async () => ({ data: defaults }))).toEqual(defaults);
  });

  it("returns undefined when the server responds with an error", async () => {
    expect(await loadSettings(async () => ({ error: { title: "Forbidden" } }))).toBeUndefined();
  });

  it("returns undefined instead of throwing when the request rejects", async () => {
    const settings = await loadSettings(async () => {
      throw new TypeError("Failed to fetch");
    });

    expect(settings).toBeUndefined();
  });
});

describe("hideCoreLogViewerIfConfigured", () => {
  it("excludes the core Log Viewer menu item when HideCoreLogViewer is true", () => {
    const registry = fakeRegistry();

    hideCoreLogViewerIfConfigured(defaults, registry);

    expect(registry.excluded).toEqual(["Umb.MenuItem.LogViewer"]);
  });

  it("keeps the core item when HideCoreLogViewer is false", () => {
    const registry = fakeRegistry();

    hideCoreLogViewerIfConfigured({ ...defaults, hideCoreLogViewer: false }, registry);

    expect(registry.excluded).toEqual([]);
  });

  it("keeps the core item when the settings could not be loaded", () => {
    const registry = fakeRegistry();

    hideCoreLogViewerIfConfigured(undefined, registry);

    expect(registry.excluded).toEqual([]);
  });
});
