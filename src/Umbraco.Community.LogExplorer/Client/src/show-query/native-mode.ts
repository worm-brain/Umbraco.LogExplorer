import type { SourceResponseModel } from "../api/index.js";
import type { FilterNode } from "../query/filter-node.js";
import type { LogExplorerViewState } from "../query/view-state.js";

/**
 * Whether "Show query" is available: the source declares `nativeQuery`, which is what gates
 * `POST /compile`. A source without a native language has nothing to show.
 *
 * @param source - The active source.
 * @returns `false` while no source is known.
 */
export function canShowQuery(source: SourceResponseModel | undefined): boolean {
  return source?.capabilities.features.includes("nativeQuery") ?? false;
}

/**
 * Whether native mode (the "Edit as native" button and the native search box) is available:
 * "Show query" is, and the source's configuration allows native queries (`AllowNativeQuery`,
 * BRIEF §12).
 *
 * @param source - The active source.
 * @returns `false` while no source is known.
 */
export function canUseNativeMode(source: SourceResponseModel | undefined): boolean {
  return canShowQuery(source) && (source?.allowNativeQuery ?? false);
}

/**
 * The view-state change for "Edit as native" (UI brief §4.6): the compiled query becomes the
 * native query, and the chips and level toggles it already expresses are cleared so they do not
 * apply twice. Chips the compiled text leaves out (those the source cannot run, and those it runs
 * but cannot express) stay as chips, and the ones the source runs keep ANDing with the native
 * query (ADR 0016).
 *
 * @param state - The view state.
 * @param compiledNative - The compiled text; `null` (no filter) opens an empty native query.
 * @param keptChips - Positions of the chips to keep: the unsupported and not-expressible ones.
 * @returns The fields to change.
 */
export function enterNativeMode(
  state: LogExplorerViewState,
  compiledNative: string | null,
  keptChips: ReadonlyArray<number>,
): Partial<LogExplorerViewState> {
  const kept = new Set(keptChips);
  return {
    // The search box is one line, and a text input silently drops line breaks (which would glue
    // `]` to the next `and`), so the clauses are joined with spaces instead.
    native: (formatClauses(compiledNative) ?? "").replace(/\n/g, " "),
    chips: state.chips.filter((_, index) => kept.has(index)),
    levels: null,
  };
}

/**
 * Whether "Edit as native" would split an OR group: include chips on one field OR together
 * (`chipsToFilter`), so when only some of them are not expressible, the compiled text holds the
 * rest of the OR and keeping the others as chips would AND them instead, losing entries.
 *
 * @param chips - The chips in the view state.
 * @param notExpressible - Positions of the chips the compiled text leaves out but the source runs.
 * @param cannotRun - Positions of the chips the source cannot run; no query sees them, so they
 *   are in no group.
 * @returns `true` when one field has include chips on both sides.
 */
export function splitsOrGroup(
  chips: ReadonlyArray<FilterNode>,
  notExpressible: ReadonlyArray<number>,
  cannotRun: ReadonlyArray<number> = [],
): boolean {
  const kept = new Set(notExpressible);
  const sides = new Map<string, Set<boolean>>();
  chips.forEach((chip, index) => {
    if (chip.kind !== "condition" || cannotRun.includes(index)) return;
    const side = sides.get(chip.field) ?? new Set<boolean>();
    side.add(kept.has(index));
    sides.set(chip.field, side);
  });
  return [...sides.values()].some((side) => side.size === 2);
}

/**
 * The view-state change for leaving native mode: the native query is dropped; chips stay.
 *
 * @returns The fields to change.
 */
export function leaveNativeMode(): Partial<LogExplorerViewState> {
  return { native: undefined };
}

/**
 * Prepares compiled text for the show-query panel: one clause per line, as the compilers emit it
 * (`and` at line starts for Serilog and the sample source, one `| where` per line for KQL), with
 * Windows line endings and blank lines removed.
 *
 * @param native - `CompileResult.native`.
 * @returns The lines joined by `\n`, or `null` when there is no filter at all.
 */
export function formatClauses(native: string | null | undefined): string | null {
  const lines = (native ?? "")
    .split(/\r?\n/)
    .map((line) => line.trimEnd())
    .filter((line) => line.trim().length > 0);
  return lines.length > 0 ? lines.join("\n") : null;
}
