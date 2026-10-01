/**
 * Client mirror of the Core filter tree (`Core/Query/FilterNode.cs`, BRIEF §8.2) in its camelCase
 * JSON shape, with `kind` as the discriminator.
 *
 * Hand-written for now because no endpoint returns a `FilterNode` yet, so the generated client
 * has no such type. Replace these with the generated types once `/parse` and `/search` are in the
 * OpenAPI document (#38, #39).
 */
export type FilterNode = AndNode | OrNode | NotNode | ConditionNode | TextNode;

/** Matches when every child matches; an empty list matches everything. */
export interface AndNode {
  kind: "and";
  children: Array<FilterNode>;
}

/** Matches when any child matches; an empty list matches nothing. */
export interface OrNode {
  kind: "or";
  children: Array<FilterNode>;
}

/** Matches when the child does not. */
export interface NotNode {
  kind: "not";
  child: FilterNode;
}

/** The comparison operators, as the server serialises `FilterOperator` (camelCase strings). */
export const FILTER_OPERATORS = [
  "equals",
  "notEquals",
  "contains",
  "startsWith",
  "endsWith",
  "greaterThan",
  "greaterOrEqual",
  "lessThan",
  "lessOrEqual",
  "in",
  "exists",
  "notExists",
  "matches",
] as const;

/** One of {@link FILTER_OPERATORS}. */
export type FilterOperator = (typeof FILTER_OPERATORS)[number];

/** A JSON value, kept with its kind (BRIEF §8.1: attributes are typed, not flattened to strings). */
export type JsonValue = string | number | boolean | null | Array<JsonValue> | { [key: string]: JsonValue };

/** A comparison of one field (portable `@field` or attribute path) against a typed value. */
export interface ConditionNode {
  kind: "condition";
  field: string;
  op: FilterOperator;
  /** An array for `in`; ignored (usually `null`) for `exists` and `notExists`. */
  value?: JsonValue;
  /** Defaults to `true` on the server when omitted. */
  caseInsensitive?: boolean;
}

/** Free-text search over the body and exception message; `phrase` requires one exact substring. */
export interface TextNode {
  kind: "text";
  text: string;
  phrase?: boolean;
}

/**
 * Checks that an untrusted value (for example decoded from a URL) is a well-formed filter tree.
 *
 * @param value - Anything.
 * @returns `true` when `value` and every descendant has a known `kind` and the fields that kind requires.
 */
export function isFilterNode(value: unknown): value is FilterNode {
  if (typeof value !== "object" || value === null) return false;
  const node = value as Record<string, unknown>;
  switch (node.kind) {
    case "and":
    case "or":
      return Array.isArray(node.children) && node.children.every(isFilterNode);
    case "not":
      return isFilterNode(node.child);
    case "condition":
      return (
        typeof node.field === "string" &&
        node.field.length > 0 &&
        (FILTER_OPERATORS as ReadonlyArray<unknown>).includes(node.op) &&
        (node.caseInsensitive === undefined || typeof node.caseInsensitive === "boolean")
      );
    case "text":
      return typeof node.text === "string" && (node.phrase === undefined || typeof node.phrase === "boolean");
    default:
      return false;
  }
}
