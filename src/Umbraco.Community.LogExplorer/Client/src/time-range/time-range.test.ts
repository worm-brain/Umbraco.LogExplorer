import { describe, expect, it } from "vitest";
import { customRangeFromInputs, fromLocalInputValue, toLocalInputValue } from "./time-range.js";

describe("datetime-local conversion", () => {
  it("round-trips an instant through the local input format at minute precision", () => {
    // Holds in any time zone the tests run in: both directions use the same local zone.
    const iso = "2026-09-02T00:40:00.000Z";

    expect(fromLocalInputValue(toLocalInputValue(iso))).toBe(iso);
  });

  it("returns an empty input value for an unparsable instant", () => {
    expect(toLocalInputValue("not a date")).toBe("");
  });

  it("rejects an input value that is not a local date-time", () => {
    expect(fromLocalInputValue("2026-09-02")).toBeUndefined();
  });
});

describe("customRangeFromInputs", () => {
  it("builds an absolute range when the start is before the end", () => {
    const range = customRangeFromInputs("2026-09-02T00:40", "2026-09-02T00:45");

    expect(range).toEqual({
      from: new Date("2026-09-02T00:40").toISOString(),
      to: new Date("2026-09-02T00:45").toISOString(),
    });
  });

  it("returns undefined when the start is not before the end", () => {
    expect(customRangeFromInputs("2026-09-02T00:45", "2026-09-02T00:45")).toBeUndefined();
  });

  it("returns undefined when an input is empty", () => {
    expect(customRangeFromInputs("", "2026-09-02T00:45")).toBeUndefined();
  });
});
