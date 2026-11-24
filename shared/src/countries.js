/**
 * ISO 3166-1 alpha-2 country list shared by the website and the mobile app (the C# twin is
 * `Countries.cs`). Names come from `Intl.DisplayNames` when the runtime has it, else the code.
 */

/** ISO 3166-1 alpha-2 country codes. */
export const COUNTRY_CODES = Object.freeze(('AD AE AF AG AI AL AM AO AQ AR AS AT AU AW AX AZ BA BB BD BE BF BG BH BI BJ BL BM BN BO BQ BR BS BT BV BW BY BZ CA CC CD CF CG CH CI CK CL CM CN CO CR CU CV CW CX CY CZ ' +
  'DE DJ DK DM DO DZ EC EE EG EH ER ES ET FI FJ FK FM FO FR GA GB GD GE GF GG GH GI GL GM GN GP GQ GR GS GT GU GW GY HK HM HN HR HT HU ID IE IL IM IN IO IQ IR IS IT JE JM JO JP ' +
  'KE KG KH KI KM KN KP KR KW KY KZ LA LB LC LI LK LR LS LT LU LV LY MA MC MD ME MF MG MH MK ML MM MN MO MP MQ MR MS MT MU MV MW MX MY MZ NA NC NE NF NG NI NL NO NP NR NU NZ ' +
  'OM PA PE PF PG PH PK PL PM PN PR PS PT PW PY QA RE RO RS RU RW SA SB SC SD SE SG SH SI SJ SK SL SM SN SO SR SS ST SV SX SY SZ TC TD TF TG TH TJ TK TL TM TN TO TR TT TV TW TZ ' +
  'UA UG UM US UY UZ VA VC VE VG VI VN VU WF WS YE YT ZA ZM ZW').split(' '));

const CODE_SET = new Set(COUNTRY_CODES);
const caches = new Map();

/** Whether a value is a known ISO 3166-1 alpha-2 code (case-insensitive). */
export function isCountryCode(value) {
  return typeof value === 'string' && CODE_SET.has(value.trim().toUpperCase());
}

/**
 * Countries sorted by name: `[{ code, name }]`.
 * @param {string} [locale] defaults to English
 */
export function countryOptions(locale = 'en') {
  if (caches.has(locale)) return caches.get(locale);
  let names = null;
  try {
    names = typeof Intl !== 'undefined' && Intl.DisplayNames ? new Intl.DisplayNames([locale], { type: 'region' }) : null;
  } catch {
    names = null;
  }
  const list = Object.freeze(COUNTRY_CODES
    .map((code) => {
      let name = code;
      try { name = (names && names.of(code)) || code; } catch { name = code; }
      return Object.freeze({ code, name });
    })
    .sort((x, y) => x.name.localeCompare(y.name, locale)));
  caches.set(locale, list);
  return list;
}

/** The name of a country code, or the code itself ('' for empty). */
export function countryName(code, locale = 'en') {
  if (!code) return '';
  const upper = String(code).toUpperCase();
  const found = countryOptions(locale).find((c) => c.code === upper);
  return found ? found.name : String(code);
}
