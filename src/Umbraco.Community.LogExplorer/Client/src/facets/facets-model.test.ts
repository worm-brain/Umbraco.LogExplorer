import { describe, expect, it } from "vitest";
import type { FieldInfo } from "../api/index.js";
import type { FilterNode } from "../query/filter-node.js";
import { createDefaultViewState } from "../query/view-state.js";
import {
  DEFAULT_PINNED_FACETS,
  buildFacetsRequest,
  excludeChip,
  fieldsRange,
  includeChip,
  isValueSelected,
  matchesFieldFilter,
  pinnedFacetsFrom,
  selectDiscoveredFields,
  shareRatio,
  valueLabel,
} from "./facets-model.js";

const field = (path: string, presenceRatio: number, kind = "string"): FieldInfo => ({ path, kind, presenceRatio });

describe("shareRatio", () => {
  it("measures a value against the field's top value", () => {
    expect(shareRatio(25, 100)).toBe(0.25);
  });

  it("fills the row for the top value", () => {
    expect(shareRatio(100, 100)).toBe(1);
  });

  it("is empty when the top count is zero", () => {
    expect(shareRatio(0, 0)).toBe(0);
  });
});

describe("valueLabel", () => {
  it("shows the last namespace segment of a source context", () => {
    expect(valueLabel("SourceContext", "Umbraco.Cms.Web.Common.ApplicationBuilder")).toBe("ApplicationBuilder");
  });

  it("shows the last segment of an exception type", () => {
    expect(valueLabel("@exception.type", "Microsoft.Data.SqlClient.SqlException")).toBe("SqlException");
  });

  it("keeps other string values whole", () => {
    expect(valueLabel("RequestPath", "/umbraco/surface/contact.submit")).toBe("/umbraco/surface/contact.submit");
  });

  it("writes non-string values as JSON", () => {
    expect(valueLabel("StatusCode", 404)).toBe("404");
  });
});

describe("isValueSelected", () => {
  it("is true when an include chip on the value is active", () => {
    const chips: Array<FilterNode> = [
      { kind: "condition", field: "MachineName", op: "equals", value: "web1", caseInsensitive: true },
    ];

    expect(isValueSelected(chips, "MachineName", "web1")).toBe(true);
  });

  it("is false when only an exclude chip on the value is active", () => {
    const chips = [excludeChip("MachineName", "web1")];

    expect(isValueSelected(chips, "MachineName", "web1")).toBe(false);
  });

  it("does not match a number value against the same text", () => {
    const chips = [includeChip("StatusCode", "200")];

    expect(isValueSelected(chips, "StatusCode", 200)).toBe(false);
  });
});

describe("excludeChip", () => {
  it("wraps the include condition in a not, as /parse does for -field:value", () => {
    expect(excludeChip("StatusCode", 200)).toEqual({
      kind: "not",
      child: { kind: "condition", field: "StatusCode", op: "equals", value: 200 },
    });
  });
});

describe("selectDiscoveredFields", () => {
  it("orders by presence, highest first, ties by path", () => {
    const fields = [field("Elapsed", 0.4), field("Url", 0.9), field("Method", 0.4)];

    expect(selectDiscoveredFields(fields, [])).toEqual(["Url", "Elapsed", "Method"]);
  });

  it("leaves out pinned fields, compared case-insensitively", () => {
    const fields = [field("sourcecontext", 1), field("Url", 0.5)];

    expect(selectDiscoveredFields(fields, ["SourceContext"])).toEqual(["Url"]);
  });

  it("leaves out the portable twin of a pinned attribute", () => {
    const fields = [field("@scope", 1), field("@exception.type", 0.1), field("Url", 0.5)];

    expect(selectDiscoveredFields(fields, ["SourceContext", "ExceptionType"])).toEqual(["Url"]);
  });

  it("leaves out absent fields, objects and portable fields that make no useful facet", () => {
    const fields = [
      field("Never", 0),
      field("Cart", 0.2, "object"),
      field("@timestamp", 1, "datetime"),
      field("@body", 1),
      field("@severity", 1, "number"),
      field("@traceId", 0.8),
      field("Cart.Total", 0.2, "number"),
    ];

    expect(selectDiscoveredFields(fields, [])).toEqual(["Cart.Total"]);
  });

  it("caps the list", () => {
    const fields = [field("A", 0.9), field("B", 0.8), field("C", 0.7)];

    expect(selectDiscoveredFields(fields, [], 2)).toEqual(["A", "B"]);
  });
});

describe("buildFacetsRequest", () => {
  it("counts the current query with pinned fields first and five values each", () => {
    const state = {
      ...createDefaultViewState("1h"),
      levels: ["error" as const],
      chips: [includeChip("MachineName", "web1")],
      sort: "asc" as const,
    };

    const request = buildFacetsRequest(state, ["SourceContext"], ["Url"]);

    expect(request).toEqual({
      query: {
        range: { relative: "1h" },
        levels: ["error"],
        filter: { kind: "condition", field: "MachineName", op: "equals", value: "web1" },
        nativeQuery: null,
        take: 1,
        cursor: null,
        sort: "descending",
      },
      fields: ["SourceContext", "Url"],
      top: 5,
    });
  });

  it("uses the histogram zoom instead of the picker's range", () => {
    const zoom = { from: "2026-09-02T00:40:00.000Z", to: "2026-09-02T00:45:00.000Z" };
    const state = { ...createDefaultViewState("1h"), zoom };

    expect(buildFacetsRequest(state, [], []).query.range).toEqual(zoom);
  });
});

describe("fieldsRange", () => {
  it("passes a relative range through", () => {
    expect(fieldsRange(createDefaultViewState("4h"))).toEqual({ relative: "4h" });
  });

  it("prefers the zoom", () => {
    const zoom = { from: "2026-09-02T00:40:00.000Z", to: "2026-09-02T00:45:00.000Z" };

    expect(fieldsRange({ ...createDefaultViewState("1h"), zoom })).toEqual(zoom);
  });
});

describe("pinnedFacetsFrom", () => {
  it("uses the configured list", () => {
    expect(pinnedFacetsFrom({ pinnedFacets: ["StatusCode"] })).toEqual(["StatusCode"]);
  });

  it("falls back to the brief defaults when settings failed to load", () => {
    expect(pinnedFacetsFrom(undefined)).toEqual(DEFAULT_PINNED_FACETS);
  });
});

describe("matchesFieldFilter", () => {
  it("matches the shown label", () => {
    expect(matchesFieldFilter("RequestPath", "Path", "pat")).toBe(true);
  });

  it("matches the field path", () => {
    expect(matchesFieldFilter("SourceContext", "Source", "context")).toBe(true);
  });

  it("rejects a field that matches neither", () => {
    expect(matchesFieldFilter("StatusCode", "Status", "machine")).toBe(false);
  });
});
