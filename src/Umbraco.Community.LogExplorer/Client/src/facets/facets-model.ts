import type { FacetsRequest, FieldInfo } from "../api/index.js";
import { sameChip } from "../query/chips.js";
import type { FilterNode } from "../query/filter-node.js";
import { toLogQuery } from "../query/log-query.js";
import type { LogExplorerViewState } from "../query/view-state.js";
import { shortenSource } from "../results/row-format.js";

/**
 * BRIEF §13's default `PinnedFacets`, used when `GET /settings` fails. Keep in step with
 * `LogExplorerOptionsDefaults.PinnedFacets` on the server.
 */
export const DEFAULT_PINNED_FACETS: ReadonlyArray<string> = [
  "SourceContext",
  "RequestPath",
  "StatusCode",
  "MachineName",
  "@exception.type",
];

/**
 * The pinned fields to show.
 *
 * @param settings - `GET /settings`, or `undefined` when it failed.
 * @returns The configured list, or {@link DEFAULT_PINNED_FACETS} when settings are missing or
 *   (from an older server) carry no list.
 */
export function pinnedFacetsFrom(settings: { pinnedFacets?: ReadonlyArray<string> } | undefined): Array<string> {
  return [...(settings?.pinnedFacets?.length ? settings.pinnedFacets : DEFAULT_PINNED_FACETS)];
}

/** Values the panel shows per field (UI brief §4.8); the API allows up to 10 (BRIEF §6.6). */
export const VISIBLE_VALUES = 5;

/**
 * The most discovered fields the panel requests facets for, after the pinned ones. Keeps one
 * `/facets` call bounded (the server accepts 50 fields) and the panel short enough to scan; the
 * highest-presence fields are the ones worth clicking.
 */
export const MAX_DISCOVERED_FIELDS = 10;

/**
 * Portable fields that are never offered as discovered facets: the timestamp and the trace and
 * span ids are unique per entry or request, the body and exception message are free text, the
 * template has its own Patterns view, and severity is the histogram's level toggles (BRIEF §6.6).
 */
const NOT_FACETABLE = new Set([
  "@timestamp",
  "@severity",
  "@body",
  "@template",
  "@traceId",
  "@spanId",
  "@exception.message",
]);

/**
 * Portable fields and the attribute names that carry the same value on Serilog sources (BRIEF
 * §9.1). Pinning either one keeps the other out of the discovered fields, so the panel does not
 * show the same values twice under one short name ("Source", "Exception").
 */
const SAME_VALUES: ReadonlyArray<ReadonlyArray<string>> = [
  ["SourceContext", "@scope"],
  ["ExceptionType", "@exception.type"],
];

/** Fields whose values are dotted type names, shown by their last segment (UI brief §4.8). */
const NAMESPACED_FIELDS = new Set(["SourceContext", "@scope", "ExceptionType", "@exception.type"]);

/**
 * Picks the discovered fields to facet: present in at least one entry, not pinned (compared
 * case-insensitively, as the server resolves fields, and counting a pinned field's
 * same-value twin as pinned), not an object (its leaves are listed
 * separately) and not one of the portable fields that make useless facets. Highest presence
 * first, ties by path, capped at `max`.
 *
 * @param fields - `GET /fields` for the source and range.
 * @param pinned - The pinned fields, which the panel shows anyway.
 * @param max - The cap; defaults to {@link MAX_DISCOVERED_FIELDS}.
 * @returns Field paths in display order.
 */
export function selectDiscoveredFields(
  fields: ReadonlyArray<FieldInfo>,
  pinned: ReadonlyArray<string>,
  max = MAX_DISCOVERED_FIELDS,
): Array<string> {
  const pinnedKeys = new Set(pinned.map((field) => field.toLowerCase()));
  for (const group of SAME_VALUES) {
    if (group.some((field) => pinnedKeys.has(field.toLowerCase()))) {
      group.forEach((field) => pinnedKeys.add(field.toLowerCase()));
    }
  }
  return fields
    .filter(
      (field) =>
        field.presenceRatio > 0 &&
        field.kind !== "object" &&
        !NOT_FACETABLE.has(field.path) &&
        !pinnedKeys.has(field.path.toLowerCase()),
    )
    .sort((a, b) => b.presenceRatio - a.presenceRatio || a.path.localeCompare(b.path))
    .slice(0, max)
    .map((field) => field.path);
}

/**
 * Builds the `POST /facets` body: the current query (chips, levels, zoom or range, native query),
 * with the sort fixed so toggling it does not refetch, and the pinned fields before the
 * discovered ones.
 *
 * @param state - The view state.
 * @param pinned - Pinned fields, in configured order.
 * @param discovered - Discovered fields from {@link selectDiscoveredFields}.
 * @returns The request body.
 */
export function buildFacetsRequest(
  state: LogExplorerViewState,
  pinned: ReadonlyArray<string>,
  discovered: ReadonlyArray<string>,
): FacetsRequest {
  return {
    query: toLogQuery({ ...state, sort: "desc" }, 1),
    fields: [...pinned, ...discovered],
    top: VISIBLE_VALUES,
  };
}

/**
 * The range `GET /fields` takes as query parameters: the zoom when there is one, else the picker's.
 *
 * @param state - The view state.
 * @returns `relative`, or `from` and `to`.
 */
export function fieldsRange(state: LogExplorerViewState): { relative: string } | { from: string; to: string } {
  const range = state.zoom ?? state.range;
  return "relative" in range ? { relative: range.relative } : { from: range.from, to: range.to };
}

/**
 * Width of a value's share bar, relative to the field's top value so the most common value fills
 * the row (UI brief §4.8).
 *
 * @param count - This value's count.
 * @param topCount - The field's highest count.
 * @returns A fraction from 0 to 1; 0 when `topCount` is not positive.
 */
export function shareRatio(count: number, topCount: number): number {
  if (topCount <= 0) return 0;
  return Math.min(1, Math.max(0, count / topCount));
}

/**
 * A facet value as plain text, for the tooltip and accessible name: strings as they are, other
 * JSON (numbers, booleans, arrays, objects) as JSON.
 *
 * @param value - A `FacetValue.value`.
 * @returns The text.
 */
export function valueText(value: unknown): string {
  if (typeof value === "string") return value;
  return value === undefined ? "" : JSON.stringify(value);
}

/**
 * The text a value row shows. Source context and exception type values show their last namespace
 * segment (`Umbraco.Cms.Web.Common.ApplicationBuilder` shows `ApplicationBuilder`); everything
 * else shows {@link valueText}.
 *
 * @param field - The facet's field path.
 * @param value - The value.
 * @returns The label.
 */
export function valueLabel(field: string, value: unknown): string {
  const text = valueText(value);
  return NAMESPACED_FIELDS.has(field) && typeof value === "string" ? shortenSource(text) : text;
}

/**
 * The chip a click on a value adds: equals that value. Two of these on one field OR together
 * (`chipsToFilter`), which is how a second click widens the filter.
 *
 * @param field - The facet's field path.
 * @param value - The value, kept in its JSON type so numbers compare as numbers.
 * @returns A `condition` chip.
 */
export function includeChip(field: string, value: unknown): FilterNode {
  return { kind: "condition", field, op: "equals", value };
}

/**
 * The chip the minus button adds: a `not` around {@link includeChip}, the shape `POST /parse`
 * gives `-field:value`.
 *
 * @param field - The facet's field path.
 * @param value - The value.
 * @returns A `not` chip.
 */
export function excludeChip(field: string, value: unknown): FilterNode {
  return { kind: "not", child: includeChip(field, value) };
}

/**
 * Whether a value is already an active include chip, so the row shows as selected.
 *
 * @param chips - The view state's chips.
 * @param field - The facet's field path.
 * @param value - The value.
 * @returns `true` when a chip equal to {@link includeChip} is present.
 */
export function isValueSelected(chips: ReadonlyArray<FilterNode>, field: string, value: unknown): boolean {
  const chip = includeChip(field, value);
  return chips.some((existing) => sameChip(existing, chip));
}

/**
 * Whether a field matches the panel's filter box, by path or by the label the panel shows.
 *
 * @param field - The field path.
 * @param label - The label shown for it.
 * @param filter - The filter box text; blank matches everything.
 * @returns `true` when the field should be listed.
 */
export function matchesFieldFilter(field: string, label: string, filter: string): boolean {
  const needle = filter.trim().toLowerCase();
  if (!needle) return true;
  return field.toLowerCase().includes(needle) || label.toLowerCase().includes(needle);
}
