import { LEVELS, type Level } from "../query/view-state.js";

/**
 * Formats a record timestamp as the results list shows it: `hh:mm:ss.fff` in the browser's time
 * zone, 24-hour, milliseconds always three digits (UI brief §4.9). The date is deliberately left
 * out; the range summary carries it.
 *
 * @param timestamp - An ISO 8601 instant, as the API sends `LogRecord.timestamp`.
 * @returns The local time, or the input unchanged when it is not a parsable date.
 */
export function formatRowTime(timestamp: string): string {
  const date = new Date(timestamp);
  if (Number.isNaN(date.getTime())) return timestamp;
  const pad = (value: number, length = 2) => String(value).padStart(length, "0");
  return `${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}.${pad(date.getMilliseconds(), 3)}`;
}

/**
 * Maps an OpenTelemetry severity number to its short level name (BRIEF §9.2): 1-4 trace, 5-8
 * debug, 9-12 info, 13-16 warn, 17-20 error, 21-24 fatal.
 *
 * @param severityNumber - `LogRecord.severityNumber`; 0 means unspecified.
 * @returns The level, or `undefined` for 0 and anything outside 1-24 (shown as "—").
 */
export function levelOf(severityNumber: number): Level | undefined {
  if (!Number.isInteger(severityNumber) || severityNumber < 1 || severityNumber > 24) return undefined;
  return LEVELS[Math.floor((severityNumber - 1) / 4)];
}

/**
 * The two numbers of the footer's "Showing {n} of {total}" (BRIEF §6.7), formatted for the
 * locale. The total gets a "≥" when the source only knows a lower bound, or does not count at
 * all and more pages remain.
 *
 * @param shown - Rows loaded so far.
 * @param total - `LogPage.totalCount`; `null` when the source did not count.
 * @param totalIsLowerBound - `LogPage.totalIsLowerBound`.
 * @param hasMore - Whether a next page exists.
 * @param locale - BCP 47 tag for number formatting, for example the backoffice language.
 * @returns The formatted pair.
 */
export function formatShowing(
  shown: number,
  total: number | null,
  totalIsLowerBound: boolean,
  hasMore: boolean,
  locale?: string,
): { shown: string; total: string } {
  const format = (value: number) => value.toLocaleString(locale);
  if (total === null) return { shown: format(shown), total: hasMore ? `≥ ${format(shown)}` : format(shown) };
  return { shown: format(shown), total: totalIsLowerBound ? `≥ ${format(total)}` : format(total) };
}

/**
 * Shortens a logger category for the Source column: the last dot-separated segment, so
 * `Umbraco.Cms.Core.Sync.ServerMessenger` becomes `ServerMessenger`. Generic arity and nested
 * type markers stay with the segment (`Foo.Bar\`1` gives ``Bar`1``).
 *
 * @param scope - `LogRecord.scope` (SourceContext).
 * @returns The short name; an empty string when there is no scope.
 */
export function shortenSource(scope: string | null | undefined): string {
  if (!scope) return "";
  const trimmed = scope.replace(/\.+$/, "");
  const lastDot = trimmed.lastIndexOf(".");
  return lastDot === -1 ? trimmed : trimmed.slice(lastDot + 1);
}
