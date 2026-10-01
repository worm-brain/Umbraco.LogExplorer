import { describe, expect, it } from "vitest";
import type { HistogramBucket } from "../api/index.js";
import {
  barIndexAt,
  layoutBar,
  levelTotals,
  parseTimeSpanMs,
  rangeFromDrag,
  targetBucketsFor,
  ticks,
  toggleLevel,
  zoomFromBucket,
  zoomLabelParts,
} from "./histogram-model.js";

const MINUTE = 60_000;

/** A local-time instant as ISO, so clock labels read the same in every test time zone. */
function local(hours: number, minutes: number, seconds = 0, day = 2): string {
  return new Date(2026, 8, day, hours, minutes, seconds).toISOString();
}

const buckets: Array<HistogramBucket> = [
  { start: "2026-09-02T00:40:00.000Z", countsBySeverityShortName: { info: 2, warn: 1 } },
  { start: "2026-09-02T00:41:00.000Z", countsBySeverityShortName: { info: 3, error: 2 } },
  { start: "2026-09-02T00:42:00.000Z", countsBySeverityShortName: { fatal: 1 } },
];

describe("targetBucketsFor", () => {
  it.each([
    ["15 minutes", 15 * MINUTE, false, 45],
    ["1 hour", 60 * MINUTE, false, 60],
    ["24 hours", 24 * 60 * MINUTE, false, 96],
    ["30 days", 30 * 24 * 60 * MINUTE, false, 120],
    ["a zoom", 5 * MINUTE, true, 30],
  ])("asks for the brief's bucket count for %s", (_name, rangeMs, zoomed, expected) => {
    expect(targetBucketsFor(rangeMs, zoomed)).toBe(expected);
  });
});

describe("parseTimeSpanMs", () => {
  it.each([
    ["a minute", "00:01:00", 60_000],
    ["days and fractions", "1.02:00:00.5000000", 26 * 60 * MINUTE + 500],
  ])("reads %s", (_name, text, expected) => {
    expect(parseTimeSpanMs(text)).toBe(expected);
  });

  it("rejects text that is not a TimeSpan", () => {
    expect(parseTimeSpanMs("PT1M")).toBeUndefined();
  });
});

describe("layoutBar", () => {
  it("stacks INFO at the bottom, then DEBUG, TRACE, WARN, ERROR and FATAL", () => {
    const counts = { fatal: 1, error: 1, warn: 1, trace: 1, debug: 1, info: 1 };

    const order = layoutBar(counts, 6, 0).map((segment) => segment.level);

    expect(order).toEqual(["info", "debug", "trace", "warn", "error", "fatal"]);
  });

  it("scales segments against the tallest bar", () => {
    expect(layoutBar({ info: 5 }, 10, 0)).toEqual([{ level: "info", count: 5, heightPercent: 50 }]);
  });

  it("raises a tiny non-zero segment to the minimum height", () => {
    const error = layoutBar({ info: 999, error: 1 }, 1000, 4).find((segment) => segment.level === "error");

    expect(error?.heightPercent).toBe(4);
  });

  it("takes the raised height from the tallest segment so the bar fits the strip", () => {
    const heights = layoutBar({ info: 999, error: 1 }, 1000, 4).map((segment) => segment.heightPercent);

    expect(heights).toEqual([96, 4]);
  });

  it("omits zero-count levels and returns nothing for an empty bucket", () => {
    expect(layoutBar({ info: 0, warn: 0 }, 10, 4)).toEqual([]);
  });
});

describe("levelTotals", () => {
  it("sums each level across buckets, with zeros for absent levels", () => {
    expect(levelTotals(buckets)).toEqual({ trace: 0, debug: 0, info: 5, warn: 1, error: 2, fatal: 1 });
  });
});

describe("zoomFromBucket", () => {
  it("zooms to five minutes starting two minutes before the bucket", () => {
    expect(zoomFromBucket("2026-09-02T00:42:00.000Z")).toEqual({
      from: "2026-09-02T00:40:00.000Z",
      to: "2026-09-02T00:45:00.000Z",
    });
  });
});

describe("rangeFromDrag", () => {
  it("runs from the first bar's start to the last bar's end", () => {
    expect(rangeFromDrag(buckets, MINUTE, 0, 1)).toEqual({
      from: "2026-09-02T00:40:00.000Z",
      to: "2026-09-02T00:42:00.000Z",
    });
  });

  it("gives the same range when dragged right to left", () => {
    expect(rangeFromDrag(buckets, MINUTE, 2, 1)).toEqual({
      from: "2026-09-02T00:41:00.000Z",
      to: "2026-09-02T00:43:00.000Z",
    });
  });

  it("returns undefined for a bar outside the histogram", () => {
    expect(rangeFromDrag(buckets, MINUTE, 0, 7)).toBeUndefined();
  });
});

describe("barIndexAt", () => {
  it("maps a position to the bar under it", () => {
    expect(barIndexAt(150, 100, 300, 3)).toBe(0);
  });

  it("clamps positions past either edge to the end bars", () => {
    expect([barIndexAt(10, 100, 300, 3), barIndexAt(900, 100, 300, 3)]).toEqual([0, 2]);
  });
});

describe("ticks", () => {
  it("places five labels evenly across an hour as hh:mm", () => {
    const from = new Date(local(0, 0)).getTime();

    expect(ticks(from, from + 60 * MINUTE, false, "en-GB")).toEqual([
      { offsetPercent: 0, label: "00:00" },
      { offsetPercent: 25, label: "00:15" },
      { offsetPercent: 50, label: "00:30" },
      { offsetPercent: 75, label: "00:45" },
      { offsetPercent: 100, label: "01:00" },
    ]);
  });

  it("shows seconds when asked, for zoomed ranges", () => {
    const from = new Date(local(0, 40)).getTime();

    expect(ticks(from, from + 5 * MINUTE, true, "en-GB")[1]?.label).toBe("00:41:15");
  });
});

describe("zoomLabelParts", () => {
  it("formats whole-minute ends as hh:mm", () => {
    expect(zoomLabelParts({ from: local(0, 40), to: local(0, 45) }, "en-GB")).toEqual({ from: "00:40", to: "00:45" });
  });

  it("adds seconds when an end is not on a whole minute", () => {
    expect(zoomLabelParts({ from: local(0, 40, 30), to: local(0, 41) }, "en-GB")).toEqual({
      from: "00:40:30",
      to: "00:41:00",
    });
  });

  it("adds the date when the ends fall on different days", () => {
    expect(zoomLabelParts({ from: local(23, 58), to: local(0, 3, 0, 3) }, "en-GB")).toEqual({
      from: "02/09, 23:58",
      to: "03/09, 00:03",
    });
  });
});

describe("toggleLevel", () => {
  it("hides one level when every level is on", () => {
    expect(toggleLevel(null, "info")).toEqual(["trace", "debug", "warn", "error", "fatal"]);
  });

  it("returns null when the last hidden level is shown again", () => {
    expect(toggleLevel(["trace", "debug", "warn", "error", "fatal"], "info")).toBeNull();
  });

  it("keeps severity order when showing a level", () => {
    expect(toggleLevel(["error"], "debug")).toEqual(["debug", "error"]);
  });

  it("allows hiding every level", () => {
    expect(toggleLevel(["warn"], "warn")).toEqual([]);
  });
});
