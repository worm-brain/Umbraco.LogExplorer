import { describe, expect, it } from "vitest";
import type { SourceResponseModel } from "../api/index.js";
import { loadSources } from "./sources-loader.js";

const sample: SourceResponseModel = {
  alias: "sample",
  displayName: "Sample data",
  type: "Fake",
  sensitive: false,
  capabilities: { features: [], operators: [], nativeLanguage: null, maxRangeSeconds: null, maxPageSize: 1000 },
};

describe("loadSources", () => {
  it("returns the sources when the request succeeds", async () => {
    const state = await loadSources(async () => ({ data: [sample] }));

    expect(state).toEqual({ status: "loaded", sources: [sample] });
  });

  it("returns empty when no source is visible", async () => {
    const state = await loadSources(async () => ({ data: [] }));

    expect(state).toEqual({ status: "empty" });
  });

  it("uses the ProblemDetails title when the server returns an error", async () => {
    const state = await loadSources(async () => ({
      error: { title: "Forbidden" },
      response: new Response(null, { status: 403 }),
    }));

    expect(state).toEqual({ status: "error", message: "Forbidden" });
  });

  it("falls back to the HTTP status when the error has no ProblemDetails", async () => {
    const state = await loadSources(async () => ({
      error: "",
      response: new Response(null, { status: 401, statusText: "Unauthorized" }),
    }));

    expect(state).toEqual({ status: "error", message: "The server responded 401 Unauthorized." });
  });

  it("returns an error state instead of throwing when the request rejects", async () => {
    const state = await loadSources(async () => {
      throw new TypeError("Failed to fetch");
    });

    expect(state).toEqual({ status: "error", message: "Failed to fetch" });
  });
});
