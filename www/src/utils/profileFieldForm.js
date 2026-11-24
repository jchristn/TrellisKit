import {
  PROFILE_FIELD_KEY_PATTERN,
  PROFILE_MAX_CARD_FIELDS,
  PROFILE_OPTION_KEY_PATTERN,
  RESERVED_PROFILE_FIELD_KEYS,
  ProfileCollectAt,
  ProfileEditableBy,
  ProfileVisibility
} from '@trelliskit/shared';
import strings from '../strings.js';

/**
 * Form helpers for the admin profile field editor (docs/MULTIPROPERTY_CONTRACT.md section 9;
 * REST_API.md "Profile fields"). A draft is the editable shape of a `ProfileField`: numbers are
 * strings so inputs can be cleared, options and sub-fields carry a client `rowKey`. The client
 * checks mirror `ProfileFieldRules.Validate` for instant feedback; the server stays authoritative
 * and its `fieldErrors` use the same keys (`options[0].key`, `validation.maxLength`).
 */

const e = strings.admin.profileFields.errors;

export const SELECT_TYPES = Object.freeze(['SingleSelect', 'MultiSelect', 'Rank']);
export const TEXT_TYPES = Object.freeze(['Text', 'LongText']);
export const NOT_FILTERABLE_TYPES = Object.freeze(['LongText', 'Url', 'Phone']);
export const DEFAULT_RANK_COLOR = '#9CA3AF';

/**
 * Attributes the backend accepts for each built-in field (ProfileFieldService.BuiltInPresent):
 * every built-in takes label, help text, and placeholder; avatar and bio also collectAt and
 * enabled; bio also visibility (not Staff) and showOnProfile.
 */
export const BUILT_IN_ATTRIBUTES = Object.freeze({
  displayName: Object.freeze(['label', 'helpText', 'placeholder']),
  handle: Object.freeze(['label', 'helpText', 'placeholder']),
  avatar: Object.freeze(['label', 'helpText', 'placeholder', 'collectAt', 'enabled']),
  bio: Object.freeze(['label', 'helpText', 'placeholder', 'collectAt', 'enabled', 'visibility', 'showOnProfile'])
});

/** Whether the editor may change an attribute of a field (always for configurable fields). */
export function canEdit(field, attribute) {
  if (!field || !field.builtIn) return true;
  return (BUILT_IN_ATTRIBUTES[field.key] || BUILT_IN_ATTRIBUTES.displayName).includes(attribute);
}

/** Whether a field type has options. */
export function hasOptions(type) {
  return SELECT_TYPES.includes(type);
}

/** Whether a type can be searchable (Text and LongText). */
export function canSearch(type) {
  return TEXT_TYPES.includes(type);
}

/** Whether a type can be filterable (not LongText, Url, Phone, or the Image avatar). */
export function canFilter(type) {
  return !NOT_FILTERABLE_TYPES.includes(type) && type !== 'Image';
}

let uid = 0;
function rowKey() {
  uid += 1;
  return 'pf' + uid;
}

function str(value) {
  return value === null || value === undefined ? '' : String(value);
}

function num(value) {
  const text = String(value === null || value === undefined ? '' : value).trim();
  if (!text) return null;
  const n = Number(text);
  return Number.isFinite(n) ? n : text;
}

function int(value) {
  const n = num(value);
  return typeof n === 'number' ? Math.trunc(n) : n;
}

function optionDraft(o) {
  return {
    rowKey: rowKey(),
    id: (o && o.id) || null,
    key: str(o && o.key),
    label: str(o && o.label),
    color: (o && o.color) || '',
    maxLevel: str(o && o.maxLevel),
    freeText: Boolean(o && o.freeText),
    retired: Boolean(o && o.retired)
  };
}

/** An empty option row for a field type (rank options start with a color). */
export function emptyOption(type) {
  return { ...optionDraft(null), color: type === 'Rank' ? DEFAULT_RANK_COLOR : '' };
}

function subFieldDraft(s) {
  return {
    rowKey: rowKey(),
    id: (s && s.id) || null,
    key: str(s && s.key),
    label: str(s && s.label),
    type: (s && s.type) || 'Text',
    required: Boolean(s && s.required),
    maxLength: str(s && s.maxLength),
    minValue: str(s && s.minValue),
    maxValue: str(s && s.maxValue),
    notInFuture: Boolean(s && s.notInFuture),
    options: ((s && s.options) || []).map(optionDraft),
    rankFieldKey: (s && s.rankFieldKey) || '',
    withLevel: Boolean(s && s.withLevel)
  };
}

/** An empty History sub-field row. */
export function emptySubField() {
  return subFieldDraft(null);
}

/** The editable draft of a field definition (or a new Text field when `field` is null). */
export function fieldToDraft(field) {
  const f = field || {};
  const v = f.validation || {};
  return {
    key: str(f.key),
    label: str(f.label),
    helpText: str(f.helpText),
    placeholder: str(f.placeholder),
    type: f.type || 'Text',
    options: (f.options || []).map(optionDraft),
    rank: { levelLabel: str(f.rank && f.rank.levelLabel), maxLevel: str(f.rank ? f.rank.maxLevel : '') },
    subFields: (f.subFields || []).slice().sort((a, b) => (a.position || 0) - (b.position || 0)).map(subFieldDraft),
    validation: {
      required: Boolean(v.required),
      minLength: str(v.minLength),
      maxLength: str(v.maxLength),
      minValue: str(v.minValue),
      maxValue: str(v.maxValue),
      integerOnly: Boolean(v.integerOnly),
      pattern: str(v.pattern),
      patternMessage: str(v.patternMessage),
      minEntries: str(v.minEntries),
      maxEntries: str(v.maxEntries),
      minDate: str(v.minDate),
      maxDate: str(v.maxDate),
      notInFuture: Boolean(v.notInFuture)
    },
    collectAt: f.collectAt || ProfileCollectAt.Profile,
    editableBy: f.editableBy || ProfileEditableBy.Member,
    visibility: f.visibility || ProfileVisibility.Public,
    showOnProfile: f.showOnProfile !== false,
    showOnCard: Boolean(f.showOnCard),
    searchable: Boolean(f.searchable),
    filterable: Boolean(f.filterable),
    enabled: field ? f.enabled !== false : true,
    metadata: { labels: f.labels || [], tags: f.tags || {} }
  };
}

/**
 * Apply the coupling rules after a change: `editableBy` follows `collectAt` (Staff for
 * AdminOnly, else Member), `required` only for SignUp and Profile, `Staff` visibility only for
 * AdminOnly, searchable and filterable only for types that allow them.
 */
export function normalizeDraft(draft) {
  const next = { ...draft, validation: { ...draft.validation } };
  const adminOnly = next.collectAt === ProfileCollectAt.AdminOnly;
  next.editableBy = adminOnly ? ProfileEditableBy.Staff : ProfileEditableBy.Member;
  if (adminOnly) next.validation.required = false;
  if (!adminOnly && next.visibility === ProfileVisibility.Staff) next.visibility = ProfileVisibility.Private;
  if (!canSearch(next.type)) next.searchable = false;
  if (!canFilter(next.type)) next.filterable = false;
  return next;
}

function optionBody(o, type) {
  const body = { key: o.key.trim(), label: o.label.trim(), retired: Boolean(o.retired) };
  if (o.id) body.id = o.id;
  if (type === 'Rank') {
    body.color = o.color || DEFAULT_RANK_COLOR;
    body.maxLevel = int(o.maxLevel);
  } else {
    body.color = o.color || null;
    body.freeText = Boolean(o.freeText);
  }
  return body;
}

function subFieldBody(s, index) {
  const body = { key: s.key.trim(), label: s.label.trim(), type: s.type, required: Boolean(s.required), position: index, options: [] };
  if (s.id) body.id = s.id;
  if (s.type === 'Text') body.maxLength = int(s.maxLength);
  if (s.type === 'Number') {
    body.minValue = num(s.minValue);
    body.maxValue = num(s.maxValue);
  }
  if (s.type === 'Date') body.notInFuture = Boolean(s.notInFuture);
  if (s.type === 'SingleSelect') body.options = s.options.map((o) => ({ ...(o.id ? { id: o.id } : {}), key: o.key.trim(), label: o.label.trim(), retired: Boolean(o.retired) }));
  if (s.type === 'RankValue') {
    body.rankFieldKey = s.rankFieldKey || null;
    body.withLevel = Boolean(s.withLevel);
  }
  return body;
}

function validationBody(draft) {
  const v = draft.validation;
  const t = draft.type;
  const text = TEXT_TYPES.includes(t);
  return {
    required: draft.collectAt !== ProfileCollectAt.AdminOnly && Boolean(v.required),
    minLength: text ? int(v.minLength) : null,
    maxLength: text ? int(v.maxLength) : null,
    minValue: t === 'Number' ? num(v.minValue) : null,
    maxValue: t === 'Number' ? num(v.maxValue) : null,
    integerOnly: t === 'Number' && Boolean(v.integerOnly),
    pattern: t === 'Text' && v.pattern.trim() ? v.pattern.trim() : null,
    patternMessage: t === 'Text' && v.pattern.trim() && v.patternMessage.trim() ? v.patternMessage.trim() : null,
    minEntries: t === 'MultiSelect' || t === 'History' ? int(v.minEntries) : null,
    maxEntries: t === 'MultiSelect' || t === 'History' ? int(v.maxEntries) : null,
    minDate: t === 'Date' && v.minDate ? v.minDate : null,
    maxDate: t === 'Date' && v.maxDate ? v.maxDate : null,
    notInFuture: t === 'Date' && Boolean(v.notInFuture)
  };
}

/**
 * The create or update body for a draft. Built-in fields send only the attributes the backend
 * accepts for them (anything else is `409 BuiltInField`); the key and type are left out when
 * `field.valueCount > 0` (they cannot change then).
 * @param {object} draft
 * @param {object|null} field the saved definition (null when creating)
 */
export function draftToBody(draft, field = null) {
  const d = normalizeDraft(draft);
  const meta = { labels: d.metadata.labels || [], tags: d.metadata.tags || {} };
  if (field && field.builtIn) {
    const body = {};
    for (const attribute of BUILT_IN_ATTRIBUTES[field.key] || BUILT_IN_ATTRIBUTES.displayName) {
      if (attribute === 'label' || attribute === 'helpText' || attribute === 'placeholder') body[attribute] = d[attribute].trim();
      else body[attribute] = d[attribute];
    }
    return { ...body, ...meta };
  }
  const body = {
    key: d.key.trim(),
    label: d.label.trim(),
    helpText: d.helpText.trim(),
    placeholder: d.placeholder.trim(),
    type: d.type,
    options: hasOptions(d.type) ? d.options.map((o) => optionBody(o, d.type)) : [],
    rank: d.type === 'Rank' ? { levelLabel: d.rank.levelLabel.trim(), maxLevel: int(d.rank.maxLevel) || 0 } : null,
    subFields: d.type === 'History' ? d.subFields.map(subFieldBody) : [],
    validation: validationBody(d),
    collectAt: d.collectAt,
    editableBy: d.editableBy,
    visibility: d.visibility,
    showOnProfile: d.showOnProfile,
    showOnCard: d.showOnCard,
    searchable: d.searchable,
    filterable: d.filterable,
    enabled: d.enabled,
    ...meta
  };
  if (field && field.valueCount > 0) {
    delete body.key;
    delete body.type;
  }
  return body;
}

/** Number of other enabled configurable fields shown on member cards. */
export function otherCardFields(fields, field) {
  return (fields || []).filter((f) => !f.builtIn && f.enabled !== false && f.showOnCard && (!field || f.id !== field.id)).length;
}

function nonEmptyNumber(text) {
  return String(text || '').trim() !== '';
}

function checkNumberText(errors, key, text, { min, max, integer = true } = {}) {
  if (!nonEmptyNumber(text)) return;
  const n = Number(String(text).trim());
  if (!Number.isFinite(n) || (integer && !Number.isInteger(n))) errors[key] = integer ? e.wholeNumber : e.number;
  else if ((min !== undefined && n < min) || (max !== undefined && n > max)) errors[key] = e.range(min, max);
}

function checkOptions(errors, prefix, options, { rank }) {
  const keys = new Set();
  options.forEach((o, i) => {
    const p = prefix + '[' + i + '].';
    const key = o.key.trim();
    if (!PROFILE_OPTION_KEY_PATTERN.test(key)) errors[p + 'key'] = e.optionKey;
    else if (keys.has(key.toLowerCase())) errors[p + 'key'] = e.optionKeyDuplicate;
    keys.add(key.toLowerCase());
    const label = o.label.trim();
    if (label.length < 1 || label.length > 60) errors[p + 'label'] = e.length(1, 60);
    if (rank && o.color && !/^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$/.test(o.color)) errors[p + 'color'] = e.color;
    if (rank) checkNumberText(errors, p + 'maxLevel', o.maxLevel, { min: 0, max: 20 });
  });
}

/**
 * Client-side checks for a draft (the server repeats them). Returns `{ <server key>: message }`.
 * @param {object} draft
 * @param {{ field?: object|null, fields?: object[], maxCardFields?: number }} [context]
 */
export function validateFieldDraft(draft, { field = null, fields = [], maxCardFields = PROFILE_MAX_CARD_FIELDS } = {}) {
  const errors = {};
  const d = normalizeDraft(draft);
  const label = d.label.trim();
  if (label.length < 1 || label.length > 60) errors.label = e.length(1, 60);
  if (d.helpText.trim().length > 300) errors.helpText = e.max(300);
  if (d.placeholder.trim().length > 100) errors.placeholder = e.max(100);
  if (field && field.builtIn) return errors;

  const key = d.key.trim();
  if (!PROFILE_FIELD_KEY_PATTERN.test(key)) errors.key = e.key;
  else if (RESERVED_PROFILE_FIELD_KEYS.some((k) => k.toLowerCase() === key.toLowerCase())) errors.key = e.keyReserved;
  else if ((fields || []).some((f) => f.key.toLowerCase() === key.toLowerCase() && (!field || f.id !== field.id))) errors.key = e.keyInUse;

  if (hasOptions(d.type)) {
    if (d.options.length < 1 || d.options.length > 200) errors.options = e.optionsCount;
    checkOptions(errors, 'options', d.options, { rank: d.type === 'Rank' });
  }
  if (d.type === 'Rank') {
    if (d.rank.levelLabel.trim().length > 30) errors['rank.levelLabel'] = e.max(30);
    checkNumberText(errors, 'rank.maxLevel', d.rank.maxLevel, { min: 0, max: 20 });
  }
  if (d.type === 'History') {
    if (d.subFields.length < 1 || d.subFields.length > 10) errors.subFields = e.subFieldsCount;
    const keys = new Set();
    d.subFields.forEach((s, i) => {
      const p = 'subFields[' + i + '].';
      const subKey = s.key.trim();
      if (!PROFILE_FIELD_KEY_PATTERN.test(subKey)) errors[p + 'key'] = e.key;
      else if (keys.has(subKey.toLowerCase())) errors[p + 'key'] = e.subKeyDuplicate;
      keys.add(subKey.toLowerCase());
      const subLabel = s.label.trim();
      if (subLabel.length < 1 || subLabel.length > 60) errors[p + 'label'] = e.length(1, 60);
      if (s.type === 'Text') checkNumberText(errors, p + 'maxLength', s.maxLength, { min: 1, max: 500 });
      if (s.type === 'Number') {
        checkNumberText(errors, p + 'minValue', s.minValue, { integer: false });
        checkNumberText(errors, p + 'maxValue', s.maxValue, { integer: false });
        if (nonEmptyNumber(s.minValue) && nonEmptyNumber(s.maxValue) && Number(s.minValue) > Number(s.maxValue)) errors[p + 'minValue'] = e.minAboveMax;
      }
      if (s.type === 'SingleSelect') {
        if (s.options.length < 1 || s.options.length > 50) errors[p + 'options'] = e.subOptionsCount;
        checkOptions(errors, p + 'options', s.options, { rank: false });
      }
      if (s.type === 'RankValue') {
        const target = (fields || []).find((f) => f.key === s.rankFieldKey && f.type === 'Rank' && (!field || f.id !== field.id));
        if (!target) errors[p + 'rankFieldKey'] = e.rankField;
      }
    });
    checkNumberText(errors, 'validation.maxEntries', d.validation.maxEntries, { min: 1, max: 50 });
  }

  const v = d.validation;
  if (TEXT_TYPES.includes(d.type)) {
    const cap = d.type === 'LongText' ? 10000 : 1000;
    checkNumberText(errors, 'validation.minLength', v.minLength, { min: 0, max: cap });
    checkNumberText(errors, 'validation.maxLength', v.maxLength, { min: 1, max: cap });
    if (!errors['validation.minLength'] && nonEmptyNumber(v.minLength) && nonEmptyNumber(v.maxLength) && Number(v.minLength) > Number(v.maxLength)) errors['validation.minLength'] = e.minAboveMax;
  }
  if (d.type === 'Text' && v.pattern.trim()) {
    if (v.pattern.trim().length > 200) errors['validation.pattern'] = e.max(200);
    else {
      try {
        new RegExp(v.pattern.trim());
      } catch {
        errors['validation.pattern'] = e.pattern;
      }
    }
  }
  if (d.type === 'Number') {
    checkNumberText(errors, 'validation.minValue', v.minValue, { integer: false });
    checkNumberText(errors, 'validation.maxValue', v.maxValue, { integer: false });
    if (!errors['validation.minValue'] && nonEmptyNumber(v.minValue) && nonEmptyNumber(v.maxValue) && Number(v.minValue) > Number(v.maxValue)) errors['validation.minValue'] = e.minAboveMax;
  }
  if (d.type === 'MultiSelect') {
    checkNumberText(errors, 'validation.minEntries', v.minEntries, { min: 0 });
    checkNumberText(errors, 'validation.maxEntries', v.maxEntries, { min: 1, max: d.options.filter((o) => !o.retired).length || 1 });
  }
  if ((d.type === 'MultiSelect' || d.type === 'History') && !errors['validation.minEntries'] && nonEmptyNumber(v.minEntries) && nonEmptyNumber(v.maxEntries) && Number(v.minEntries) > Number(v.maxEntries)) {
    errors['validation.minEntries'] = e.minAboveMax;
  }
  if (d.type === 'Date' && v.minDate && v.maxDate && v.minDate > v.maxDate) errors['validation.minDate'] = e.dateOrder;

  if (d.showOnCard && d.enabled && otherCardFields(fields, field) + 1 > maxCardFields) errors.showOnCard = e.cardLimit(maxCardFields);
  return errors;
}

/**
 * Option keys named by a `409 OptionInUse` message ("Members have values for option a, b; set
 * retired instead of removing it.").
 */
export function optionKeysInUse(message) {
  const match = /option (.+?);/.exec(String(message || ''));
  return match ? match[1].split(',').map((x) => x.trim()).filter(Boolean) : [];
}

/** The id order after moving `id` one step up (`-1`) or down (`+1`); null at the ends. */
export function moveInOrder(ids, id, delta) {
  const index = ids.indexOf(id);
  const target = index + delta;
  if (index < 0 || target < 0 || target >= ids.length) return null;
  const next = ids.slice();
  next[index] = ids[target];
  next[target] = id;
  return next;
}

/**
 * The `fields` list for `POST /admin/profile-fields/preview` with an unsaved draft: the server
 * replaces every saved configurable field with this list (built-ins always come from the
 * database), so the draft is sent together with the other saved configurable fields.
 * @param {object[]} saved every saved definition
 * @param {object|null} field the definition being edited (null for a new field)
 * @param {object} draft
 */
export function previewFieldsWithDraft(saved, field, draft) {
  const custom = (saved || []).filter((f) => !f.builtIn);
  if (field && field.builtIn) return custom;
  const body = { ...draftToBody(draft, null), id: field ? field.id : undefined };
  if (field) {
    body.profileOrder = field.profileOrder;
    return custom.map((f) => (f.id === field.id ? body : f));
  }
  body.profileOrder = Math.min(100000, custom.reduce((max, f) => Math.max(max, Number(f.profileOrder) || 0), 0) + 10);
  return [...custom, body];
}
