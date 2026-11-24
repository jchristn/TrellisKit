/**
 * Configurable profile fields (docs/MULTIPROPERTY_CONTRACT.md section 9; REST_API.md "Profile
 * fields"). Clients render onboarding, the profile editor, profile pages, member cards, and member
 * filters entirely from the definitions in `GET /api/v1/property` `profileFields`; they never
 * hard-code field keys, labels, or option sets, and they skip types they do not know.
 *
 * This module holds the definition helpers, the form-value conversions, client-side validation
 * that mirrors the server (for instant feedback only; the server is authoritative), formatters,
 * and member filter helpers. It contains no user-facing sentences: validation returns error
 * `{ code, params }` objects that each app maps to text from its strings module, and formatters
 * take the few words they need (`yes`, `no`) from the caller.
 */

import { countryName, isCountryCode } from './countries.js';

// ---- Enumerations ----

/** `ProfileField.type` values (`Image` is the built-in avatar only). */
export const ProfileFieldType = Object.freeze({
  Text: 'Text', LongText: 'LongText', Number: 'Number', Date: 'Date', Boolean: 'Boolean', Url: 'Url', Email: 'Email', Phone: 'Phone',
  Country: 'Country', SingleSelect: 'SingleSelect', MultiSelect: 'MultiSelect', Rank: 'Rank', History: 'History', Image: 'Image'
});

/** Types an administrator can create (every type except the built-in `Image`). */
export const CREATABLE_FIELD_TYPES = Object.freeze(['Text', 'LongText', 'Number', 'Date', 'Boolean', 'Url', 'Email', 'Phone', 'Country', 'SingleSelect', 'MultiSelect', 'Rank', 'History']);

/** `History` sub-field types. */
export const ProfileSubFieldType = Object.freeze({
  Text: 'Text', Number: 'Number', Date: 'Date', Boolean: 'Boolean', Url: 'Url', SingleSelect: 'SingleSelect', RankValue: 'RankValue', Member: 'Member'
});

/** `collectAt` values. */
export const ProfileCollectAt = Object.freeze({ SignUp: 'SignUp', Profile: 'Profile', AdminOnly: 'AdminOnly' });

/** `editableBy` values. */
export const ProfileEditableBy = Object.freeze({ Member: 'Member', Staff: 'Staff' });

/** `visibility` values. */
export const ProfileVisibility = Object.freeze({ Public: 'Public', Members: 'Members', Private: 'Private', Staff: 'Staff' });

/** Built-in field keys; their values stay top-level (`displayName`, `handle`, `avatarUrl`, `bio`). */
export const BUILT_IN_FIELD_KEYS = Object.freeze(['displayName', 'handle', 'avatar', 'bio']);

/** Field key rule and reserved keys (section 9.1). */
export const PROFILE_FIELD_KEY_PATTERN = /^[a-z][A-Za-z0-9]{0,39}$/;
export const PROFILE_OPTION_KEY_PATTERN = /^[A-Za-z0-9_-]{1,64}$/;
export const RESERVED_PROFILE_FIELD_KEYS = Object.freeze([
  'id', 'email', 'displayName', 'handle', 'avatar', 'avatarUrl', 'bio', 'roles', 'flags', 'membership', 'profile', 'labels', 'tags', 'createdUtc', 'updatedUtc', 'isStaff', 'isDeleted'
]);

/** Server defaults (`profileFields.*` settings and section 9.6). */
export const PROFILE_MAX_CARD_FIELDS = 3;
export const PROFILE_MAX_FILTERS = 10;
export const PROFILE_TEXT_DEFAULT_MAX = 200;
export const PROFILE_LONG_TEXT_DEFAULT_MAX = 2000;
export const PROFILE_HISTORY_DEFAULT_MAX = 12;
export const PROFILE_OTHER_TEXT_MAX = 100;
export const PROFILE_SUB_TEXT_DEFAULT_MAX = 100;
/** Select fields with at most this many options render as radios (section 9.11). */
export const PROFILE_RADIO_MAX_OPTIONS = 5;

const KNOWN = new Set(Object.values(ProfileFieldType));
const KNOWN_SUB = new Set(Object.values(ProfileSubFieldType));
const PHONE = /^\+[1-9][0-9]{6,14}$/;
const DATE = /^\d{4}-\d{2}-\d{2}$/;

/** Whether this client knows how to render a field type. Unknown types are skipped everywhere. */
export function isKnownFieldType(type) {
  return KNOWN.has(type);
}

// ---- Definitions ----

function order(a, b) {
  return (Number(a.profileOrder) || 0) - (Number(b.profileOrder) || 0);
}

function isObject(value) {
  return Boolean(value) && typeof value === 'object' && !Array.isArray(value);
}

function validationOf(field) {
  return (field && field.validation) || {};
}

/** Whether a field is required (`validation.required`, or the client copy's `required`). */
export function isFieldRequired(field) {
  return Boolean(field && (field.required || validationOf(field).required));
}

/**
 * Field definitions converted from a schema version 1 `profile` section, for build-time and
 * offline clients whose copy of the property has no `profileFields` (section 9.13 conversion).
 * @param {object} property
 * @returns {object[]}
 */
export function legacyProfileFields(property) {
  const profile = (property && property.profile) || {};
  const base = (key, label, type, extra = {}) => ({
    key, builtIn: false, label, helpText: '', placeholder: '', type, options: [], rank: null, subFields: [],
    validation: { required: false, maxLength: null, maxEntries: null, notInFuture: false }, required: false,
    collectAt: 'Profile', editableBy: 'Member', visibility: 'Public', showOnProfile: true, showOnCard: false, searchable: false, filterable: false, ...extra
  });
  const fields = [
    { ...base('displayName', '', 'Text', { builtIn: true, collectAt: 'SignUp', required: true, showOnCard: true, validation: { required: true, maxLength: 60 } }), profileOrder: 0 },
    { ...base('handle', '', 'Text', { builtIn: true, collectAt: 'SignUp', required: true, showOnCard: true, validation: { required: true, maxLength: 30 } }), profileOrder: 1 },
    { ...base('avatar', '', 'Image', { builtIn: true, showOnCard: true }), profileOrder: 2 },
    { ...base('bio', '', 'LongText', { builtIn: true, validation: { required: false, maxLength: 1000 } }), profileOrder: 5 }
  ];
  const rank = profile.rank;
  if (rank && rank.enabled) {
    fields.push({
      ...base('rank', rank.label || '', 'Rank', { collectAt: 'SignUp', required: true, validation: { required: true }, showOnCard: true, filterable: true }),
      options: (rank.values || []).map((v) => ({ key: v.key, label: v.label || v.key, color: v.color || null, maxLevel: null, freeText: false, retired: false })),
      rank: { levelLabel: rank.levelLabel || '', maxLevel: Number(rank.maxLevel) || 0 },
      profileOrder: 10
    });
  }
  const affiliation = profile.affiliation;
  if (affiliation && affiliation.enabled) {
    fields.push({ ...base('affiliation', affiliation.label || '', 'Text', { searchable: true, filterable: true, validation: { required: false, maxLength: 100 } }), profileOrder: 20 });
  }
  fields.push({ ...base('city', '', 'Text', { validation: { required: false, maxLength: 100 } }), profileOrder: 30 });
  fields.push({ ...base('region', '', 'Text', { validation: { required: false, maxLength: 100 } }), profileOrder: 40 });
  fields.push({ ...base('country', '', 'Country', { filterable: true }), profileOrder: 50 });
  const goals = profile.goals;
  if (goals && goals.enabled && Array.isArray(goals.values) && goals.values.length) {
    fields.push({
      ...base('goals', goals.label || '', 'MultiSelect', { visibility: 'Private', filterable: true }),
      options: goals.values.map((v) => ({ key: v.key, label: v.label || v.key, color: null, maxLevel: null, freeText: Boolean(v.freeText), retired: false })),
      profileOrder: 60
    });
  }
  const history = profile.rankHistory;
  if (rank && rank.enabled && history && history.enabled) {
    fields.push({
      ...base('rankHistory', history.label || '', 'History', { validation: { required: false, maxEntries: Number(history.maxEntries) || PROFILE_HISTORY_DEFAULT_MAX } }),
      subFields: [
        { key: 'rank', label: rank.label || '', type: 'RankValue', required: true, rankFieldKey: 'rank', withLevel: false, options: [], position: 0 },
        { key: 'awardedBy', label: history.awardedByLabel || '', type: 'Member', required: false, options: [], position: 1 },
        ...(affiliation && affiliation.enabled ? [{ key: 'affiliation', label: affiliation.label || '', type: 'Text', required: false, maxLength: 100, options: [], position: 2 }] : []),
        { key: 'dateAwarded', label: '', type: 'Date', required: false, notInFuture: true, options: [], position: 3 }
      ],
      profileOrder: 70
    });
  }
  return fields;
}

/**
 * The property's field definitions in profile order: `property.profileFields` when the server
 * sent them, else the conversion of the version 1 `profile` section (offline and build-time
 * fallback). Fields of unknown types are dropped.
 * @param {object} property
 * @returns {object[]}
 */
export function profileFieldsOf(property) {
  let list = property && Array.isArray(property.profileFields) && property.profileFields.length
    ? property.profileFields
    : legacyProfileFields(property);
  // A seeding copy (property.json `profileFields`) may omit the built-ins: add the engine defaults.
  if (!list.some((f) => f && f.builtIn)) {
    const present = new Set(list.map((f) => f && f.key));
    list = [...legacyProfileFields({}).filter((f) => f.builtIn && !present.has(f.key)), ...list];
  }
  return list.filter((f) => f && typeof f.key === 'string' && isKnownFieldType(f.type) && f.enabled !== false).slice().sort(order);
}

/** A field by key, or null. */
export function findProfileField(fields, key) {
  return (fields || []).find((f) => f.key === key) || null;
}

/** A built-in field definition (`displayName`, `handle`, `avatar`, `bio`), or null. */
export function builtInField(fields, key) {
  const field = findProfileField(fields, key);
  return field && field.builtIn ? field : null;
}

/** Configurable (non built-in) fields of known types, in profile order. */
export function customFields(fields) {
  return (fields || []).filter((f) => f && !f.builtIn && isKnownFieldType(f.type) && f.type !== 'Image' && f.enabled !== false).slice().sort(order);
}

/** Fields asked during onboarding (`collectAt: SignUp`), in profile order. */
export function onboardingFields(fields) {
  return customFields(fields).filter((f) => f.collectAt === ProfileCollectAt.SignUp);
}

/** Fields the member edits in the profile editor (`SignUp` and `Profile`, `editableBy: Member`). */
export function editorFields(fields) {
  return customFields(fields).filter((f) => f.collectAt !== ProfileCollectAt.AdminOnly && f.editableBy !== ProfileEditableBy.Staff);
}

/** Fields set by staff that the member sees read-only in the editor ("Set by staff"). */
export function staffSetFields(fields) {
  return customFields(fields).filter((f) => f.collectAt === ProfileCollectAt.AdminOnly || f.editableBy === ProfileEditableBy.Staff);
}

/** Whether a field is shown on the profile page. */
export function showsOnProfile(field) {
  return Boolean(field) && field.showOnProfile !== false;
}

/**
 * Fields to show on a profile page for a `profile` map: shown on profile and with a value, in
 * profile order. Keys of the map that the client has no definition for are skipped.
 */
export function profilePageFields(fields, profile) {
  return customFields(fields).filter((f) => showsOnProfile(f) && hasProfileValue(f, profile && profile[f.key]));
}

/** Card fields of a `profile` map: `showOnCard` with a value, at most `max`, in profile order. */
export function cardFields(fields, profile, max = PROFILE_MAX_CARD_FIELDS) {
  return customFields(fields).filter((f) => f.showOnCard && hasProfileValue(f, profile && profile[f.key])).slice(0, max);
}

/** Fields that can be filtered in member search (section 9.12), in profile order. */
export function filterableFields(fields) {
  return customFields(fields).filter((f) => f.filterable && profileFilterOperators(f.type).length > 0);
}

/** The keys that keep `profileComplete` false, with labels from the definitions. */
export function missingProfileFields(me, fields) {
  const keys = Array.isArray(me && me.profileMissing) ? me.profileMissing : [];
  return keys.map((key) => ({ key, field: findProfileField(fields, key), label: (findProfileField(fields, key) || {}).label || key }));
}

// ---- Options and rank ----

/** Options a picker offers: non-retired ones, plus the retired option currently selected. */
export function pickerOptions(field, ...currentKeys) {
  const keep = new Set(currentKeys.flat().filter(Boolean));
  return ((field && field.options) || []).filter((o) => !o.retired || keep.has(o.key));
}

/** The option of a field with a key (case-insensitive), or null. */
export function findOption(field, key) {
  if (!key) return null;
  const lower = String(key).toLowerCase();
  return ((field && field.options) || []).find((o) => String(o.key).toLowerCase() === lower) || null;
}

/** Display label of an option key (the key itself when unknown). */
export function optionLabel(field, key) {
  const option = findOption(field, key);
  return option ? option.label : String(key || '');
}

/** The maximum level for a rank option (`option.maxLevel`, else `rank.maxLevel`, else 0). */
export function rankMaxLevel(field, key) {
  const option = findOption(field, key);
  if (option && Number.isInteger(option.maxLevel)) return option.maxLevel;
  return Math.max(0, Number(field && field.rank && field.rank.maxLevel) || 0);
}

/** Whether a rank field has a level (a level label and a maximum above 0). */
export function rankHasLevel(field, key) {
  return Boolean(field && field.rank && String(field.rank.levelLabel || '').trim()) && rankMaxLevel(field, key) > 0;
}

/** The color of a rank (or select) option, or null. */
export function optionColor(field, key) {
  const option = findOption(field, key);
  return (option && option.color) || null;
}

function countNoun(word, count) {
  const lower = String(word || '').toLowerCase();
  if (count === 1 && /[^s]s$/.test(lower)) return lower.slice(0, -1);
  return lower;
}

/**
 * Rank text from configuration only (section 9.11): "<option label>" for level 0, otherwise
 * "<option label>, <level> <levelLabel>" (singular for 1). '' when unset.
 * @param {object} field a `Rank` field
 * @param {{ key: string, level?: number }|null} value
 */
export function formatRankValue(field, value) {
  if (!value || !value.key) return '';
  let text = optionLabel(field, value.key);
  const level = Math.floor(Number(value.level) || 0);
  const levelLabel = field && field.rank ? String(field.rank.levelLabel || '').trim() : '';
  if (level > 0 && levelLabel) text += ', ' + level + ' ' + countNoun(levelLabel, level);
  return text;
}

// ---- Values ----

function blank(value) {
  return value === null || value === undefined || (typeof value === 'string' && !value.trim());
}

/** Whether an API value counts as set for a field. */
export function hasProfileValue(field, value) {
  if (blank(value)) return false;
  switch (field && field.type) {
    case 'Boolean': return typeof value === 'boolean';
    case 'SingleSelect': return typeof value === 'string' ? Boolean(value) : Boolean(value && value.key);
    case 'MultiSelect': return Boolean(value && Array.isArray(value.keys) && value.keys.length);
    case 'Rank': return Boolean(value && value.key);
    case 'History': return Array.isArray(value) && value.length > 0;
    default: return true;
  }
}

let uid = 0;
/** A client-side key for list rows (History entries) that have no id yet. */
export function nextProfileRowKey() {
  uid += 1;
  return 'row-' + uid;
}

function subToForm(sub, value) {
  switch (sub.type) {
    case 'Number': return value === null || value === undefined ? '' : String(value);
    case 'Boolean': return typeof value === 'boolean' ? value : null;
    case 'SingleSelect': return typeof value === 'string' ? value : (value && value.key) || '';
    case 'RankValue': return { key: (value && value.key) || '', level: Number(value && value.level) || 0 };
    case 'Member':
      if (typeof value === 'string') return { name: value, userId: null };
      return { name: (value && value.name) || '', userId: (value && value.userId) || null };
    default: return typeof value === 'string' ? value : '';
  }
}

/** An empty History entry in form shape. */
export function emptyHistoryEntry(field) {
  const values = {};
  for (const sub of (field && field.subFields) || []) values[sub.key] = subToForm(sub, null);
  return { rowKey: nextProfileRowKey(), id: null, values };
}

/**
 * Convert an API value to the editable form shape of its field:
 * strings for text-like, number, date, and country types; `true`, `false`, or null for Boolean;
 * `{ key, otherText }`, `{ keys, otherText }`, `{ key, level }`; History `[{ rowKey, id, values }]`.
 */
export function toFormValue(field, value) {
  switch (field && field.type) {
    case 'Number': return value === null || value === undefined ? '' : String(value);
    case 'Boolean': return typeof value === 'boolean' ? value : null;
    case 'SingleSelect': {
      if (typeof value === 'string') return { key: value, otherText: '' };
      return { key: (value && value.key) || '', otherText: (value && value.otherText) || '' };
    }
    case 'MultiSelect': return { keys: Array.isArray(value && value.keys) ? value.keys.slice() : [], otherText: (value && value.otherText) || '' };
    case 'Rank': return { key: (value && value.key) || '', level: Math.max(0, Math.floor(Number(value && value.level) || 0)) };
    case 'History':
      return (Array.isArray(value) ? value : []).map((entry) => {
        const values = {};
        for (const sub of field.subFields || []) values[sub.key] = subToForm(sub, entry && entry.values ? entry.values[sub.key] : null);
        return { rowKey: (entry && entry.id) || nextProfileRowKey(), id: (entry && entry.id) || null, values };
      });
    case 'Country': return typeof value === 'string' ? value.toUpperCase() : '';
    default: return typeof value === 'string' ? value : '';
  }
}

/** Form values for every field from a `profile` map. */
export function toFormValues(fields, profile) {
  const out = {};
  for (const field of customFields(fields)) out[field.key] = toFormValue(field, profile ? profile[field.key] : null);
  return out;
}

function trimOrNull(value) {
  return typeof value === 'string' && value.trim() ? value.trim() : null;
}

function subFromForm(sub, value, rankField) {
  switch (sub.type) {
    case 'Number': {
      if (blank(value)) return null;
      const n = Number(value);
      return Number.isFinite(n) ? n : value;
    }
    case 'Boolean': return typeof value === 'boolean' ? value : null;
    case 'SingleSelect': return trimOrNull(value);
    case 'RankValue': {
      if (!value || !value.key) return null;
      const out = { key: value.key };
      if (sub.withLevel && rankField && rankHasLevel(rankField, value.key)) out.level = Math.max(0, Math.floor(Number(value.level) || 0));
      return out;
    }
    case 'Member': {
      const name = trimOrNull(value && value.name);
      const userId = (value && value.userId) || null;
      if (!name && !userId) return null;
      return { name, userId };
    }
    default: return trimOrNull(value);
  }
}

/**
 * Convert a form value back to the API value of its field (null clears).
 * @param {object} field
 * @param {any} value form value
 * @param {object[]} [fields] every field (History `RankValue` sub-fields look up their rank field)
 */
export function fromFormValue(field, value, fields = []) {
  switch (field && field.type) {
    case 'Number': {
      if (blank(value)) return null;
      const n = Number(String(value).trim());
      return Number.isFinite(n) ? n : String(value).trim();
    }
    case 'Boolean': return typeof value === 'boolean' ? value : null;
    case 'Country': return trimOrNull(value) ? value.trim().toUpperCase() : null;
    case 'SingleSelect': {
      if (!value || !value.key) return null;
      const option = findOption(field, value.key);
      return { key: value.key, otherText: option && option.freeText ? trimOrNull(value.otherText) : null };
    }
    case 'MultiSelect': {
      const keys = Array.isArray(value && value.keys) ? value.keys.filter(Boolean) : [];
      if (!keys.length) return null;
      const free = keys.some((k) => { const o = findOption(field, k); return o && o.freeText; });
      return { keys, otherText: free ? trimOrNull(value.otherText) : null };
    }
    case 'Rank': {
      if (!value || !value.key) return null;
      const max = rankMaxLevel(field, value.key);
      return { key: value.key, level: Math.min(max, Math.max(0, Math.floor(Number(value.level) || 0))) };
    }
    case 'History': {
      const list = Array.isArray(value) ? value : [];
      if (!list.length) return null;
      return list.map((entry) => {
        const values = {};
        for (const sub of field.subFields || []) {
          const rankField = sub.type === 'RankValue' ? findProfileField(fields, sub.rankFieldKey) : null;
          const v = subFromForm(sub, entry.values ? entry.values[sub.key] : null, rankField);
          if (v !== null) values[sub.key] = v;
        }
        return { id: entry.id || null, values };
      });
    }
    case 'LongText': return trimOrNull(typeof value === 'string' ? value.replace(/\r\n/g, '\n') : value);
    default: return trimOrNull(value);
  }
}

function sameJson(a, b) {
  return JSON.stringify(a === undefined ? null : a) === JSON.stringify(b === undefined ? null : b);
}

/**
 * The `profile` map for `PUT /api/v1/me/profile` (or the staff route): every field in `editable`
 * whose value changed from `initial` (both form values), or every editable field without
 * `initial`. A cleared value is sent as null.
 * @param {object[]} editable fields the caller may write
 * @param {Record<string, any>} values form values
 * @param {Record<string, any>} [initial] form values when the form opened
 * @param {object[]} [fields] every field (for History rank lookups)
 */
export function buildProfilePatch(editable, values, initial = null, fields = editable) {
  const patch = {};
  for (const field of editable || []) {
    if (!values || !Object.prototype.hasOwnProperty.call(values, field.key)) continue;
    const next = fromFormValue(field, values[field.key], fields);
    if (initial) {
      const before = fromFormValue(field, initial[field.key], fields);
      if (sameJson(next, before)) continue;
    }
    patch[field.key] = next;
  }
  return patch;
}

// ---- Validation (mirrors section 9.6; the server is authoritative) ----

function err(code, params = {}) {
  return { code, params };
}

/** `YYYY-MM-DD` of a date in UTC. */
function utcDay(date) {
  return date.toISOString().slice(0, 10);
}

/** Whether a string is a valid calendar date `YYYY-MM-DD`. */
export function isIsoDate(value) {
  if (typeof value !== 'string' || !DATE.test(value)) return false;
  const d = new Date(value + 'T00:00:00Z');
  return !Number.isNaN(d.getTime()) && utcDay(d) === value;
}

/** Whether a string is an absolute http or https URL with a host. */
export function isWebUrl(value) {
  if (typeof value !== 'string' || value.length > 2048) return false;
  try {
    const url = new URL(value.trim());
    return (url.protocol === 'http:' || url.protocol === 'https:') && Boolean(url.hostname);
  } catch {
    return false;
  }
}

function emailOk(text) {
  const at = text.indexOf('@');
  return text.length <= 254 && at > 0 && text.indexOf('@', at + 1) < 0 && text.slice(at + 1).includes('.') && !text.includes(' ');
}

function checkText(text, max, min) {
  if (text.length > max) return err('tooLong', { max });
  if (min && text.length < min) return err('tooShort', { min });
  return null;
}

function checkDate(text, { notInFuture, minDate, maxDate }, today) {
  if (!isIsoDate(text)) return err('date');
  if (notInFuture && text > utcDay(today)) return err('dateFuture');
  if (minDate && text < minDate) return err('dateMin', { min: minDate });
  if (maxDate && text > maxDate) return err('dateMax', { max: maxDate });
  return null;
}

function checkNumber(raw, v) {
  const n = Number(String(raw).trim());
  if (!Number.isFinite(n)) return err('number');
  if (v.integerOnly && Math.floor(n) !== n) return err('integer');
  if (Math.round(n * 10000) / 10000 !== n) return err('decimals');
  if (v.minValue !== null && v.minValue !== undefined && n < v.minValue) return err('min', { min: v.minValue });
  if (v.maxValue !== null && v.maxValue !== undefined && n > v.maxValue) return err('max', { max: v.maxValue });
  return null;
}

function checkOther(field, key, otherText) {
  const option = findOption(field, key);
  if (option && option.freeText && typeof otherText === 'string' && otherText.trim().length > PROFILE_OTHER_TEXT_MAX) return err('tooLong', { max: PROFILE_OTHER_TEXT_MAX });
  return null;
}

/**
 * Validate one form value. Returns a map of error key to `{ code, params }` (empty when valid).
 * Keys follow the server: `profile.<key>` and `profile.<key>[<index>].<subKey>`.
 * Codes: `required`, `tooLong {max}`, `tooShort {min}`, `pattern {message}`, `number`, `integer`,
 * `decimals`, `min {min}`, `max {max}`, `date`, `dateFuture`, `dateMin {min}`, `dateMax {max}`,
 * `url`, `email`, `phone`, `country`, `option`, `minEntries {min}`, `maxEntries {max}`, `level {max}`.
 * @param {object} field
 * @param {any} value form value
 * @param {{ today?: Date, fields?: object[], required?: boolean }} [options] `required: false` skips the required check (optional onboarding skip)
 */
export function validateProfileValue(field, value, { today = new Date(), fields = [], required = true } = {}) {
  const name = 'profile.' + field.key;
  const v = validationOf(field);
  const errors = {};
  const set = (key, error) => { if (error) errors[key] = error; };
  const api = fromFormValue(field, value, fields);
  if (api === null) {
    if (required && isFieldRequired(field)) set(name, err('required'));
    return errors;
  }
  switch (field.type) {
    case 'Text': {
      const text = String(api);
      const e = checkText(text, v.maxLength || PROFILE_TEXT_DEFAULT_MAX, v.minLength);
      if (e) { set(name, e); break; }
      if (v.pattern) {
        let ok = true;
        try { ok = new RegExp(v.pattern).test(text); } catch { ok = true; }
        if (!ok) set(name, err('pattern', { message: v.patternMessage || '' }));
      }
      break;
    }
    case 'LongText': set(name, checkText(String(api), v.maxLength || PROFILE_LONG_TEXT_DEFAULT_MAX, v.minLength)); break;
    case 'Number': set(name, checkNumber(api, v)); break;
    case 'Date': set(name, checkDate(String(api), v, today)); break;
    case 'Url': if (!isWebUrl(api)) set(name, err('url')); break;
    case 'Email': if (!emailOk(String(api))) set(name, err('email')); break;
    case 'Phone': if (!PHONE.test(String(api))) set(name, err('phone')); break;
    case 'Country': if (!isCountryCode(api)) set(name, err('country')); break;
    case 'SingleSelect':
      if (!findOption(field, api.key)) set(name, err('option'));
      else set(name, checkOther(field, api.key, api.otherText));
      break;
    case 'MultiSelect': {
      if (api.keys.some((k) => !findOption(field, k))) { set(name, err('option')); break; }
      if (v.minEntries && api.keys.length < v.minEntries) { set(name, err('minEntries', { min: v.minEntries })); break; }
      if (v.maxEntries && api.keys.length > v.maxEntries) { set(name, err('maxEntries', { max: v.maxEntries })); break; }
      const free = api.keys.find((k) => { const o = findOption(field, k); return o && o.freeText; });
      if (free) set(name, checkOther(field, free, api.otherText));
      break;
    }
    case 'Rank': {
      if (!findOption(field, api.key)) { set(name, err('option')); break; }
      const max = rankMaxLevel(field, api.key);
      const level = Math.floor(Number(value && value.level) || 0);
      if (level < 0 || level > max) set(name, err('level', { max }));
      break;
    }
    case 'History': {
      const max = v.maxEntries || PROFILE_HISTORY_DEFAULT_MAX;
      if (value.length > max) { set(name, err('maxEntries', { max })); break; }
      if (v.minEntries && value.length < v.minEntries) { set(name, err('minEntries', { min: v.minEntries })); break; }
      value.forEach((entry, index) => {
        for (const sub of field.subFields || []) {
          const subName = name + '[' + index + '].' + sub.key;
          const rankField = sub.type === 'RankValue' ? findProfileField(fields, sub.rankFieldKey) : null;
          const subValue = subFromForm(sub, entry.values ? entry.values[sub.key] : null, rankField);
          if (subValue === null) {
            if (sub.required) set(subName, err('required'));
            continue;
          }
          switch (sub.type) {
            case 'Text': set(subName, checkText(subValue, sub.maxLength || PROFILE_SUB_TEXT_DEFAULT_MAX)); break;
            case 'Url': if (!isWebUrl(subValue)) set(subName, err('url')); break;
            case 'Number': set(subName, checkNumber(subValue, { minValue: sub.minValue, maxValue: sub.maxValue })); break;
            case 'Date': set(subName, checkDate(subValue, { notInFuture: sub.notInFuture }, today)); break;
            case 'SingleSelect': if (!(sub.options || []).some((o) => o.key === subValue)) set(subName, err('option')); break;
            case 'RankValue':
              if (rankField && !findOption(rankField, subValue.key)) set(subName, err('option'));
              break;
            case 'Member': if (subValue.name && subValue.name.length > 100) set(subName, err('tooLong', { max: 100 })); break;
            default: break;
          }
        }
      });
      break;
    }
    default: break;
  }
  return errors;
}

/**
 * Validate form values for a list of fields. Returns the merged error map (empty when valid).
 * @param {object[]} list fields to check
 * @param {Record<string, any>} values form values
 * @param {{ today?: Date, fields?: object[], skipRequired?: Set<string> }} [options]
 */
export function validateProfileValues(list, values, { today = new Date(), fields = list, skipRequired = null } = {}) {
  let errors = {};
  for (const field of list || []) {
    const required = !(skipRequired && skipRequired.has(field.key));
    errors = { ...errors, ...validateProfileValue(field, values ? values[field.key] : null, { today, fields, required }) };
  }
  return errors;
}

/**
 * Server `fieldErrors` keyed `profile.<key>` (and nested) to a flat map of messages; other keys
 * (`displayName`, `handle`, `bio`) pass through. Array values are joined.
 */
export function profileFieldErrors(fieldErrors) {
  const out = {};
  for (const [key, value] of Object.entries(fieldErrors || {})) out[key] = Array.isArray(value) ? value.join(' ') : String(value);
  return out;
}

// ---- Formatting ----

function formatDateOnly(text, locale) {
  if (!isIsoDate(text)) return String(text || '');
  const d = new Date(text + 'T00:00:00Z');
  try {
    return d.toLocaleDateString(locale || 'en-US', { month: 'short', day: 'numeric', year: 'numeric', timeZone: 'UTC' });
  } catch {
    return text;
  }
}

/** Host of a URL for display (the URL itself when it does not parse). */
export function urlHost(value) {
  try {
    return new URL(String(value)).hostname;
  } catch {
    return String(value || '');
  }
}

function formatSub(sub, value, { fields, locale, yes, no }) {
  if (value === null || value === undefined || value === '') return '';
  switch (sub.type) {
    case 'Date': return formatDateOnly(value, locale);
    case 'Boolean': return value ? yes : no;
    case 'SingleSelect': {
      const key = typeof value === 'string' ? value : value.key;
      const option = (sub.options || []).find((o) => o.key === key);
      return option ? option.label : String(key || '');
    }
    case 'RankValue': {
      const rankField = findProfileField(fields, sub.rankFieldKey);
      return rankField ? formatRankValue(rankField, value) : String(value.key || '');
    }
    case 'Member': return typeof value === 'string' ? value : (value.name || '');
    case 'Number': return Number(value).toLocaleString(locale || 'en-US');
    case 'Url': return urlHost(value);
    default: return String(value);
  }
}

/**
 * The parts of one History entry for display, in sub-field order: `[{ key, label, type, text, color, userId }]`
 * with empty parts dropped. `color` is set for `RankValue`; `userId` for linked `Member` names.
 */
export function historyEntryParts(field, entry, { fields = [], locale = 'en-US', yes = 'Yes', no = 'No' } = {}) {
  const subs = ((field && field.subFields) || []).filter((s) => KNOWN_SUB.has(s.type)).slice().sort((a, b) => (a.position || 0) - (b.position || 0));
  const values = (entry && entry.values) || {};
  const parts = [];
  for (const sub of subs) {
    const value = values[sub.key];
    const text = formatSub(sub, value, { fields, locale, yes, no });
    if (!text) continue;
    const part = { key: sub.key, label: sub.label || '', type: sub.type, text, color: null, userId: null };
    if (sub.type === 'RankValue') part.color = optionColor(findProfileField(fields, sub.rankFieldKey), value && value.key);
    if (sub.type === 'Member' && value && typeof value === 'object') part.userId = value.userId || null;
    parts.push(part);
  }
  return parts;
}

/**
 * A field's value as display text (section 9.11 `display`): locale numbers and dates, "Yes" or
 * "No", option labels (or the free text), comma-separated labels, rank text, country names,
 * and URL hosts. History entries are joined with "; " (use `historyEntryParts` for rich lists).
 * @param {object} field
 * @param {any} value API value
 * @param {{ fields?: object[], locale?: string, yes?: string, no?: string }} [options]
 */
export function formatProfileValue(field, value, options = {}) {
  const { locale = 'en-US', yes = 'Yes', no = 'No' } = options;
  if (!hasProfileValue(field, value)) return '';
  switch (field.type) {
    case 'Number': return Number(value).toLocaleString(locale);
    case 'Date': return formatDateOnly(value, locale);
    case 'Boolean': return value ? yes : no;
    case 'Country': return countryName(value);
    case 'Url': return urlHost(value);
    case 'SingleSelect': {
      const key = typeof value === 'string' ? value : value.key;
      const option = findOption(field, key);
      if (option && option.freeText && value.otherText) return value.otherText;
      return optionLabel(field, key);
    }
    case 'MultiSelect':
      return value.keys.map((k) => {
        const option = findOption(field, k);
        return option && option.freeText && value.otherText ? value.otherText : optionLabel(field, k);
      }).join(', ');
    case 'Rank': return formatRankValue(field, value);
    case 'History': return value.map((entry) => historyEntryParts(field, entry, options).map((p) => p.text).join(', ')).join('; ');
    default: return String(value);
  }
}

// ---- Member filters (section 9.12) ----

/** Filter operators each type allows (REST_API.md "Member filters"). */
export function profileFilterOperators(type) {
  switch (type) {
    case 'Text': return ['eq', 'prefix', 'hasValue'];
    case 'Email': return ['eq', 'hasValue'];
    case 'Number': return ['eq', 'gte', 'lte', 'hasValue'];
    case 'Date': return ['gte', 'lte', 'hasValue'];
    case 'Boolean': return ['eq', 'hasValue'];
    case 'Country':
    case 'SingleSelect': return ['in', 'hasValue'];
    case 'MultiSelect': return ['any', 'all', 'hasValue'];
    case 'Rank': return ['in', 'minRank', 'maxRank', 'minLevel', 'hasValue'];
    case 'History':
    case 'LongText':
    case 'Url':
    case 'Phone': return ['hasValue'];
    default: return [];
  }
}

const LIST_OPERATORS = new Set(['in', 'any', 'all']);
const PARAM_PREFIX = 'profile.';

function cleanOperatorValue(op, value) {
  if (LIST_OPERATORS.has(op)) {
    const list = (Array.isArray(value) ? value : [value]).map((x) => String(x || '').trim()).filter(Boolean).slice(0, 20);
    return list.length ? list : null;
  }
  if (op === 'hasValue') {
    if (value === true || value === 'true') return true;
    if (value === false || value === 'false') return false;
    return null;
  }
  if (op === 'minLevel') {
    if (value === '' || value === null || value === undefined) return null;
    const n = Math.floor(Number(value));
    return Number.isFinite(n) && n > 0 ? n : null;
  }
  if (value === null || value === undefined) return null;
  if (typeof value === 'boolean' || typeof value === 'number') return value;
  const text = String(value).trim();
  return text ? text : null;
}

/**
 * Normalize a profile filter state `{ <key>: { <op>: value } }` to the `filters.profile` body:
 * empty operators and fields are dropped; at most `PROFILE_MAX_FILTERS` fields. Number filters
 * become numbers; `eq` on a Boolean field becomes a boolean when `fields` are given.
 */
export function buildProfileFilters(state, fields = []) {
  const out = {};
  let count = 0;
  for (const [key, ops] of Object.entries(state || {})) {
    if (!isObject(ops) || count >= PROFILE_MAX_FILTERS) continue;
    const field = findProfileField(fields, key);
    const clean = {};
    for (const [op, raw] of Object.entries(ops)) {
      let value = cleanOperatorValue(op, raw);
      if (value === null) continue;
      if (field && field.type === 'Number' && ['eq', 'gte', 'lte'].includes(op) && Number.isFinite(Number(value))) value = Number(value);
      if (field && field.type === 'Boolean' && op === 'eq') value = value === true || value === 'true';
      clean[op] = value;
    }
    if (Object.keys(clean).length) {
      out[key] = clean;
      count += 1;
    }
  }
  return out;
}

/** The GET form of profile filters: `{ 'profile.<key>.<op>': value | [values] }`. */
export function profileFilterParams(filters) {
  const params = {};
  for (const [key, ops] of Object.entries(filters || {})) {
    for (const [op, value] of Object.entries(ops || {})) params[PARAM_PREFIX + key + '.' + op] = Array.isArray(value) ? value.slice() : String(value);
  }
  return params;
}

/**
 * Parse profile filters from URL search params (`profile.<key>.<op>=value`, repeatable for list
 * operators) back to a filter state.
 * @param {URLSearchParams|Record<string, string|string[]>} params
 */
export function parseProfileFilterParams(params) {
  const state = {};
  const entries = params && typeof params.getAll === 'function'
    ? Array.from(params.entries())
    : Object.entries(params || {}).flatMap(([k, v]) => (Array.isArray(v) ? v.map((x) => [k, x]) : [[k, v]]));
  for (const [name, value] of entries) {
    if (!name.startsWith(PARAM_PREFIX)) continue;
    const rest = name.slice(PARAM_PREFIX.length);
    const dot = rest.lastIndexOf('.');
    if (dot <= 0) continue;
    const key = rest.slice(0, dot);
    const op = rest.slice(dot + 1);
    if (!PROFILE_FIELD_KEY_PATTERN.test(key)) continue;
    state[key] = state[key] || {};
    if (LIST_OPERATORS.has(op)) state[key][op] = [...(state[key][op] || []), value];
    else state[key][op] = value;
  }
  return state;
}

/** Whether a URL parameter name is a profile filter parameter. */
export function isProfileFilterParam(name) {
  return typeof name === 'string' && name.startsWith(PARAM_PREFIX);
}

/**
 * Removable chips for active profile filters: `[{ key, op, label, value }]` where `label` is the
 * field label and `value` the display text of the operator value (option labels and country names).
 */
export function profileFilterChips(filters, fields) {
  const chips = [];
  for (const [key, ops] of Object.entries(filters || {})) {
    const field = findProfileField(fields, key);
    for (const [op, value] of Object.entries(ops || {})) {
      let text;
      if (Array.isArray(value)) text = value.map((x) => (field && field.type === 'Country' ? countryName(x) : field ? optionLabel(field, x) : x)).join(', ');
      else if ((op === 'minRank' || op === 'maxRank') && field) text = optionLabel(field, value);
      else text = String(value);
      chips.push({ key, op, label: (field && field.label) || key, value: text });
    }
  }
  return chips;
}

/** Remove one operator (or the whole field when `op` is omitted) from a filter state. */
export function removeProfileFilter(state, key, op) {
  const next = { ...(state || {}) };
  if (!op) {
    delete next[key];
    return next;
  }
  const ops = { ...(next[key] || {}) };
  delete ops[op];
  if (Object.keys(ops).length) next[key] = ops;
  else delete next[key];
  return next;
}
