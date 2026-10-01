import type { LogRecord } from "../api/index.js";
import type { FilterNode } from "../query/filter-node.js";
import { propertyChip } from "./property-tree.js";

/**
 * Formats a record timestamp for the drawer header: `dd/mm/yyyy hh:mm:ss.fff` in the browser's
 * time zone (UI brief §4.11 section 1), whatever the backoffice language, matching the copy deck.
 *
 * @param timestamp - An ISO 8601 instant, as the API sends `LogRecord.timestamp`.
 * @returns The local date and time, or the input unchanged when it is not a parsable date.
 */
export function formatDetailTime(timestamp: string): string {
  const date = new Date(timestamp);
  if (Number.isNaN(date.getTime())) return timestamp;
  const pad = (value: number, length = 2) => String(value).padStart(length, "0");
  return (
    `${pad(date.getDate())}/${pad(date.getMonth() + 1)}/${date.getFullYear()} ` +
    `${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}.${pad(date.getMilliseconds(), 3)}`
  );
}

/**
 * The machine an entry came from, for the drawer header: the `MachineName` property Umbraco
 * logs, else the `host.name` resource attribute the files provider fills from the file name.
 *
 * @param record - The entry.
 * @returns The machine name, or `undefined` when neither is a non-empty string.
 */
export function machineOf(record: LogRecord): string | undefined {
  const candidates = [record.attributes["MachineName"], record.resource["host.name"]];
  return candidates.find((value): value is string => typeof value === "string" && value.length > 0);
}

/**
 * What Copy as JSON puts on the clipboard: the record exactly as the API served it, indented.
 * Masking (BRIEF §12) happens on the server before the record is sent, so nothing is removed here.
 *
 * @param record - The entry.
 * @returns Pretty-printed JSON.
 */
export function recordJson(record: LogRecord): string {
  return JSON.stringify(record, null, 2);
}

/**
 * The Same pattern chip (UI brief §4.11 section 3): `@template` equals the entry's message
 * template, the same chip the Patterns view's Focus adds.
 *
 * @param record - The entry.
 * @returns The chip, or `undefined` when the entry has no template.
 */
export function samePatternChip(record: LogRecord): FilterNode | undefined {
  return record.messageTemplate
    ? { kind: "condition", field: "@template", op: "equals", value: record.messageTemplate }
    : undefined;
}

/**
 * The include chip for a property value in the drawer's rendered message: `field` equals the
 * typed attribute value behind the highlighted text, not the text itself, so `{StatusCode}`
 * filters on the number 404 and `{RequestPath}` on the string without Serilog's quotes.
 *
 * @param record - The entry.
 * @param field - The template property, as `tokeniseMessage` reports it.
 * @returns The chip, or `undefined` when the attribute is missing or not a string, number or
 *   boolean (see `propertyChip`); the drawer then shows the value highlighted but not clickable.
 */
export function messageValueChip(record: LogRecord, field: string): FilterNode | undefined {
  return propertyChip(field.startsWith("@") ? undefined : field, record.attributes[field], false);
}
