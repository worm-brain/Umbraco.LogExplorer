import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { UmbControllerHostElementMixin } from "@umbraco-cms/backoffice/controller-api";
import { umbLocalizationManager } from "@umbraco-cms/backoffice/localization-api";
import { UmbNotificationContext } from "@umbraco-cms/backoffice/notification";
import en from "../lang/en.js";
import { LogExplorerQueryContext } from "../query/query.context.js";
import { LogExplorerShareButtonElement } from "./share-button.element.js";

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

umbLocalizationManager.registerLocalization({
  $code: "en",
  $dir: "ltr",
  ...Object.fromEntries(Object.entries(en.logExplorer).map(([key, value]) => [`logExplorer_${key}`, value])),
});

const PATTERNS_PATH = "/umbraco/section/settings/workspace/log-explorer/view/patterns";

/** Stands in for `log-explorer-workspace` and the backoffice root, which provide the contexts. */
class TestWorkspaceElement extends UmbControllerHostElementMixin(HTMLElement) {}
customElements.define("test-share-button-workspace", TestWorkspaceElement);

let workspace: TestWorkspaceElement;
let context: LogExplorerQueryContext;
let notifications: UmbNotificationContext;
let button: LogExplorerShareButtonElement;

async function settle(): Promise<void> {
  for (let i = 0; i < 5; i++) await new Promise((resolve) => setTimeout(resolve, 0));
  await button.updateComplete;
}

function stubClipboard(writeText: (text: string) => Promise<void>): void {
  Object.defineProperty(navigator, "clipboard", { value: { writeText }, configurable: true });
}

function clickShare(): void {
  button.shadowRoot!.querySelector<HTMLElement>("uui-button[label='Copy link to this view']")!.click();
}

beforeEach(async () => {
  window.history.replaceState({}, "", PATTERNS_PATH);
  workspace = new TestWorkspaceElement();
  notifications = new UmbNotificationContext(workspace);
  context = new LogExplorerQueryContext(workspace, {
    loadDefaultTimeRange: async () => undefined,
    loadDefaultSource: async () => undefined,
    loadSources: async () => ({ status: "empty" }),
  });
  button = new LogExplorerShareButtonElement();
  workspace.appendChild(button);
  document.body.appendChild(workspace);
  await settle();
});

afterEach(() => {
  workspace.remove();
  vi.restoreAllMocks();
});

describe("log-explorer-share-button", () => {
  it("copies the absolute URL of the current tab and view", async () => {
    const writeText = vi.fn(async () => undefined);
    stubClipboard(writeText);
    context.update({ range: { relative: "15m" }, sort: "asc" });

    clickShare();
    await settle();

    expect(writeText).toHaveBeenCalledWith(`${window.location.origin}${PATTERNS_PATH}?range=15m&sort=asc`);
  });

  it("confirms the copy with a notification", async () => {
    stubClipboard(async () => undefined);
    const peek = vi.spyOn(notifications, "peek");

    clickShare();
    await settle();

    expect(peek).toHaveBeenCalledWith("positive", { data: { message: "Link to this exact view copied" } });
  });

  it("reports a refused clipboard write with a danger notification", async () => {
    stubClipboard(async () => Promise.reject(new DOMException("Denied", "NotAllowedError")));
    const peek = vi.spyOn(notifications, "peek");

    clickShare();
    await settle();

    expect(peek).toHaveBeenCalledWith("danger", { data: { message: "Could not copy the link to the clipboard" } });
  });
});
