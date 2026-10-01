import { describe, expect, it } from "vitest";
import type { SourceResponseModel } from "../api/index.js";
import { findSource, resolveActiveSource } from "./active-source.js";

function source(alias: string): SourceResponseModel {
  return {
    alias,
    displayName: alias,
    type: "Fake",
    sensitive: false,
    capabilities: { features: [], operators: [], nativeLanguage: null, maxRangeSeconds: null, maxPageSize: 1000 },
  };
}

const files = source("files");
const sample = source("sample");
const prod = source("prod-ai");
const visible = [files, sample, prod];

describe("resolveActiveSource", () => {
  it("uses the requested source when it is visible", () => {
    expect(resolveActiveSource(visible, "prod-ai", "sample")).toBe(prod);
  });

  it("falls back to the default source when the requested one is not visible", () => {
    expect(resolveActiveSource(visible, "removed", "sample")).toBe(sample);
  });

  it("uses the default source when nothing is requested", () => {
    expect(resolveActiveSource(visible, undefined, "sample")).toBe(sample);
  });

  it("falls back to the first visible source when the default is not visible", () => {
    expect(resolveActiveSource(visible, undefined, "hidden")).toBe(files);
  });

  it("falls back to the first visible source when the default is unknown", () => {
    expect(resolveActiveSource(visible, "removed", undefined)).toBe(files);
  });

  it("matches aliases ignoring case, as the server does", () => {
    expect(resolveActiveSource(visible, "SAMPLE", undefined)).toBe(sample);
  });

  it("returns undefined when no source is visible", () => {
    expect(resolveActiveSource([], "sample", "sample")).toBeUndefined();
  });
});

describe("findSource", () => {
  it("finds a source by alias", () => {
    expect(findSource(visible, "prod-ai")).toBe(prod);
  });

  it("returns undefined when no alias is given", () => {
    expect(findSource(visible, undefined)).toBeUndefined();
  });
});
