import { UMB_WORKSPACE_CONDITION_ALIAS } from "@umbraco-cms/backoffice/workspace";
import { LOG_EXPLORER_WORKSPACE_ALIAS } from "../workspace/constants.js";

/**
 * The Overview tab of the Log Explorer workspace. Tabs sort by weight, highest first, so the weights
 * give Search (300), Patterns (200), Overview (100); the first tab is the workspace's default.
 */
export const manifests: Array<UmbExtensionManifest> = [
  {
    type: "workspaceView",
    alias: "Umbraco.Community.LogExplorer.WorkspaceView.Overview",
    name: "Log Explorer Overview Workspace View",
    element: () => import("./overview-view.element.js"),
    weight: 100,
    meta: {
      label: "#logExplorer_tabOverview",
      pathname: "overview",
      icon: "icon-bar-chart",
    },
    conditions: [{ alias: UMB_WORKSPACE_CONDITION_ALIAS, match: LOG_EXPLORER_WORKSPACE_ALIAS }],
  },
];
