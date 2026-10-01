import type { SourceResponseModel } from "../api/index.js";
import { sameChip } from "../query/chips.js";
import type { FilterNode } from "../query/filter-node.js";
import type { LogExplorerViewState } from "../query/view-state.js";
import { canUseNativeMode } from "./native-mode.js";

/**
 * Finds the chips the active source cannot run (BRIEF §6.3). A chip is unsupported when any node
 * in it:
 *
 * - is a condition whose operator the source does not declare (known at once, from
 *   `capabilities.operators`), or
 * - equals a node `POST /compile` returned in `unsupported` (the source's language cannot
 *   express it, for example a field it has no column for).
 *
 * @param chips - The chips in the view state.
 * @param source - The active source; `undefined` checks only `unsupported`.
 * @param unsupported - `CompileResult.unsupported` from the latest compile.
 * @returns The positions of the unsupported chips, ascending.
 */
export function findUnsupportedChips(
  chips: ReadonlyArray<FilterNode>,
  source: SourceResponseModel | undefined,
  unsupported: ReadonlyArray<FilterNode>,
): Array<number> {
  const operators = source ? new Set<string>(source.capabilities.operators) : undefined;
  const isUnsupported = (node: FilterNode): boolean => {
    if (unsupported.some((other) => sameChip(other, node))) return true;
    switch (node.kind) {
      case "and":
      case "or":
        return node.children.some(isUnsupported);
      case "not":
        return isUnsupported(node.child);
      case "condition":
        return operators !== undefined && !operators.has(node.op);
      case "text":
        return false;
    }
  };

  return chips.flatMap((chip, index) => (isUnsupported(chip) ? [index] : []));
}

/**
 * The view state as queries should see it: unsupported chips left out (they stay visible, and
 * disabled, in the search box), and the native query left out when the source does not allow
 * native mode, so a link made for another source cannot send one.
 *
 * @param state - The view state.
 * @param source - The active source.
 * @param unsupportedChips - Positions from {@link findUnsupportedChips}.
 * @returns `state` itself when nothing is left out, else a copy.
 */
export function toQueryState(
  state: LogExplorerViewState,
  source: SourceResponseModel | undefined,
  unsupportedChips: ReadonlyArray<number>,
): LogExplorerViewState {
  const dropNative = state.native !== undefined && source !== undefined && !canUseNativeMode(source);
  if (unsupportedChips.length === 0 && !dropNative) return state;

  const skipped = new Set(unsupportedChips);
  return {
    ...state,
    chips: state.chips.filter((_, index) => !skipped.has(index)),
    native: dropNative ? undefined : state.native,
  };
}
