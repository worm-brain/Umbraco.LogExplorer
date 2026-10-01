import { css, customElement, html } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import { LogExplorerQueryContext } from "../query/query.context.js";
import "../sources/source-picker.element.js";

/**
 * The Log Explorer workspace shell (UI brief §3, §4.1).
 *
 * `umb-workspace-editor` draws the standard workspace header with the "Log Explorer" headline and
 * the Search, Patterns and Overview tabs, which it builds from the `workspaceView` manifests that
 * match this workspace's alias, and routes between them. This element only adds the source
 * picker into the editor's `header` slot, which sits right of the headline. There is no footer,
 * so `enforceNoFooter` stops the editor reserving one. It also provides `LogExplorerQueryContext`,
 * the view state shared by the header and every view.
 *
 * Bound by the `Umbraco.Community.LogExplorer.Workspace` manifest (default workspace kind, which
 * supplies the workspace context the views' workspace condition reads).
 *
 * @element log-explorer-workspace
 */
@customElement("log-explorer-workspace")
export class LogExplorerWorkspaceElement extends UmbLitElement {
  constructor() {
    super();
    // Provides the shared view state to every view in this workspace. The context registers
    // itself on this element as a controller, so it needs no field: it is in place before any
    // view asks for it and lives exactly as long as the workspace.
    new LogExplorerQueryContext(this);
  }

  /**
   * Renders the workspace editor with the source picker in its header.
   *
   * @returns The template.
   */
  override render() {
    return html`
      <umb-workspace-editor headline=${this.localize.term("logExplorer_title")} .enforceNoFooter=${true}>
        <log-explorer-source-picker slot="header"></log-explorer-source-picker>
      </umb-workspace-editor>
    `;
  }

  static override styles = [
    UmbTextStyles,
    css`
      :host {
        display: block;
        width: 100%;
        height: 100%;
      }
    `,
  ];
}

export { LogExplorerWorkspaceElement as element };

declare global {
  interface HTMLElementTagNameMap {
    "log-explorer-workspace": LogExplorerWorkspaceElement;
  }
}
