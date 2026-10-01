import { describe, expect, it } from "vitest";
import type { LogRecord } from "../api/index.js";
import { DEFAULT_CORRELATION_FIELDS, fieldValue, sameRequestTarget } from "./same-request.js";

function entry(overrides: Partial<LogRecord> = {}): LogRecord {
  return {
    id: "e1",
    timestamp: "2026-09-02T00:41:10.000Z",
    severityNumber: 17,
    attributes: {},
    resource: {},
    sourceAlias: "sample",
    ...overrides,
  };
}

describe("sameRequestTarget", () => {
  it("picks the trace id first with the default fields", () => {
    const record = entry({ traceId: "4bf92f35", attributes: { RequestId: "0HNFKQ41A1:00000001" } });

    expect(sameRequestTarget(record, DEFAULT_CORRELATION_FIELDS)).toEqual({
      field: "@traceId",
      value: "4bf92f35",
      chip: { kind: "condition", field: "@traceId", op: "equals", value: "4bf92f35" },
    });
  });

  it("falls through to the first attribute with a value when the trace id is missing", () => {
    const record = entry({ attributes: { RequestId: "", HttpRequestId: "abc" } });

    expect(sameRequestTarget(record, DEFAULT_CORRELATION_FIELDS)?.field).toBe("HttpRequestId");
  });

  it("follows the configured order rather than the default one", () => {
    const record = entry({ traceId: "4bf92f35", attributes: { RequestId: "0HNFKQ41A1:00000001" } });

    expect(sameRequestTarget(record, ["RequestId", "@traceId"])?.value).toBe("0HNFKQ41A1:00000001");
  });

  it("returns nothing when no correlation field has a value", () => {
    const record = entry({ attributes: { RequestPath: "/" } });

    expect(sameRequestTarget(record, DEFAULT_CORRELATION_FIELDS)).toBeUndefined();
  });
});

describe("fieldValue", () => {
  it.each([
    ["a nested attribute path", entry({ attributes: { Http: { Id: 7 } } }), "Http.Id", 7],
    ["a whole dotted key before splitting it", entry({ attributes: { "Http.Id": "x" } }), "Http.Id", "x"],
    ["a key in another case", entry({ attributes: { requestid: "r1" } }), "RequestId", "r1"],
    ["a resource attribute", entry({ resource: { "host.name": "web1" } }), "@resource.host.name", "web1"],
    ["the span id", entry({ spanId: "00f067aa" }), "@spanId", "00f067aa"],
  ])("reads %s", (_, record, field, expected) => {
    expect(fieldValue(record, field)).toBe(expected);
  });

  it.each([
    ["null", { RequestId: null }],
    ["an object", { RequestId: { Value: 1 } }],
    ["an array", { RequestId: ["a"] }],
  ])("ignores %s", (_, attributes) => {
    expect(fieldValue(entry({ attributes }), "RequestId")).toBeUndefined();
  });
});
