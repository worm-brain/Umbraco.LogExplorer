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
    timeRangeMenuLabel: "Time ranges",
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
    histogramBarsLabel: "Entries per time bucket",
    // Counts take the number (for the singular) and its locale-formatted text.
    histogramLevelLabel: (level: string, count: number, formatted: string) =>
      `${level}, ${formatted} ${count === 1 ? "entry" : "entries"}`,
    histogramLevelHide: (level: string) => `Hide ${level} entries`,
    histogramLevelShow: (level: string) => `Show ${level} entries`,
    histogramSummary: (count: number, formatted: string, from: string, to: string) =>
      `${formatted} ${count === 1 ? "entry" : "entries"} · ${from} to ${to}`,
    histogramApproximate: "Approximate",
    histogramScannedFrom: (from: string) => `counts from ${from}`,
    histogramUnread: (from: string) =>
      `Not read: the scan limit was reached, so counts start at ${from}. Pick a shorter time range, or drag across the earlier bars to zoom in, to read further back.`,
    histogramApproximateHint:
      "Counts cover only part of the range: the source sampled its data or stopped at its scan budget.",
    histogramBarLabel: (time: string, count: number, formatted: string) =>
      `${time}, ${formatted} ${count === 1 ? "entry" : "entries"}, select to zoom in`,
    histogramError: (message: string) => `Could not load the histogram: ${message}`,
    histogramAlreadyZoomed: "Already zoomed in. Clear the time chip to zoom out.",
    zoomChip: (from: string, to: string) => `Time: ${from} to ${to}`,
    zoomChipRemove: (chip: string) => `Remove filter ${chip}`,
    // Search box (UI brief §4.3, §4.14).
    searchLabel: "Search logs",
    searchPlaceholder: "Search text, or filter with field:value  ·  try  level:warn  path:/umbraco*  -status:200",
    searchPlaceholderMore: "Add another filter…",
    searchUnbalancedQuote: "Unbalanced quote, so this was searched as plain text",
    searchParseError: (message: string) => `Could not read the search: ${message}`,

    // Filter chips (UI brief §4.4). Short field names are chip labels; the description is the
    // tooltip and the accessible name, with the full field name.
    fieldPath: "Path",
    fieldSource: "Source",
    fieldStatus: "Status",
    fieldMachine: "Machine",
    fieldException: "Exception",
    fieldRequest: "Request",
    fieldTemplate: "Template",
    fieldText: "Text",
    chipNot: (text: string) => `not ${text}`,
    chipHas: (field: string) => `has ${field}`,
    chipDescription: (field: string, operator: string, value: string) => `${field} ${operator} ${value}`.trim(),
    chipRemove: (description: string) => `Remove filter ${description}`,
    chipEdit: (description: string) => `Edit filter ${description}`,
    opEquals: "equals",
    opNotEquals: "does not equal",
    opStartsWith: "starts with",
    opContains: "contains",
    opContainsPhrase: "contains the phrase",
    opEndsWith: "ends with",
    opGreaterThan: "greater than",
    opGreaterOrEqual: "greater than or equal to",
    opLessThan: "less than",
    opLessOrEqual: "less than or equal to",
    opIn: "is one of",
    opExists: "exists",
    opNotExists: "does not exist",
    opMatches: "matches",
    chipEditorOperator: "Operator",
    chipEditorValue: "Value",
    chipEditorText: "Text",
    chipEditorPhrase: "Exact phrase",
    chipEditorExclude: "Exclude matching entries",
    chipEditorSave: "Save",
    chipEditorCancel: "Cancel",

    // Fields panel and results list (UI brief §4.8, §4.9).
    fieldsHeader: "Fields",
    fieldsSubheader: "top values in results",
    fieldsHide: "Hide fields panel",
    fieldsShow: "Show fields panel",
    fieldsResize: "Resize fields panel",
    fieldsResizeHint: "Drag, or use the arrow keys, to resize the fields panel. Double-click to reset.",
    fieldsFilterLabel: "Filter fields",
    fieldsPresence: (percent: string) => `${percent}% of entries`,
    fieldsInclude: (field: string, value: string, count: string) => `Include ${field} ${value}, ${count} entries`,
    fieldsExclude: (field: string, value: string) => `Exclude ${field} ${value}`,
    fieldsApproximateHint: (from: string, to: string) =>
      `The source sampled or stopped at its scan budget, so these counts cover ${from} to ${to} only.`,
    fieldsEmpty: "No field values in the current results.",
    fieldsNoMatch: "No fields match the filter.",
    fieldsError: (message: string) => `Could not load fields: ${message}`,
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
    detailLabel: "Entry details",
    detailClose: "Close entry details",
    detailFilterOn: (field: string) => `Filter on ${field}`,
    detailInclude: (name: string, value: string) => `Include ${name} ${value}`,
    detailExclude: (name: string, value: string) => `Exclude ${name} ${value}`,
    detailExpand: (name: string) => `Expand ${name}`,
    detailCollapse: (name: string) => `Collapse ${name}`,
    detailPropertyName: "Name",
    detailPropertyValue: "Value",
    detailPropertyFilter: "Filter",
    detailNoProperties: "This entry has no properties.",
    detailObjectSummary: (count: number) => (count === 1 ? "1 property" : `${count} properties`),
    detailArraySummary: (count: number) => (count === 1 ? "1 item" : `${count} items`),
    detailShowFrameworkFrames: "Show framework frames",
    detailHideFrameworkFrames: "Hide framework frames",
    detailFrameworkFramesHidden: (count: number) =>
      count === 1 ? "… 1 framework frame hidden" : `… ${count} framework frames hidden`,
    detailCopied: "Entry copied as JSON",
    detailCopyFailed: "Could not copy the entry to the clipboard",
    detailSamePatternApplied: (template: string) => `Filtering on the message template ${template}`,
    actionSameRequest: "Same request",
    actionAroundThis: "Around this",
    actionSamePattern: "Same pattern",
    actionCopyJson: "Copy as JSON",
    detailSameRequestApplied: (id: string) => `Showing every entry from request ${id}`,
    detailSameRequestUnavailable: (fields: string) => `Same request needs a value in one of: ${fields}`,
    aroundBanner: (time: string) => `Showing 7 entries either side of ${time}, ignoring filters`,
    aroundBannerNoAnchor: "Entries around the selected entry, ignoring filters",
    aroundError: (message: string) => `Could not load the entries around this entry: ${message}`,
    aroundBack: "Back to filtered results",

    // Saved views, show query and share (UI brief §4.5, §4.6, §4.14).
    savedViewsCaption: "Saved views",
    savedViewsSaveCurrent: "Save current view…",
    showQueryEditAsNative: "Edit as native",
    showQueryButton: "Show generated query",
    shareButton: "Copy link to this view",
    shareCopied: "Link to this exact view copied",
    shareCopyFailed: "Could not copy the link to the clipboard",
    showQueryLabel: (language: string) => `Generated ${language} query`,
    showQueryEmpty: "(no filter: every entry in the time range)",
    showQueryError: (message: string) => `Could not compile the query: ${message}`,
    chipUnsupported: (source: string) => `Not supported by ${source}`,
    chipUnsupportedDescription: (description: string, source: string) => `${description}, not supported by ${source}`,
    chipNotShown: (description: string, language: string) =>
      `${description} (runs, but not shown in the ${language} query)`,
    showQueryNotShown: (language: string, description: string) => `Not shown in ${language}: ${description}`,
    showQueryNothingShown: (language: string) => `(nothing to show in ${language}: the filters below still apply)`,
    showQueryKeptAsChips: (language: string) =>
      `Filters ${language} cannot express stay as chips and still apply with the native query`,
    showQuerySplitsGroup: (language: string) =>
      `Edit as native is unavailable: some filters on one field can be shown in ${language} and some cannot. Change or remove one first.`,

    // Native mode in the search box (BRIEF §6.2, UI brief §4.15).
    nativeModeLabel: (language: string) => `Search logs with a ${language} query`,
    nativeModePlaceholder: (language: string) => `Type a ${language} query, then press Enter`,
    nativeModeLeave: (language: string) => `Leave ${language} mode and return to simple search`,
    nativeInvalid: (message: string) => `Invalid query: ${message}`,
    nativeInvalidAt: (message: string, position: string) => `Invalid query at character ${position}: ${message}`,
    nativeValidateError: (message: string) => `Could not check the query: ${message}`,

    // Patterns view (UI brief §4.12, §4.14).
    patternsLabel: "Message patterns",
    patternsHeader: (count: string) => `Message template · ${count} patterns in the current results`,
    patternsHeaderOne: "Message template · 1 pattern in the current results",
    patternsColumnLevelMix: "Level mix",
    patternsColumnVolume: (from: string, to: string) => `Volume, ${from} to ${to}`,
    patternsColumnCount: "Count",
    patternsColumnActions: "Actions",
    patternsSample: (source: string, sample: string) => `${source} · e.g. ${sample}`,
    patternsSampleNoSource: (sample: string) => `e.g. ${sample}`,
    patternsLevelMix: (levels: string) => `Level mix: ${levels}`,
    patternsVolume: (count: string, peak: string) => `${count} entries, at most ${peak} in one interval`,
    patternsVolumeOne: "1 entry",
    patternsFocus: "Focus",
    patternsFocusLabel: (template: string) => `Focus on ${template}`,
    patternsMute: "Mute",
    patternsMuteLabel: (template: string) => `Mute ${template}`,
    patternsMuted: (template: string) => `Muted: ${template}`,
    patternsEmpty: "No patterns match. Remove a filter or widen the time range.",
    patternsError: (message: string) => `Could not load patterns: ${message}`,
    patternsUnsupported: (source: string) => `${source} cannot group entries by message template.`,
    patternsLevelsFiltered: (levels: string) => `Only ${levels} entries`,
    patternsShowAllLevels: "Show all levels",

    // Overview (UI brief §4.13).
    overviewEntriesByLevel: (range: string) => `Entries by level · ${range}`,
    overviewMinimumLevels: "Minimum levels (configuration)",
    overviewFrequentMessages: "Most frequent messages",
    overviewExceptionTypes: "Exception types",
    overviewLevelHidden: (level: string) => `${level} entries are hidden by the level filter`,
    overviewLevelsUnsupported: (source: string) => `${source} cannot count entries by level.`,
    overviewLevelsError: (message: string) => `Could not count entries by level: ${message}`,
    overviewMinimumLevelsError: (message: string) => `Could not read the minimum levels: ${message}`,
    overviewFocusTemplate: (template: string, count: string) => `Show the ${count} entries of ${template} in Search`,
    overviewMessagesEmpty: "No messages in the current results.",
    overviewFocusException: (type: string, count: string) => `Show the ${count} ${type} entries in Search`,
    overviewExceptionsUnsupported: (source: string) => `${source} cannot count exception types.`,
    overviewExceptionsError: (message: string) => `Could not load exception types: ${message}`,
    overviewExceptionsEmpty: "No exceptions in the current results.",
  },
} satisfies UmbLocalizationDictionary;
