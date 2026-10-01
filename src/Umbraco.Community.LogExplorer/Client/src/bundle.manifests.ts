import { manifests as entrypoints } from "./entrypoints/manifest.js";
import { manifests as workspace } from "./workspace/manifests.js";
import { manifests as search } from "./search/manifests.js";
import { manifests as patterns } from "./patterns/manifests.js";
import { manifests as overview } from "./overview/manifests.js";

/**
 * Every manifest the package registers. `umbraco-package.json` loads this bundle, and each
 * feature folder contributes its own `manifests` array here.
 */
export const manifests: Array<UmbExtensionManifest> = [
  ...entrypoints,
  ...workspace,
  ...search,
  ...patterns,
  ...overview,
];
