import type { AndNode, ConditionNode, FilterOperator, NotNode, OrNode, TextNode } from "../api/index.js";

export type { AndNode, ConditionNode, FilterOperator, NotNode, OrNode, TextNode };

/**
 * The Core filter tree (`Core/Query/FilterNode.cs`, BRIEF §8.2) in its camelCase JSON shape, with
 * `kind` as the discriminator. The node types come from the generated client; the API document
 * has no named type for the union itself, only `oneOf` the nodes wherever a node is expected.
 */
export type FilterNode = AndNode | OrNode | NotNode | ConditionNode | TextNode;

/**
 * Every `FilterOperator` at runtime, for validating untrusted input. The `satisfies` check plus
 * {@link AllOperatorsListed} fail type-checking when the generated union gains or loses a member.
 */
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
] as const satisfies ReadonlyArray<FilterOperator>;

/** Compile-time proof that {@link FILTER_OPERATORS} lists every generated operator. */
type AllOperatorsListed = Exclude<FilterOperator, (typeof FILTER_OPERATORS)[number]> extends never ? true : never;
const allOperatorsListed: AllOperatorsListed = true;
void allOperatorsListed;

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
