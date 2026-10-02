import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { UmbControllerHostElementMixin } from "@umbraco-cms/backoffice/controller-api";
import { umbLocalizationManager } from "@umbraco-cms/backoffice/localization-api";
import type { LogRecord } from "../api/index.js";
import en from "../lang/en.js";
import { LogExplorerQueryContext } from "../query/query.context.js";
import { LogExplorerEntryDetailElement } from "./entry-detail.element.js";
import { recordJson } from "./entry-format.js";

// happy-dom has no ElementInternals, which UUI controls call in their constructors (see
// search-box.element.test.ts); a no-op stub is enough to construct and render them.
if (!("attachInternals" in HTMLElement.prototype)) {
  Object.defineProperty(HTMLElement.prototype, "attachInternals", {
    value: () => new Proxy({}, { get: (_, key) => (key === "validity" ? {} : () => undefined) }),
  });
}

// The backoffice router's window click listener (imported with the notification context) tests
// `instanceof SVGAElement`, which happy-dom does not define; every uui-button click reaches it.
if (!("SVGAElement" in globalThis)) {
  Object.defineProperty(globalThis, "SVGAElement", { value: class SVGAElement {}, configurable: true });
}

// The tests find controls by their localised labels, so register the shipped English strings the
// way the backoffice does: flattened to `logExplorer_{key}`.
umbLocalizationManager.registerLocalization({
  $code: "en",
  $dir: "ltr",
  ...Object.fromEntries(Object.entries(en.logExplorer).map(([key, value]) => [`logExplorer_${key}`, value])),
});

const SEARCH_PATH = "/umbraco/section/settings/workspace/log-explorer/view/search";

/** Stands in for `log-explorer-workspace`, which provides the query context. */
class TestWorkspaceElement extends UmbControllerHostElementMixin(HTMLElement) {}
customElements.define("test-entry-detail-workspace", TestWorkspaceElement);

const ERROR_ENTRY: LogRecord = {
  id: "e7",
  timestamp: "2026-09-02T00:43:50.077Z",
  severityNumber: 17,
  body: "Failed to publish document 1203 (<b>key</b>)",
  messageTemplate: "Failed to publish document {ContentId} ({DocumentKey})",
  attributes: { ContentId: 1203, DocumentKey: "<b>key</b>", Cart: { Total: 3 } },
  resource: {},
  exception: {
    type: "System.InvalidOperationException",
    message: "Boom",
    stackTrace: [
      "System.InvalidOperationException: Boom",
      "   at Client.Web.Handler.Handle()",
      "   at Umbraco.Cms.Core.Events.EventAggregator.PublishCore(...)",
    ].join("\n"),
  },
  sourceAlias: "sample",
};

let workspace: TestWorkspaceElement;
let context: LogExplorerQueryContext;
let drawer: LogExplorerEntryDetailElement;

async function settle(): Promise<void> {
  for (let i = 0; i < 5; i++) await new Promise((resolve) => setTimeout(resolve, 0));
  await drawer.updateComplete;
}

function byLabel(label: string): HTMLElement {
  const element = drawer.shadowRoot!.querySelector<HTMLElement>(`[label='${label}'], [aria-label='${label}']`);
  if (!element) throw new Error(`No control labelled ${label}`);
  return element;
}

beforeEach(async () => {
  window.history.replaceState({}, "", SEARCH_PATH);
  workspace = new TestWorkspaceElement();
  context = new LogExplorerQueryContext(workspace, {
    loadDefaultTimeRange: async () => undefined,
    loadDefaultSource: async () => undefined,
    loadCorrelationFields: async () => undefined,
    loadSources: async () => ({ status: "empty" }),
  });
  drawer = new LogExplorerEntryDetailElement();
  drawer.record = ERROR_ENTRY;
  workspace.appendChild(drawer);
  document.body.appendChild(workspace);
  await settle();
});

afterEach(() => {
  workspace.remove();
  vi.restoreAllMocks();
});

describe("log-explorer-entry-detail", () => {
  it("adds an include chip from a property's + button", async () => {
    byLabel("Include ContentId 1203").click();
    await settle();

    expect(context.getState().chips).toEqual([{ kind: "condition", field: "ContentId", op: "equals", value: 1203 }]);
  });

  it("adds an exclude chip from a property's - button", async () => {
    byLabel("Exclude ContentId 1203").click();
    await settle();

    expect(context.getState().chips).toEqual([
      { kind: "not", child: { kind: "condition", field: "ContentId", op: "equals", value: 1203 } },
    ]);
  });

  it("adds an include chip from a value in the rendered message", async () => {
    byLabel("Filter on ContentId: 1203").click();
    await settle();

    expect(context.getState().chips).toEqual([{ kind: "condition", field: "ContentId", op: "equals", value: 1203 }]);
  });

  it("adds a template chip for Same pattern", async () => {
    byLabel("Same pattern").click();
    await settle();

    expect(context.getState().chips).toEqual([
      { kind: "condition", field: "@template", op: "equals", value: ERROR_ENTRY.messageTemplate },
    ]);
  });

  it("disables Same request, saying why, when no correlation field has a value", () => {
    const button = byLabel("Same request needs a value in one of: @traceId, RequestId, HttpRequestId");

    expect(button.hasAttribute("disabled")).toBe(true);
  });

  it("replaces the filters, levels and zoom with the request chip for Same request", async () => {
    context.update({ chips: [{ kind: "text", text: "timeout" }], levels: ["error"] });
    drawer.record = { ...ERROR_ENTRY, attributes: { ...ERROR_ENTRY.attributes, RequestId: "0HNFKQ41A1:00000001" } };
    await settle();

    byLabel("Same request").click();
    await settle();

    expect([context.getState().chips, context.getState().levels]).toEqual([
      [{ kind: "condition", field: "RequestId", op: "equals", value: "0HNFKQ41A1:00000001" }],
      null,
    ]);
  });

  it("switches the results to the entries around this one", async () => {
    byLabel("Around this").click();
    await settle();

    expect(context.getState().around).toBe("e7");
  });

  it("renders log text as text, never as HTML", () => {
    expect(drawer.shadowRoot!.querySelector(".message b")).toBeNull();
  });

  it("offers no filter buttons for an object property until it is expanded", () => {
    expect(drawer.shadowRoot!.querySelector("[label^='Include Cart']")).toBeNull();
  });

  it("puts an object's expand toggle in the row's actions cell, not before its name", () => {
    expect(byLabel("Expand Cart").closest("uui-table-cell")?.classList.contains("filter")).toBe(true);
  });

  it("shows an object's members with dotted filter paths once expanded", async () => {
    byLabel("Expand Cart").click();
    await settle();

    expect(byLabel("Include Cart.Total 3")).toBeTruthy();
  });

  it("collapses framework frames until asked to show them", async () => {
    const stack = () => [...drawer.shadowRoot!.querySelectorAll("umb-code-block")].at(-1)!.textContent!;
    const collapsed = stack();
    byLabel("Show framework frames").click();
    await settle();

    expect([collapsed.includes("EventAggregator"), stack().includes("EventAggregator")]).toEqual([false, true]);
  });

  it("copies the record as JSON", async () => {
    const writeText = vi.fn(async () => undefined);
    Object.defineProperty(navigator, "clipboard", { value: { writeText }, configurable: true });

    byLabel("Copy as JSON").click();
    await settle();

    expect(writeText).toHaveBeenCalledWith(recordJson(ERROR_ENTRY));
  });

  it("asks to close on Escape", () => {
    const closed = vi.fn();
    drawer.addEventListener("log-explorer-entry-close", closed);

    byLabel("Same pattern").dispatchEvent(
      new KeyboardEvent("keydown", { key: "Escape", bubbles: true, composed: true }),
    );

    expect(closed).toHaveBeenCalledOnce();
  });

  it("does not close on an Escape a control already handled", () => {
    const closed = vi.fn();
    drawer.addEventListener("log-explorer-entry-close", closed);
    const event = new KeyboardEvent("keydown", { key: "Escape", bubbles: true, composed: true, cancelable: true });
    event.preventDefault();

    byLabel("Same pattern").dispatchEvent(event);

    expect(closed).not.toHaveBeenCalled();
  });

  it("collapses the tree again when another entry opens", async () => {
    byLabel("Expand Cart").click();
    await settle();
    drawer.record = { ...ERROR_ENTRY, id: "e8" };
    await settle();

    expect(drawer.shadowRoot!.querySelector("[label^='Include Cart']")).toBeNull();
  });
});
