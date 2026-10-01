import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { ValidationResult } from "../api/index.js";
import { NativeValidator, VALIDATE_DEBOUNCE_MS, type NativeValidation, type ValidateFn } from "./native-validator.js";

/** A validate stub that records each call and answers at once with `answer`. */
function stubValidate(answer: (native: string) => ValidationResult) {
  const calls: Array<{ native: string; signal: AbortSignal }> = [];
  const validate: ValidateFn = async (_, native, signal) => {
    calls.push({ native, signal });
    return { data: answer(native) };
  };
  return { validate, calls };
}

let verdicts: Array<NativeValidation>;

beforeEach(() => {
  vi.useFakeTimers();
  verdicts = [];
});

afterEach(() => {
  vi.useRealTimers();
});

describe("NativeValidator", () => {
  it("waits 400 ms after the last keystroke before checking", async () => {
    const { validate, calls } = stubValidate(() => ({ valid: true }));
    const validator = new NativeValidator(validate, (verdict) => verdicts.push(verdict));

    validator.check("sample", "a");
    await vi.advanceTimersByTimeAsync(VALIDATE_DEBOUNCE_MS - 1);
    validator.check("sample", "ab");
    await vi.advanceTimersByTimeAsync(VALIDATE_DEBOUNCE_MS);

    expect(calls.map((call) => call.native)).toEqual(["ab"]);
  });

  it("reports an invalid query with its message and position", async () => {
    const { validate } = stubValidate(() => ({ valid: false, error: "Unexpected ')'.", position: 4 }));
    const validator = new NativeValidator(validate, (verdict) => verdicts.push(verdict));

    validator.check("sample", "a b )");
    await vi.runAllTimersAsync();

    expect(verdicts.at(-1)).toEqual({ status: "invalid", message: "Unexpected ')'.", position: 4 });
  });

  it("reports a valid query as valid", async () => {
    const { validate } = stubValidate(() => ({ valid: true }));
    const validator = new NativeValidator(validate, (verdict) => verdicts.push(verdict));

    validator.check("sample", "a");
    await vi.runAllTimersAsync();

    expect(verdicts).toEqual([{ status: "pending" }, { status: "valid" }]);
  });

  it("does not send blank input", async () => {
    const { validate, calls } = stubValidate(() => ({ valid: true }));
    const validator = new NativeValidator(validate, (verdict) => verdicts.push(verdict));

    validator.check("sample", "   ");
    await vi.runAllTimersAsync();

    expect([calls.length, verdicts]).toEqual([0, [{ status: "idle" }]]);
  });

  it("aborts a check in flight when the text changes and ignores its answer", async () => {
    let release: ((value: { data: ValidationResult }) => void) | undefined;
    const signals: Array<AbortSignal> = [];
    const validate: ValidateFn = (_, native, signal) => {
      signals.push(signal);
      return native === "old"
        ? new Promise((resolve) => (release = resolve))
        : Promise.resolve({ data: { valid: true } });
    };
    const validator = new NativeValidator(validate, (verdict) => verdicts.push(verdict));
    validator.check("sample", "old");
    await vi.advanceTimersByTimeAsync(VALIDATE_DEBOUNCE_MS);

    validator.check("sample", "new");
    release!({ data: { valid: false, error: "stale", position: 0 } });
    await vi.runAllTimersAsync();

    expect([signals[0]!.aborted, verdicts.at(-1)]).toEqual([true, { status: "valid" }]);
  });

  it("reports a failed check as an error, not as invalid", async () => {
    const validate: ValidateFn = async () => ({ error: { detail: "Native queries are turned off." } });
    const validator = new NativeValidator(validate, (verdict) => verdicts.push(verdict));

    validator.check("sample", "a");
    await vi.runAllTimersAsync();

    expect(verdicts.at(-1)).toEqual({ status: "error", message: "Native queries are turned off." });
  });
});
