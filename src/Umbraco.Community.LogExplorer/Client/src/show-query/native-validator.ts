import type { ValidationResult } from "../api/index.js";
import { describeError } from "../shared/describe-error.js";

/**
 * The `POST /sources/{alias}/validate` call the validator depends on, in hey-api's non-throwing
 * result shape. It may reject when `signal` aborts or the network fails.
 */
export type ValidateFn = (
  alias: string,
  native: string,
  signal: AbortSignal,
) => Promise<{ data?: ValidationResult; error?: unknown; response?: Response }>;

/**
 * What the search box shows for the native query being typed.
 *
 * - `idle`: nothing checked (empty input, or simple mode).
 * - `pending`: waiting for the debounce or the answer; the previous verdict is not shown.
 * - `valid` / `invalid`: the source's verdict; `invalid` carries its message and, when the
 *   source knows it, the zero-based `position` of the error.
 * - `error`: the check itself failed; the input is not marked invalid for that.
 */
export type NativeValidation =
  | { status: "idle" | "pending" | "valid" }
  | { status: "invalid"; message: string; position?: number }
  | { status: "error"; message: string };

/** Wait after the last keystroke before checking (BRIEF §17: typing is debounced 400 ms). */
export const VALIDATE_DEBOUNCE_MS = 400;

/**
 * Checks native-mode input while the user types: each change restarts a
 * {@link VALIDATE_DEBOUNCE_MS} wait, then `POST /validate` runs; a newer change aborts a check in
 * flight, and an answer to a superseded check is dropped.
 */
export class NativeValidator {
  #controller?: AbortController;
  #timer?: ReturnType<typeof setTimeout>;

  /**
   * @param validate - Performs the request.
   * @param onChange - Called with every new verdict.
   * @param debounceMs - Wait before sending; tests use fake timers instead of changing it.
   */
  constructor(
    private readonly validate: ValidateFn,
    private readonly onChange: (validation: NativeValidation) => void,
    private readonly debounceMs = VALIDATE_DEBOUNCE_MS,
  ) {}

  /**
   * Schedules a check of `native`. Blank input is not sent: it reports `idle` at once.
   *
   * @param alias - The source.
   * @param native - The text in the box.
   */
  check(alias: string, native: string): void {
    this.cancel();
    if (native.trim().length === 0) {
      this.onChange({ status: "idle" });
      return;
    }

    this.onChange({ status: "pending" });
    this.#timer = setTimeout(() => void this.#run(alias, native), this.debounceMs);
  }

  /** Drops any pending or running check without reporting. */
  cancel(): void {
    clearTimeout(this.#timer);
    this.#timer = undefined;
    this.#controller?.abort();
    this.#controller = undefined;
  }

  async #run(alias: string, native: string): Promise<void> {
    const controller = new AbortController();
    this.#controller = controller;
    try {
      const result = await this.validate(alias, native, controller.signal);
      if (controller !== this.#controller) return;
      this.#controller = undefined;
      if (result.error !== undefined || !result.data) {
        this.onChange({ status: "error", message: describeError(result.error, result.response) });
      } else if (result.data.valid) {
        this.onChange({ status: "valid" });
      } else {
        this.onChange({
          status: "invalid",
          message: result.data.error ?? "",
          position: result.data.position ?? undefined,
        });
      }
    } catch (error) {
      // An aborted request rejects here; it was superseded, so there is nothing to report.
      if (controller !== this.#controller) return;
      this.#controller = undefined;
      this.onChange({ status: "error", message: error instanceof Error ? error.message : String(error) });
    }
  }
}
