/**
 * The package's English dictionary. `culture: "en"` is also the backoffice's fallback culture,
 * so these strings show for every user language until other dictionaries ship.
 */
export const manifests: Array<UmbExtensionManifest> = [
  {
    type: "localization",
    alias: "Umbraco.Community.LogExplorer.Localization.En",
    name: "Log Explorer English Localization",
    meta: { culture: "en" },
    js: () => import("./en.js"),
  },
];
