import { isFilterNode, type FilterNode } from "./filter-node.js";

/** The relative presets the time range picker offers, matching `RelativeRange.Supported` in Core. */
export const RELATIVE_RANGES = ["15m", "1h", "4h", "24h", "7d", "30d"] as const;

/** One of {@link RELATIVE_RANGES}. */
export type RelativeRange = (typeof RELATIVE_RANGES)[number];

/** The six OTel short level names, lower case as on the wire (ADR 0008), in severity order. */
export const LEVELS = ["trace", "debug", "info", "warn", "error", "fatal"] as const;

/** One of {@link LEVELS}. */
export type Level = (typeof LEVELS)[number];

/**
 * A time range as the URL and saved views keep it (BRIEF §6.5): relative ranges stay relative so a
 * shared link always means "the last hour from now"; absolute ranges are ISO 8601 UTC instants.
 */
export type ViewTimeRange = { relative: RelativeRange } | AbsoluteRange;

/** An absolute time range as ISO 8601 UTC instants, `from` before `to`. */
export interface AbsoluteRange {
  from: string;
  to: string;
}

/**
 * Everything that defines what the explorer shows (BRIEF §6.11), apart from the active tab, which
 * is the workspace view's route path and so is already in the URL.
 */
export interface LogExplorerViewState {
  /** Source alias; `undefined` means the configured `DefaultSource`. `LogExplorerQueryContext.activeSource` resolves it. */
  source: string | undefined;
  range: ViewTimeRange;
  /** Filter chips; chips on the same field OR together, different fields AND (BRIEF §6.3). */
  chips: Array<FilterNode>;
  /** The level set (ADR 0004); `null` means no level filter. Kept in {@link LEVELS} order. */
  levels: Array<Level> | null;
  /**
   * Native-mode query in the source's own language. `undefined` is simple mode; any string,
   * including `""` (native mode with nothing typed yet), is native mode (BRIEF §6.2).
   */
  native: string | undefined;
  /** `desc` is newest first. */
  sort: "desc" | "asc";
  /**
   * The histogram's time zoom (UI brief §4.7), shown as the time chip. While set, queries run over
   * it instead of {@link range}; clearing it returns to {@link range} unchanged, so zooming into a
   * relative "last hour" and back keeps the range relative. `undefined` means not zoomed.
   */
  zoom?: AbsoluteRange;
  /** Whether the show-query panel is open (UI brief §4.6). Absent means closed. */
  showQuery?: boolean;
}

/** Fallback when the server's `DefaultTimeRange` is unknown or not a supported preset (BRIEF §6.5). */
export const FALLBACK_RELATIVE_RANGE: RelativeRange = "1h";

/**
 * The query-string keys the view state owns. Other keys in the URL are left untouched, so the
 * backoffice or other extensions can use the query string too.
 */
export const VIEW_STATE_KEYS = [
  "src",
  "range",
  "from",
  "to",
  "levels",
  "f",
  "native",
  "sort",
  "zf",
  "zt",
  "sq",
] as const;

/**
 * Builds the default view state.
 *
 * @param defaultTimeRange - The server's `DefaultTimeRange`; anything that is not a supported
 *   preset falls back to {@link FALLBACK_RELATIVE_RANGE}.
 * @returns A fresh default state.
 */
export function createDefaultViewState(defaultTimeRange?: string): LogExplorerViewState {
  return {
    source: undefined,
    range: { relative: isRelativeRange(defaultTimeRange) ? defaultTimeRange : FALLBACK_RELATIVE_RANGE },
    chips: [],
    levels: null,
    native: undefined,
    sort: "desc",
  };
}

/**
 * Serialises the view state into query-string parameters, leaving out every field that equals
 * its default so the plain workspace URL means "the defaults".
 *
 * Keys: `src`; `range` (relative) or `from` + `to` (absolute ISO); `levels` (comma list, empty
 * value when every level is hidden); `f` (base64url JSON of the chips); `native`; `sort=asc`;
 * `zf` + `zt` (ISO) for the time zoom; `sq=1` when the show-query panel is open. `native` is
 * written even when empty, because an empty native query still means native mode.
 *
 * @param state - The state to encode.
 * @param defaults - The defaults to compare against.
 * @returns The parameters, in a stable order so equal states give equal strings.
 */
export function encodeViewState(state: LogExplorerViewState, defaults: LogExplorerViewState): URLSearchParams {
  const params = new URLSearchParams();
  if (state.source !== undefined && state.source !== defaults.source) params.set("src", state.source);

  if (!sameRange(state.range, defaults.range)) {
    if ("relative" in state.range) {
      params.set("range", state.range.relative);
    } else {
      params.set("from", state.range.from);
      params.set("to", state.range.to);
    }
  }

  const levels = normaliseLevels(state.levels);
  if (levels !== null) params.set("levels", levels.join(","));

  if (state.chips.length > 0) params.set("f", toBase64Url(JSON.stringify(state.chips)));
  if (state.native !== undefined) params.set("native", state.native);
  if (state.sort !== defaults.sort) params.set("sort", state.sort);
  if (state.zoom) {
    params.set("zf", state.zoom.from);
    params.set("zt", state.zoom.to);
  }
  if (state.showQuery) params.set("sq", "1");
  return params;
}

/**
 * Reads the view state from query-string parameters. Never throws: each field that is missing or
 * invalid falls back to its default on its own, so one corrupted parameter in a shared link does
 * not discard the rest.
 *
 * @param params - The current query string's parameters.
 * @param defaults - The defaults for missing or invalid fields.
 * @returns The decoded state.
 */
export function decodeViewState(params: URLSearchParams, defaults: LogExplorerViewState): LogExplorerViewState {
  return {
    source: params.get("src") || defaults.source,
    range: decodeRange(params) ?? defaults.range,
    chips: decodeChips(params.get("f")) ?? defaults.chips,
    levels: decodeLevels(params.get("levels"), defaults.levels),
    native: params.get("native") ?? defaults.native,
    sort: params.get("sort") === "asc" ? "asc" : params.get("sort") === "desc" ? "desc" : defaults.sort,
    zoom: decodeAbsolute(params.get("zf"), params.get("zt")),
    showQuery: params.get("sq") === "1" ? true : undefined,
  };
}

/**
 * Whether the parameters carry any view-state key, as opposed to a URL that means "the defaults"
 * only because nothing was written yet.
 *
 * @param params - The query string's parameters.
 * @returns `true` when at least one of {@link VIEW_STATE_KEYS} is present.
 */
export function hasViewState(params: URLSearchParams): boolean {
  return VIEW_STATE_KEYS.some((key) => params.has(key));
}

/**
 * Replaces the view-state keys in a query string and keeps every other key.
 *
 * @param search - The current query string, with or without the leading `?`.
 * @param viewState - The encoded view state.
 * @returns The merged query string without the leading `?` (empty when there is nothing to keep).
 */
export function mergeViewStateIntoSearch(search: string, viewState: URLSearchParams): string {
  const merged = new URLSearchParams(search);
  for (const key of VIEW_STATE_KEYS) merged.delete(key);
  for (const [key, value] of viewState) merged.set(key, value);
  return merged.toString();
}

/**
 * Narrows a string to a supported relative preset.
 *
 * @param value - Anything.
 * @returns `true` when `value` is one of {@link RELATIVE_RANGES}.
 */
export function isRelativeRange(value: unknown): value is RelativeRange {
  return (RELATIVE_RANGES as ReadonlyArray<unknown>).includes(value);
}

function sameRange(a: ViewTimeRange, b: ViewTimeRange): boolean {
  if ("relative" in a) return "relative" in b && a.relative === b.relative;
  return "from" in b && a.from === b.from && a.to === b.to;
}

/**
 * Absolute wins over relative when both are present and valid, because only a custom range
 * writes `from`/`to` (the time zoom has its own keys).
 */
function decodeRange(params: URLSearchParams): ViewTimeRange | undefined {
  const absolute = decodeAbsolute(params.get("from"), params.get("to"));
  if (absolute) return absolute;

  const relative = params.get("range");
  return isRelativeRange(relative) ? { relative } : undefined;
}

/** An absolute range needs both ends, parsable, and `from < to`; anything else is no range. */
function decodeAbsolute(fromValue: string | null, toValue: string | null): AbsoluteRange | undefined {
  const from = parseInstant(fromValue);
  const to = parseInstant(toValue);
  return from && to && from < to ? { from: from.toISOString(), to: to.toISOString() } : undefined;
}

function parseInstant(value: string | null): Date | undefined {
  if (!value) return undefined;
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? undefined : date;
}

function decodeChips(value: string | null): Array<FilterNode> | undefined {
  if (!value) return undefined;
  try {
    const parsed: unknown = JSON.parse(fromBase64Url(value));
    return Array.isArray(parsed) && parsed.every(isFilterNode) ? parsed : undefined;
  } catch {
    // Not base64url, not UTF-8 or not JSON: treat the whole parameter as invalid.
    return undefined;
  }
}

/** An empty value is a real state (every level hidden); an unknown name invalidates the field. */
function decodeLevels(value: string | null, fallback: Array<Level> | null): Array<Level> | null {
  if (value === null) return fallback;
  const names = value === "" ? [] : value.split(",").map((name) => name.trim().toLowerCase());
  if (!names.every((name): name is Level => (LEVELS as ReadonlyArray<string>).includes(name))) return fallback;
  return normaliseLevels(names);
}

/**
 * Puts a level set into the form the view state keeps (ADR 0004).
 *
 * @param levels - Levels in any order, possibly repeated; `null` for no filter.
 * @returns The levels in {@link LEVELS} order without duplicates, or `null` when all six are
 *   present, because all six is the same as no filter.
 */
export function normaliseLevels(levels: ReadonlyArray<Level> | null): Array<Level> | null {
  if (levels === null) return null;
  const set = new Set(levels);
  return set.size === LEVELS.length ? null : LEVELS.filter((level) => set.has(level));
}

function toBase64Url(text: string): string {
  let binary = "";
  for (const byte of new TextEncoder().encode(text)) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

/** @throws When the input is not valid base64url or not valid UTF-8. */
function fromBase64Url(value: string): string {
  const binary = atob(value.replace(/-/g, "+").replace(/_/g, "/"));
  const bytes = Uint8Array.from(binary, (char) => char.charCodeAt(0));
  return new TextDecoder("utf-8", { fatal: true }).decode(bytes);
}
