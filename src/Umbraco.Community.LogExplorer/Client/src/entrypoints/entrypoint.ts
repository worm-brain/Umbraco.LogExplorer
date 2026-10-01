import type { UmbEntryPointOnInit, UmbEntryPointOnUnload } from "@umbraco-cms/backoffice/extension-api";

/**
 * Backoffice entry point for Log Explorer.
 *
 * Runs once when the backoffice loads the package bundle. It will configure the generated
 * Management API client with the backoffice's auth (added with the client in #27) and, when
 * `HideCoreLogViewer` is on, hide the core Log Viewer menu item (#47).
 *
 * @param _host - The backoffice host element, used to consume contexts.
 * @param _extensionRegistry - The extension registry, used to exclude core extensions.
 */
export const onInit: UmbEntryPointOnInit = (_host, _extensionRegistry) => {
  // Nothing to set up yet; the scaffold only proves the bundle loads.
};

/**
 * Called when the backoffice unloads the package. The entry point holds no resources yet, so
 * there is nothing to release; the backoffice's entry-point module type requires the export.
 *
 * @param _host - The backoffice host element.
 * @param _extensionRegistry - The extension registry.
 */
export const onUnload: UmbEntryPointOnUnload = (_host, _extensionRegistry) => {};
