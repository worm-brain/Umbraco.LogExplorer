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
    patternsEmpty: "No patterns to show yet. Message templates grouped by count arrive here.",
    overviewEmpty: "Nothing to summarise yet. Counts by level, frequent messages and exception types arrive here.",

    // Source picker (UI brief §4.1).
    sourcesLoading: "Loading log sources…",
    sourcesEmpty: "No log sources are available. Add one under LogExplorer:Sources in appsettings.json.",
    sourcesError: (message: string) => `Could not load log sources: ${message}`,
    sourcesMenuCaption: "Log sources (from appsettings)",
    sourcePickerLabel: (name: string) => `Log source: ${name}`,
    sourceNote: (language: string, type: string) => `${language} · ${type}`,
    sourceItemLabel: (name: string, note: string) => `${name}, ${note}`,
    sourceItemLabelSensitive: (name: string, note: string) => `${name}, sensitive source, ${note}`,
    sensitiveSource: "Sensitive source",
    retry: "Retry",

    // Time range picker (UI brief §4.2).
    timeRangePickerLabel: (range: string) => `Time range: ${range}`,
    timeRangeLast15m: "Last 15 minutes",
    timeRangeLast1h: "Last 1 hour",
    timeRangeLast4h: "Last 4 hours",
    timeRangeLast24h: "Last 24 hours",
    timeRangeLast7d: "Last 7 days",
    timeRangeLast30d: "Last 30 days",
    timeRangeCustom: "Custom range…",
    timeRangeFrom: "From",
    timeRangeTo: "To",
    timeRangeTimezone: (zone: string) => `Times are in ${zone}`,
    timeRangeInvalid: "Choose a start that is before the end.",
    timeRangeApply: "Apply",

    // Histogram and its level toggles (UI brief §4.7), and the time chip (§4.4).
    histogramLabel: "Entries over time",
    histogramLevelsLabel: "Show or hide levels",
    histogramLevelLabel: (level: string, count: string) => `${level}, ${count} entries`,
    histogramLevelHide: (level: string) => `Hide ${level} entries`,
    histogramLevelShow: (level: string) => `Show ${level} entries`,
    histogramSummary: (count: string, from: string, to: string) => `${count} entries · ${from} to ${to}`,
    histogramApproximate: "Approximate",
    histogramApproximateHint:
      "Counts cover only part of the range: the source sampled its data or stopped at its scan budget.",
    histogramBarLabel: (time: string, count: string) => `${time}, ${count} entries, select to zoom in`,
    histogramError: (message: string) => `Could not load the histogram: ${message}`,
    histogramAlreadyZoomed: "Already zoomed in. Clear the time chip to zoom out.",
    zoomChip: (from: string, to: string) => `Time: ${from} to ${to}`,
    zoomChipRemove: (chip: string) => `Remove filter ${chip}`,

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
    resultsOpenEntry: (time: string, level: string) => `Open entry ${time} ${level}`,
    resultsError: (message: string) => `Could not load entries: ${message}`,
    resultsSortNewestFirst: "Time, newest first. Select to show oldest first",
    resultsSortOldestFirst: "Time, oldest first. Select to show newest first",

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
