import { Role, accessOf, membershipStepRequired } from '@trelliskit/shared';
import { hasPendingDocuments } from '../documents/documents';

/** Onboarding and app routes, in the order a new member passes through them. */
export const Routes = Object.freeze({
  welcome: '/welcome',
  signIn: '/sign-in',
  age: '/age',
  documents: '/review-documents',
  profileSetup: '/profile-setup',
  subscribe: '/subscribe',
  allowNotifications: '/allow-notifications',
  feed: '/feed'
});

/**
 * Whether the member holds the SystemAdmin role (which passes the membership check).
 * @param {object|null} user Me.
 */
export function isSystemAdmin(user) {
  return Boolean(user && Array.isArray(user.roles) && user.roles.some((r) => r && r.role === Role.SystemAdmin));
}

/**
 * Whether the member may use member content: an active membership, or SystemAdmin.
 * @param {object|null} user Me.
 */
export function hasMemberAccess(user) {
  return Boolean(user && ((user.membership && user.membership.isActive) || isSystemAdmin(user)));
}

/**
 * Whether the paywall step applies to this member: `Me.membershipRequired` (FullyGated and no
 * membership; never in Open or PartiallyGated, ACCESS_MODELS_CONTRACT.md section 5). SystemAdmin
 * passes. Older servers that do not send the field behave as FullyGated.
 * @param {object|null} user Me.
 */
export function needsPaywall(user) {
  return Boolean(user) && !isSystemAdmin(user) && membershipStepRequired(user);
}

/**
 * Whether the tab layout may render: signed in, age attested, no required documents pending
 * (`Me.pendingDocuments`), profile complete, and past the
 * paywall step when it applies. `profileComplete` can turn false for an existing account when a new
 * required sign-up field is added (docs/MULTIPROPERTY_CONTRACT.md section 9.10); the member is then
 * sent back to profile setup until it is complete. Signed-out guests may browse when the property
 * allows anonymous browsing (`property` given and `access.anonymousBrowsing` effective).
 * @param {{ status: string, user: object|null }} session
 * @param {object} [property]
 */
export function canEnterTabs(session, property) {
  if (isGuest(session, property)) return true;
  const user = session && session.user;
  return Boolean(session && session.status === 'signedIn' && user && user.ageAttested && !hasPendingDocuments(user) && user.profileComplete !== false && !needsPaywall(user));
}

/**
 * Whether the session is a signed-out guest browsing under anonymous browsing (never in
 * FullyGated). Guest mode starts only from "Browse as a guest" on Welcome (`session.browsing`);
 * signing out or a revoked session returns to Welcome or Sign in.
 * @param {{ status: string, browsing?: boolean }} session
 * @param {object} [property]
 */
export function isGuest(session, property) {
  return Boolean(property && session && session.status === 'signedOut' && session.browsing && accessOf(property).anonymousBrowsing);
}

/**
 * Where a session belongs next (contract section 2 and plan 7.1). Signed out: Welcome, or Sign
 * in (with a notice) when the session was revoked or expired during use. Signed in: age attestation, then
 * the documents step while `Me.pendingDocuments` is not empty, then profile setup, then the paywall when `Me.membershipRequired`, then the one-time notification
 * prompt, then the tabs.
 * @param {{ status: string, user: object|null, notificationsPrompted?: boolean }} session
 * @returns {string}
 */
export function nextRoute(session) {
  if (!session || session.status !== 'signedIn' || !session.user) return session && session.expired ? Routes.signIn : Routes.welcome;
  const user = session.user;
  if (!user.ageAttested) return Routes.age;
  if (hasPendingDocuments(user)) return Routes.documents;
  if (!user.profileComplete) return Routes.profileSetup;
  if (needsPaywall(user)) return Routes.subscribe;
  if (!session.notificationsPrompted) return Routes.allowNotifications;
  return Routes.feed;
}
