import type { HistogramBucket } from "../api/index.js";
import { LEVELS, type AbsoluteRange, type Level } from "../query/view-state.js";

const SECOND = 1_000;
const MINUTE = 60 * SECOND;
const HOUR = 60 * MINUTE;

/** Length of the window a bar click zooms to (UI brief §4.7). */
export const CLICK_ZOOM_MS = 5 * MINUTE;

/** How far before the clicked bucket's start the click-zoom window begins (UI brief §4.7). */
export const CLICK_ZOOM_LEAD_MS = 2 * MINUTE;

/**
 * Levels in the order bars stack them, bottom first (UI brief §4.7): INFO, the bulk of most logs,
 * sits on the baseline; the levels people look for sit on top, where they are easiest to see.
 */
export const STACK_ORDER: ReadonlyArray<Level> = ["info", "debug", "trace", "warn", "error", "fatal"];

/**
 * Picks how many buckets to ask for (BRIEF §6.4, UI brief §4.7): 30 when zoomed, 45 for 15 minutes,
 * 60 for an hour, and more for longer ranges so a day or a month still shows its shape. Sources
 * round the width to their own steps, so the bars that come back are close to, not exactly, this.
 *
 * @param rangeMs - Length of the range the histogram covers, in milliseconds.
 * @param zoomed - Whether the range is a time zoom rather than the picker's range.
 * @returns The target bucket count, from 30 to 120.
 */
export function targetBucketsFor(rangeMs: number, zoomed: boolean): number {
  if (zoomed) return 30;
  if (rangeMs <= 15 * MINUTE) return 45;
  if (rangeMs <= 4 * HOUR) return 60;
  if (rangeMs <= 24 * HOUR) return 96;
  return 120;
}

/**
 * Parses a .NET `TimeSpan` as System.Text.Json writes it (`[d.]hh:mm:ss[.fffffff]`), which is how
 * `HistogramResult.bucketSize` arrives.
 *
 * @param value - The serialised span.
 * @returns Milliseconds, or `undefined` when the text is not a `TimeSpan`.
 */
export function parseTimeSpanMs(value: string): number | undefined {
  const match = /^(?:(\d+)\.)?(\d{1,2}):(\d{2}):(\d{2})(?:\.(\d{1,7}))?$/.exec(value);
  if (!match) return undefined;
  const [, days = "0", hours, minutes, seconds, fraction = "0"] = match;
  return (
    Number(days) * 24 * HOUR +
    Number(hours) * HOUR +
    Number(minutes) * MINUTE +
    Number(seconds) * SECOND +
    // Ticks are 100 ns: pad to seven digits, then keep whole milliseconds.
    Math.floor(Number(fraction.padEnd(7, "0")) / 10_000)
  );
}

/** One coloured part of a bar. */
export interface BarSegment {
  level: Level;
  count: number;
  /** Height as a percentage of the bar strip. */
  heightPercent: number;
}

/**
 * Lays out one bar's segments, bottom first in {@link STACK_ORDER}. Heights are proportional to
 * the tallest bar (`maxTotal` fills the strip), but every non-zero segment is at least
 * `minSegmentPercent` tall so a single ERROR among thousands of INFO entries still shows.
 *
 * Raising small segments can push a bar past the strip; the excess is taken from the tallest
 * segments (never below the minimum), so the bar's total never exceeds 100%.
 *
 * @param counts - Count per lower-case level name; missing levels count as zero.
 * @param maxTotal - The largest bar total in the histogram.
 * @param minSegmentPercent - Smallest visible segment height, as a percentage of the strip.
 * @returns The non-zero segments, bottom first. Empty when the bucket has no entries.
 */
export function layoutBar(
  counts: Readonly<Record<string, number>>,
  maxTotal: number,
  minSegmentPercent: number,
): Array<BarSegment> {
  const segments = STACK_ORDER.map((level) => ({ level, count: counts[level] ?? 0, heightPercent: 0 })).filter(
    (segment) => segment.count > 0,
  );
  if (segments.length === 0 || maxTotal <= 0) return [];

  for (const segment of segments) {
    segment.heightPercent = Math.max(minSegmentPercent, (segment.count / maxTotal) * 100);
  }

  let excess = segments.reduce((sum, segment) => sum + segment.heightPercent, 0) - 100;
  for (const segment of [...segments].sort((a, b) => b.heightPercent - a.heightPercent)) {
    if (excess <= 0) break;
    const take = Math.min(excess, segment.heightPercent - minSegmentPercent);
    segment.heightPercent -= take;
    excess -= take;
  }
  return segments;
}

/**
 * @param bucket - A histogram bucket.
 * @returns The bucket's entry count across every level.
 */
export function bucketTotal(bucket: HistogramBucket): number {
  return Object.values(bucket.countsBySeverityShortName).reduce((sum, count) => sum + count, 0);
}

/**
 * Sums each level across the buckets, for the level toggles' counts.
 *
 * @param buckets - The histogram's buckets.
 * @returns A count for each of the six levels, zeros included.
 */
export function levelTotals(buckets: ReadonlyArray<HistogramBucket>): Record<Level, number> {
  const totals = Object.fromEntries(LEVELS.map((level) => [level, 0])) as Record<Level, number>;
  for (const bucket of buckets) {
    for (const level of LEVELS) totals[level] += bucket.countsBySeverityShortName[level] ?? 0;
  }
  return totals;
}

/**
 * The window a bar click zooms to: five minutes starting two minutes before the bucket, so the
 * clicked spike sits near the start with context on both sides (UI brief §4.7).
 *
 * @param bucketStart - The clicked bucket's start, ISO 8601.
 * @returns The zoom range.
 */
export function zoomFromBucket(bucketStart: string): AbsoluteRange {
  const from = new Date(bucketStart).getTime() - CLICK_ZOOM_LEAD_MS;
  return { from: new Date(from).toISOString(), to: new Date(from + CLICK_ZOOM_MS).toISOString() };
}

/**
 * The range a drag across bars selects: from the start of the leftmost bar touched to the end of
 * the rightmost, whichever direction the pointer moved.
 *
 * @param buckets - The histogram's buckets, oldest first.
 * @param bucketSizeMs - Width of every bucket.
 * @param startIndex - The bar the drag started on.
 * @param endIndex - The bar the drag ended on.
 * @returns The selected range, or `undefined` when either index is outside the buckets.
 */
export function rangeFromDrag(
  buckets: ReadonlyArray<HistogramBucket>,
  bucketSizeMs: number,
  startIndex: number,
  endIndex: number,
): AbsoluteRange | undefined {
  const first = buckets[Math.min(startIndex, endIndex)];
  const last = buckets[Math.max(startIndex, endIndex)];
  if (!first || !last) return undefined;
  const to = new Date(last.start).getTime() + bucketSizeMs;
  return { from: new Date(first.start).toISOString(), to: new Date(to).toISOString() };
}

/**
 * Maps a pointer position to the bar under it. Positions outside the strip clamp to the first or
 * last bar, so a drag that overshoots the edge still selects up to it.
 *
 * @param clientX - Pointer x in viewport pixels.
 * @param left - The bar strip's left edge in viewport pixels.
 * @param width - The bar strip's width in pixels.
 * @param count - Number of bars, all the same width.
 * @returns The bar index, from 0 to `count - 1`.
 */
export function barIndexAt(clientX: number, left: number, width: number, count: number): number {
  if (count <= 0 || width <= 0) return 0;
  const index = Math.floor(((clientX - left) / width) * count);
  return Math.min(count - 1, Math.max(0, index));
}

/** One label under the bars. */
export interface Tick {
  /** Position from the strip's left edge, as a percentage of its width. */
  offsetPercent: number;
  label: string;
}

/**
 * Five evenly spaced time labels across the bars, both ends included (UI brief §4.7), in the
 * user's time zone: `hh:mm`, or `hh:mm:ss` when zoomed and minutes alone would repeat. Spans
 * longer than a day add the day and month, since `hh:mm` alone would repeat across days.
 *
 * @param fromMs - Start of the first bar.
 * @param toMs - End of the last bar.
 * @param withSeconds - Whether to show seconds.
 * @param locale - A BCP 47 locale, usually the backoffice user's.
 * @returns The five ticks, left to right.
 */
export function ticks(fromMs: number, toMs: number, withSeconds: boolean, locale?: string): Array<Tick> {
  const format = new Intl.DateTimeFormat(locale, {
    ...(toMs - fromMs > 24 * HOUR ? { day: "2-digit", month: "2-digit" } : {}),
    ...clockParts(withSeconds),
  });
  return [0, 1, 2, 3, 4].map((step) => ({
    offsetPercent: step * 25,
    label: format.format(new Date(fromMs + ((toMs - fromMs) * step) / 4)),
  }));
}

/**
 * Formats an instant as a 24-hour clock time in the user's time zone, for bar labels.
 *
 * @param ms - The instant.
 * @param withSeconds - Whether to show seconds.
 * @param locale - A BCP 47 locale.
 * @returns For example `00:41` or `00:41:30`.
 */
export function formatClock(ms: number, withSeconds: boolean, locale?: string): string {
  return new Intl.DateTimeFormat(locale, clockParts(withSeconds)).format(new Date(ms));
}

/**
 * The two ends of the time chip's label (UI brief §4.4, `Time: 00:40 to 00:45`): clock times,
 * with seconds when either end is not on a whole minute (a drag inside a zoom), and with the date
 * when the ends fall on different local days.
 *
 * @param zoom - The zoom range.
 * @param locale - A BCP 47 locale.
 * @returns The formatted start and end.
 */
export function zoomLabelParts(zoom: AbsoluteRange, locale?: string): { from: string; to: string } {
  const from = new Date(zoom.from);
  const to = new Date(zoom.to);
  const withSeconds = from.getSeconds() !== 0 || to.getSeconds() !== 0;
  const sameDay = from.toDateString() === to.toDateString();
  const format = new Intl.DateTimeFormat(locale, {
    ...(sameDay ? {} : { day: "2-digit", month: "2-digit" }),
    ...clockParts(withSeconds),
  });
  return { from: format.format(from), to: format.format(to) };
}

function clockParts(withSeconds: boolean): Intl.DateTimeFormatOptions {
  return { hour: "2-digit", minute: "2-digit", second: withSeconds ? "2-digit" : undefined, hourCycle: "h23" };
}

/**
 * Turns one level on or off in the level set (ADR 0004), where `null` means every level is on.
 * Turning the last hidden level back on returns `null`, so "everything" has one representation
 * and the URL drops the `levels` key.
 *
 * @param levels - The current set; `null` for no level filter.
 * @param level - The level whose toggle was pressed.
 * @returns The new set in severity order, or `null` when every level is on.
 */
export function toggleLevel(levels: ReadonlyArray<Level> | null, level: Level): Array<Level> | null {
  const on = new Set<Level>(levels ?? LEVELS);
  if (on.has(level)) on.delete(level);
  else on.add(level);
  return on.size === LEVELS.length ? null : LEVELS.filter((name) => on.has(name));
}

/**
 * @param levels - The level set; `null` for no level filter.
 * @param level - A level.
 * @returns Whether that level's entries are shown.
 */
export function isLevelOn(levels: ReadonlyArray<Level> | null, level: Level): boolean {
  return levels === null || levels.includes(level);
}
