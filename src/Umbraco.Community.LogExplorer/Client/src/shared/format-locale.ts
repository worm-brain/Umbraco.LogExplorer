/**
 * The locale to format dates and numbers in for a backoffice language.
 *
 * The backoffice names its UK English localization plain `en` (and US English `en-us`), but
 * `Intl` reads a bare `en` as US English, so UK users would see month-first dates. Map it to
 * `en-GB`; every other tag already carries its region or is unambiguous.
 *
 * @param lang - The backoffice language, as `localize.lang()` returns it (lower case).
 * @returns A BCP 47 tag for `Intl` formatting.
 */
export function formatLocale(lang: string): string {
  return lang.toLowerCase() === "en" ? "en-GB" : lang;
}
