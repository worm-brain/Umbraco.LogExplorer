import type { LogRecord } from "../api/index.js";
import type { FilterNode } from "../query/filter-node.js";

/** BRIEF §13's `CorrelationFields` default, used until (or when) `GET /settings` does not answer. */
export const DEFAULT_CORRELATION_FIELDS: ReadonlyArray<string> = ["@traceId", "RequestId", "HttpRequestId"];

/** What Same request filters on for one entry. */
export interface SameRequestTarget {
  /** The correlation field that had a value, exactly as configured. */
  field: string;
  /** Its value on the entry, for the notification. */
  value: string | number | boolean;
  /** The include chip `field` equals `value`, the shape `POST /parse` returns for `field:value`. */
  chip: FilterNode;
}

/**
 * Picks the Same request filter for an entry (BRIEF §6.8, §10.1): the first of the configured
 * correlation fields with a non-empty string, number or boolean value on the entry.
 *
 * @param record - The entry.
 * @param correlationFields - `CorrelationFields`, in priority order.
 * @returns The target, or `undefined` when no field has a value, which disables the action.
 */
export function sameRequestTarget(
  record: LogRecord,
  correlationFields: ReadonlyArray<string>,
): SameRequestTarget | undefined {
  for (const field of correlationFields) {
    const value = fieldValue(record, field);
    if (value === undefined) continue;
    return { field, value, chip: { kind: "condition", field, op: "equals", value } };
  }
  return undefined;
}

/**
 * Reads one field from an entry the way Core's `LogFields` does for the cases a correlation id
 * can be: the portable `@` fields map to record members, `@resource.<path>` reads the resource
 * attributes, anything else is an attribute path. A key is matched whole first (`host.name`),
 * then split on dots into nested objects; keys match exactly, then ignoring case. Arrays (`[]`)
 * are not followed: a correlation id is one value.
 *
 * @param record - The entry.
 * @param field - A portable field or attribute path.
 * @returns The value when it is a non-empty string, a finite number or a boolean; otherwise
 *   `undefined` (absent, null, empty, object or array).
 */
export function fieldValue(record: LogRecord, field: string): string | number | boolean | undefined {
  let value: unknown;
  if (field.startsWith("@")) {
    const lower = field.toLowerCase();
    if (lower.startsWith("@resource.")) {
      value = readPath(record.resource, field.slice("@resource.".length));
    } else {
      // An unknown `@` name is an attribute, as in LogFields.
      const read = PORTABLE[lower];
      value = read ? read(record) : readPath(record.attributes, field);
    }
  } else {
    value = readPath(record.attributes, field);
  }

  if (typeof value === "string") return value.length > 0 ? value : undefined;
  if (typeof value === "number") return Number.isFinite(value) ? value : undefined;
  return typeof value === "boolean" ? value : undefined;
}

/** The portable fields a correlation id can live in, keyed by lower-case name (BRIEF §9.1). */
const PORTABLE: Readonly<Record<string, (record: LogRecord) => unknown>> = {
  "@traceid": (record) => record.traceId,
  "@spanid": (record) => record.spanId,
  "@scope": (record) => record.scope,
  "@template": (record) => record.messageTemplate,
  "@body": (record) => record.body,
};

function readPath(root: Readonly<Record<string, unknown>>, path: string): unknown {
  const whole = getKey(root, path);
  if (whole !== undefined) return whole;

  let current: unknown = root;
  for (const segment of path.split(".")) {
    if (typeof current !== "object" || current === null || Array.isArray(current)) return undefined;
    current = getKey(current as Record<string, unknown>, segment);
  }
  return current;
}

function getKey(object: Readonly<Record<string, unknown>>, key: string): unknown {
  if (Object.hasOwn(object, key)) return object[key];
  const lower = key.toLowerCase();
  const match = Object.keys(object).find((candidate) => candidate.toLowerCase() === lower);
  return match === undefined ? undefined : object[match];
}
