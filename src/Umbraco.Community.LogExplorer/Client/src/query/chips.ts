import type { FilterNode } from "./filter-node.js";
import { LEVELS, normaliseLevels, type Level } from "./view-state.js";

/**
 * Whether two chips filter the same way. Fields the wire format may leave out are compared at
 * their defaults (`caseInsensitive` true, `phrase` false, `value` null), so a chip from
 * `POST /parse` equals the same chip decoded from a URL or built by the chip editor.
 *
 * @param a - One chip.
 * @param b - The other.
 * @returns `true` when both have the same canonical form.
 */
export function sameChip(a: FilterNode, b: FilterNode): boolean {
  return canonical(a) === canonical(b);
}

/**
 * Appends chips, skipping any that is already in the list (or earlier in `added`), so pressing
 * Enter twice on the same input does not double a filter.
 *
 * @param chips - The current chips.
 * @param added - The chips to add, in order.
 * @returns A new list; the same array when nothing was added.
 */
export function appendChips(chips: ReadonlyArray<FilterNode>, added: ReadonlyArray<FilterNode>): Array<FilterNode> {
  const result = [...chips];
  for (const chip of added) {
    if (!result.some((existing) => sameChip(existing, chip))) result.push(chip);
  }
  return result.length === chips.length ? (chips as Array<FilterNode>) : result;
}

/**
 * Replaces the chip at `index`, keeping its position. When the replacement equals another chip
 * already in the list, the edited chip is dropped instead, so the list never holds duplicates.
 *
 * @param chips - The current chips.
 * @param index - Position of the chip to replace; out of range leaves the list unchanged.
 * @param chip - The replacement.
 * @returns A new list.
 */
export function replaceChipAt(chips: ReadonlyArray<FilterNode>, index: number, chip: FilterNode): Array<FilterNode> {
  if (index < 0 || index >= chips.length) return [...chips];
  const duplicate = chips.some((existing, i) => i !== index && sameChip(existing, chip));
  return duplicate ? chips.filter((_, i) => i !== index) : chips.map((existing, i) => (i === index ? chip : existing));
}

/**
 * Removes the chip at `index`.
 *
 * @param chips - The current chips.
 * @param index - Position to remove; out of range leaves the list unchanged.
 * @returns A new list.
 */
export function removeChipAt(chips: ReadonlyArray<FilterNode>, index: number): Array<FilterNode> {
  return chips.filter((_, i) => i !== index);
}

/**
 * Reads the level set `POST /parse` returned into the view state's form. Names are compared
 * case-insensitively and unknown names are dropped rather than failing the whole set.
 *
 * @param levels - `ParseResult.levels`: lower-case OTel short names, in no particular order.
 * @returns The normalised set (`null` when all six are named).
 */
export function toViewLevels(levels: ReadonlyArray<string>): Array<Level> | null {
  const known = levels
    .map((name) => name.toLowerCase())
    .filter((name): name is Level => (LEVELS as ReadonlyArray<string>).includes(name));
  return normaliseLevels(known);
}

/** Stable JSON with defaults filled in and object keys sorted. */
function canonical(node: FilterNode): string {
  return JSON.stringify(withDefaults(node));
}

function withDefaults(node: FilterNode): unknown {
  switch (node.kind) {
    case "and":
    case "or":
      return { kind: node.kind, children: node.children.map(withDefaults) };
    case "not":
      return { kind: "not", child: withDefaults(node.child) };
    case "condition":
      return {
        kind: "condition",
        field: node.field,
        op: node.op,
        value: sortKeys(node.value ?? null),
        caseInsensitive: node.caseInsensitive ?? true,
      };
    case "text":
      return { kind: "text", text: node.text, phrase: node.phrase ?? false };
  }
}

/** Condition values are arbitrary JSON; sort object keys so `{a,b}` equals `{b,a}`. */
function sortKeys(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(sortKeys);
  if (typeof value !== "object" || value === null) return value;
  return Object.fromEntries(
    Object.keys(value)
      .sort()
      .map((key) => [key, sortKeys((value as Record<string, unknown>)[key])]),
  );
}
