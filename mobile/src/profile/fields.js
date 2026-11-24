import { builtInField, findProfileField, isHandle, profileFieldsOf } from '@trelliskit/shared';
import { useMemo } from 'react';
import { useProperty } from '../property';
import strings from '../strings';

/**
 * Mobile glue for configurable profile fields (docs/MULTIPROPERTY_CONTRACT.md section 9): the
 * property's definitions, the built-in fields with their defaults, and error text for the
 * `{ code, params }` objects that the shared validation returns.
 */

/** Phase 1 limits of the built-in fields (section 9.3). */
export const BuiltInLimits = Object.freeze({ displayNameMin: 2, displayNameMax: 60, bioMax: 1000 });

/** The property's profile field definitions, in profile order (unknown types dropped). */
export function useProfileFields() {
  const property = useProperty();
  return useMemo(() => profileFieldsOf(property), [property]);
}

/**
 * The built-in fields of a definition list. Server definitions always carry the four built-ins,
 * so a missing `bio` or `avatar` there means it is disabled. A bundled copy without built-ins
 * (schema version 1 `profileFields` in the create-body shape) gets the Phase 1 defaults.
 * @param {object[]} fields
 * @returns {{ displayName: object, handle: object, avatar: object|null, bio: object|null }}
 */
export function builtInsOf(fields) {
  const hasBuiltIns = (fields || []).some((f) => f && f.builtIn);
  const fallback = (key, extra) => ({ key, builtIn: true, label: '', helpText: '', placeholder: '', collectAt: 'Profile', showOnProfile: true, visibility: 'Public', validation: {}, ...extra });
  const pick = (key, extra) => builtInField(fields, key) || (hasBuiltIns && key !== 'displayName' && key !== 'handle' ? null : fallback(key, extra));
  return {
    displayName: pick('displayName', { collectAt: 'SignUp', required: true, validation: { required: true, maxLength: BuiltInLimits.displayNameMax } }),
    handle: pick('handle', { collectAt: 'SignUp', required: true, validation: { required: true, maxLength: 30 } }),
    avatar: pick('avatar', {}),
    bio: pick('bio', { validation: { maxLength: BuiltInLimits.bioMax } })
  };
}

/** A built-in field's label from its definition when set, else the engine string. */
export function builtInLabel(field, fallback) {
  const label = field && typeof field.label === 'string' ? field.label.trim() : '';
  return label || fallback;
}

/** Maximum length of the display name (the built-in's `maxLength`, capped by Phase 1). */
export function displayNameMax(field) {
  const max = Number(field && field.validation && field.validation.maxLength);
  return Number.isInteger(max) && max >= BuiltInLimits.displayNameMin && max <= BuiltInLimits.displayNameMax ? max : BuiltInLimits.displayNameMax;
}

/** Maximum length of the bio. */
export function bioMax(field) {
  const max = Number(field && field.validation && field.validation.maxLength);
  return Number.isInteger(max) && max > 0 && max <= BuiltInLimits.bioMax ? max : BuiltInLimits.bioMax;
}

/**
 * Text for a validation error `{ code, params }` (strings.profileFields.errors). Strings pass
 * through (server messages).
 * @param {{ code: string, params?: object }|string|null} error
 * @returns {string|null}
 */
export function profileErrorText(error) {
  if (!error) return null;
  if (typeof error === 'string') return error;
  const e = strings.profileFields.errors;
  const p = error.params || {};
  switch (error.code) {
    case 'required': return e.required;
    case 'tooLong': return e.tooLong(p.max);
    case 'tooShort': return e.tooShort(p.min);
    case 'pattern': return p.message || e.pattern;
    case 'number': return e.number;
    case 'integer': return e.integer;
    case 'decimals': return e.decimals;
    case 'min': return e.min(p.min);
    case 'max': return e.max(p.max);
    case 'date': return e.date;
    case 'dateFuture': return e.dateFuture;
    case 'dateMin': return e.dateMin(p.min);
    case 'dateMax': return e.dateMax(p.max);
    case 'url': return e.url;
    case 'email': return e.email;
    case 'phone': return e.phone;
    case 'country': return e.country;
    case 'option': return e.option;
    case 'minEntries': return e.minEntries(p.min);
    case 'maxEntries': return e.maxEntries(p.max);
    case 'level': return e.level(p.max);
    default: return e.invalid;
  }
}

/** Map a shared validation result (`{ key: { code, params } }`) to `{ key: text }`. */
export function profileErrorTexts(errors) {
  const out = {};
  for (const [key, value] of Object.entries(errors || {})) out[key] = profileErrorText(value);
  return out;
}

/**
 * Validate the built-in fields of a form (display name, handle, bio).
 * @param {{ displayName: string, handle: string, bio?: string }} values
 * @param {{ displayName: object, bio: object|null }} builtIns
 * @returns {Record<string, string>}
 */
export function validateBuiltIns(values, builtIns) {
  const e = strings.profileForm.errors;
  const errors = {};
  const name = (values.displayName || '').trim();
  const max = displayNameMax(builtIns && builtIns.displayName);
  if (name.length < BuiltInLimits.displayNameMin || name.length > max) errors.displayName = max === BuiltInLimits.displayNameMax ? e.displayName : e.displayNameMax(max);
  if (!isHandle(values.handle || '')) errors.handle = e.handle;
  if (builtIns && builtIns.bio && (values.bio || '').length > bioMax(builtIns.bio)) errors.bio = e.bio(bioMax(builtIns.bio));
  return errors;
}

/**
 * The first error for a field key: `profile.<key>`, then its free text (`profile.<key>.otherText`).
 * @param {Record<string, string>} errors
 * @param {string} key field key
 */
export function fieldError(errors, key) {
  if (!errors) return null;
  return errors['profile.' + key] || null;
}

/** Labels of the fields that keep `profileComplete` false, joined for a sentence. */
export function missingLabels(missing, fields) {
  const built = builtInsOf(fields);
  return (missing || [])
    .map((m) => {
      if (m.key === 'displayName') return builtInLabel(built.displayName, strings.profileForm.displayName);
      if (m.key === 'handle') return builtInLabel(built.handle, strings.profileForm.handle);
      const field = m.field || findProfileField(fields, m.key);
      return (field && field.label) || null;
    })
    .filter(Boolean)
    .join(', ');
}

function isBlank(value) {
  if (value === null || value === undefined) return true;
  if (typeof value === 'string') return value.trim().length === 0;
  if (typeof value === 'number' || typeof value === 'boolean') return false;
  if (Array.isArray(value)) return value.length === 0 || value.every(isBlank);
  if (typeof value === 'object') {
    // Option values ({ key, level }) count as filled only once an option is picked.
    if (Object.prototype.hasOwnProperty.call(value, 'key')) return isBlank(value.key);
    if (Object.prototype.hasOwnProperty.call(value, 'value')) return isBlank(value.value);
    return Object.values(value).every(isBlank);
  }
  return false;
}

/**
 * The missing fields that are still empty in the form being filled (`Me.profileMissing` entries
 * minus the ones the member has typed or picked a value for), so the "Still needed" note updates
 * live as fields are filled. The server still validates on save.
 * @param {Array<{ key: string }>} missing From `missingProfileFields`.
 * @param {{ displayName?: string, handle?: string, profile?: object }|null} values Form draft, or null before it reports.
 */
export function stillMissing(missing, values) {
  if (!values) return missing || [];
  return (missing || []).filter((m) => {
    if (m.key === 'displayName' || m.key === 'handle') return isBlank(values[m.key]);
    return isBlank(values.profile ? values.profile[m.key] : undefined);
  });
}
