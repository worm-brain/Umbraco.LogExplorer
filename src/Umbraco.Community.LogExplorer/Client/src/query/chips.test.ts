import { describe, expect, it } from "vitest";
import { appendChips, removeChipAt, replaceChipAt, sameChip, toViewLevels } from "./chips.js";
import type { FilterNode } from "./filter-node.js";

const pathApi: FilterNode = {
  kind: "condition",
  field: "RequestPath",
  op: "startsWith",
  value: "/api",
  caseInsensitive: true,
};
const status200: FilterNode = { kind: "condition", field: "StatusCode", op: "equals", value: "200" };
const timeout: FilterNode = { kind: "text", text: "timeout", phrase: false };

describe("sameChip", () => {
  it("treats fields left at their defaults as equal to the defaults written out", () => {
    expect(sameChip({ kind: "condition", field: "RequestPath", op: "startsWith", value: "/api" }, pathApi)).toBe(true);
  });

  it("tells apart chips that differ only in value type", () => {
    expect(sameChip(status200, { ...status200, value: 200 })).toBe(false);
  });

  it("ignores key order inside object values", () => {
    const a: FilterNode = { kind: "condition", field: "Cart", op: "equals", value: { total: 1, items: 2 } };
    const b: FilterNode = { kind: "condition", field: "Cart", op: "equals", value: { items: 2, total: 1 } };

    expect(sameChip(a, b)).toBe(true);
  });
});

describe("appendChips", () => {
  it("appends new chips in order", () => {
    expect(appendChips([pathApi], [status200, timeout])).toEqual([pathApi, status200, timeout]);
  });

  it("skips chips already in the list and repeats within the added ones", () => {
    expect(appendChips([pathApi], [{ ...pathApi }, timeout, { kind: "text", text: "timeout" }])).toEqual([
      pathApi,
      timeout,
    ]);
  });

  it("returns the same array when every chip is a duplicate", () => {
    const chips = [pathApi];

    expect(appendChips(chips, [pathApi])).toBe(chips);
  });
});

describe("replaceChipAt", () => {
  it("replaces the chip at the index and keeps the others in place", () => {
    const edited: FilterNode = { ...status200, value: "500" };

    expect(replaceChipAt([pathApi, status200, timeout], 1, edited)).toEqual([pathApi, edited, timeout]);
  });

  it("drops the edited chip when the edit duplicates another chip", () => {
    expect(replaceChipAt([pathApi, status200], 1, { ...pathApi })).toEqual([pathApi]);
  });

  it("leaves the list unchanged for an index out of range", () => {
    expect(replaceChipAt([pathApi], 3, status200)).toEqual([pathApi]);
  });
});

describe("removeChipAt", () => {
  it("removes the chip at the index", () => {
    expect(removeChipAt([pathApi, status200, timeout], 1)).toEqual([pathApi, timeout]);
  });

  it("leaves the list unchanged for an index out of range", () => {
    expect(removeChipAt([pathApi], -1)).toEqual([pathApi]);
  });
});

describe("toViewLevels", () => {
  it("sorts the parsed set into severity order", () => {
    expect(toViewLevels(["fatal", "error", "warn"])).toEqual(["warn", "error", "fatal"]);
  });

  it("drops unknown names", () => {
    expect(toViewLevels(["ERROR", "verbose"])).toEqual(["error"]);
  });

  it("reads all six levels as no level filter", () => {
    expect(toViewLevels(["trace", "debug", "info", "warn", "error", "fatal"])).toBeNull();
  });
});
