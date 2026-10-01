import type { RelativeRange, ViewTimeRange } from "../query/view-state.js";

/** The localization key for each preset's menu label (UI brief §4.2). */
export const PRESET_LABEL_KEYS: Record<RelativeRange, string> = {
  "15m": "logExplorer_timeRangeLast15m",
  "1h": "logExplorer_timeRangeLast1h",
  "4h": "logExplorer_timeRangeLast4h",
  "24h": "logExplorer_timeRangeLast24h",
  "7d": "logExplorer_timeRangeLast7d",
  "30d": "logExplorer_timeRangeLast30d",
};

/**
 * Converts an instant to the value a `datetime-local` input expects, in the browser's time zone.
 *
 * @param iso - An ISO 8601 instant.
 * @returns `YYYY-MM-DDTHH:mm` in local time, or an empty string when `iso` does not parse.
 */
export function toLocalInputValue(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "";
  const pad = (n: number) => String(n).padStart(2, "0");
  return (
    `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}` +
    `T${pad(date.getHours())}:${pad(date.getMinutes())}`
  );
}

/**
 * Converts a `datetime-local` value, which has no zone and so means local time, to a UTC instant.
 *
 * @param value - `YYYY-MM-DDTHH:mm` (seconds optional).
 * @returns The ISO 8601 UTC instant, or `undefined` when the value is empty or invalid.
 */
export function fromLocalInputValue(value: string): string | undefined {
  // A date-time string without an offset is parsed as local time by the Date constructor.
  if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2})?$/.test(value)) return undefined;
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? undefined : date.toISOString();
}

/**
 * Builds an absolute range from the custom-range inputs.
 *
 * @param fromValue - The `datetime-local` value for the start.
 * @param toValue - The `datetime-local` value for the end.
 * @returns The range, or `undefined` unless both parse and the start is before the end.
 */
export function customRangeFromInputs(fromValue: string, toValue: string): ViewTimeRange | undefined {
  const from = fromLocalInputValue(fromValue);
  const to = fromLocalInputValue(toValue);
  return from && to && from < to ? { from, to } : undefined;
}

/**
 * Formats an absolute range for the picker button in the user's locale and time zone.
 *
 * @param range - The absolute range.
 * @param locale - A BCP 47 locale, usually the backoffice user's.
 * @returns For example `02/09/2026, 00:40 – 02/09/2026, 00:45`.
 */
export function formatAbsoluteRange(range: { from: string; to: string }, locale?: string): string {
  const format = new Intl.DateTimeFormat(locale, { dateStyle: "short", timeStyle: "short" });
  return format.formatRange(new Date(range.from), new Date(range.to));
}

/** @returns The browser's IANA time zone, for example `Europe/London`. */
export function localTimeZone(): string {
  return Intl.DateTimeFormat().resolvedOptions().timeZone;
}
