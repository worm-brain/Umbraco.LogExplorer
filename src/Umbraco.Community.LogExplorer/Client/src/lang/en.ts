import type { UmbLocalizationDictionary } from "@umbraco-cms/backoffice/localization-api";

/**
 * English strings for Log Explorer, the only language shipped in v1 (UI brief §8).
 *
 * Every key sits in the `logExplorer` section, so manifests reference them as
 * `#logExplorer_{key}` and elements as `this.localize.term("logExplorer_{key}")`. The backoffice
 * falls back to `en` for any culture without its own dictionary, so users on other languages see
 * these strings rather than missing-key markers.
 *
 * Entries that take values are functions; `localize.term(key, ...args)` passes the arguments
 * through. Copy-deck strings for slices not built yet are seeded here so those slices start
 * from keys.
 */
export default {
  logExplorer: {
    // Workspace, menu item and tabs.
    title: "Log Explorer",
    tabSearch: "Search",
    tabPatterns: "Patterns",
    tabOverview: "Overview",

    // Empty views, until each view is built.
    searchEmpty: "No entries to show yet. Search, the histogram and the fields panel arrive here.",
    patternsEmpty: "No patterns to show yet. Message templates grouped by count arrive here.",
    overviewEmpty: "Nothing to summarise yet. Counts by level, frequent messages and exception types arrive here.",

    // Source picker (UI brief §4.1).
    sourcesLoading: "Loading log sources…",
    sourcesEmpty: "No log sources are available. Add one under LogExplorer:Sources in appsettings.json.",
    sourcesError: (message: string) => `Could not load log sources: ${message}`,
    sourcesMenuCaption: "Log sources (from appsettings)",
    sensitiveSource: "Sensitive source",
    retry: "Retry",

    // Fields panel and results list (UI brief §4.8, §4.9).
    fieldsHeader: "Fields",
    fieldsSubheader: "top values in results",
    columnTime: "Time",
    columnLevel: "Level",
    columnMessage: "Message",
    columnSource: "Source",
    resultsShowing: (shown: string, total: string) => `Showing ${shown} of ${total}`,
    resultsLoadMore: "Load 60 more",
    resultsEmpty: "No entries match. Remove a filter or widen the time range.",

    // Entry detail drawer and the around banner (UI brief §4.10, §4.11).
    detailMessageTemplate: "Message template",
    detailProperties: "Properties",
    detailException: "Exception",
    actionSameRequest: "Same request",
    actionAroundThis: "Around this",
    actionSamePattern: "Same pattern",
    actionCopyJson: "Copy as JSON",
    aroundBanner: (time: string) => `Showing 7 entries either side of ${time}, ignoring filters`,
    aroundBack: "Back to filtered results",

    // Saved views and show query (UI brief §4.5, §4.6).
    savedViewsCaption: "Saved views",
    savedViewsSaveCurrent: "Save current view…",
    showQueryEditAsNative: "Edit as native",

    // Overview (UI brief §4.13).
    overviewEntriesByLevel: (range: string) => `Entries by level · ${range}`,
    overviewMinimumLevels: "Minimum levels (configuration)",
    overviewFrequentMessages: "Most frequent messages",
    overviewExceptionTypes: "Exception types",
  },
} satisfies UmbLocalizationDictionary;
