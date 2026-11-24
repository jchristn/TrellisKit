import strings from '../strings.js';

const s = strings.admin.settings;

/**
 * Group settings fields by `section`, preserving the server's order.
 * @param {Array<object>} fields
 * @returns {Array<{ section: string, fields: Array<object> }>}
 */
export function groupFields(fields) {
  const groups = [];
  const index = new Map();
  for (const field of fields || []) {
    const name = field.section || 'General';
    if (!index.has(name)) {
      index.set(name, groups.length);
      groups.push({ section: name, fields: [] });
    }
    groups[index.get(name)].fields.push(field);
  }
  return groups;
}

/**
 * Initial draft value (what the input shows) for a field.
 * Secrets always start empty; integers are strings so the input can be edited freely.
 */
export function initialDraft(field) {
  switch (field.type) {
    case 'Secret': return '';
    case 'Boolean': return Boolean(field.value);
    case 'Integer': return field.value === null || field.value === undefined ? '' : String(field.value);
    default: return field.value === null || field.value === undefined ? '' : String(field.value);
  }
}

/** Build the initial draft map for all fields. */
export function initialDrafts(fields) {
  const drafts = {};
  for (const field of fields || []) drafts[field.key] = initialDraft(field);
  return drafts;
}

/** Convert a draft value to the value sent to the server. */
export function draftToValue(field, draft) {
  if (field.type === 'Boolean') return Boolean(draft);
  if (field.type === 'Integer') return Number(String(draft).trim());
  return draft;
}

/** Whether a field's draft differs from the server value. */
export function isChanged(field, draft) {
  if (field.overriddenByEnvironment) return false;
  if (field.type === 'Secret') return typeof draft === 'string' && draft.length > 0;
  if (field.type === 'Integer') return String(draft).trim() !== initialDraft(field);
  return draft !== initialDraft(field);
}

/**
 * Validate a draft value against field metadata. Returns an error message or null.
 */
export function validateField(field, draft) {
  if (field.type !== 'Integer' || !isChanged(field, draft)) return null;
  const text = String(draft).trim();
  if (!/^-?\d+$/.test(text)) return s.integerInvalid;
  const value = Number(text);
  const hasMin = typeof field.min === 'number';
  const hasMax = typeof field.max === 'number';
  if (hasMin && hasMax && (value < field.min || value > field.max)) return s.rangeInvalid(field.min, field.max);
  if (hasMin && value < field.min) return s.minInvalid(field.min);
  if (hasMax && value > field.max) return s.maxInvalid(field.max);
  return null;
}

/**
 * Compute the `values` payload for PUT /settings: only changed, non-overridden keys.
 * @returns {Record<string, unknown>}
 */
export function computeChanges(fields, drafts) {
  const values = {};
  for (const field of fields || []) {
    const draft = drafts[field.key];
    if (isChanged(field, draft)) values[field.key] = draftToValue(field, draft);
  }
  return values;
}
