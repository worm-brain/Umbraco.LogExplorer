import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { LogRecord } from "../api/index.js";
import { formatDetailTime, machineOf, messageValueChip, recordJson, samePatternChip } from "./entry-format.js";

/** A record with only what a test sets; the rest at harmless defaults. */
function record(overrides: Partial<LogRecord> = {}): LogRecord {
  return {
    id: "e1",
    timestamp: "2026-09-02T00:41:15.604Z",
    severityNumber: 17,
    attributes: {},
    resource: {},
    sourceAlias: "sample",
    ...overrides,
  };
}

describe("formatDetailTime", () => {
  beforeEach(() => {
    // A non-zero offset that also crosses midnight, so a UTC rendering would be caught.
    vi.stubEnv("TZ", "Pacific/Auckland");
  });

  afterEach(() => {
    vi.unstubAllEnvs();
  });

  it("shows the local date and time as dd/mm/yyyy hh:mm:ss.fff", () => {
    expect(formatDetailTime("2026-09-01T23:05:09.007Z")).toBe("02/09/2026 11:05:09.007");
  });

  it("returns an unparsable value unchanged", () => {
    expect(formatDetailTime("yesterday")).toBe("yesterday");
  });
});

describe("machineOf", () => {
  it("prefers the MachineName property", () => {
    expect(machineOf(record({ attributes: { MachineName: "web-1" }, resource: { "host.name": "file-host" } }))).toBe(
      "web-1",
    );
  });

  it("falls back to the host.name resource attribute", () => {
    expect(machineOf(record({ resource: { "host.name": "file-host" } }))).toBe("file-host");
  });

  it("is undefined when neither is a non-empty string", () => {
    expect(machineOf(record({ attributes: { MachineName: "" }, resource: { "host.name": 3 } }))).toBeUndefined();
  });
});

describe("recordJson", () => {
  it("copies the record as served, every field kept", () => {
    const entry = record({ body: "Boom", attributes: { RequestId: "r1", Cart: { Total: 3 } } });

    expect(JSON.parse(recordJson(entry))).toEqual(entry);
  });

  it("indents the JSON for reading", () => {
    expect(recordJson(record())).toContain('\n  "id": "e1"');
  });
});

describe("samePatternChip", () => {
  it("includes on the message template", () => {
    expect(samePatternChip(record({ messageTemplate: "Slow request {RequestPath}" }))).toEqual({
      kind: "condition",
      field: "@template",
      op: "equals",
      value: "Slow request {RequestPath}",
    });
  });

  it("is undefined without a template", () => {
    expect(samePatternChip(record({ messageTemplate: null }))).toBeUndefined();
  });
});

describe("messageValueChip", () => {
  it("filters on the typed attribute, not the rendered text", () => {
    expect(messageValueChip(record({ attributes: { StatusCode: 404 } }), "StatusCode")).toEqual({
      kind: "condition",
      field: "StatusCode",
      op: "equals",
      value: 404,
    });
  });

  it("is undefined when the attribute is missing", () => {
    expect(messageValueChip(record(), "RequestPath")).toBeUndefined();
  });

  it("is undefined when the attribute is an object", () => {
    expect(messageValueChip(record({ attributes: { Cart: { Total: 3 } } }), "Cart")).toBeUndefined();
  });
});
