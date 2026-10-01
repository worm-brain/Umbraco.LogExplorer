import type { ConditionNode, NotNode } from "../query/filter-node.js";
import { LEVELS, type Level, type LogExplorerViewState, type RelativeRange } from "../query/view-state.js";

/** How many patterns the view asks for; the API caps requests at 200. */
export const PATTERNS_TOP = 100;

/** Height of a non-zero sparkline bar at least, as a percentage, so one entry still shows. */
const MIN_SPARK_PERCENT = 10;

const MINUTE = 60_000;
const HOUR = 60 * MINUTE;

/** Lengths of the relative presets, to label the window before any response says what ran. */
const RELATIVE_MS: Record<RelativeRange, number> = {
  "15m": 15 * MINUTE,
  "1h": HOUR,
  "4h": 4 * HOUR,
  "24h": 24 * HOUR,
  "7d": 7 * 24 * HOUR,
  "30d": 30 * 24 * HOUR,
};

/** The template field every pattern chip filters on (BRIEF §6.2 alias `template`). */
export const TEMPLATE_FIELD = "@template";

/**
 * The include chip **Focus** adds (BRIEF §6.9): entries with exactly this template. It is the
 * chip `template:"…"` parses to, so focusing twice, or focusing a template already typed, does
 * not add a duplicate.
 *
 * @param template - `Pattern.template`.
 * @returns A `condition` chip on `@template`.
 */
export function focusChip(template: string): ConditionNode {
  return { kind: "condition", field: TEMPLATE_FIELD, op: "equals", value: template };
}

/**
 * The exclude chip **Mute** adds (BRIEF §6.9, §5 J4): a `not` around {@link focusChip}, the shape
 * every exclude chip has.
 *
 * @param template - `Pattern.template`.
 * @returns A `not` chip.
 */
export function muteChip(template: string): NotNode {
  return { kind: "not", child: focusChip(template) };
}

/** One segment of a pattern's level-mix bar. */
export interface LevelShare {
  level: Level;
  count: number;
  /** Share of the pattern's entries, 0 to 100. */
  percent: number;
}

/**
 * The level-mix bar of one pattern (UI brief §4.12): the share of each level, least severe
 * first so the bar reads left to right like the level toggles.
 *
 * @param counts - `Pattern.countsBySeverityShortName`; missing levels count as zero and unknown
 *   names are ignored.
 * @returns Only the levels with entries; an empty array when there are none.
 */
export function levelMix(counts: Readonly<Record<string, number>>): Array<LevelShare> {
  const present = LEVELS.map((level) => ({ level, count: counts[level] ?? 0 })).filter(({ count }) => count > 0);
  const total = present.reduce((sum, { count }) => sum + count, 0);
  return present.map(({ level, count }) => ({ level, count, percent: (count / total) * 100 }));
}

/** One sparkline bar. */
export interface SparkBar {
  /** Height as a percentage of the strip; 0 for an empty bucket. */
  heightPercent: number;
  /** An empty bucket, which the view draws as a faint baseline instead. */
  empty: boolean;
}

/**
 * Scales a pattern's sparkline to its own busiest bucket, so each row shows the shape of its
 * volume rather than its size (the count column carries that).
 *
 * @param values - `Pattern.sparkline`, oldest first.
 * @returns One bar per bucket; a non-zero bucket is at least {@link MIN_SPARK_PERCENT} tall.
 */
export function sparkBars(values: ReadonlyArray<number>): Array<SparkBar> {
  const max = Math.max(0, ...values);
  return values.map((value) =>
    value > 0
      ? { heightPercent: Math.max(MIN_SPARK_PERCENT, (value / max) * 100), empty: false }
      : { heightPercent: 0, empty: true },
  );
}

/**
 * The time window the patterns cover, for the "Volume, {start} to {end}" header. `PatternResult`
 * does not return the range that ran, so a relative range is resolved here against `nowMs`; it
 * can differ from the server's by the request's round trip.
 *
 * @param state - The view state; the zoom replaces the range, as it does in queries.
 * @param nowMs - The current time.
 * @returns Start and end in epoch milliseconds.
 */
export function patternsWindow(state: LogExplorerViewState, nowMs: number): { fromMs: number; toMs: number } {
  const range = state.zoom ?? state.range;
  if ("relative" in range) return { fromMs: nowMs - RELATIVE_MS[range.relative], toMs: nowMs };
  return { fromMs: new Date(range.from).getTime(), toMs: new Date(range.to).getTime() };
}

/**
 * The route of the Search tab for a URL inside the workspace. The workspace editor routes each
 * view at `{workspace}/view/{pathname}`, and the bare workspace path shows the first tab.
 *
 * @param pathname - `location.pathname` while a workspace view is open.
 * @returns The same path with its view segment replaced by `view/search`.
 */
export function searchTabPath(pathname: string): string {
  const base = pathname.replace(/\/view\/[^/]*\/?$/, "").replace(/\/$/, "");
  return `${base}/view/search`;
}

/**
 * Labels for the two ends of the patterns window: times of day when it spans at most a day, short
 * dates beyond that, so the header stays narrow.
 *
 * @param fromMs - Window start, epoch milliseconds.
 * @param toMs - Window end, epoch milliseconds.
 * @param locale - A BCP 47 locale, usually the backoffice user's.
 * @returns The start and end labels.
 */
export function windowLabels(fromMs: number, toMs: number, locale?: string): { from: string; to: string } {
  const options: Intl.DateTimeFormatOptions =
    toMs - fromMs <= 24 * HOUR ? { timeStyle: "short" } : { dateStyle: "short" };
  const format = new Intl.DateTimeFormat(locale, options);
  return { from: format.format(new Date(fromMs)), to: format.format(new Date(toMs)) };
}
