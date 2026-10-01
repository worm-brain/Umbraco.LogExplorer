import { describe, expect, it } from "vitest";
import type { FilterNode } from "../query/filter-node.js";
import { chipFromDraft, draftFromChip, isDraftValid, operatorsFor, type ConditionDraft } from "./chip-edit.js";

const pathApi: FilterNode = { kind: "condition", field: "RequestPath", op: "startsWith", value: "/api" };

const draft = (partial: Partial<ConditionDraft> = {}): ConditionDraft => ({
  kind: "condition",
  field: "RequestPath",
  op: "equals",
  value: "/api",
  exclude: false,
  numeric: false,
  caseInsensitive: true,
  ...partial,
});

describe("draftFromChip", () => {
  it("reads an include chip", () => {
    expect(draftFromChip(pathApi)).toEqual(draft({ op: "startsWith" }));
  });

  it("reads a not chip as its condition with exclude on", () => {
    expect(draftFromChip({ kind: "not", child: pathApi })).toEqual(draft({ op: "startsWith", exclude: true }));
  });

  it("reads notEquals as equals with exclude on, remembering a numeric value", () => {
    expect(draftFromChip({ kind: "condition", field: "StatusCode", op: "notEquals", value: 200 })).toEqual(
      draft({ field: "StatusCode", value: "200", exclude: true, numeric: true }),
    );
  });

  it("reads notExists as exists with exclude on", () => {
    expect(draftFromChip({ kind: "condition", field: "ContentId", op: "notExists" })).toEqual(
      draft({ field: "ContentId", op: "exists", value: "", exclude: true }),
    );
  });

  it("reads a text chip", () => {
    expect(draftFromChip({ kind: "text", text: "timeout" })).toEqual({ kind: "text", text: "timeout", phrase: false });
  });

  it.each([
    ["an or group", { kind: "or", children: [pathApi] }],
    ["an in chip", { kind: "condition", field: "RequestPath", op: "in", value: ["/a"] }],
    ["a matches chip", { kind: "condition", field: "RequestPath", op: "matches", value: "^/a" }],
    ["an object value", { kind: "condition", field: "Cart", op: "equals", value: { total: 1 } }],
  ] as const)("cannot edit %s", (_, chip) => {
    expect(draftFromChip(chip as FilterNode)).toBeUndefined();
  });
});

describe("chipFromDraft", () => {
  it("writes an include condition", () => {
    expect(chipFromDraft(draft({ op: "contains", value: " login " }))).toEqual({
      kind: "condition",
      field: "RequestPath",
      op: "contains",
      value: "login",
      caseInsensitive: true,
    });
  });

  it("writes an exclude as a not around the condition", () => {
    expect(chipFromDraft(draft({ exclude: true }))).toEqual({
      kind: "not",
      child: { kind: "condition", field: "RequestPath", op: "equals", value: "/api", caseInsensitive: true },
    });
  });

  it("writes a numeric comparison value as a number", () => {
    expect(chipFromDraft(draft({ field: "Duration", op: "greaterThan", value: "1000" }))).toMatchObject({
      value: 1000,
    });
  });

  it("keeps a date comparison value a string", () => {
    expect(chipFromDraft(draft({ field: "When", op: "greaterThan", value: "2026-01-01" }))).toMatchObject({
      value: "2026-01-01",
    });
  });

  it("keeps a typed status code a string unless it was a number", () => {
    expect(chipFromDraft(draft({ field: "StatusCode", value: "500" }))).toMatchObject({ value: "500" });
  });

  it("keeps a number a number when it was one", () => {
    expect(chipFromDraft(draft({ field: "StatusCode", value: "500", numeric: true }))).toMatchObject({ value: 500 });
  });

  it("writes an excluded exists as notExists, the parser's -has: shape", () => {
    expect(chipFromDraft(draft({ op: "exists", exclude: true }))).toEqual({
      kind: "condition",
      field: "RequestPath",
      op: "notExists",
      value: null,
      caseInsensitive: true,
    });
  });

  it("writes a text chip", () => {
    expect(chipFromDraft({ kind: "text", text: "connection refused", phrase: true })).toEqual({
      kind: "text",
      text: "connection refused",
      phrase: true,
    });
  });

  it("round-trips a chip the parser made", () => {
    const chip: FilterNode = { kind: "not", child: { ...pathApi, caseInsensitive: true } };

    expect(chipFromDraft(draftFromChip(chip)!)).toEqual(chip);
  });
});

describe("operatorsFor", () => {
  it("offers the string matches and exists for a text value", () => {
    expect(operatorsFor(draft())).toEqual(["equals", "startsWith", "contains", "endsWith", "exists"]);
  });

  it("adds the comparisons for a numeric value", () => {
    expect(operatorsFor(draft({ value: "1000" }))).toContain("greaterOrEqual");
  });

  it("keeps the comparisons for a comparison on a date", () => {
    expect(operatorsFor(draft({ op: "lessThan", value: "2026-01-01" }))).toContain("lessThan");
  });
});

describe("isDraftValid", () => {
  it("accepts a value", () => {
    expect(isDraftValid(draft())).toBe(true);
  });

  it("rejects a blank value", () => {
    expect(isDraftValid(draft({ value: "  " }))).toBe(false);
  });

  it("accepts exists without a value", () => {
    expect(isDraftValid(draft({ op: "exists", value: "" }))).toBe(true);
  });

  it("rejects blank text", () => {
    expect(isDraftValid({ kind: "text", text: " ", phrase: false })).toBe(false);
  });
});
