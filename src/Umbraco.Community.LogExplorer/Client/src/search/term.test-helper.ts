import en from "../lang/en.js";
import type { Term } from "./chip-format.js";

/**
 * A `localize.term` backed by the shipped English dictionary, so tests assert the strings users
 * see and fail when a key is missing.
 */
export const enTerm: Term = (key, ...args) => {
  const entry = (en.logExplorer as Record<string, unknown>)[key.replace(/^logExplorer_/, "")];
  if (typeof entry === "function") return (entry as (...values: Array<string>) => string)(...args);
  if (typeof entry === "string") return entry;
  throw new Error(`Missing dictionary key ${key}`);
};
