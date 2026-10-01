import { describe, expect, it } from "vitest";
import type { FilterNode } from "../query/filter-node.js";
import { describeChip, shortFieldName } from "./chip-format.js";
import { enTerm } from "./term.test-helper.js";

const condition = (op: Extract<FilterNode, { kind: "condition" }>["op"], value?: unknown, field = "RequestPath") =>
  ({ kind: "condition", field, op, value }) as FilterNode;

describe("shortFieldName", () => {
  it.each([
    ["RequestPath", "Path"],
    ["SourceContext", "Source"],
    ["@scope", "Source"],
    ["StatusCode", "Status"],
    ["MachineName", "Machine"],
    ["@exception.type", "Exception"],
    ["RequestId", "Request"],
    ["@template", "Template"],
    ["@body", "Text"],
  ])("shortens %s to %s", (field, short) => {
    expect(shortFieldName(field, enTerm)).toBe(short);
  });

  it("keeps any other field name as it is", () => {
    expect(shortFieldName("Cart.Total", enTerm)).toBe("Cart.Total");
  });
});

describe("describeChip", () => {
  it.each([
    ["equals", "/healtz", "Path: /healtz", 'RequestPath equals "/healtz"'],
    ["startsWith", "/api", "Path: /api*", 'RequestPath starts with "/api"'],
    ["endsWith", ".php", "Path: *.php", 'RequestPath ends with ".php"'],
    ["contains", "login", "Path: *login*", 'RequestPath contains "login"'],
    ["in", ["/a", "/b"], "Path: /a, /b", 'RequestPath is one of "/a", "/b"'],
    ["matches", "^/a", "Path: ^/a", 'RequestPath matches "^/a"'],
  ] as const)("labels an include %s chip", (op, value, label, description) => {
    expect(describeChip(condition(op, value), enTerm)).toEqual({ kind: "include", label, description });
  });

  it.each([
    ["greaterThan", "Duration > 1000", "Duration greater than 1000"],
    ["greaterOrEqual", "Duration >= 1000", "Duration greater than or equal to 1000"],
    ["lessThan", "Duration < 1000", "Duration less than 1000"],
    ["lessOrEqual", "Duration <= 1000", "Duration less than or equal to 1000"],
  ] as const)("labels a %s comparison with its symbol", (op, label, description) => {
    expect(describeChip(condition(op, 1000, "Duration"), enTerm)).toEqual({ kind: "include", label, description });
  });

  it("labels an exists chip", () => {
    expect(describeChip(condition("exists", null, "ContentId"), enTerm)).toEqual({
      kind: "include",
      label: "has ContentId",
      description: "ContentId exists",
    });
  });

  it("labels a not-exists chip as an exclude", () => {
    expect(describeChip(condition("notExists", null, "ContentId"), enTerm)).toEqual({
      kind: "exclude",
      label: "not has ContentId",
      description: "ContentId does not exist",
    });
  });

  it("labels a not-equals chip as an exclude", () => {
    expect(describeChip(condition("notEquals", 200, "StatusCode"), enTerm)).toEqual({
      kind: "exclude",
      label: "not Status: 200",
      description: "StatusCode does not equal 200",
    });
  });

  it("labels a not chip as an exclude of its condition", () => {
    const chip: FilterNode = { kind: "not", child: condition("startsWith", "Umbraco.Cms.Core.Sync", "SourceContext") };

    expect(describeChip(chip, enTerm)).toEqual({
      kind: "exclude",
      label: "not Source: Umbraco.Cms.Core.Sync*",
      description: 'not SourceContext starts with "Umbraco.Cms.Core.Sync"',
    });
  });

  it("labels a text chip with its words in quotes", () => {
    expect(describeChip({ kind: "text", text: "timeout" }, enTerm)).toEqual({
      kind: "text",
      label: '"timeout"',
      description: 'Text contains "timeout"',
    });
  });

  it("describes a phrase text chip as a phrase", () => {
    expect(describeChip({ kind: "text", text: "connection refused", phrase: true }, enTerm).description).toBe(
      'Text contains the phrase "connection refused"',
    );
  });

  it("labels an or group from a hand-edited URL readably", () => {
    const chip: FilterNode = { kind: "or", children: [condition("equals", "/a"), condition("equals", "/b")] };

    expect(describeChip(chip, enTerm).label).toBe("(Path: /a or Path: /b)");
  });
});
