import type { ConditionNode, FilterNode } from "../query/filter-node.js";

/** The operators the chip editor offers (UI brief §4.4: "change its operator or value"). */
export type EditOperator =
  | "equals"
  | "startsWith"
  | "contains"
  | "endsWith"
  | "exists"
  | "greaterThan"
  | "greaterOrEqual"
  | "lessThan"
  | "lessOrEqual";

const STRING_OPERATORS: ReadonlyArray<EditOperator> = ["equals", "startsWith", "contains", "endsWith", "exists"];
const COMPARISON_OPERATORS: ReadonlyArray<EditOperator> = ["greaterThan", "greaterOrEqual", "lessThan", "lessOrEqual"];

/**
 * The chip editor's form for a field chip. Exclusion is a separate switch rather than an
 * operator, so every operator can be excluded the way `-field:val*` is typed.
 */
export interface ConditionDraft {
  kind: "condition";
  /** Not editable; the editor changes how a field is matched, not which field. */
  field: string;
  op: EditOperator;
  /** As typed; ignored for `exists`. */
  value: string;
  exclude: boolean;
  /** Whether the original value was a JSON number, so an unchanged-type edit stays a number. */
  numeric: boolean;
  caseInsensitive: boolean;
}

/** The chip editor's form for a text chip. */
export interface TextDraft {
  kind: "text";
  text: string;
  phrase: boolean;
}

/** The chip editor's form. */
export type ChipDraft = ConditionDraft | TextDraft;

/**
 * Builds the editor form for a chip.
 *
 * `notEquals` and `notExists` become their positive operator with `exclude` on; saving writes
 * them back in the parser's shape (`not` around the condition, or `notExists`), which filters
 * the same entries.
 *
 * @param chip - The chip being edited.
 * @returns The form, or `undefined` for chips the editor cannot represent (`and`/`or` groups,
 *   `in`, `matches`, `not` around anything but a condition), which are then not editable.
 */
export function draftFromChip(chip: FilterNode): ChipDraft | undefined {
  if (chip.kind === "text") return { kind: "text", text: chip.text, phrase: chip.phrase ?? false };
  if (chip.kind === "not") {
    if (chip.child.kind !== "condition" || chip.child.op === "notExists") return undefined;
    const inner = conditionDraft(chip.child);
    return inner && !inner.exclude ? { ...inner, exclude: true } : undefined;
  }
  return chip.kind === "condition" ? conditionDraft(chip) : undefined;
}

/**
 * Writes the editor form back as a chip, in the shape `POST /parse` would have produced for the
 * equivalent typed input.
 *
 * Values are sent as JSON numbers for comparisons whose value is numeric, and for other
 * operators when the original value was a number and the new one still is; otherwise as
 * strings (BRIEF §6.2: date comparisons stay strings).
 *
 * @param draft - A form that passes {@link isDraftValid}.
 * @returns The chip.
 */
export function chipFromDraft(draft: ChipDraft): FilterNode {
  if (draft.kind === "text") return { kind: "text", text: draft.text.trim(), phrase: draft.phrase };

  if (draft.op === "exists") {
    return {
      kind: "condition",
      field: draft.field,
      op: draft.exclude ? "notExists" : "exists",
      value: null,
      caseInsensitive: draft.caseInsensitive,
    };
  }

  const value = draft.value.trim();
  const asNumber = isNumeric(value) && (COMPARISON_OPERATORS.includes(draft.op) || draft.numeric);
  const condition: ConditionNode = {
    kind: "condition",
    field: draft.field,
    op: draft.op,
    value: asNumber ? Number(value) : value,
    caseInsensitive: draft.caseInsensitive,
  };
  return draft.exclude ? { kind: "not", child: condition } : condition;
}

/**
 * The operators the editor lists for a draft: the string matches and `exists` always, the
 * comparisons only when the value is numeric, or when the draft already compares (a date
 * comparison typed as `field>2026-01-01` keeps its operator available).
 *
 * @param draft - The current form.
 * @returns The operators, in display order.
 */
export function operatorsFor(draft: ConditionDraft): ReadonlyArray<EditOperator> {
  const compare = isNumeric(draft.value.trim()) || COMPARISON_OPERATORS.includes(draft.op);
  return compare ? [...STRING_OPERATORS, ...COMPARISON_OPERATORS] : STRING_OPERATORS;
}

/**
 * Whether Save may be pressed.
 *
 * @param draft - The current form.
 * @returns `true` when there is a value to match (any value for `exists`).
 */
export function isDraftValid(draft: ChipDraft): boolean {
  if (draft.kind === "text") return draft.text.trim().length > 0;
  return draft.op === "exists" || draft.value.trim().length > 0;
}

function conditionDraft(chip: ConditionNode): ConditionDraft | undefined {
  const base = { kind: "condition" as const, field: chip.field, caseInsensitive: chip.caseInsensitive ?? true };
  if (chip.op === "exists" || chip.op === "notExists") {
    return { ...base, op: "exists", value: "", exclude: chip.op === "notExists", numeric: false };
  }
  if (chip.op === "in" || chip.op === "matches") return undefined;
  if (typeof chip.value !== "string" && typeof chip.value !== "number") return undefined;

  const op: EditOperator = chip.op === "notEquals" ? "equals" : chip.op;
  return {
    ...base,
    op,
    value: String(chip.value),
    exclude: chip.op === "notEquals",
    numeric: typeof chip.value === "number",
  };
}

/** A finite number in the invariant form the parser accepts (`1000`, `-1.5`, `1e3`). */
function isNumeric(value: string): boolean {
  return value.length > 0 && Number.isFinite(Number(value));
}
