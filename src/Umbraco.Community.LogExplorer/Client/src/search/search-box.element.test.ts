import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { UmbControllerHostElementMixin } from "@umbraco-cms/backoffice/controller-api";
import type { SourceResponseModel } from "../api/index.js";
import { LogExplorerQueryContext } from "../query/query.context.js";
import type { FilterNode } from "../query/filter-node.js";
import { LogExplorerSearchBoxElement } from "./search-box.element.js";

// happy-dom has no ElementInternals, which every UUI form control (uui-input, uui-toggle) calls
// in its constructor. These tests never touch form association, so a stub whose members are
// all no-ops is enough for the controls to construct and render. `form` is null (no form), as
// uui-input's own Enter handler reads it to submit one.
if (!("attachInternals" in HTMLElement.prototype)) {
  Object.defineProperty(HTMLElement.prototype, "attachInternals", {
    value: () =>
      new Proxy({}, { get: (_, key) => (key === "validity" ? {} : key === "form" ? null : () => undefined) }),
  });
}

const SEARCH_PATH = "/umbraco/section/settings/workspace/log-explorer/view/search";

/** Stands in for `log-explorer-workspace`, which provides the query context. */
class TestWorkspaceElement extends UmbControllerHostElementMixin(HTMLElement) {}
customElements.define("test-search-box-workspace", TestWorkspaceElement);

const pathApi: FilterNode = { kind: "condition", field: "RequestPath", op: "startsWith", value: "/api" };
const timeout: FilterNode = { kind: "text", text: "timeout" };

let workspace: TestWorkspaceElement;
let context: LogExplorerQueryContext;
let box: LogExplorerSearchBoxElement;

/** Lets the context reach the box and Lit render. */
async function settle(): Promise<void> {
  for (let i = 0; i < 5; i++) await new Promise((resolve) => setTimeout(resolve, 0));
  await box.updateComplete;
}

/** Types into the box's input the way a user does: set the value, fire input, then the key. */
function press(key: string, value: string): KeyboardEvent {
  const input = box.shadowRoot!.querySelector("uui-input")!;
  input.value = value;
  input.dispatchEvent(new Event("input", { bubbles: true, composed: true }));
  const event = new KeyboardEvent("keydown", { key, bubbles: true, composed: true, cancelable: true });
  input.dispatchEvent(event);
  return event;
}

beforeEach(async () => {
  window.history.replaceState({}, "", SEARCH_PATH);
  workspace = new TestWorkspaceElement();
  context = new LogExplorerQueryContext(workspace, {
    loadDefaultTimeRange: async () => undefined,
    loadDefaultSource: async () => undefined,
    loadSources: async () => ({ status: "empty" }),
  });
  box = new LogExplorerSearchBoxElement();
  workspace.appendChild(box);
  document.body.appendChild(workspace);
  await settle();
  context.addChips([pathApi, timeout]);
  await settle();
});

afterEach(() => {
  workspace.remove();
});

describe("log-explorer-search-box", () => {
  it("renders one chip per chip in the context", () => {
    expect(box.shadowRoot!.querySelectorAll("log-explorer-filter-chip")).toHaveLength(2);
  });

  it("removes the last chip on Backspace in an empty input", async () => {
    press("Backspace", "");

    expect(context.getState().chips).toEqual([pathApi]);
  });

  it("keeps the chips on Backspace while the input has text", () => {
    press("Backspace", "abc");

    expect(context.getState().chips).toHaveLength(2);
  });

  it("clears the input on Escape", async () => {
    press("Escape", "timeout");
    await box.updateComplete;

    expect(box.shadowRoot!.querySelector("uui-input")!.value).toBe("");
  });

  it("ignores keys that come from a chip rather than the input", () => {
    const chip = box.shadowRoot!.querySelector("log-explorer-filter-chip")!;

    chip.dispatchEvent(new KeyboardEvent("keydown", { key: "Backspace", bubbles: true, composed: true }));

    expect(context.getState().chips).toHaveLength(2);
  });

  it("removes a chip when its remove button is pressed", async () => {
    const chip = box.shadowRoot!.querySelectorAll("log-explorer-filter-chip")[0]!;
    await chip.updateComplete;

    chip.shadowRoot!.querySelector("uui-button")!.dispatchEvent(new MouseEvent("click", { bubbles: true }));

    expect(context.getState().chips).toEqual([timeout]);
  });
});

describe("log-explorer-search-box with a native-capable source", () => {
  const sample: SourceResponseModel = {
    alias: "sample",
    displayName: "Sample data",
    type: "Fake",
    sensitive: false,
    // `startsWith` is not declared, so the path chip cannot run.
    capabilities: {
      features: ["nativeQuery"],
      operators: ["equals"],
      nativeLanguage: "Sample",
      maxRangeSeconds: null,
      maxPageSize: 1000,
    },
    allowNativeQuery: true,
  };

  beforeEach(async () => {
    workspace.remove();
    workspace = new TestWorkspaceElement();
    context = new LogExplorerQueryContext(workspace, {
      loadDefaultTimeRange: async () => undefined,
      loadDefaultSource: async () => "sample",
      loadSources: async () => ({ status: "loaded", sources: [sample] }),
      compile: async () => ({ data: { native: null, unsupported: [] } }),
      compileDebounceMs: 0,
    });
    box = new LogExplorerSearchBoxElement();
    workspace.appendChild(box);
    document.body.appendChild(workspace);
    await settle();
    context.addChips([pathApi, timeout]);
    await settle();
  });

  it("draws the chip the source cannot run as unsupported and the others normally", () => {
    const chips = [...box.shadowRoot!.querySelectorAll("log-explorer-filter-chip")];

    expect(chips.map((chip) => chip.unsupported)).toEqual([true, false]);
  });

  it("stores the typed text as the native query on Enter in native mode, without parsing it", async () => {
    context.update({ native: "" });
    await settle();

    press("Enter", 'text("slow")');

    expect([context.getState().native, context.getState().chips]).toEqual(['text("slow")', [pathApi, timeout]]);
  });

  it("returns to simple mode from the language tag's button", async () => {
    context.update({ native: 'text("slow")' });
    await settle();

    box
      .shadowRoot!.querySelector("uui-tag.language uui-button")!
      .dispatchEvent(new MouseEvent("click", { bubbles: true }));

    expect(context.getState().native).toBeUndefined();
  });
});

describe("log-explorer-search-box with a chip the source runs but cannot show", () => {
  const sample: SourceResponseModel = {
    alias: "sample",
    displayName: "Sample data",
    type: "Fake",
    sensitive: false,
    capabilities: {
      features: ["nativeQuery"],
      operators: ["startsWith"],
      nativeLanguage: "Sample",
      maxRangeSeconds: null,
      maxPageSize: 1000,
    },
    allowNativeQuery: true,
  };

  beforeEach(async () => {
    workspace.remove();
    workspace = new TestWorkspaceElement();
    context = new LogExplorerQueryContext(workspace, {
      loadDefaultTimeRange: async () => undefined,
      loadDefaultSource: async () => "sample",
      loadSources: async () => ({ status: "loaded", sources: [sample] }),
      compile: async () => ({ data: { native: 'text("timeout")', unsupported: [pathApi] } }),
      compileDebounceMs: 0,
    });
    box = new LogExplorerSearchBoxElement();
    workspace.appendChild(box);
    document.body.appendChild(workspace);
    await settle();
    context.addChips([pathApi, timeout]);
    await settle();
  });

  it("keeps the chip active and notes only the language it is not shown in", () => {
    const chips = [...box.shadowRoot!.querySelectorAll("log-explorer-filter-chip")];

    expect(chips.map((chip) => [chip.unsupported, chip.notShownIn])).toEqual([
      [false, "Sample"],
      [false, ""],
    ]);
  });
});
