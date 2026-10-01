import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { formatRowTime, formatShowing, levelOf, shortenSource } from "./row-format.js";

describe("formatRowTime", () => {
  beforeEach(() => {
    // A zone with a non-zero offset, so a UTC rendering would be caught.
    vi.stubEnv("TZ", "Asia/Kolkata");
  });

  afterEach(() => {
    vi.unstubAllEnvs();
  });

  it("shows local time with milliseconds", () => {
    expect(formatRowTime("2026-09-02T00:59:09.112Z")).toBe("06:29:09.112");
  });

  it("pads every part", () => {
    expect(formatRowTime("2026-09-01T18:31:02.005Z")).toBe("00:01:02.005");
  });

  it("returns an unparsable value unchanged", () => {
    expect(formatRowTime("not a date")).toBe("not a date");
  });
});

describe("levelOf", () => {
  it.each([
    [1, "trace"],
    [4, "trace"],
    [5, "debug"],
    [9, "info"],
    [13, "warn"],
    [17, "error"],
    [21, "fatal"],
    [24, "fatal"],
  ])("maps severity %i to %s", (severity, level) => {
    expect(levelOf(severity)).toBe(level);
  });

  it.each([0, 25, -1, 9.5])("has no level for %s", (severity) => {
    expect(levelOf(severity)).toBeUndefined();
  });
});

describe("shortenSource", () => {
  it("keeps the last namespace segment", () => {
    expect(shortenSource("Umbraco.Cms.Core.Sync.ServerMessenger")).toBe("ServerMessenger");
  });

  it("keeps a name without dots", () => {
    expect(shortenSource("Startup")).toBe("Startup");
  });

  it("keeps generic arity with the type name", () => {
    expect(shortenSource("Umbraco.Cms.Core.Cache.Refresher`1")).toBe("Refresher`1");
  });

  it("ignores a trailing dot", () => {
    expect(shortenSource("Client.Web.")).toBe("Web");
  });

  it("is empty without a scope", () => {
    expect(shortenSource(null)).toBe("");
  });
});

describe("formatShowing", () => {
  it("shows an exact total", () => {
    expect(formatShowing(60, 238, false, true, "en-GB")).toEqual({ shown: "60", total: "238" });
  });

  it("marks a lower-bound total", () => {
    expect(formatShowing(100, 1234, true, true, "en-GB")).toEqual({ shown: "100", total: "≥ 1,234" });
  });

  it("marks an uncounted total while more pages remain", () => {
    expect(formatShowing(60, null, false, true, "en-GB")).toEqual({ shown: "60", total: "≥ 60" });
  });

  it("shows the loaded count as the total once an uncounted list is complete", () => {
    expect(formatShowing(42, null, false, false, "en-GB")).toEqual({ shown: "42", total: "42" });
  });
});
