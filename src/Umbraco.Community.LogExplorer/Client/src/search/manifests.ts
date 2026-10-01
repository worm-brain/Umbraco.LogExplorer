import { UMB_WORKSPACE_CONDITION_ALIAS } from "@umbraco-cms/backoffice/workspace";
import { LOG_EXPLORER_WORKSPACE_ALIAS } from "../workspace/constants.js";

/**
 * The Search tab of the Log Explorer workspace. Tabs sort by weight, highest first, so the weights
 * give Search (300), Patterns (200), Overview (100); the first tab is the workspace's default.
 */
export const manifests: Array<UmbExtensionManifest> = [
  {
    type: "workspaceView",
    alias: "Umbraco.Community.LogExplorer.WorkspaceView.Search",
    name: "Log Explorer Search Workspace View",
    element: () => import("./search-view.element.js"),
    weight: 300,
    meta: {
      // TODO: localise (UI brief §8) once the package registers a localization manifest.
      label: "Search",
      pathname: "search",
      icon: "icon-search",
    },
    conditions: [{ alias: UMB_WORKSPACE_CONDITION_ALIAS, match: LOG_EXPLORER_WORKSPACE_ALIAS }],
  },
];
