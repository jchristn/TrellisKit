import strings from '../strings.js';
import { fill } from '../property/current.js';

const e = strings.auth.errors;

/** Join provider names in plain English: "Email", "Email or Google", "Email, Apple, or Google". */
export function joinMethods(methods) {
  const labels = (methods || []).map((m) => (m === 'Email' ? 'your email and password' : m));
  if (labels.length === 0) return 'your existing sign-in method';
  if (labels.length === 1) return labels[0];
  if (labels.length === 2) return labels[0] + ' or ' + labels[1];
  return labels.slice(0, -1).join(', ') + ', or ' + labels[labels.length - 1];
}

/**
 * A member-facing message for an authentication or account error (contract sections 2 and 3).
 * @param {any} err ApiError
 * @param {string} [fallback]
 * @returns {string}
 */
export function authErrorMessage(err, fallback = strings.common.errorGeneric) {
  if (!err) return fallback;
  const code = err.code;
  const body = err.body && typeof err.body === 'object' ? err.body : {};
  if (err.status === 0) return e.unreachable;
  if (code === 'AccountLinkRequired') return e.accountLinkRequired(joinMethods(body.existingMethods));
  if (code === 'AccountLocked' || err.status === 423) return err.message || e.locked;
  if (code === 'AccountBanned') return e.banned;
  if (code === 'AgeRequirementNotMet') return fill(e.ageNotMet);
  if (code === 'InvalidCredentials') return e.invalidCredentials;
  if (code === 'EmailInUse') return e.emailInUse;
  if (code === 'IdentityInUse') return e.identityInUse;
  if (code === 'InvalidIdentityToken') return e.invalidIdentity;
  if (code === 'IntegrationNotConfigured' || err.status === 503) return e.providerUnavailable;
  if (code === 'RateLimited' || err.status === 429) return e.rateLimited;
  return err.message || fallback;
}
