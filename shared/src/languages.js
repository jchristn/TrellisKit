/**
 * Language choices for pickers (caption tracks and similar): common BCP 47 tags with names from
 * `Intl.DisplayNames` when the runtime has it, else the tag itself.
 */

/** Common BCP 47 language tags. */
export const COMMON_LANGUAGE_CODES = Object.freeze([
  'ar', 'bn', 'cs', 'da', 'de', 'el', 'en', 'en-GB', 'es', 'es-419', 'fa', 'fi', 'fil', 'fr', 'fr-CA', 'he', 'hi', 'hu', 'id', 'it', 'ja', 'ko', 'ms', 'nl', 'no',
  'pl', 'pt', 'pt-BR', 'ro', 'ru', 'sk', 'sv', 'sw', 'ta', 'th', 'tr', 'uk', 'ur', 'vi', 'zh-Hans', 'zh-Hant'
]);

/**
 * Display name of a language tag.
 * @param {string} code BCP 47 tag.
 * @param {string} [locale] Display locale.
 * @returns {string}
 */
export function languageName(code, locale = 'en') {
  if (!code) return '';
  try {
    if (typeof Intl !== 'undefined' && typeof Intl.DisplayNames === 'function') {
      const name = new Intl.DisplayNames([locale], { type: 'language' }).of(code);
      if (name && name !== code) return name;
    }
  } catch {
    // Invalid tag or no display names: fall back to the tag.
  }
  return code;
}

/**
 * Language options sorted by name, always including `current` (so a stored tag outside the list still shows).
 * @param {string} [current]
 * @param {string} [locale]
 * @returns {Array<{ code: string, name: string }>}
 */
export function languageOptions(current, locale = 'en') {
  const codes = new Set(COMMON_LANGUAGE_CODES);
  if (current) codes.add(current);
  return Array.from(codes).map((code) => ({ code, name: languageName(code, locale) + ' (' + code + ')' })).sort((a, b) => a.name.localeCompare(b.name));
}
