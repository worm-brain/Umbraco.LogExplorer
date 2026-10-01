import { SourcesService, type SourceResponseModel } from "../api/index.js";

/**
 * The state of the visible-sources fetch, as the workspace header renders it.
 *
 * - `loading`: the request is in flight.
 * - `loaded`: at least one source is visible to the signed-in user.
 * - `empty`: the request succeeded but no source is configured or visible.
 * - `error`: the request failed; `message` is safe to show to the user as text.
 */
export type SourcesState =
  | { status: "loading" }
  | { status: "loaded"; sources: Array<SourceResponseModel> }
  | { status: "empty" }
  | { status: "error"; message: string };

/**
 * The shape of `SourcesService.getSources()` that the loader depends on: hey-api's non-throwing
 * result, where a failed request sets `error` (the parsed body, usually ProblemDetails) and
 * `response` (the raw response, absent on network failures).
 */
export type FetchSources = () => Promise<{
  data?: Array<SourceResponseModel>;
  error?: unknown;
  response?: Response;
}>;

/**
 * Fetches the sources the signed-in user may see (`GET /sources`) and reduces the outcome to a
 * {@link SourcesState}. It never throws: transport failures and non-success responses both become
 * an `error` state so the caller only has to render.
 *
 * @param fetchSources - The request to make. Defaults to the generated client's `getSources`;
 *   tests pass a stub.
 * @returns `loaded` with the sources in server order, `empty` when there are none, or `error`
 *   with a readable message.
 */
export async function loadSources(
  fetchSources: FetchSources = () => SourcesService.getSources(),
): Promise<SourcesState> {
  try {
    const result = await fetchSources();
    if (result.error !== undefined || !result.data) {
      return { status: "error", message: describeError(result.error, result.response) };
    }
    return result.data.length > 0 ? { status: "loaded", sources: result.data } : { status: "empty" };
  } catch (error) {
    // fetch rejects on network failures and aborted requests rather than returning a result.
    return { status: "error", message: error instanceof Error ? error.message : String(error) };
  }
}

/**
 * Builds a user-facing message from a failed response, preferring the ProblemDetails `detail`
 * or `title` the package API returns (BRIEF §11.1) over the bare HTTP status.
 *
 * @param error - The parsed error body, if any.
 * @param response - The raw response, if one arrived.
 * @returns A short message for the header's error state.
 */
function describeError(error: unknown, response: Response | undefined): string {
  if (typeof error === "object" && error !== null) {
    const problem = error as { detail?: unknown; title?: unknown };
    if (typeof problem.detail === "string" && problem.detail) return problem.detail;
    if (typeof problem.title === "string" && problem.title) return problem.title;
  }
  if (response)
    return `The server responded ${response.status}${response.statusText ? ` ${response.statusText}` : ""}.`;
  return "The request failed.";
}
