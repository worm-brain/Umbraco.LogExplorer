import type { HistogramBucket, SinkMinimumLevel } from "../api/index.js";
import { includeChip } from "../facets/facets-model.js";
import type { FilterNode } from "../query/filter-node.js";
import type { LogExplorerQueryContext } from "../query/query.context.js";
import { LEVELS, type Level } from "../query/view-state.js";
import { levelTotals } from "../histogram/histogram-model.js";
import { searchTabPath } from "../patterns/patterns-model.js";

/** How many templates "Most frequent messages" lists (UI brief §4.13). */
export const OVERVIEW_TEMPLATES_TOP = 6;

/** How many exception types the panel lists: the most the facets API returns (BRIEF §6.6). */
export const OVERVIEW_EXCEPTIONS_TOP = 10;

/**
 * Buckets asked of the histogram for the level counts. Only the totals are used, so the number
 * barely matters; a small one keeps the response small.
 */
export const OVERVIEW_BUCKETS = 30;

/** Longest template text the messages panel shows before it truncates (UI brief §4.13). */
export const TEMPLATE_MAX_LENGTH = 72;

/** The facet field of the exception types panel (BRIEF §9.1 portable name). */
export const EXCEPTION_TYPE_FIELD = "@exception.type";

/** The source type whose sink configuration `GET /minimum-levels` can read (ADR 0019). */
export const FILES_SOURCE_TYPE = "UmbracoFiles";

/** One row of "Entries by level". */
export interface LevelRow {
  level: Level;
  count: number;
  /** Share of all entries in the range, 0 to 100, for the proportional bar. */
  percent: number;
  /**
   * The query's level set leaves this level out. Counts ignore the set, as the histogram's
   * toggles do (ADR 0004), so the row stays, drawn muted, and the user sees what is hidden.
   */
  hidden: boolean;
}

/**
 * The six rows of "Entries by level" from a histogram that ignored the level set.
 *
 * @param buckets - `HistogramResult.buckets`.
 * @param levels - The view state's level set; `null` means every level.
 * @returns One row per level, least severe first, zero counts included.
 */
export function levelRows(
  buckets: ReadonlyArray<HistogramBucket>,
  levels: ReadonlyArray<Level> | null,
): Array<LevelRow> {
  const totals = levelTotals(buckets);
  const total = LEVELS.reduce((sum, level) => sum + totals[level], 0);
  return LEVELS.map((level) => ({
    level,
    count: totals[level],
    percent: total > 0 ? (totals[level] / total) * 100 : 0,
    hidden: levels !== null && !levels.includes(level),
  }));
}

/**
 * Shortens a template for the messages panel, cutting at {@link TEMPLATE_MAX_LENGTH} and adding an
 * ellipsis. The full text goes in the row's tooltip and accessible name.
 *
 * @param template - `Pattern.template`.
 * @param max - The longest text kept, ellipsis included.
 * @returns The template unchanged when it fits.
 */
export function truncateTemplate(template: string, max = TEMPLATE_MAX_LENGTH): string {
  if (template.length <= max) return template;
  return `${template.slice(0, max - 1).trimEnd()}…`;
}

/**
 * The include chip an exception type row adds: the chip a click on that value in the fields
 * panel adds, so focusing a type that is already a chip adds no duplicate.
 *
 * @param type - The exception type name.
 * @returns A `condition` chip on `@exception.type`.
 */
export function exceptionFocusChip(type: string): FilterNode {
  return includeChip(EXCEPTION_TYPE_FIELD, type);
}

/** One row of "Minimum levels (configuration)". */
export interface SinkRow {
  /** The sink name as Umbraco reports it (`Global`, `UmbracoFile`). */
  name: string;
  /** The level as a short name, for the badge colour; `undefined` when the server sent another name. */
  level: Level | undefined;
  /** The text the badge shows: the level in upper case. */
  label: string;
}

/**
 * The sink rows from `GET /minimum-levels`, in the server's order.
 *
 * @param sinks - `MinimumLevelsResult.sinks`.
 * @returns One row per sink.
 */
export function sinkRows(sinks: ReadonlyArray<SinkMinimumLevel>): Array<SinkRow> {
  return sinks.map(({ name, level }) => {
    const known = (LEVELS as ReadonlyArray<string>).includes(level) ? (level as Level) : undefined;
    return { name, level: known, label: level.toUpperCase() };
  });
}

/**
 * Adds a chip and opens the Search tab with it in place, as the Patterns view's Focus does: the
 * chip goes through the context (which writes the URL), then the Search route is pushed with the
 * query string the context has just written. The tabs are router links listening to the
 * patched history's `changestate`, so pushing the route switches tab.
 *
 * @param context - The query context; only `addChips` is used.
 * @param chip - The include chip to add.
 * @param win - The window whose history and location are used; a stand-in in tests.
 */
export function focusInSearch(
  context: Pick<LogExplorerQueryContext, "addChips">,
  chip: FilterNode,
  win: { history: Pick<History, "state" | "pushState">; location: Pick<Location, "pathname" | "search"> } = window,
): void {
  context.addChips([chip]);
  win.history.pushState(win.history.state, "", `${searchTabPath(win.location.pathname)}${win.location.search}`);
}
