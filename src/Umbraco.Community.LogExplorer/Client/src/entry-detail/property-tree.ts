import type { FilterNode } from "../query/filter-node.js";

/** The JSON type of a property value, which decides how the drawer shows it. */
export type PropertyKind = "string" | "number" | "boolean" | "null" | "object" | "array";

/** One property in the drawer's typed tree (UI brief §4.11 section 5). */
export interface PropertyNode {
  /** The label: the object key, or `[0]`, `[1]`... for array items. */
  name: string;
  /** Unique within the tree (display names joined with `/`); keys the expanded state. */
  key: string;
  /**
   * The field path a filter chip uses, in the syntax Core's `LogFields` resolves: dots into
   * objects (`Cart.Total`) and `[]` across arrays (`Tags[]`, `Lines[].Sku`). `undefined` when no
   * path can address the value; see {@link buildPropertyTree}.
   */
  path: string | undefined;
  kind: PropertyKind;
  /** The raw JSON value. */
  value: unknown;
  /** Object members (sorted by name) or array items (in order); empty for scalars. */
  children: Array<PropertyNode>;
}

/**
 * Turns a record's attributes into the typed tree the drawer's Properties table shows, sorted
 * alphabetically (case-insensitive) at every object level; array items keep their order.
 *
 * Filter paths follow `LogFields`:
 * - a top-level key is the path as it is, even with dots (`host.name`), because `LogFields`
 *   matches a whole key before splitting it;
 * - a nested key appends `.key`, unless it contains a dot or ends in `[]`, which the path syntax
 *   cannot address, so it and everything under it gets no path;
 * - an array item's path is the array's path plus `[]`, which matches when any item matches.
 *   So `+` on `Tags[1]` = "beta" filters on `Tags[]` equals "beta": entries with "beta" anywhere
 *   in `Tags`, which is what a user filtering on a tag means. An index-specific path does not
 *   exist in `LogFields`.
 * - top-level keys starting with `@` get no path: `LogFields` would read them as portable fields.
 *
 * @param attributes - `LogRecord.attributes` (or `resource`).
 * @returns The top-level nodes.
 */
export function buildPropertyTree(attributes: Readonly<Record<string, unknown>>): Array<PropertyNode> {
  return sortedEntries(attributes).map(([name, value]) =>
    toNode(name, name, name.startsWith("@") ? undefined : name, value),
  );
}

/**
 * The chip that the `+` or `-` button of a property (or a value in the rendered message) adds:
 * `field` equals the value, wrapped in `not` to exclude, the same shape `POST /parse` returns for
 * `field:value` and `-field:value`, so the chip reads and de-duplicates the same way.
 *
 * Only strings, numbers and booleans make chips. Objects and arrays would compare as raw JSON
 * text, which is fragile; their items have their own buttons. `null` counts as absent in
 * `LogFields`, so an equals filter on it could never match.
 *
 * @param path - {@link PropertyNode.path}; `undefined` means the value cannot be filtered.
 * @param value - The property value; numbers stay numbers so the comparison is numeric.
 * @param exclude - `true` for the exclude chip.
 * @returns The chip, or `undefined` when the value cannot be filtered on.
 */
export function propertyChip(path: string | undefined, value: unknown, exclude: boolean): FilterNode | undefined {
  if (!path || !isFilterable(value)) return undefined;
  const condition: FilterNode = { kind: "condition", field: path, op: "equals", value };
  return exclude ? { kind: "not", child: condition } : condition;
}

/**
 * Whether a value can become a chip (see {@link propertyChip}).
 *
 * @param value - A property value.
 * @returns `true` for strings, finite numbers and booleans.
 */
export function isFilterable(value: unknown): value is string | number | boolean {
  return typeof value === "string" || typeof value === "boolean" || (typeof value === "number" && isFinite(value));
}

/**
 * A scalar value as the Properties table shows it: strings in double quotes so `"200"` and `200`
 * look different, everything else as JSON.
 *
 * @param value - A string, number, boolean or null.
 * @returns The display text.
 */
export function formatScalar(value: unknown): string {
  return JSON.stringify(value) ?? String(value);
}

/**
 * Flattens the tree into the table rows currently shown: every top-level node, plus the
 * children of each expanded object or array, depth first, each with its nesting depth for the
 * indent.
 *
 * @param nodes - From {@link buildPropertyTree}.
 * @param expanded - {@link PropertyNode.key}s of the expanded nodes; a collapsed node hides its
 *   whole subtree, whatever is expanded below it.
 * @returns The rows in display order.
 */
export function visibleRows(
  nodes: ReadonlyArray<PropertyNode>,
  expanded: ReadonlySet<string>,
): Array<{ node: PropertyNode; depth: number }> {
  const rows: Array<{ node: PropertyNode; depth: number }> = [];
  const visit = (node: PropertyNode, depth: number) => {
    rows.push({ node, depth });
    if (expanded.has(node.key)) node.children.forEach((child) => visit(child, depth + 1));
  };
  nodes.forEach((node) => visit(node, 0));
  return rows;
}

function toNode(name: string, key: string, path: string | undefined, value: unknown): PropertyNode {
  const kind = kindOf(value);
  let children: Array<PropertyNode> = [];
  if (kind === "array") {
    const itemPath = path === undefined ? undefined : `${path}[]`;
    children = (value as Array<unknown>).map((item, index) => toNode(`[${index}]`, `${key}/${index}`, itemPath, item));
  } else if (kind === "object") {
    children = sortedEntries(value as Record<string, unknown>).map(([childName, child]) =>
      toNode(childName, `${key}/${childName}`, childPath(path, childName), child),
    );
  }
  return { name, key, path, kind, value, children };
}

function childPath(parent: string | undefined, name: string): string | undefined {
  if (parent === undefined || name.includes(".") || name.endsWith("[]")) return undefined;
  return `${parent}.${name}`;
}

function kindOf(value: unknown): PropertyKind {
  if (value === null || value === undefined) return "null";
  if (Array.isArray(value)) return "array";
  switch (typeof value) {
    case "string":
      return "string";
    case "number":
      return "number";
    case "boolean":
      return "boolean";
    default:
      return "object";
  }
}

function sortedEntries(object: Readonly<Record<string, unknown>>): Array<[string, unknown]> {
  return Object.entries(object).sort(([a], [b]) => a.localeCompare(b, undefined, { sensitivity: "base" }));
}
