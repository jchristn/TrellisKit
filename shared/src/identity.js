/**
 * Identity and membership enumerations and validators (Phase 1 contract). Values match the
 * backend's enum names exactly.
 */

/** Client identifiers sent in the `X-Client` header. */
export const ClientKind = Object.freeze({ Web: 'web', Mobile: 'mobile' });

/** Sign-in methods. */
export const SignInMethod = Object.freeze({ Email: 'Email', Apple: 'Apple', Google: 'Google', Facebook: 'Facebook' });

/** Where an active membership comes from. */
export const MembershipSource = Object.freeze({ Subscription: 'Subscription', SignupCode: 'SignupCode', AdminComp: 'AdminComp' });

/** Billing stores. */
export const Store = Object.freeze({ Apple: 'Apple', Google: 'Google', Stripe: 'Stripe' });

/** Store subscription states. */
export const SubscriptionStatus = Object.freeze({
  Active: 'Active',
  InGrace: 'InGrace',
  InBillingRetry: 'InBillingRetry',
  Expired: 'Expired',
  Revoked: 'Revoked',
  Refunded: 'Refunded',
  Paused: 'Paused'
});

/** How a sign-up code or admin comp grants membership; Discount (sign-up codes only) gives a discount at web checkout instead. */
export const GrantType = Object.freeze({ DurationMonths: 'DurationMonths', FixedEndDate: 'FixedEndDate', Lifetime: 'Lifetime', Discount: 'Discount' });

/** How long a discount code's discount applies: the first billing period, or every renewal. */
export const DiscountDuration = Object.freeze({ Once: 'Once', Forever: 'Forever' });

/** Derived sign-up code status. */
export const SignupCodeStatus = Object.freeze({
  Active: 'Active',
  Scheduled: 'Scheduled',
  Expired: 'Expired',
  Exhausted: 'Exhausted',
  Inactive: 'Inactive'
});

/** Role scopes. */
export const ResourceType = Object.freeze({ Global: 'Global', Forum: 'Forum', Feed: 'Feed', Directory: 'Directory', Events: 'Events' });

/** Password length limits. */
export const PasswordMinLength = 10;
export const PasswordMaxLength = 128;

/**
 * Problems with a password under the account password policy (10 to 128 characters, not equal
 * to the email). Returns machine-readable problem codes; an empty array means acceptable.
 * @param {string} password
 * @param {string} [email]
 * @returns {Array<'tooShort'|'tooLong'|'matchesEmail'>}
 */
export function passwordProblems(password, email = '') {
  const value = typeof password === 'string' ? password : '';
  const problems = [];
  if (value.length < PasswordMinLength) problems.push('tooShort');
  if (value.length > PasswordMaxLength) problems.push('tooLong');
  if (email && value && value.trim().toLowerCase() === String(email).trim().toLowerCase()) problems.push('matchesEmail');
  return problems;
}

/**
 * Whether a display name is valid (2 to 60 characters after trimming).
 * @param {string} value
 * @returns {boolean}
 */
export function isDisplayName(value) {
  if (typeof value !== 'string') return false;
  const length = value.trim().length;
  return length >= 2 && length <= 60;
}

/**
 * Normalize a sign-up code as clients send it: trimmed and upper-cased.
 * @param {string} value
 * @returns {string}
 */
export function normalizeSignupCode(value) {
  return typeof value === 'string' ? value.trim().toUpperCase() : '';
}

/**
 * Whether a sign-up code has a valid shape: 6 to 40 characters of A-Z, 0-9, and hyphen (after
 * normalizing). Server-side validation is authoritative.
 * @param {string} value
 * @returns {boolean}
 */
export function isSignupCode(value) {
  return /^[A-Z0-9-]{6,40}$/.test(normalizeSignupCode(value));
}

/**
 * Parse a `YYYY-MM-DD` date string as a local calendar date.
 * @param {string} value
 * @returns {Date|null}
 */
export function parseDateOnly(value) {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(typeof value === 'string' ? value : '');
  if (!match) return null;
  const year = Number(match[1]);
  const month = Number(match[2]) - 1;
  const day = Number(match[3]);
  const date = new Date(year, month, day);
  if (date.getFullYear() !== year || date.getMonth() !== month || date.getDate() !== day) return null;
  return date;
}
