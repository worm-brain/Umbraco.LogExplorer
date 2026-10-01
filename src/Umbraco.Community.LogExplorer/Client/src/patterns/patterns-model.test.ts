import { describe, expect, it } from "vitest";
import { chipsToFilter } from "../query/log-query.js";
import { createDefaultViewState } from "../query/view-state.js";
import en from "../lang/en.js";
import { focusChip, levelMix, muteChip, patternsWindow, searchTabPath, sparkBars } from "./patterns-model.js";

const template = "HTTP {RequestMethod} {RequestPath} responded {StatusCode}";

describe("focusChip", () => {
  it("includes entries with exactly the template", () => {
    expect(focusChip(template)).toEqual({ kind: "condition", field: "@template", op: "equals", value: template });
  });
});

describe("muteChip", () => {
  it("excludes entries with the template", () => {
    expect(muteChip(template)).toEqual({
      kind: "not",
      child: { kind: "condition", field: "@template", op: "equals", value: template },
    });
  });

  it("ANDs with a focus chip on another template instead of ORing", () => {
    const filter = chipsToFilter([focusChip("A {X}"), muteChip("B {Y}")]);

    expect(filter).toMatchObject({ kind: "and" });
  });
});

describe("levelMix", () => {
  it("gives each present level its share, least severe first", () => {
    expect(levelMix({ error: 1, info: 3, warn: 0 })).toEqual([
      { level: "info", count: 3, percent: 75 },
      { level: "error", count: 1, percent: 25 },
    ]);
  });

  it("is empty when no level has entries", () => {
    expect(levelMix({ info: 0 })).toEqual([]);
  });
});

describe("sparkBars", () => {
  it("scales to the busiest bucket and marks empty buckets", () => {
    expect(sparkBars([0, 2, 4])).toEqual([
      { heightPercent: 0, empty: true },
      { heightPercent: 50, empty: false },
      { heightPercent: 100, empty: false },
    ]);
  });

  it("keeps a small bucket visible next to a large one", () => {
    expect(sparkBars([1, 1000])[0]!.heightPercent).toBe(10);
  });

  it("draws an all-empty sparkline as baselines only", () => {
    expect(sparkBars([0, 0]).every((bar) => bar.empty)).toBe(true);
  });
});

describe("patternsWindow", () => {
  const now = Date.parse("2026-09-02T01:00:00Z");

  it("ends a relative range now", () => {
    expect(patternsWindow(createDefaultViewState("1h"), now)).toEqual({
      fromMs: Date.parse("2026-09-02T00:00:00Z"),
      toMs: now,
    });
  });

  it("uses the zoom instead of the range", () => {
    const state = {
      ...createDefaultViewState("1h"),
      zoom: { from: "2026-09-02T00:40:00Z", to: "2026-09-02T00:45:00Z" },
    };

    expect(patternsWindow(state, now)).toEqual({
      fromMs: Date.parse("2026-09-02T00:40:00Z"),
      toMs: Date.parse("2026-09-02T00:45:00Z"),
    });
  });
});

describe("searchTabPath", () => {
  it("replaces the Patterns view segment", () => {
    expect(searchTabPath("/umbraco/section/settings/workspace/log-explorer/view/patterns")).toBe(
      "/umbraco/section/settings/workspace/log-explorer/view/search",
    );
  });

  it("adds the Search view segment to the bare workspace path", () => {
    expect(searchTabPath("/umbraco/section/settings/workspace/log-explorer/")).toBe(
      "/umbraco/section/settings/workspace/log-explorer/view/search",
    );
  });
});

describe("patterns header", () => {
  it("counts the patterns in the current results", () => {
    expect(en.logExplorer.patternsHeader("12")).toBe("Message template · 12 patterns in the current results");
  });

  it("uses the singular for one pattern", () => {
    expect(en.logExplorer.patternsHeaderOne).toBe("Message template · 1 pattern in the current results");
  });

  it("names the volume window", () => {
    expect(en.logExplorer.patternsColumnVolume("00:00", "01:00")).toBe("Volume, 00:00 to 01:00");
  });
});
