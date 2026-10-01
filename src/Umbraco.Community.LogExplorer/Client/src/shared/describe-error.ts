/**
 * Builds a user-facing message from a failed request, preferring the ProblemDetails `detail` or
 * `title` the package API returns (BRIEF §11.1) over the bare HTTP status. The result is shown
 * as text, never as HTML.
 *
 * @param error - The parsed error body, if any (hey-api's `error`).
 * @param response - The raw response, if one arrived; absent on network failures.
 * @returns A short message.
 */
export function describeError(error: unknown, response: Response | undefined): string {
  if (typeof error === "object" && error !== null) {
    const problem = error as { detail?: unknown; title?: unknown };
    if (typeof problem.detail === "string" && problem.detail) return problem.detail;
    if (typeof problem.title === "string" && problem.title) return problem.title;
  }
  if (response)
    return `The server responded ${response.status}${response.statusText ? ` ${response.statusText}` : ""}.`;
  return "The request failed.";
}
