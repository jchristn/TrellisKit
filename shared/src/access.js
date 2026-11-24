/**
 * Access models (docs/ACCESS_MODELS_CONTRACT.md, binding). The property's `access` section from
 * `GET /api/v1/property` (or the build-time `property.json` copy) decides which models apply:
 * `Open` (no payments), `PartiallyGated` (optional membership, some features gated), and
 * `FullyGated` (membership required). These helpers turn that configuration and the caller's
 * `Me` into the decisions both apps share: navigation and lock badges, the paywall step, locked
 * post stubs, 401 and 402 denials, and the composer audience picker. The server decides; these
 * helpers only shape the UI (client checks are never authorization).
 *
 * Nothing here names a property; every value comes from the configuration passed in.
 */

import { Role } from './enums.js';

/** Access models. */
export const AccessModel = Object.freeze({ Open: 'Open', PartiallyGated: 'PartiallyGated', FullyGated: 'FullyGated' });

/** Every access model, in the order the admin page lists them. */
export const AccessModels = Object.freeze([AccessModel.Open, AccessModel.PartiallyGated, AccessModel.FullyGated]);

/** Feature keys a `PartiallyGated` property can gate (contract section 1). */
export const AccessFeature = Object.freeze({
  Feed: 'feed',
  Members: 'members',
  Forums: 'forums',
  Library: 'library',
  Events: 'events',
  Directory: 'directory',
  Messaging: 'messaging',
  Assistants: 'assistants',
  Search: 'search',
  Ideas: 'ideas'
});

/** Every gateable feature key, in the contract's order. */
export const AccessFeatures = Object.freeze(Object.values(AccessFeature));

/** Content audiences. */
export const ContentAudience = Object.freeze({ Public: 'Public', SignedIn: 'SignedIn', Members: 'Members' });

/** What a denial or a locked object asks of the caller. */
export const AccessRequirement = Object.freeze({ SignIn: 'SignIn', Membership: 'Membership' });

/** Caller tiers (contract section 3.1); `Me.accessTier` reports the signed-in ones. */
export const AccessTier = Object.freeze({ Anonymous: 'Anonymous', SignedIn: 'SignedIn', Member: 'Member', Staff: 'Staff' });

/**
 * Features signed-out visitors may browse when anonymous browsing is on (contract sections 2 and 8;
 * Phase 4 adds the library and events catalogs, whose `Public` objects are browsable).
 */
export const SignedOutFeatures = Object.freeze([AccessFeature.Feed, AccessFeature.Forums, AccessFeature.Assistants, AccessFeature.Library, AccessFeature.Events]);

/**
 * Features whose reads (not only writes) need a membership when gated in `PartiallyGated`.
 * The feed is the exception: a gated feed stays readable signed in, with members-only posts as
 * locked stubs. Phase 4 resolved decision 2 adds the library and events catalogs to the
 * exceptions (locked stubs). The `directory` key stays read gated here because it also covers the
 * member profile directory; the venue directory uses `catalogAccess`.
 */
const READ_GATED = Object.freeze([
  AccessFeature.Members,
  AccessFeature.Forums,
  AccessFeature.Directory,
  AccessFeature.Messaging,
  AccessFeature.Assistants,
  AccessFeature.Search,
  AccessFeature.Ideas
]);

function isObject(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

/**
 * The effective access configuration of a property, normalized. Works with the server's
 * `GET /api/v1/property` `access` (effective values) and with a build-time `property.json`
 * `access` section (stored values, which lack `purchasesEnabled`). A missing section means
 * `FullyGated` (contract section 1).
 *
 * @param {object} property
 * @returns {{ model: string, anonymousBrowsing: boolean, gatedFeatures: string[], defaultAudience: string,
 *   purchasesEnabled: boolean, signupCodesEnabled: boolean, revision: number }}
 */
export function accessOf(property) {
  const p = isObject(property) ? property : {};
  const raw = isObject(p.access) ? p.access : {};
  const model = AccessModels.includes(raw.model) ? raw.model : AccessModel.FullyGated;
  const anonymousBrowsing = model !== AccessModel.FullyGated && raw.anonymousBrowsing === true;
  let gatedFeatures = [];
  if (model === AccessModel.PartiallyGated && Array.isArray(raw.gatedFeatures)) {
    gatedFeatures = Array.from(new Set(raw.gatedFeatures.filter((key) => AccessFeatures.includes(key))));
    if (gatedFeatures.includes(AccessFeature.Members) && !gatedFeatures.includes(AccessFeature.Directory)) gatedFeatures.push(AccessFeature.Directory);
  }
  let defaultAudience = Object.values(ContentAudience).includes(raw.defaultAudience) ? raw.defaultAudience : null;
  if (model === AccessModel.FullyGated) defaultAudience = ContentAudience.Members;
  else if (!defaultAudience) defaultAudience = model === AccessModel.Open ? ContentAudience.SignedIn : ContentAudience.Members;
  if (model === AccessModel.Open && defaultAudience === ContentAudience.Members) defaultAudience = ContentAudience.SignedIn;
  const purchasesEnabled = typeof raw.purchasesEnabled === 'boolean' ? raw.purchasesEnabled : model !== AccessModel.Open;
  const signupFeature = !isObject(p.features) || p.features.signupCodes !== false;
  const signupCodesEnabled = typeof raw.signupCodesEnabled === 'boolean' ? raw.signupCodesEnabled && purchasesEnabled : purchasesEnabled && signupFeature;
  const revision = Number.isInteger(raw.revision) ? raw.revision : 0;
  return { model, anonymousBrowsing, gatedFeatures, defaultAudience, purchasesEnabled, signupCodesEnabled, revision };
}

/** Whether the property sells memberships (false in `Open`): prices, paywalls, and restore show only then. */
export function purchasesEnabled(property) {
  return accessOf(property).purchasesEnabled;
}

/** Whether sign-up codes are offered (purchases enabled and `features.signupCodes` on). */
export function signupCodesEnabled(property) {
  return accessOf(property).signupCodesEnabled;
}

/**
 * The membership product to show, or null when purchases are off or the server sent
 * `membership: null` (contract section 4).
 * @param {object} property
 * @returns {object|null}
 */
export function membershipProduct(property) {
  if (!purchasesEnabled(property)) return null;
  return property && isObject(property.membership) ? property.membership : null;
}

/** The minimum age for the age gate: `minimumAge`, else `membership.minimumAge`, else 18. */
export function minimumAgeOf(property) {
  const p = isObject(property) ? property : {};
  if (Number.isInteger(p.minimumAge) && p.minimumAge >= 0) return p.minimumAge;
  const m = isObject(p.membership) ? p.membership.minimumAge : undefined;
  return Number.isInteger(m) && m >= 0 ? m : 18;
}

function isAdmin(me) {
  return Boolean(me && Array.isArray(me.roles) && me.roles.some((r) => r && r.role === Role.SystemAdmin));
}

/**
 * The caller's tier: `Anonymous` without `Me`, else `Me.accessTier` when the server sends it,
 * else derived from the membership and the SystemAdmin role (older servers).
 * @param {object|null} me
 * @returns {string}
 */
export function callerTier(me) {
  if (!me) return AccessTier.Anonymous;
  if ([AccessTier.SignedIn, AccessTier.Member, AccessTier.Staff].includes(me.accessTier)) return me.accessTier;
  if (isAdmin(me)) return AccessTier.Staff;
  return me.membership && me.membership.isActive ? AccessTier.Member : AccessTier.SignedIn;
}

/** Whether the caller holds a membership or passes every check (staff). */
export function hasMemberTier(me) {
  const tier = callerTier(me);
  return tier === AccessTier.Member || tier === AccessTier.Staff;
}

/**
 * Whether onboarding must show the paywall step: `Me.membershipRequired` (true only in
 * `FullyGated` for a non-member). Older servers that do not send it behave as `FullyGated`.
 * @param {object|null} me
 * @returns {boolean}
 */
export function membershipStepRequired(me) {
  if (!me) return false;
  if (typeof me.membershipRequired === 'boolean') return me.membershipRequired;
  return !hasMemberTier(me);
}

/** Whether a feature is gated for non-members: every feature in `FullyGated`, the listed ones in `PartiallyGated`, none in `Open`. */
export function isFeatureGated(property, feature) {
  const access = accessOf(property);
  if (access.model === AccessModel.FullyGated) return true;
  if (access.model === AccessModel.Open) return false;
  return access.gatedFeatures.includes(feature);
}

/**
 * Whether a feature can be used, and what is missing when it cannot.
 *
 * - `read`: lists and detail. Signed out only for the feed, forums, and assistants under
 *   anonymous browsing (forums and assistants not gated); signed in always outside `FullyGated`
 *   except gated features other than the feed in `PartiallyGated`.
 * - `write`: create, comment, react, follow, vote. Never signed out; a membership in
 *   `FullyGated` and for gated features in `PartiallyGated`.
 *
 * @param {object} property
 * @param {object|null} me
 * @param {string} feature an AccessFeature value
 * @param {'read'|'write'} [action]
 * @returns {{ allowed: boolean, requires: string|null }}
 */
export function featureAccess(property, me, feature, action = 'read') {
  const tier = callerTier(me);
  if (tier === AccessTier.Staff || tier === AccessTier.Member) return { allowed: true, requires: null };
  const access = accessOf(property);
  const gated = isFeatureGated(property, feature);
  if (tier === AccessTier.Anonymous) {
    const browsable = action === 'read' && access.anonymousBrowsing && SignedOutFeatures.includes(feature) && !(gated && READ_GATED.includes(feature));
    return browsable ? { allowed: true, requires: null } : { allowed: false, requires: AccessRequirement.SignIn };
  }
  if (access.model === AccessModel.Open) return { allowed: true, requires: null };
  if (access.model === AccessModel.FullyGated) return { allowed: false, requires: AccessRequirement.Membership };
  if (!gated) return { allowed: true, requires: null };
  if (action === 'read' && !READ_GATED.includes(feature)) return { allowed: true, requires: null };
  return { allowed: false, requires: AccessRequirement.Membership };
}

/**
 * Catalog features (docs/PHASE4_CONTRACT.md section 0.4 with resolved decision 2): the library,
 * events, and the venue directory. Unlike forums, their catalogs stay readable to signed-in free
 * accounts when gated in `PartiallyGated` (members-only objects arrive as locked stubs), and
 * signed-out visitors may browse their `Public` objects when anonymous browsing is on outside
 * `FullyGated`. The member profile directory (`/members/directory`) keeps `featureAccess`.
 */
export const CatalogFeatures = Object.freeze([AccessFeature.Library, AccessFeature.Events, AccessFeature.Directory]);

/**
 * Whether a catalog feature (library, events, venue directory) can be used, and how.
 *
 * - `read`: lists and detail. Members and staff always. Signed-in free accounts: always in
 *   `Open` and `PartiallyGated` (gated objects come back as locked stubs, `lockedStubs: true`);
 *   a membership in `FullyGated`. Signed out: allowed with `publicOnly: true` when anonymous
 *   browsing is on (never in `FullyGated`), else `SignIn`.
 * - `write`: RSVP, progress, submissions, proposals, comments. Never signed out; a membership in
 *   `FullyGated` and for a gated feature in `PartiallyGated` (the server still decides per object,
 *   for example sample lessons).
 *
 * @param {object} property
 * @param {object|null} me
 * @param {string} feature `library`, `events`, or `directory`
 * @param {'read'|'write'} [action]
 * @returns {{ allowed: boolean, requires: string|null, lockedStubs: boolean, publicOnly: boolean }}
 */
export function catalogAccess(property, me, feature, action = 'read') {
  const tier = callerTier(me);
  const result = (allowed, requires = null, extra = {}) => ({ allowed, requires, lockedStubs: false, publicOnly: false, ...extra });
  if (tier === AccessTier.Staff || tier === AccessTier.Member) return result(true);
  const access = accessOf(property);
  const gated = isFeatureGated(property, feature);
  if (tier === AccessTier.Anonymous) {
    if (action === 'read' && access.anonymousBrowsing && access.model !== AccessModel.FullyGated) return result(true, null, { publicOnly: true });
    return result(false, AccessRequirement.SignIn);
  }
  if (access.model === AccessModel.FullyGated) return result(false, AccessRequirement.Membership);
  if (access.model === AccessModel.Open || !gated) return result(true);
  if (action === 'read') return result(true, null, { lockedStubs: true });
  return result(false, AccessRequirement.Membership);
}

/**
 * Whether navigation shows a lock badge on a feature: `PartiallyGated`, the feature is gated,
 * and the caller is not a member (staff never see locks).
 */
export function showsLockBadge(property, me, feature) {
  return accessOf(property).model === AccessModel.PartiallyGated && isFeatureGated(property, feature) && !hasMemberTier(me);
}

/** Whether signed-out visitors may browse a feature (anonymous browsing on and the feature open to them). */
export function signedOutBrowsable(property, feature) {
  return featureAccess(property, null, feature, 'read').allowed;
}

/** Whether a post (or any object with `viewer`) is a locked stub the caller cannot open. */
export function isLockedItem(item) {
  if (!item) return false;
  return item.locked === true || Boolean(item.viewer && item.viewer.canOpen === false);
}

/** What a locked item asks for (`viewer.requires`), defaulting to a membership. */
export function lockedRequirement(item) {
  const value = item && item.viewer && item.viewer.requires;
  return value === AccessRequirement.SignIn ? AccessRequirement.SignIn : AccessRequirement.Membership;
}

/**
 * The access denial in an API error, or null. `402` (any body) is a membership requirement;
 * `401` only counts when its body carries `required: "SignIn"` (a plain 401 is an expired
 * session). The body's `feature`, `targetType`, and `targetId` give context (contract 3.4).
 * @param {any} error ApiError
 * @returns {{ required: string, feature: string|null, targetType: string|null, targetId: string|null }|null}
 */
export function accessDenial(error) {
  if (!error) return null;
  const body = isObject(error.body) ? error.body : {};
  const context = { feature: body.feature || null, targetType: body.targetType || null, targetId: body.targetId || null };
  if (error.status === 402 || error.code === 'MembershipRequired' || body.required === AccessRequirement.Membership) {
    return { required: AccessRequirement.Membership, ...context };
  }
  if (error.status === 401 && body.required === AccessRequirement.SignIn) return { required: AccessRequirement.SignIn, ...context };
  return null;
}

/**
 * What the caller should be offered for a requirement: `signIn`, `upgrade` (a membership can be
 * bought here), or `none` (purchases are off, so nothing can be offered; the app explains).
 * @param {object} property
 * @param {string} required an AccessRequirement value
 * @returns {'signIn'|'upgrade'|'none'}
 */
export function accessAction(property, required) {
  if (required === AccessRequirement.SignIn) return 'signIn';
  return purchasesEnabled(property) ? 'upgrade' : 'none';
}

/**
 * The composer audience picker (contract section 8.9).
 *
 * - `Open`: `SignedIn` (default) plus `Public` for SystemAdmin.
 * - `PartiallyGated`: `SignedIn` and `Members` (default `access.defaultAudience`, `Members` in a
 *   gated feature) plus `Public` for SystemAdmin.
 * - `FullyGated`: `Members` only (the picker is hidden) plus `Public` for SystemAdmin.
 *
 * A `Public` default applies only to SystemAdmin authors; others fall back to the first option.
 *
 * @param {object} property
 * @param {{ isSystemAdmin?: boolean, feature?: string|null }} [options]
 * @returns {{ options: string[], defaultAudience: string, visible: boolean }}
 */
export function composerAudiences(property, { isSystemAdmin = false, feature = null } = {}) {
  const access = accessOf(property);
  let options;
  if (access.model === AccessModel.Open) options = [ContentAudience.SignedIn];
  else if (access.model === AccessModel.PartiallyGated) options = [ContentAudience.SignedIn, ContentAudience.Members];
  else options = [ContentAudience.Members];
  if (isSystemAdmin) options = [...options, ContentAudience.Public];
  let defaultAudience = feature && isFeatureGated(property, feature) && access.model === AccessModel.PartiallyGated ? ContentAudience.Members : access.defaultAudience;
  if (!options.includes(defaultAudience)) defaultAudience = options[0];
  return { options, defaultAudience, visible: options.length > 1 };
}

const AUDIENCE_RANK = Object.freeze({ Public: 0, SignedIn: 1, Members: 2 });

/**
 * Keep only audiences at least as narrow as `floor` (a thread is never broader than its forum).
 * When nothing is left the floor itself is returned.
 * @param {string[]} options
 * @param {string|null|undefined} floor
 */
export function audiencesWithin(options, floor) {
  if (!floor || AUDIENCE_RANK[floor] === undefined) return options;
  const kept = options.filter((a) => AUDIENCE_RANK[a] >= AUDIENCE_RANK[floor]);
  return kept.length ? kept : [floor];
}

/**
 * The audience to show for an existing object in a picker (an audience the current model does
 * not offer, for example `Members` after a switch to `Open`, is kept so saving does not change it
 * silently).
 * @param {string[]} options
 * @param {string|null|undefined} current
 */
export function audienceChoices(options, current) {
  if (current && !options.includes(current)) return [...options, current];
  return options;
}

/**
 * Validate an admin access form before `PUT /api/v1/admin/property/access`; mirrors the
 * server's checks (contract section 1). Returns `{ field: code }`: `modelInvalid`,
 * `featureInvalid`, `membersInOpen`.
 * @param {{ model: string, gatedFeatures: string[], defaultAudience: string|null }} form
 */
export function validateAccessForm(form) {
  const problems = {};
  if (!AccessModels.includes(form && form.model)) problems.model = 'modelInvalid';
  const features = (form && form.gatedFeatures) || [];
  if (!Array.isArray(features) || features.some((key) => !AccessFeatures.includes(key)) || new Set(features).size !== features.length) problems.gatedFeatures = 'featureInvalid';
  if (form && form.model === AccessModel.Open && form.defaultAudience === ContentAudience.Members) problems.defaultAudience = 'membersInOpen';
  return problems;
}

/**
 * Build the `PUT /api/v1/admin/property/access` body from a form: every field is sent, and an
 * empty default audience becomes an explicit null (back to the model default), and FullyGated always
 * sends null (its default is always Members).
 * @param {{ model: string, anonymousBrowsing: boolean, gatedFeatures: string[], defaultAudience: string|null }} form
 */
export function accessUpdateBody(form) {
  return {
    model: form.model,
    anonymousBrowsing: Boolean(form.anonymousBrowsing),
    gatedFeatures: AccessFeatures.filter((key) => (form.gatedFeatures || []).includes(key)),
    // FullyGated always uses Members, so a stale value is never sent there.
    defaultAudience: form.model === AccessModel.FullyGated ? null : (form.defaultAudience || null)
  };
}

/**
 * What switching from one model to another changes, as keys the admin page turns into text:
 * `purchasesOff` (paid to `Open`: purchases, codes, and paywalls disappear; existing
 * subscriptions keep billing at the stores until members cancel there), `purchasesOn` (back to
 * paid: active entitlements count again), `membershipRequired` (to `FullyGated`: everything
 * needs a membership; anonymous browsing and gated features stop applying), `partial` (to
 * `PartiallyGated`). Empty when nothing changes.
 * @param {string} from
 * @param {string} to
 * @returns {string[]}
 */
export function accessSwitchConsequences(from, to) {
  if (!from || !to || from === to) return [];
  const out = [];
  if (to === AccessModel.Open) out.push('purchasesOff');
  if (from === AccessModel.Open) out.push('purchasesOn');
  if (to === AccessModel.FullyGated) out.push('membershipRequired');
  if (to === AccessModel.PartiallyGated) out.push('partial');
  return out;
}

// ---- Phase 5 feature keys (docs/PHASE5_CONTRACT.md sections 0.2, 0.3, 1.1, as-built notes C and D) ----

/**
 * Phase 5 feature keys. `messaging` and `ideas` are gatable (`ideas` joins `gatedFeatures` in
 * Phase 5); `support` is never gated (every signed-in account reaches it); `broadcasts` is a
 * staff feature.
 */
export const Phase5Feature = Object.freeze({ Messaging: 'messaging', Support: 'support', Ideas: 'ideas', Broadcasts: 'broadcasts' });

function roleEntries(me) {
  return (me && Array.isArray(me.roles) ? me.roles : []).map((r) => (typeof r === 'string' ? { role: r } : r || {}));
}

function hasRole(me, role, resourceType) {
  return roleEntries(me).some((r) => r.role === role && (!resourceType || r.resourceType === resourceType));
}

/** Whether `features.<key>` is on in the property configuration (missing means on). */
export function propertyFeatureEnabled(property, key) {
  const features = isObject(property) && isObject(property.features) ? property.features : {};
  return features[key] !== false;
}

/** Whether the caller holds `SystemAdmin`. */
export function isSystemAdmin(me) {
  return hasRole(me, Role.SystemAdmin);
}

/** Whether the caller works the ticket inbox and the ideas board (`SystemAdmin` or `SupportAgent`). */
export function isSupportStaff(me) {
  return hasRole(me, Role.SystemAdmin) || hasRole(me, Role.SupportAgent);
}

/** Whether the caller may use the broadcast console (`SystemAdmin` or `Communicator`, as-built note D). */
export function isCommunicator(me) {
  return hasRole(me, Role.SystemAdmin) || hasRole(me, Role.Communicator);
}

/**
 * Whether the caller may use the Releases console (`SystemAdmin` or `ReleaseManager`, docs/LIVE_UPDATES_CONTRACT.md as-built
 * notes): updates, channels, rollouts, flags, kill switches, strings, banners, assets, and content versions.
 */
export function isReleaseManager(me) {
  return hasRole(me, Role.SystemAdmin) || hasRole(me, Role.ReleaseManager);
}

/** Whether the caller may manage update signing and deploy keys (`SystemAdmin` only). */
export function canManageSigningKeys(me) {
  return hasRole(me, Role.SystemAdmin);
}

/** Whether the caller may send an `Essential` broadcast (`SystemAdmin` only). */
export function canSendEssentialBroadcast(me) {
  return hasRole(me, Role.SystemAdmin);
}

/** Whether the caller works the message report queue (`SystemAdmin` or a `Moderator` with resource type `Messaging`). */
export function isMessagingModerator(me) {
  return hasRole(me, Role.SystemAdmin) || hasRole(me, Role.Moderator, 'Messaging');
}

/**
 * Whether direct messages can be used. Never signed out; gated like any gatable feature, reads
 * included (resolved decision 26: a lapsed member in a gated model loses history access);
 * `write` (start, send, react) also needs `flags.canSendDirectMessages` and not muted.
 * @param {object} property
 * @param {object|null} me
 * @param {'read'|'write'} [action]
 * @returns {{ allowed: boolean, requires: string|null, reason: string|null }}
 */
export function canUseMessaging(property, me, action = 'read') {
  if (!propertyFeatureEnabled(property, Phase5Feature.Messaging)) return { allowed: false, requires: null, reason: 'FeatureOff' };
  if (!me) return { allowed: false, requires: AccessRequirement.SignIn, reason: null };
  const decision = featureAccess(property, me, AccessFeature.Messaging, 'write');
  if (!decision.allowed) return { ...decision, reason: null };
  if (action === 'write') {
    if (me.flags && me.flags.canSendDirectMessages === false) return { allowed: false, requires: null, reason: 'MessagingNotAllowed' };
    if (me.isMuted === true) return { allowed: false, requires: null, reason: 'MessagingRestricted' };
  }
  return { allowed: true, requires: null, reason: null };
}

/**
 * Whether the ideas board can be used. Reads: signed out only with anonymous browsing and
 * `ideas` not gated (Public ideas); `write` (submit, vote, comment, react) needs sign-in, and a
 * membership where `ideas` is gated (every feature in `FullyGated`).
 * @param {object} property
 * @param {object|null} me
 * @param {'read'|'write'} [action]
 * @returns {{ allowed: boolean, requires: string|null, reason: string|null }}
 */
export function canUseIdeas(property, me, action = 'read') {
  if (!propertyFeatureEnabled(property, Phase5Feature.Ideas)) return { allowed: false, requires: null, reason: 'FeatureOff' };
  const tier = callerTier(me);
  if (tier === AccessTier.Staff || tier === AccessTier.Member) return { allowed: true, requires: null, reason: null };
  const access = accessOf(property);
  const gated = access.model === AccessModel.FullyGated || (access.model === AccessModel.PartiallyGated && access.gatedFeatures.includes(AccessFeature.Ideas));
  if (tier === AccessTier.Anonymous) {
    const browsable = action === 'read' && access.anonymousBrowsing && !gated;
    return browsable ? { allowed: true, requires: null, reason: null } : { allowed: false, requires: AccessRequirement.SignIn, reason: null };
  }
  if (gated) return { allowed: false, requires: AccessRequirement.Membership, reason: null };
  return { allowed: true, requires: null, reason: null };
}

/**
 * Whether support tickets can be used: every signed-in account in every access model (never a
 * membership), when `features.support` is on. Signed out, clients show the support email.
 * @returns {{ allowed: boolean, requires: string|null, reason: string|null }}
 */
export function canUseSupport(property, me) {
  if (!propertyFeatureEnabled(property, Phase5Feature.Support)) return { allowed: false, requires: null, reason: 'FeatureOff' };
  if (!me) return { allowed: false, requires: AccessRequirement.SignIn, reason: null };
  return { allowed: true, requires: null, reason: null };
}

/** Whether the broadcast console is available (`features.broadcasts` on and a Communicator or SystemAdmin). */
export function canUseBroadcasts(property, me) {
  return propertyFeatureEnabled(property, Phase5Feature.Broadcasts) && isCommunicator(me);
}
