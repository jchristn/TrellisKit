import strings from '../strings.js';

const e = strings.profileFields.errors;

/**
 * Text for a shared profile validation error (`{ code, params }` from `validateProfileValue`).
 * Strings pass through, so server messages and client codes can share one map.
 * @param {{ code: string, params?: object }|string|null} error
 * @returns {string}
 */
export function profileErrorText(error) {
  if (!error) return '';
  if (typeof error === 'string') return error;
  const entry = e[error.code];
  if (typeof entry === 'function') return entry(error.params || {});
  return entry || e.unknown;
}

/** Map an error map of `{ code, params }` (or strings) to an error map of text. */
export function profileErrorsText(errors) {
  const out = {};
  for (const [key, value] of Object.entries(errors || {})) out[key] = profileErrorText(value);
  return out;
}

/**
 * Server `fieldErrors` to an error map of text (array messages are joined). Keys are kept as the
 * server sends them (`profile.<key>`, `profile.<key>[0].<subKey>`, `displayName`).
 */
export function serverFieldErrors(fieldErrors) {
  const out = {};
  for (const [key, value] of Object.entries(fieldErrors || {})) out[key] = Array.isArray(value) ? value.join(' ') : String(value);
  return out;
}
