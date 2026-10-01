import type { LogQuery } from "../api/index.js";
import type { FilterNode } from "./filter-node.js";
import type { LogExplorerViewState } from "./view-state.js";

/**
 * Combines filter chips into one tree (BRIEF §6.3): include chips on the same field OR together;
 * everything else (chips on different fields, exclude chips, text) ANDs. An include chip is a
 * `condition` node; an exclude chip is a `not` around one.
 *
 * @param chips - The chips in display order.
 * @returns `null` for no chips, the chip itself for one, otherwise an `and` node whose children
 *   keep the order in which each field (or other chip) first appears.
 */
export function chipsToFilter(chips: ReadonlyArray<FilterNode>): FilterNode | null {
  if (chips.length === 0) return null;
  if (chips.length === 1) return chips[0]!;

  // Groups hold either one chip, or every include condition on a field; the Map keeps the order
  // in which a field first appears.
  const groups = new Map<string, Array<FilterNode>>();
  chips.forEach((chip, index) => {
    const key = chip.kind === "condition" ? `field:${chip.field}` : `chip:${index}`;
    const group = groups.get(key);
    if (group) group.push(chip);
    else groups.set(key, [chip]);
  });

  const children = [...groups.values()].map((group) =>
    group.length === 1 ? group[0]! : ({ kind: "or", children: group } satisfies FilterNode),
  );
  return children.length === 1 ? children[0]! : { kind: "and", children };
}

/**
 * Builds the `POST /search` body for the view state (BRIEF §8.2).
 *
 * @param state - The view state; only range (or the zoom, which replaces it), levels, chips,
 *   native query and sort are used.
 * @param take - Page size.
 * @param cursor - The previous page's `nextCursor`, or `undefined` for the first page.
 * @returns The query.
 */
export function toLogQuery(state: LogExplorerViewState, take: number, cursor?: string): LogQuery {
  // The histogram's time zoom narrows whatever range the picker holds (UI brief §4.7).
  const range = state.zoom ?? state.range;
  return {
    range: "relative" in range ? { relative: range.relative } : { from: range.from, to: range.to },
    levels: state.levels,
    filter: chipsToFilter(state.chips),
    nativeQuery: state.native ?? null,
    take,
    cursor: cursor ?? null,
    sort: state.sort === "asc" ? "ascending" : "descending",
  };
}
