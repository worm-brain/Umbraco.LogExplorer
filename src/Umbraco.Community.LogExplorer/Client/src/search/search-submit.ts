import type { ParseResult } from "../api/index.js";
import { appendChips, toViewLevels } from "../query/chips.js";
import type { LogExplorerViewState } from "../query/view-state.js";
import { describeError } from "../shared/describe-error.js";

/** `ParseFallback.code` for an open quote (`SimpleSyntaxParser.UnbalancedQuoteCode`). */
export const UNBALANCED_QUOTE = "unbalanced_quote";

/**
 * The `POST /parse` call the submitter depends on, in hey-api's non-throwing result shape. It may
 * reject when `signal` aborts or the network fails.
 */
export type ParseFn = (
  input: string,
  signal: AbortSignal,
) => Promise<{ data?: ParseResult; error?: unknown; response?: Response }>;

/** The part of `LogExplorerQueryContext` the submitter writes to. */
export interface SubmitTarget {
  getState(): LogExplorerViewState;
  update(partial: Partial<LogExplorerViewState>): void;
}

/**
 * What became of one submission.
 *
 * - `applied`: the chips and levels are in the view state; the input can be cleared. `fallback`
 *   carries the server's reason (code and English message) when the input was searched as text.
 * - `error`: the request failed; `message` is safe to show as text. The input should be kept.
 * - `superseded`: a newer submission (or {@link SearchSubmitter.abort}) replaced this one.
 * - `empty`: the input was blank; nothing was sent.
 */
export type SubmitOutcome =
  | { status: "applied"; fallback?: { code: string; message: string } }
  | { status: "error"; message: string }
  | { status: "superseded" }
  | { status: "empty" };

/**
 * Merges a parse result into the view state (BRIEF §6.2, ADR 0004, ADR 0005): the chips are
 * appended without duplicates, and a returned level set replaces the level toggles. Without a
 * level set the toggles stay as they are.
 *
 * @param state - The current view state.
 * @param result - The `POST /parse` response.
 * @returns Only the fields that change; empty when nothing does.
 */
export function mergeParseResult(state: LogExplorerViewState, result: ParseResult): Partial<LogExplorerViewState> {
  const partial: Partial<LogExplorerViewState> = {};
  const chips = appendChips(state.chips, result.chips);
  if (chips !== state.chips) partial.chips = chips;
  if (result.levels) partial.levels = toViewLevels(result.levels);
  return partial;
}

/**
 * Sends search box input to `POST /parse` on Enter and applies the answer to the view state in
 * one update, so one Enter is one history entry.
 *
 * Only one parse is ever in flight: a new submission aborts the previous one, and an answer to a
 * superseded request is dropped even if it arrives.
 */
export class SearchSubmitter {
  #controller?: AbortController;

  /**
   * @param parse - Performs the request.
   * @param target - Receives the chips and levels.
   */
  constructor(
    private readonly parse: ParseFn,
    private readonly target: SubmitTarget,
  ) {}

  /**
   * Parses and applies one input.
   *
   * @param input - The search box text.
   * @returns What happened; never rejects.
   */
  async submit(input: string): Promise<SubmitOutcome> {
    this.#controller?.abort();
    if (input.trim().length === 0) {
      this.#controller = undefined;
      return { status: "empty" };
    }

    const controller = new AbortController();
    this.#controller = controller;
    try {
      const result = await this.parse(input, controller.signal);
      if (controller !== this.#controller) return { status: "superseded" };
      this.#controller = undefined;
      if (result.error !== undefined || !result.data) {
        return { status: "error", message: describeError(result.error, result.response) };
      }

      const partial = mergeParseResult(this.target.getState(), result.data);
      if (Object.keys(partial).length > 0) this.target.update(partial);
      return result.data.fallback ? { status: "applied", fallback: result.data.fallback } : { status: "applied" };
    } catch (error) {
      // An aborted request rejects here; it was superseded, so there is nothing to report.
      if (controller !== this.#controller) return { status: "superseded" };
      this.#controller = undefined;
      return { status: "error", message: error instanceof Error ? error.message : String(error) };
    }
  }

  /** Aborts the parse in flight, if any; its submission resolves as `superseded`. */
  abort(): void {
    this.#controller?.abort();
    this.#controller = undefined;
  }
}
