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

  it("formats the source menu note from the language and type", () => {
    expect(en.logExplorer.sourceNote("KQL", "ApplicationInsights")).toBe("KQL · ApplicationInsights");
  });
});
