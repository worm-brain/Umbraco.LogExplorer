import { UMB_ADVANCED_SETTINGS_MENU_ALIAS } from "@umbraco-cms/backoffice/settings";
import { LOG_EXPLORER_ENTITY_TYPE, LOG_EXPLORER_WORKSPACE_ALIAS } from "./constants.js";

/**
 * Weight of the Log Explorer menu item. The Settings advanced menu sorts highest first; the core
 * items are Relations (800), Log Viewer (`Umb.MenuItem.LogViewer`, 300), Extension Insights (200)
 * and Webhooks (100) in both 17.x and 18.2, so 250 places Log Explorer directly below Log Viewer.
 */
const MENU_ITEM_WEIGHT = 250;

/**
 * Manifests for the Settings menu item and the workspace it opens.
 *
 * - `menuItem` in `Umb.Menu.AdvancedSettings` (Settings > Advanced). With no `kind` it is the
 *   default menu item, which links to the workspace registered for its `entityType`.
 * - `workspace` of the `default` kind, which supplies `UmbDefaultWorkspaceContext` (so the views'
 *   workspace condition can match the alias, and the browser title follows `headline`); the
 *   `element` replaces the kind's element to add the source picker to the header.
 */
export const manifests: Array<UmbExtensionManifest> = [
  {
    type: "menuItem",
    alias: "Umbraco.Community.LogExplorer.MenuItem",
    name: "Log Explorer Menu Item",
    weight: MENU_ITEM_WEIGHT,
    meta: {
      label: "#logExplorer_title",
      icon: "icon-search",
      entityType: LOG_EXPLORER_ENTITY_TYPE,
      menus: [UMB_ADVANCED_SETTINGS_MENU_ALIAS],
    },
  },
  {
    type: "workspace",
    kind: "default",
    alias: LOG_EXPLORER_WORKSPACE_ALIAS,
    name: "Log Explorer Workspace",
    element: () => import("./workspace.element.js"),
    meta: {
      entityType: LOG_EXPLORER_ENTITY_TYPE,
      // The default workspace kind localises `#` keys for the browser title.
      headline: "#logExplorer_title",
    },
  },
];
