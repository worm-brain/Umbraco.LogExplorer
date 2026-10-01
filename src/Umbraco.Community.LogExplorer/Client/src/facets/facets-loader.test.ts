import { describe, expect, it } from "vitest";
import type { FacetResult, FacetsRequest, FieldInfo } from "../api/index.js";
import { createDefaultViewState } from "../query/view-state.js";
import { FacetsLoader, type FacetsFn, type FieldsFn } from "./facets-loader.js";

const result: FacetResult = {
  facets: [{ field: "MachineName", presenceRatio: 1, topValues: [{ value: "web1", count: 3 }] }],
  approximate: false,
  scannedRange: { from: "2026-09-02T00:00:00Z", to: "2026-09-02T01:00:00Z" },
};

const discovered: Array<FieldInfo> = [{ path: "Url", kind: "string", presenceRatio: 0.5 }];

const state = createDefaultViewState("1h");

/** Lets every pending promise in the loader settle. */
const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

/** Records each facets request and answers with {@link result}. */
function recordingFacets(): FacetsFn & { requests: Array<FacetsRequest> } {
  const requests: Array<FacetsRequest> = [];
  const fn: FacetsFn = async (_alias, request) => {
    requests.push(request);
    return { data: result };
  };
  return Object.assign(fn, { requests });
}

/** Counts fields requests and answers with {@link discovered}. */
function countingFields(): FieldsFn & { calls: number } {
  const counter = Object.assign(
    (async () => {
      counter.calls++;
      return { data: discovered };
    }) satisfies FieldsFn,
    { calls: 0 },
  );
  return counter;
}

describe("FacetsLoader", () => {
  it("facets the pinned fields, then the discovered ones", async () => {
    const facets = recordingFacets();
    const loader = new FacetsLoader(facets, countingFields(), () => {});

    loader.load("sample", { state, pinned: ["MachineName"], discover: true });
    await settle();

    expect([loader.state, facets.requests[0]?.fields]).toEqual([{ status: "loaded", result }, ["MachineName", "Url"]]);
  });

  it("discovers fields once per source and range", async () => {
    const fields = countingFields();
    const loader = new FacetsLoader(recordingFacets(), fields, () => {});

    loader.load("sample", { state, pinned: [], discover: true });
    await settle();
    loader.load("sample", {
      state: { ...state, chips: [{ kind: "text", text: "timeout" }] },
      pinned: [],
      discover: true,
    });
    await settle();

    expect(fields.calls).toBe(1);
  });

  it("skips discovery when the source does not support it", async () => {
    const fields = countingFields();
    const loader = new FacetsLoader(recordingFacets(), fields, () => {});

    loader.load("sample", { state, pinned: ["MachineName"], discover: false });
    await settle();

    expect(fields.calls).toBe(0);
  });

  it("still facets the pinned fields when discovery fails", async () => {
    const facets = recordingFacets();
    const loader = new FacetsLoader(
      facets,
      async () => ({ error: { title: "Boom" } }),
      () => {},
    );

    loader.load("sample", { state, pinned: ["MachineName"], discover: true });
    await settle();

    expect(facets.requests[0]?.fields).toEqual(["MachineName"]);
  });

  it("reports the ProblemDetails detail when the facets request fails", async () => {
    const loader = new FacetsLoader(
      async () => ({ error: { detail: "Log source 'files' does not support facets." } }),
      countingFields(),
      () => {},
    );

    loader.load("files", { state, pinned: ["MachineName"], discover: false });
    await settle();

    expect(loader.state).toMatchObject({ status: "error", error: "Log source 'files' does not support facets." });
  });

  it("aborts the load in flight when a new one starts", () => {
    const signals: Array<AbortSignal> = [];
    const loader = new FacetsLoader(
      (_alias, _request, signal) => {
        signals.push(signal);
        return new Promise(() => {});
      },
      countingFields(),
      () => {},
    );

    loader.load("sample", { state, pinned: ["MachineName"], discover: false });
    loader.load("sample", { state, pinned: ["StatusCode"], discover: false });

    expect(signals[0]?.aborted).toBe(true);
  });
});
