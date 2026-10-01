import { manifests as entrypoints } from "./entrypoints/manifest.js";

/**
 * Every manifest the package registers. `umbraco-package.json` loads this bundle, and each
 * feature folder contributes its own `manifests` array here.
 */
export const manifests: Array<UmbExtensionManifest> = [...entrypoints];
