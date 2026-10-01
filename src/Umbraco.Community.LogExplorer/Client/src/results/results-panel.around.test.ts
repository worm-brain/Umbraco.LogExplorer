import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { UmbControllerHostElementMixin } from "@umbraco-cms/backoffice/controller-api";
import { umbLocalizationManager } from "@umbraco-cms/backoffice/localization-api";
import type { ContextResult, LogPage, LogRecord, SourceResponseModel } from "../api/index.js";
import en from "../lang/en.js";
import { LogExplorerQueryContext } from "../query/query.context.js";
import { formatRowTime } from "./row-format.js";

// The panel calls the generated client directly; replace the two requests it makes, at the
// network boundary, and keep everything else in the module real.
const search = vi.fn();
const getContext = vi.fn();
vi.mock("../api/index.js", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/index.js")>()),
  SearchService: { search: (...args: Array<unknown>) => search(...args) },
  ContextService: { getContext: (...args: Array<unknown>) => getContext(...args) },
}));

// Imported after the mock so the element module binds to the mocked services.
const { LogExplorerResultsElement } = await import("./results-panel.element.js");

// See entry-detail.element.test.ts: happy-dom has no ElementInternals, which UUI controls call.
if (!("attachInternals" in HTMLElement.prototype)) {
  Object.defineProperty(HTMLElement.prototype, "attachInternals", {
    value: () => new Proxy({}, { get: (_, key) => (key === "validity" ? {} : () => undefined) }),
  });
}
if (!("SVGAElement" in globalThis)) {
  Object.defineProperty(globalThis, "SVGAElement", { value: class SVGAElement {}, configurable: true });
}

umbLocalizationManager.registerLocalization({
  $code: "en",
  $dir: "ltr",
  ...Object.fromEntries(Object.entries(en.logExplorer).map(([key, value]) => [`logExplorer_${key}`, value])),
});

const SEARCH_PATH = "/umbraco/section/settings/workspace/log-explorer/view/search";

class TestWorkspaceElement extends UmbControllerHostElementMixin(HTMLElement) {}
customElements.define("test-results-around-workspace", TestWorkspaceElement);

const sample: SourceResponseModel = {
  alias: "sample",
  displayName: "Sample data",
  type: "Fake",
  sensitive: false,
  capabilities: {
    features: ["context"],
    operators: ["equals"],
    nativeLanguage: null,
    maxRangeSeconds: null,
    maxPageSize: 1000,
  },
  allowNativeQuery: false,
};

function record(id: string, timestamp: string): LogRecord {
  return { id, timestamp, severityNumber: 17, body: id, attributes: {}, resource: {}, sourceAlias: "sample" };
}

const filteredPage: LogPage = {
  records: [record("f1", "2026-09-02T00:59:00.000Z")],
  nextCursor: null,
  range: { from: "2026-09-02T00:00:00.000Z", to: "2026-09-02T01:00:00.000Z" },
  totalCount: 1,
  totalIsLowerBound: false,
  warnings: [],
};

const context: ContextResult = {
  before: [record("b1", "2026-09-02T00:41:00.000Z")],
  anchor: record("a", "2026-09-02T00:41:30.123Z"),
  after: [record("c1", "2026-09-02T00:42:00.000Z")],
};

let workspace: TestWorkspaceElement;
let queryContext: LogExplorerQueryContext;
let panel: InstanceType<typeof LogExplorerResultsElement>;

async function settle(): Promise<void> {
  for (let i = 0; i < 5; i++) await new Promise((resolve) => setTimeout(resolve, 0));
  await panel.updateComplete;
}

const text = (selector: string) => panel.shadowRoot!.querySelector(selector)?.textContent?.trim();
const rowIds = () => [...panel.shadowRoot!.querySelectorAll<HTMLElement>(".row")].map((row) => row.dataset.id);

beforeEach(async () => {
  search.mockReset().mockResolvedValue({ data: filteredPage });
  getContext.mockReset().mockResolvedValue({ data: context });
  window.history.replaceState({}, "", SEARCH_PATH);
  workspace = new TestWorkspaceElement();
  queryContext = new LogExplorerQueryContext(workspace, {
    loadDefaultTimeRange: async () => undefined,
    loadDefaultSource: async () => "sample",
    loadCorrelationFields: async () => undefined,
    loadSources: async () => ({ status: "loaded", sources: [sample] }),
  });
  panel = new LogExplorerResultsElement();
  workspace.appendChild(panel);
  document.body.appendChild(workspace);
  await settle();
});

afterEach(() => {
  workspace.remove();
});

describe("log-explorer-results in Around-this mode", () => {
  it("asks for seven entries either side of the anchor", async () => {
    queryContext.showAround("a");
    await settle();

    expect(getContext).toHaveBeenCalledWith(
      expect.objectContaining({ path: { alias: "sample", id: "a" }, query: { before: 7, after: 7 } }),
    );
  });

  it("shows the banner with the anchor's time", async () => {
    queryContext.showAround("a");
    await settle();

    expect(text(".around-text")).toBe(
      `Showing 7 entries either side of ${formatRowTime(context.anchor.timestamp)}, ignoring filters`,
    );
  });

  it("marks only the anchor row", async () => {
    queryContext.showAround("a");
    await settle();

    const anchors = [...panel.shadowRoot!.querySelectorAll<HTMLElement>(".row.anchor")].map((row) => row.dataset.id);
    expect(anchors).toEqual(["a"]);
  });

  it("lists the neighbours newest first like the filtered list", async () => {
    queryContext.showAround("a");
    await settle();

    expect(rowIds()).toEqual(["c1", "a", "b1"]);
  });

  it("returns to the filtered rows on Back without searching again", async () => {
    queryContext.showAround("a");
    await settle();
    const searches = search.mock.calls.length;

    panel.shadowRoot!.querySelector<HTMLElement>(".around-banner uui-button")!.click();
    await settle();

    expect([rowIds(), search.mock.calls.length, queryContext.getState().around]).toEqual([["f1"], searches, undefined]);
  });

  it("shows the error and keeps Back for a record that no longer exists", async () => {
    getContext.mockResolvedValue({ error: { detail: "Log source 'sample' has no record 'gone'." } });

    queryContext.showAround("gone");
    await settle();

    expect([text(".error span"), panel.shadowRoot!.querySelector(".around-banner uui-button") !== null]).toEqual([
      "Could not load the entries around this entry: Log source 'sample' has no record 'gone'.",
      true,
    ]);
  });
});
