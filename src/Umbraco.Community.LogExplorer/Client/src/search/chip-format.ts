import type { FilterNode, FilterOperator } from "../query/filter-node.js";

/** `localize.term` as the chip helpers need it; tests pass one backed by the `en` dictionary. */
export type Term = (key: string, ...args: Array<string>) => string;

/**
 * How a chip is styled (UI brief §4.4): `include` keeps matching entries, `exclude` hides them,
 * `text` is a free-text search.
 */
export type ChipKind = "include" | "exclude" | "text";

/** What a chip shows. */
export interface ChipDisplay {
  kind: ChipKind;
  /** The pill text, with the short field name (`Path: /api*`). */
  label: string;
  /** The tooltip and accessible name, with the full field name (`RequestPath starts with "/api"`). */
  description: string;
}

/**
 * Short chip names for the common fields (UI brief §4.4). Fields not listed show their own name.
 * Both the portable names and the files source's attribute names map, because chips from facets
 * carry the attribute name.
 */
const SHORT_FIELD_KEYS: Readonly<Record<string, string>> = {
  RequestPath: "logExplorer_fieldPath",
  SourceContext: "logExplorer_fieldSource",
  "@scope": "logExplorer_fieldSource",
  StatusCode: "logExplorer_fieldStatus",
  MachineName: "logExplorer_fieldMachine",
  ExceptionType: "logExplorer_fieldException",
  "@exception.type": "logExplorer_fieldException",
  RequestId: "logExplorer_fieldRequest",
  "@template": "logExplorer_fieldTemplate",
  "@body": "logExplorer_fieldText",
};

/** Operator words for descriptions and the chip editor's operator list. */
export const OPERATOR_KEYS: Readonly<Record<FilterOperator, string>> = {
  equals: "logExplorer_opEquals",
  notEquals: "logExplorer_opNotEquals",
  contains: "logExplorer_opContains",
  startsWith: "logExplorer_opStartsWith",
  endsWith: "logExplorer_opEndsWith",
  greaterThan: "logExplorer_opGreaterThan",
  greaterOrEqual: "logExplorer_opGreaterOrEqual",
  lessThan: "logExplorer_opLessThan",
  lessOrEqual: "logExplorer_opLessOrEqual",
  in: "logExplorer_opIn",
  exists: "logExplorer_opExists",
  notExists: "logExplorer_opNotExists",
  matches: "logExplorer_opMatches",
};

const COMPARISON_SYMBOLS: Partial<Record<FilterOperator, string>> = {
  greaterThan: ">",
  greaterOrEqual: ">=",
  lessThan: "<",
  lessOrEqual: "<=",
};

/**
 * The short name a chip shows for a field.
 *
 * @param field - A portable (`@template`) or attribute (`RequestPath`) field name.
 * @param term - Localises the short names.
 * @returns The short name, or `field` itself when it has none.
 */
export function shortFieldName(field: string, term: Term): string {
  const key = SHORT_FIELD_KEYS[field];
  return key ? term(key) : field;
}

/**
 * Describes a chip for display. Wildcards are written back the way they are typed (`/api*`,
 * `*api`, `*api*`); comparisons use their symbol (`Duration > 1000`); an exclude chip (a `not`
 * node, `notEquals` or `notExists`) is prefixed with "not".
 *
 * @param chip - A chip from the view state.
 * @param term - Localises labels and operator words.
 * @returns The kind, label and description.
 */
export function describeChip(chip: FilterNode, term: Term): ChipDisplay {
  switch (chip.kind) {
    case "text": {
      const quoted = `"${chip.text}"`;
      const op = term(chip.phrase ? "logExplorer_opContainsPhrase" : "logExplorer_opContains");
      return {
        kind: "text",
        label: quoted,
        description: term("logExplorer_chipDescription", term("logExplorer_fieldText"), op, quoted),
      };
    }
    case "condition":
      return describeCondition(chip, term);
    case "not": {
      const inner = describeChip(chip.child, term);
      return {
        kind: "exclude",
        label: term("logExplorer_chipNot", inner.label),
        description: term("logExplorer_chipNot", inner.description),
      };
    }
    case "and":
    case "or": {
      // The parser never builds these as chips; a hand-edited URL can. Show them readably.
      const parts = chip.children.map((child) => describeChip(child, term));
      const join = ` ${chip.kind} `;
      return {
        kind: "include",
        label: `(${parts.map((part) => part.label).join(join)})`,
        description: `(${parts.map((part) => part.description).join(join)})`,
      };
    }
  }
}

function describeCondition(chip: Extract<FilterNode, { kind: "condition" }>, term: Term): ChipDisplay {
  const short = shortFieldName(chip.field, term);
  const op = term(OPERATOR_KEYS[chip.op]);
  const describe = (value: string) => term("logExplorer_chipDescription", chip.field, op, value);

  switch (chip.op) {
    case "exists":
      return { kind: "include", label: term("logExplorer_chipHas", short), description: describe("") };
    case "notExists":
      return {
        kind: "exclude",
        label: term("logExplorer_chipNot", term("logExplorer_chipHas", short)),
        description: describe(""),
      };
    case "greaterThan":
    case "greaterOrEqual":
    case "lessThan":
    case "lessOrEqual":
      return {
        kind: "include",
        label: `${short} ${COMPARISON_SYMBOLS[chip.op]} ${plain(chip.value)}`,
        description: describe(quoted(chip.value)),
      };
    case "notEquals":
      return {
        kind: "exclude",
        label: term("logExplorer_chipNot", `${short}: ${plain(chip.value)}`),
        description: describe(quoted(chip.value)),
      };
    default:
      return {
        kind: "include",
        label: `${short}: ${wildcard(chip.op, plain(chip.value))}`,
        description: describe(quoted(chip.value)),
      };
  }
}

function wildcard(op: FilterOperator, value: string): string {
  switch (op) {
    case "startsWith":
      return `${value}*`;
    case "endsWith":
      return `*${value}`;
    case "contains":
      return `*${value}*`;
    default:
      return value;
  }
}

/** A JSON value as chip text: strings bare, arrays comma-separated, everything else as JSON. */
function plain(value: unknown): string {
  if (typeof value === "string") return value;
  if (Array.isArray(value)) return value.map(plain).join(", ");
  return value === undefined ? "" : JSON.stringify(value);
}

/** Like {@link plain}, but strings are quoted so the description shows where they start and end. */
function quoted(value: unknown): string {
  if (typeof value === "string") return `"${value}"`;
  if (Array.isArray(value)) return value.map(quoted).join(", ");
  return plain(value);
}
