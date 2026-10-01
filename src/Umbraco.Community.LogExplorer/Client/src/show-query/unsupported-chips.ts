import type { SourceResponseModel } from "../api/index.js";
import { sameChip } from "../query/chips.js";
import type { FilterNode } from "../query/filter-node.js";
import type { LogExplorerViewState } from "../query/view-state.js";
import { canUseNativeMode } from "./native-mode.js";

/**
 * Finds the chips the active source cannot run (BRIEF §6.3): those holding a condition whose
 * operator the source does not declare in `capabilities.operators`. Known at once, so a
 * just-added chip the source cannot run never reaches `/search`.
 *
 * A chip the source can run but its compiler cannot express is not one of these; see
 * {@link findNotExpressibleChips}.
 *
 * @param chips - The chips in the view state.
 * @param source - The active source; `undefined` marks nothing.
 * @returns The positions of the chips the source cannot run, ascending.
 */
export function findUnsupportedChips(
  chips: ReadonlyArray<FilterNode>,
  source: SourceResponseModel | undefined,
): Array<number> {
  if (!source) return [];
  const operators = new Set<string>(source.capabilities.operators);
  const cannotRun = (node: FilterNode): boolean => {
    switch (node.kind) {
      case "and":
      case "or":
        return node.children.some(cannotRun);
      case "not":
        return cannotRun(node.child);
      case "condition":
        return !operators.has(node.op);
      case "text":
        return false;
    }
  };

  return chips.flatMap((chip, index) => (cannotRun(chip) ? [index] : []));
}

/**
 * Finds the chips the source runs but "Show query" cannot display: those holding a node the
 * latest `POST /compile` returned in `unsupported` (ADR 0016). They stay active and filter as
 * usual; the show-query panel lists them as not shown, because the compiled text leaves them out.
 *
 * @param chips - The chips in the view state.
 * @param unsupported - `CompileResult.unsupported` from the latest compile.
 * @param cannotRun - Positions from {@link findUnsupportedChips}; never reported again here.
 * @returns The positions, ascending.
 */
export function findNotExpressibleChips(
  chips: ReadonlyArray<FilterNode>,
  unsupported: ReadonlyArray<FilterNode>,
  cannotRun: ReadonlyArray<number>,
): Array<number> {
  if (unsupported.length === 0) return [];
  const reported = (node: FilterNode): boolean => {
    if (unsupported.some((other) => sameChip(other, node))) return true;
    switch (node.kind) {
      case "and":
      case "or":
        return node.children.some(reported);
      case "not":
        return reported(node.child);
      default:
        return false;
    }
  };

  return chips.flatMap((chip, index) => (!cannotRun.includes(index) && reported(chip) ? [index] : []));
}

/**
 * The view state as queries should see it: chips the source cannot run left out (they stay
 * visible, and disabled, in the search box), and the native query left out when the source does
 * not allow native mode, so a link made for another source cannot send one. Chips that are only
 * not expressible stay in.
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
