import { describe, expect, it } from "vitest";
import { formatLocale } from "./format-locale.js";

describe("formatLocale", () => {
  it("formats the backoffice's UK English (en) as en-GB, day first", () => {
    const date = new Date(Date.UTC(2026, 8, 25, 12));
    expect(new Intl.DateTimeFormat(formatLocale("en"), { dateStyle: "short", timeZone: "UTC" }).format(date)).toBe(
      "25/09/2026",
    );
  });

  it("leaves a tag with a region, such as en-us, as it is", () => {
    expect(formatLocale("en-us")).toBe("en-us");
  });
});
