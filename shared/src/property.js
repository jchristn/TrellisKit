/**
 * Property helpers. A property is the configured personality an engine deployment runs as
 * (name, brand, membership, profile taxonomy, reactions). Its shape is defined by
 * `property.json` (docs/TRELLISKIT.md section 2) and served at `GET /api/v1/property`.
 *
 * Nothing here knows any specific property: every label, color, and word comes from the
 * configuration passed in. No browser-only or Node-only APIs are used.
 */

/** The property schema version these helpers understand. */
export const PropertySchemaVersion = 1;

import { minimumAgeOf } from './access.js';

const HEX_COLOR = /^#[0-9A-Fa-f]{6}$/;

/** Generated property id, app_{k-sortable}_{random} (PrettyId); mirrors the backend's PropertyIds. */
export const PropertyIdPattern = /^app_[a-z0-9]{6,16}_[A-Za-z0-9]{8,40}$/;

/** Cosmetic slug: lower case letters, digits, and hyphens, 1 to 63 characters. */
export const PropertySlugPattern = /^[a-z0-9][a-z0-9-]{0,62}$/;

/** Pseudo-ids no property may use as its id or slug. */
export const ReservedPropertyIds = Object.freeze(['platform', 'unresolved', 'none', 'all', 'system']);

/**
 * Whether a value is an acceptable property id: a generated id (app_{k-sortable}_{random}).
 * Reserved pseudo-ids never are.
 * @param {string} id
 * @returns {boolean}
 */
export function isValidPropertyId(id) {
  if (typeof id !== 'string' || !id || ReservedPropertyIds.includes(id)) return false;
  return PropertyIdPattern.test(id);
}

/**
 * Whether a value is a well-formed cosmetic slug (and not a reserved pseudo-id).
 * @param {string} slug
 * @returns {boolean}
 */
export function isValidPropertySlug(slug) {
  return typeof slug === 'string' && PropertySlugPattern.test(slug) && !ReservedPropertyIds.includes(slug);
}

/**
 * The property's cosmetic slug, for human-readable, filesystem-safe, or URL-safe names. Mirrors the
 * backend's EffectiveSlug: the configured `slug`, else one derived from
 * shortName (or name), else "property". Never use it to identify the property in requests or data.
 * @param {object} property
 * @returns {string}
 */
export function propertySlug(property) {
  const p = property || {};
  if (typeof p.slug === 'string' && p.slug.trim()) return p.slug.trim();
  const source = String((p.shortName && String(p.shortName).trim()) || p.name || '').toLowerCase();
  let slug = '';
  for (const c of source) {
    if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) slug += c;
    else if (slug.length > 0 && !slug.endsWith('-')) slug += '-';
  }
  slug = slug.replace(/^-+|-+$/g, '');
  if (slug.length > 63) slug = slug.substring(0, 63).replace(/-+$/, '');
  return slug || 'property';
}
const COLOR_KEYS = ['primary', 'primaryHover', 'onPrimary', 'accent', 'onAccent', 'accentSoft', 'themeColor'];
const OPTIONAL_COLOR_KEYS = ['onAccentSoft'];

function isObject(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function nonEmpty(value) {
  return typeof value === 'string' && value.trim().length > 0;
}

function checkKeys(values, path, problems, extra) {
  if (!Array.isArray(values)) {
    problems.push(path + ' must be an array');
    return;
  }
  const seen = new Set();
  values.forEach((entry, i) => {
    const at = path + '[' + i + ']';
    if (!isObject(entry) || !nonEmpty(entry.key)) {
      problems.push(at + '.key is required');
      return;
    }
    if (seen.has(entry.key)) problems.push(path + ' has a duplicate key "' + entry.key + '"');
    seen.add(entry.key);
    if (!nonEmpty(entry.label)) problems.push(at + '.label is required');
    if (extra) extra(entry, at);
  });
}

/**
 * Validate the shape of a property object. Returns human-readable problems; an empty array means
 * the property is usable. The backend's startup validation is authoritative; this mirrors it for
 * clients and build tooling.
 * @param {object} property
 * @returns {string[]}
 */
export function validateProperty(property) {
  const problems = [];
  if (!isObject(property)) return ['property must be an object'];
  if (property.schemaVersion !== PropertySchemaVersion) problems.push('schemaVersion must be ' + PropertySchemaVersion);
  for (const key of ['id', 'name']) {
    if (!nonEmpty(property[key])) problems.push(key + ' is required');
  }
  if (property.id && !isValidPropertyId(property.id)) problems.push('id must be a generated property id of the form app_{k-sortable}_{random} (for example app_mwcnf2ia_Yk3hhcUgbET5kqGV)');
  if (property.slug !== undefined && property.slug !== null && !isValidPropertySlug(property.slug)) problems.push('slug must be lower case letters, digits, and hyphens (1 to 63 characters) and not a reserved name');
  if (!isObject(property.domains) || !nonEmpty(property.domains.web)) problems.push('domains.web is required');

  const colors = property.brand && property.brand.colors;
  if (!isObject(colors)) {
    problems.push('brand.colors is required');
  } else {
    for (const scheme of ['light', 'dark']) {
      const set = colors[scheme];
      if (!isObject(set)) {
        problems.push('brand.colors.' + scheme + ' is required');
        continue;
      }
      for (const key of COLOR_KEYS) {
        if (!HEX_COLOR.test(set[key] || '')) problems.push('brand.colors.' + scheme + '.' + key + ' must be a #RRGGBB color');
      }
      // Optional (defaults to primary): the text color on accentSoft (docs/TRELLISKIT.md section 2.1).
      for (const key of OPTIONAL_COLOR_KEYS) {
        if (set[key] !== undefined && set[key] !== null && set[key] !== '' && !HEX_COLOR.test(set[key])) problems.push('brand.colors.' + scheme + '.' + key + ' must be a #RRGGBB color');
      }
    }
  }

  const membership = property.membership;
  // membership is optional only when nothing is sold: the Open access model, or a server that
  // reports purchases off (GET /api/v1/property sends membership: null then).
  const access = isObject(property.access) ? property.access : {};
  const membershipOptional = access.model === 'Open' || access.purchasesEnabled === false;
  if (!isObject(membership)) {
    if (!(membershipOptional && (membership === null || membership === undefined))) problems.push('membership is required');
  } else {
    if (!Number.isInteger(membership.priceUsdCents) || membership.priceUsdCents <= 0) problems.push('membership.priceUsdCents must be a positive whole number');
    if (!Number.isInteger(membership.minimumAge) || membership.minimumAge < 0) problems.push('membership.minimumAge must be a whole number');
  }

  const profile = property.profile || {};
  if (profile.rank && profile.rank.enabled) {
    checkKeys(profile.rank.values, 'profile.rank.values', problems, (entry, at) => {
      if (entry.color !== undefined && !HEX_COLOR.test(entry.color)) problems.push(at + '.color must be a #RRGGBB color');
    });
    if (!Number.isInteger(profile.rank.maxLevel) || profile.rank.maxLevel < 0) problems.push('profile.rank.maxLevel must be a whole number');
  }
  if (profile.goals && profile.goals.enabled) checkKeys(profile.goals.values, 'profile.goals.values', problems);
  if (property.reactions !== undefined) {
    checkKeys(property.reactions, 'reactions', problems, (entry, at) => {
      if (!nonEmpty(entry.emoji)) problems.push(at + '.emoji is required');
    });
  }
  return problems;
}

/**
 * Parse and validate a property (a JSON string or an object).
 * @param {string|object} source
 * @returns {object} the property
 * @throws {Error} when the JSON is invalid or the shape has problems (the message names them)
 */
export function loadProperty(source) {
  let property = source;
  if (typeof source === 'string') {
    try {
      property = JSON.parse(source);
    } catch (err) {
      throw new Error('property.json is not valid JSON: ' + err.message);
    }
  }
  const problems = validateProperty(property);
  if (problems.length) throw new Error('property.json is invalid: ' + problems.join('; '));
  return property;
}

/**
 * Merge a runtime property (from `GET /api/v1/property`) over a build-time copy. The runtime copy
 * wins for every field it provides; fields only the build knows (for example `web`) are kept.
 * Invalid runtime values are ignored.
 * @param {object} base
 * @param {object} runtime
 * @returns {object}
 */
export function mergeProperty(base, runtime) {
  if (!isObject(runtime) || validateProperty({ ...base, ...runtime }).length) return base;
  return { ...base, ...runtime };
}

// ---- Server capabilities ----

/**
 * Capabilities the server announces in `GET /api/v1/property` `features` (booleans) for sections
 * whose backend ships separately from the clients. Unlike property feature switches (where a
 * missing flag means on), a capability is available only when the server sends `true`.
 */
export const ServerCapability = Object.freeze({ Forums: 'forums', Search: 'search' });

/**
 * Whether the server reports a capability (`features[key] === true`). Missing or any other value
 * counts as unavailable, so clients show a "coming soon" page and make no calls for it.
 * @param {object} property
 * @param {string} key a ServerCapability value
 * @returns {boolean}
 */
export function hasServerCapability(property, key) {
  const features = property && isObject(property.features) ? property.features : null;
  return Boolean(features && features[key] === true);
}

// ---- Profile configuration ----

function section(property, key) {
  const profile = (property && property.profile) || {};
  const value = profile[key];
  return isObject(value) ? value : { enabled: false };
}

/** The `profile.rank` section, or `{ enabled: false }`. */
export function rankConfig(property) {
  const config = section(property, 'rank');
  return { ...config, values: Array.isArray(config.values) ? config.values : [], maxLevel: Number.isInteger(config.maxLevel) ? config.maxLevel : 0 };
}

/** The `profile.rankHistory` section, or `{ enabled: false }`. */
export function rankHistoryConfig(property) {
  return section(property, 'rankHistory');
}

/** The `profile.affiliation` section, or `{ enabled: false }`. */
export function affiliationConfig(property) {
  return section(property, 'affiliation');
}

/** The `profile.goals` section, or `{ enabled: false }`. */
export function goalsConfig(property) {
  const config = section(property, 'goals');
  return { ...config, values: Array.isArray(config.values) ? config.values : [] };
}

/** Whether a profile section (`rank`, `rankHistory`, `affiliation`, `goals`) is enabled. */
export function isProfileSectionEnabled(property, key) {
  return Boolean(section(property, key).enabled);
}

/** The rank value `{ key, label, color }` for a key, or null. */
export function findRank(property, key) {
  if (!key) return null;
  return rankConfig(property).values.find((v) => v.key === key) || null;
}

/** Display label for a rank key (the key itself when unknown, '' when empty). */
export function rankLabel(property, key) {
  if (!key) return '';
  const value = findRank(property, key);
  return value ? value.label : String(key);
}

/** Color for a rank key, or null. */
export function rankColor(property, key) {
  const value = findRank(property, key);
  return value && value.color ? value.color : null;
}

/** Rank options `{ value, label, color }` for pickers, in configured order. */
export function rankOptions(property) {
  return rankConfig(property).values.map((v) => ({ value: v.key, label: v.label, color: v.color || null }));
}

/** Clamp a rank level to 0..maxLevel. */
export function clampRankLevel(property, level) {
  const n = Math.floor(Number(level) || 0);
  return Math.min(Math.max(n, 0), rankConfig(property).maxLevel);
}

/**
 * Singular form of a configured plural count noun for a count of one: "Stars" becomes
 * "Star" and "Degrees" becomes "Degree". Words that do not end in a single "s" are kept.
 */
function countNoun(word, count) {
  const lower = String(word || '').toLowerCase();
  if (count === 1 && /[^s]s$/.test(lower)) return lower.slice(0, -1);
  return lower;
}

/**
 * Rank text from configuration only, for example "Gold tier, 2 stars" for a property whose
 * rank label is "Tier" and level label is "Stars". Returns '' when rank is disabled or unset.
 * @param {object} property
 * @param {string|null} rank a key from `profile.rank.values`
 * @param {number} [level]
 * @param {{ withLabel?: boolean }} [options] `withLabel: false` omits the rank word ("Gold, 2 stars")
 * @returns {string}
 */
export function formatRank(property, rank, level = 0, { withLabel = true } = {}) {
  const config = rankConfig(property);
  if (!config.enabled || !rank) return '';
  const word = withLabel && nonEmpty(config.label) ? ' ' + config.label.toLowerCase() : '';
  let text = rankLabel(property, rank) + word;
  const n = clampRankLevel(property, level);
  if (n > 0 && config.maxLevel > 0) text += ', ' + n + (nonEmpty(config.levelLabel) ? ' ' + countNoun(config.levelLabel, n) : '');
  return text;
}

/** Goal options `{ value, label, freeText }` in configured order. */
export function goalOptions(property) {
  return goalsConfig(property).values.map((v) => ({ value: v.key, label: v.label, freeText: Boolean(v.freeText) }));
}

/** Display label for a goal key. */
export function goalLabel(property, key) {
  const value = goalsConfig(property).values.find((v) => v.key === key);
  return value ? value.label : String(key || '');
}

/** Whether a goal key takes free text (for example "Other"). */
export function isFreeTextGoal(property, key) {
  const value = goalsConfig(property).values.find((v) => v.key === key);
  return Boolean(value && value.freeText);
}

// ---- Reactions ----

/** Configured reactions `{ key, emoji, label }`, in order. */
export function reactionList(property) {
  return Array.isArray(property && property.reactions) ? property.reactions : [];
}

/** The reaction for a key, or null. */
export function findReaction(property, key) {
  return reactionList(property).find((r) => r.key === key) || null;
}

// ---- Membership and text tokens ----

/**
 * Format a price in cents as US dollars, for example 1250 becomes "$12.50".
 * @param {number} cents
 * @param {string} [locale]
 */
export function formatUsdCents(cents, locale = 'en-US') {
  const amount = (Number(cents) || 0) / 100;
  return new Intl.NumberFormat(locale, { style: 'currency', currency: 'USD', minimumFractionDigits: 2 }).format(amount);
}

const INTERVAL_WORDS = { Day: 'day', Week: 'week', Month: 'month', Year: 'year' };

/** Lower-case billing interval word, for example "Year" becomes "year". */
export function intervalWord(interval) {
  return INTERVAL_WORDS[interval] || String(interval || '').toLowerCase();
}

/**
 * Values for `{token}` placeholders in engine strings and property content:
 * `{name}`, `{shortName}`, `{tagline}`, `{description}`, `{owner}`, `{copyrightHolder}` (owner.copyrightHolder,
 * else owner.name), `{id}`, `{domain}`,
 * `{siteUrl}`, `{supportEmail}`, `{productName}`, `{price}`, `{priceAmount}`, `{interval}`,
 * `{minimumAge}`, `{trialDays}`, `{member}`, `{members}`, `{community}`.
 * @param {object} property
 * @returns {Record<string, string>}
 */
export function propertyTokens(property) {
  const p = property || {};
  const membership = p.membership || {};
  const terminology = p.terminology || {};
  const domain = (p.domains && p.domains.web) || '';
  const price = formatUsdCents(membership.priceUsdCents, p.locale || 'en-US');
  return {
    name: p.name || '',
    shortName: p.shortName || p.name || '',
    tagline: p.tagline || '',
    description: p.description || '',
    owner: (p.owner && p.owner.name) || '',
    copyrightHolder: (p.owner && (p.owner.copyrightHolder || p.owner.name)) || '',
    id: p.id || '',
    slug: propertySlug(p),
    domain,
    siteUrl: domain ? 'https://' + domain : '',
    supportEmail: (p.contact && p.contact.supportEmail) || '',
    productName: membership.productName || '',
    price,
    priceAmount: price.replace(/^\$/, ''),
    interval: intervalWord(membership.interval),
    minimumAge: String(minimumAgeOf(p)),
    trialDays: membership.trialDays === undefined ? '' : String(membership.trialDays),
    member: terminology.member || 'member',
    members: terminology.members || 'members',
    community: terminology.community || 'community'
  };
}

/**
 * Replace `{token}` placeholders with values. Unknown tokens are left as they are, so literal
 * braces in content survive.
 * @param {string} template
 * @param {Record<string, string|number>} values
 * @returns {string}
 */
export function formatTemplate(template, values) {
  if (typeof template !== 'string') return template;
  return template.replace(/\{([A-Za-z][A-Za-z0-9]*)\}/g, (match, key) => (
    Object.prototype.hasOwnProperty.call(values, key) && values[key] !== undefined && values[key] !== null ? String(values[key]) : match
  ));
}

/**
 * Fill a template with a property's tokens plus optional extra values.
 * @param {string} template
 * @param {object} property
 * @param {Record<string, string|number>} [extra]
 */
export function fillTemplate(template, property, extra = {}) {
  return formatTemplate(template, { ...propertyTokens(property), ...extra });
}
