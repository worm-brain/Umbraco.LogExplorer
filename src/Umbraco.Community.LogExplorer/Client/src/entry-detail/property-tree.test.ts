import { describe, expect, it } from "vitest";
import { buildPropertyTree, formatScalar, propertyChip, visibleRows, type PropertyNode } from "./property-tree.js";

/** Finds a node by its key anywhere in the tree. */
function find(nodes: ReadonlyArray<PropertyNode>, key: string): PropertyNode | undefined {
  for (const node of nodes) {
    if (node.key === key) return node;
    const child = find(node.children, key);
    if (child) return child;
  }
  return undefined;
}

describe("buildPropertyTree", () => {
  it("sorts top-level properties alphabetically, ignoring case", () => {
    const tree = buildPropertyTree({ StatusCode: 200, elapsed: 4.2, RequestPath: "/healtz" });

    expect(tree.map((node) => node.name)).toEqual(["elapsed", "RequestPath", "StatusCode"]);
  });

  it("types each value", () => {
    const tree = buildPropertyTree({ a: "x", b: 1, c: true, d: null, e: {}, f: [] });

    expect(tree.map((node) => node.kind)).toEqual(["string", "number", "boolean", "null", "object", "array"]);
  });

  it("gives nested object members dotted paths", () => {
    const tree = buildPropertyTree({ Cart: { Total: 12.5, Currency: "GBP" } });

    expect(find(tree, "Cart/Total")?.path).toBe("Cart.Total");
  });

  it("gives array items the array path with [] so any item matches", () => {
    const tree = buildPropertyTree({ Tags: ["alpha", "beta"] });

    expect(find(tree, "Tags/1")).toMatchObject({ name: "[1]", path: "Tags[]", value: "beta" });
  });

  it("addresses members of objects inside arrays across the array", () => {
    const tree = buildPropertyTree({ Lines: [{ Sku: "A1" }] });

    expect(find(tree, "Lines/0/Sku")?.path).toBe("Lines[].Sku");
  });

  it("keeps a dotted top-level key whole, as LogFields matches it", () => {
    const tree = buildPropertyTree({ "host.name": "web-1" });

    expect(tree[0]?.path).toBe("host.name");
  });

  it("gives no path to a nested key the path syntax cannot address, nor to its children", () => {
    const tree = buildPropertyTree({ Outer: { "a.b": { c: 1 } } });

    expect([find(tree, "Outer/a.b")?.path, find(tree, "Outer/a.b/c")?.path]).toEqual([undefined, undefined]);
  });

  it("gives no path to a top-level key starting with @, which LogFields reads as a portable field", () => {
    expect(buildPropertyTree({ "@odd": 1 })[0]?.path).toBeUndefined();
  });

  it("returns no nodes for no attributes", () => {
    expect(buildPropertyTree({})).toEqual([]);
  });
});

describe("visibleRows", () => {
  const tree = buildPropertyTree({ Cart: { Lines: [{ Sku: "A1" }], Total: 3 }, Id: 1 });

  it("shows only top-level rows when nothing is expanded", () => {
    expect(visibleRows(tree, new Set()).map((row) => row.node.key)).toEqual(["Cart", "Id"]);
  });

  it("shows the children of expanded nodes, depth first, with their depth", () => {
    const rows = visibleRows(tree, new Set(["Cart", "Cart/Lines"]));

    expect(rows.map((row) => [row.node.key, row.depth])).toEqual([
      ["Cart", 0],
      ["Cart/Lines", 1],
      ["Cart/Lines/0", 2],
      ["Cart/Total", 1],
      ["Id", 0],
    ]);
  });

  it("hides an expanded subtree when its parent is collapsed", () => {
    const rows = visibleRows(tree, new Set(["Cart/Lines"]));

    expect(rows.map((row) => row.node.key)).toEqual(["Cart", "Id"]);
  });
});

describe("propertyChip", () => {
  it("includes with an equals condition on the path", () => {
    expect(propertyChip("RequestPath", "/healtz", false)).toEqual({
      kind: "condition",
      field: "RequestPath",
      op: "equals",
      value: "/healtz",
    });
  });

  it("excludes by wrapping the condition in not, as -field:value parses", () => {
    expect(propertyChip("StatusCode", 200, true)).toEqual({
      kind: "not",
      child: { kind: "condition", field: "StatusCode", op: "equals", value: 200 },
    });
  });

  it("keeps a number a number so the filter compares numerically", () => {
    expect(propertyChip("Cart.Total", 12.5, false)).toMatchObject({ field: "Cart.Total", value: 12.5 });
  });

  it("filters an array item on the array path", () => {
    expect(propertyChip("Tags[]", "beta", false)).toMatchObject({ field: "Tags[]", value: "beta" });
  });

  it.each([
    ["an object", { a: 1 }],
    ["an array", [1]],
    ["null", null],
  ])("makes no chip for %s", (_name, value) => {
    expect(propertyChip("Field", value, false)).toBeUndefined();
  });

  it("makes no chip without a path", () => {
    expect(propertyChip(undefined, "x", false)).toBeUndefined();
  });
});

describe("formatScalar", () => {
  it("quotes strings so they read differently from numbers", () => {
    expect([formatScalar("200"), formatScalar(200)]).toEqual(['"200"', "200"]);
  });

  it("shows null and booleans as JSON", () => {
    expect([formatScalar(null), formatScalar(false)]).toEqual(["null", "false"]);
  });
});
