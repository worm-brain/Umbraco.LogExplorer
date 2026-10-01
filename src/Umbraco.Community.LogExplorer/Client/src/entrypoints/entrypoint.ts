import type { UmbEntryPointOnInit, UmbEntryPointOnUnload } from "@umbraco-cms/backoffice/extension-api";
import { UMB_AUTH_CONTEXT } from "@umbraco-cms/backoffice/auth";
import { client } from "../api/client.gen.js";

/**
 * Backoffice entry point for Log Explorer.
 *
 * Runs once when the backoffice loads the package bundle. It points the generated Management API
 * client (`src/api`, from the `log-explorer` Swagger document) at the backoffice's base URL and
 * auth, so every call carries the signed-in user's token. Hiding the core Log Viewer menu item
 * when `HideCoreLogViewer` is on is added with #49.
 *
 * @param host - The backoffice host element, used to consume the auth context.
 * @param _extensionRegistry - The extension registry (not used yet).
 */
export const onInit: UmbEntryPointOnInit = (host, _extensionRegistry) => {
  host.consumeContext(UMB_AUTH_CONTEXT, (authContext) => {
    // The auth context supplies a token getter that refreshes as needed, the API base URL and
    // the credentials mode the backoffice itself uses.
    const config = authContext?.getOpenApiConfiguration();
    client.setConfig({
      auth: config?.token ?? undefined,
      baseUrl: config?.base ?? "",
      credentials: config?.credentials ?? "same-origin",
    });
  });
};

/**
 * Called when the backoffice unloads the package. The entry point holds no resources, so there
 * is nothing to release; the backoffice's entry-point module type requires the export.
 *
 * @param _host - The backoffice host element.
 * @param _extensionRegistry - The extension registry.
 */
export const onUnload: UmbEntryPointOnUnload = (_host, _extensionRegistry) => {};
