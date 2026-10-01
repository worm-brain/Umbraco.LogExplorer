import { SettingsService, type SettingsResponseModel } from "../api/index.js";

/** Alias of the core Settings > Advanced > Log Viewer menu item, the same in 17 and 18. */
export const CORE_LOG_VIEWER_MENU_ITEM_ALIAS = "Umb.MenuItem.LogViewer";

/**
 * The shape of `SettingsService.getSettings()` the loader depends on: hey-api's non-throwing
 * result, where a failed request sets `error` instead of `data`.
 */
export type FetchSettings = () => Promise<{ data?: SettingsResponseModel; error?: unknown }>;

/** The part of the extension registry {@link hideCoreLogViewerIfConfigured} needs. */
export interface ExtensionExcluder {
  exclude(alias: string): void;
}

/**
 * Fetches the client settings (`GET /settings`, ADR 0011).
 *
 * Never throws. A user without Settings-section access gets 403 here, which is expected rather
 * than an error worth surfacing, so every failure just yields `undefined`.
 *
 * @param fetchSettings - The request to make; defaults to the generated client, tests pass a stub.
 * @returns The settings, or `undefined` when the request failed for any reason.
 */
export async function loadSettings(
  fetchSettings: FetchSettings = () => SettingsService.getSettings(),
): Promise<SettingsResponseModel | undefined> {
  try {
    const result = await fetchSettings();
    return result.error === undefined ? result.data : undefined;
  } catch {
    // fetch rejects on network failures and aborts rather than returning a result.
    return undefined;
  }
}

/**
 * Removes the core Log Viewer menu item when `HideCoreLogViewer` is on (ADR 0007).
 *
 * Missing settings keep the core item: if the explorer cannot reach its own API, the core viewer
 * is the user's only way to read logs. The exclusion lives in the registry only, so uninstalling
 * the package brings the item back with no clean-up.
 *
 * @param settings - The loaded settings, or `undefined` when loading failed.
 * @param registry - The backoffice extension registry.
 * @returns `true` when the core item was excluded.
 */
export function hideCoreLogViewerIfConfigured(
  settings: SettingsResponseModel | undefined,
  registry: ExtensionExcluder,
): boolean {
  if (!settings?.hideCoreLogViewer) return false;
  registry.exclude(CORE_LOG_VIEWER_MENU_ITEM_ALIAS);
  return true;
}
