import { encodeViewState, mergeViewStateIntoSearch, type LogExplorerViewState } from "../query/view-state.js";

/** The parts of `window.location` a share link is built from. */
export type ShareLocation = Pick<Location, "origin" | "pathname" | "search" | "hash">;

/**
 * Builds the link "Copy link to this view" copies (BRIEF §6.11, UI brief §4.5): the current
 * absolute URL, whose path carries the active tab, with the view-state keys rewritten from
 * `state`. Encoding the state rather than copying `location.href` as it stands keeps the link
 * exact even if a URL write has not caught up yet; keys that are not view state are kept.
 *
 * Fields equal to `defaults` are left out, as in the address bar, so the link means "the
 * server's defaults" for them; a link is opened on the same server, which has the same defaults.
 *
 * @param location - The current location (`window.location`).
 * @param state - The view state on screen.
 * @param defaults - The defaults in force, as the URL is encoded against them.
 * @returns The absolute URL.
 */
export function buildShareUrl(
  location: ShareLocation,
  state: LogExplorerViewState,
  defaults: LogExplorerViewState,
): string {
  const search = mergeViewStateIntoSearch(location.search, encodeViewState(state, defaults));
  return `${location.origin}${location.pathname}${search ? `?${search}` : ""}${location.hash}`;
}
