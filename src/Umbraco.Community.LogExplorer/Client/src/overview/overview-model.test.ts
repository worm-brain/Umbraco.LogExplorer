import { describe, expect, it } from "vitest";
import type { HistogramBucket } from "../api/index.js";
import type { FilterNode } from "../query/filter-node.js";
import { exceptionFocusChip, focusInSearch, levelRows, sinkRows, truncateTemplate } from "./overview-model.js";

const buckets: Array<HistogramBucket> = [
  { start: "2026-09-02T00:00:00Z", countsBySeverityShortName: { info: 6, warn: 2 } },
  { start: "2026-09-02T00:01:00Z", countsBySeverityShortName: { info: 4, error: 8 } },
];

describe("levelRows", () => {
  it("sums each level across the buckets with its share of all entries", () => {
    const rows = levelRows(buckets, null);

    expect(rows).toEqual([
      { level: "trace", count: 0, percent: 0, hidden: false },
      { level: "debug", count: 0, percent: 0, hidden: false },
      { level: "info", count: 10, percent: 50, hidden: false },
      { level: "warn", count: 2, percent: 10, hidden: false },
      { level: "error", count: 8, percent: 40, hidden: false },
      { level: "fatal", count: 0, percent: 0, hidden: false },
    ]);
  });

  it("keeps the counts of levels the level set leaves out and marks them hidden", () => {
    const rows = levelRows(buckets, ["error", "fatal"]);

    expect(rows.find((row) => row.level === "info")).toEqual({ level: "info", count: 10, percent: 50, hidden: true });
  });

  it("gives every level a zero share when there are no entries", () => {
    const rows = levelRows([], null);

    expect(rows.map((row) => row.percent)).toEqual([0, 0, 0, 0, 0, 0]);
  });
});

describe("truncateTemplate", () => {
  it("leaves a template within the limit unchanged", () => {
    expect(truncateTemplate("Running recurring background job {JobName}")).toBe(
      "Running recurring background job {JobName}",
    );
  });

  it("cuts a long template to the limit, ellipsis included", () => {
    const template = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed} ms";

    expect(truncateTemplate(template, 20)).toBe("HTTP {RequestMethod…");
  });

  it("keeps a template exactly at the limit whole", () => {
    expect(truncateTemplate("x".repeat(72))).toBe("x".repeat(72));
  });
});

describe("exceptionFocusChip", () => {
  it("includes entries with exactly that exception type", () => {
    expect(exceptionFocusChip("Microsoft.Data.SqlClient.SqlException")).toEqual({
      kind: "condition",
      field: "@exception.type",
      op: "equals",
      value: "Microsoft.Data.SqlClient.SqlException",
    });
  });
});

describe("sinkRows", () => {
  it("shows each sink's level in upper case with its badge level", () => {
    expect(
      sinkRows([
        { name: "Global", level: "info" },
        { name: "UmbracoFile", level: "trace" },
      ]),
    ).toEqual([
      { name: "Global", level: "info", label: "INFO" },
      { name: "UmbracoFile", level: "trace", label: "TRACE" },
    ]);
  });

  it("keeps a level name it does not know as text without a badge level", () => {
    expect(sinkRows([{ name: "Global", level: "verbose" }])).toEqual([
      { name: "Global", level: undefined, label: "VERBOSE" },
    ]);
  });
});

describe("focusInSearch", () => {
  /** A window stand-in whose pushState records the URL it was given. */
  function fakeWindow(pathname: string, search: string) {
    const pushed: Array<string> = [];
    const win = {
      location: { pathname, search },
      history: {
        state: null,
        pushState: (_state: unknown, _unused: string, url?: string | URL | null) => {
          pushed.push(String(url));
        },
      },
    };
    return { win, pushed };
  }

  it("adds the chip through the context", () => {
    const added: Array<ReadonlyArray<FilterNode>> = [];
    const { win } = fakeWindow("/umbraco/section/settings/workspace/log-explorer/view/overview", "?src=files");
    const chip = exceptionFocusChip("System.InvalidOperationException");

    focusInSearch({ addChips: (chips) => added.push(chips) }, chip, win);

    expect(added).toEqual([[chip]]);
  });

  it("opens the Search tab with the current query string", () => {
    const { win, pushed } = fakeWindow("/umbraco/section/settings/workspace/log-explorer/view/overview", "?src=files");

    focusInSearch({ addChips: () => {} }, exceptionFocusChip("System.InvalidOperationException"), win);

    expect(pushed).toEqual(["/umbraco/section/settings/workspace/log-explorer/view/search?src=files"]);
  });
});
