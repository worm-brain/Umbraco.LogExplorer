/** Manifests for the package's backoffice entry point. */
export const manifests: Array<UmbExtensionManifest> = [
  {
    name: "Umbraco Community Log Explorer Entrypoint",
    alias: "Umbraco.Community.LogExplorer.Entrypoint",
    type: "backofficeEntryPoint",
    js: () => import("./entrypoint.js"),
  },
];
