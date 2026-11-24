import { formatRetryAfter } from '@trelliskit/shared';
import strings from '../strings';

const MethodLabels = { Email: 'email and password', Apple: 'Apple', Google: 'Google', Facebook: 'Facebook' };

/**
 * Join method names for a sentence: "Apple", "Apple or Google", "Apple, Google, or email".
 * @param {string[]} methods
 */
export function joinMethods(methods) {
  const labels = (methods || []).map((m) => MethodLabels[m] || m);
  if (labels.length <= 1) return labels[0] || '';
  if (labels.length === 2) return labels[0] + ' or ' + labels[1];
  return labels.slice(0, -1).join(', ') + ', or ' + labels[labels.length - 1];
}

/**
 * Turn an ApiError (or any error) into a message for the member. Contract error codes get
 * specific copy; anything else uses the server's message.
 * @param {any} error
 * @param {object} [context]
 * @param {string} [context.providerLabel] For example "Google", used in link-required copy.
 * @returns {string}
 */
export function describeError(error, { providerLabel } = {}) {
  if (!error) return strings.errors.generic;
  const e = strings.errors;
  if (error.status === 0) return error.code === 'Timeout' ? e.timeout : e.unreachable;
  switch (error.code) {
    case 'AccountLinkRequired': {
      const methods = (error.body && error.body.existingMethods) || [];
      return e.accountLinkRequired(joinMethods(methods), providerLabel);
    }
    case 'AccountLocked':
      return error.message || e.accountLocked;
    case 'AccountBanned':
      return e.accountBanned;
    case 'AgeRequirementNotMet':
      return e.ageRequirementNotMet;
    case 'InvalidCredentials':
      return e.invalidCredentials;
    case 'InvalidIdentityToken':
      return e.invalidIdentityToken;
    case 'EmailInUse':
      return e.emailInUse;
    case 'RateLimited': {
      // The server sends Retry-After on every 429; say how long to wait when it is known.
      const wait = formatRetryAfter(error.retryAfterSeconds);
      return wait ? e.rateLimitedFor(wait) : e.rateLimited;
    }
    case 'IdentityInUse':
      return e.identityInUse;
    case 'LastSignInMethod':
      return e.lastSignInMethod;
    case 'IntegrationNotConfigured':
      return e.integrationNotConfigured;
    case 'TransactionBelongsToAnotherAccount':
      return e.transactionBelongsToAnotherAccount;
    case 'InvalidTransaction':
    case 'InvalidPurchase':
      return e.invalidPurchase;
    case 'TooLarge':
      return e.imageTooLarge;
    case 'InvalidImage':
      return e.invalidImage;
    default:
      break;
  }
  if (error.status === 423) return error.message || e.accountLocked;
  return error.message || e.generic;
}

/**
 * Field errors from a 400 ValidationFailed response, as { field: message }.
 * @param {any} error
 * @returns {Record<string, string>}
 */
export function fieldErrorsOf(error) {
  const raw = error && error.body && error.body.fieldErrors;
  if (!raw || typeof raw !== 'object') return {};
  const result = {};
  for (const [key, value] of Object.entries(raw)) {
    result[key] = Array.isArray(value) ? value.join(' ') : String(value);
  }
  return result;
}
