import type { SourceResponseModel } from "../api/index.js";

/**
 * Picks the source the explorer queries: the alias the URL asks for (`src`) when the user can see
 * it, else the configured `DefaultSource` when visible, else the first visible source.
 *
 * Aliases match ignoring case ({@link findSource}). An alias that is not visible (unknown,
 * removed from configuration, or outside the user's `AllowedUserGroups`) falls through silently,
 * so an old or shared link still opens on something the user may read.
 *
 * @param sources - The visible sources in server order.
 * @param requested - The `src` alias from the view state, if any.
 * @param defaultSource - The server's `DefaultSource`, if known.
 * @returns The active source, or `undefined` when no source is visible.
 */
export function resolveActiveSource(
  sources: ReadonlyArray<SourceResponseModel>,
  requested: string | undefined,
  defaultSource: string | undefined,
): SourceResponseModel | undefined {
  return findSource(sources, requested) ?? findSource(sources, defaultSource) ?? sources[0];
}

/**
 * Finds a source by alias, ignoring case as the server's registry does.
 *
 * @returns The source, or `undefined` when the alias is missing or not among `sources`.
 */
export function findSource(
  sources: ReadonlyArray<SourceResponseModel>,
  alias: string | undefined,
): SourceResponseModel | undefined {
  if (alias === undefined) return undefined;
  const wanted = alias.toLowerCase();
  return sources.find((source) => source.alias.toLowerCase() === wanted);
}
