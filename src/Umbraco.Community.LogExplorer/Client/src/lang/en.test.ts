/// <reference types="vite/client" />
import { describe, expect, it } from "vitest";
import en from "./en.js";

// Every source file except tests and the generated client, as raw text, so the test can find
// the keys the code references without a hand-kept list.
const sources = import.meta.glob(["../**/*.ts", "!../**/*.test.ts", "!../api/**"], {
  query: "?raw",
  import: "default",
  eager: true,
}) as Record<string, string>;

/** Collects every `logExplorer_{key}` reference, whether `#`-prefixed in a manifest or in `term()`. */
function referencedKeys(): Array<string> {
  const keys = new Set<string>();
  for (const text of Object.values(sources)) {
    for (const match of text.matchAll(/logExplorer_(\w+)/g)) keys.add(match[1]!);
  }
  return [...keys].sort();
}

describe("en dictionary", () => {
  it("finds the source files to scan", () => {
    // Guards the glob: an empty scan would make the next test pass vacuously.
    expect(referencedKeys()).toContain("title");
  });

  it("defines every key the code references", () => {
    const missing = referencedKeys().filter((key) => !(key in en.logExplorer));

    expect(missing).toEqual([]);
  });

  it("formats entries that take values", () => {
    expect(en.logExplorer.sourcesError("Forbidden")).toBe("Could not load log sources: Forbidden");
  });

  it.each([
    ["one entry", 1, "1", "17:07, 1 entry, select to zoom in"],
    ["several entries", 1234, "1,234", "17:07, 1,234 entries, select to zoom in"],
    ["no entries", 0, "0", "17:07, 0 entries, select to zoom in"],
  ])("words a histogram bar with %s", (_name, count, formatted, expected) => {
    expect(en.logExplorer.histogramBarLabel("17:07", count, formatted)).toBe(expected);
  });

  it("words the histogram summary and level toggles in the singular for one entry", () => {
    expect([
      en.logExplorer.histogramSummary(1, "1", "00:00", "01:00"),
      en.logExplorer.histogramLevelLabel("ERROR", 1, "1"),
    ]).toEqual(["1 entry · 00:00 to 01:00", "ERROR, 1 entry"]);
  });

  it("words the drawer's counts in the singular and plural", () => {
    expect([
      en.logExplorer.detailArraySummary(1),
      en.logExplorer.detailObjectSummary(3),
      en.logExplorer.detailFrameworkFramesHidden(1),
      en.logExplorer.detailFrameworkFramesHidden(2),
    ]).toEqual(["1 item", "3 properties", "… 1 framework frame hidden", "… 2 framework frames hidden"]);
  });

  it("formats the source menu note from the language and type", () => {
    expect(en.logExplorer.sourceNote("KQL", "ApplicationInsights")).toBe("KQL · ApplicationInsights");
  });
});
