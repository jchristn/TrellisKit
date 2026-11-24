import { Role, hasPendingDocuments, membershipStepRequired } from '@trelliskit/shared';

export const AGE_PATH = '/app/onboarding/age';
export const PROFILE_PATH = '/app/onboarding/profile';
export const DOCUMENTS_PATH = '/app/onboarding/documents';
export const MEMBERSHIP_PATH = '/app/membership';

/** Gate levels, in order. Each route declares the level it needs. */
export const LEVELS = Object.freeze({ signedIn: 0, age: 1, profile: 2, member: 3 });

/** Whether the member holds the global SystemAdmin role. */
export function isSystemAdmin(me) {
  return Boolean(me && Array.isArray(me.roles) && me.roles.some((r) => r && r.role === Role.SystemAdmin));
}

/** Whether the member has an active membership. */
export function hasActiveMembership(me) {
  return Boolean(me && me.membership && me.membership.isActive);
}

/**
 * The onboarding step a member must complete before reaching a route at `level`, or null when
 * the member may proceed. Order: age attestation, required documents (`Me.pendingDocuments`,
 * for routes at the profile level and above: the server gates content, staff, and purchase routes
 * while account routes stay open), profile setup, membership. The membership
 * step applies only when `Me.membershipRequired` is true (FullyGated and no membership; never in
 * Open or PartiallyGated, ACCESS_MODELS_CONTRACT.md section 5). SystemAdmin users pass it.
 * @param {object|null} me
 * @param {number} [level]
 * @returns {string|null}
 */
export function onboardingRedirect(me, level = LEVELS.member) {
  if (!me) return '/sign-in';
  if (level >= LEVELS.age && !me.ageAttested) return AGE_PATH;
  if (level >= LEVELS.profile && hasPendingDocuments(me)) return DOCUMENTS_PATH;
  if (level >= LEVELS.profile && !me.profileComplete) return PROFILE_PATH;
  if (level >= LEVELS.member && membershipStepRequired(me) && !isSystemAdmin(me)) return MEMBERSHIP_PATH;
  return null;
}

/** Only allow in-app relative redirect targets (no open redirects). */
export function safeNext(value, fallback = '/app') {
  if (typeof value !== 'string' || !value.startsWith('/') || value.startsWith('//')) return fallback;
  return value;
}
