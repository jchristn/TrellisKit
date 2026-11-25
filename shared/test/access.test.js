import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import {
  AccessFeatures, AccessModel, accessAction, accessDenial, accessOf, accessSwitchConsequences, accessUpdateBody, ApiError, audienceChoices,
  callerTier, composerAudiences, featureAccess, isFeatureGated, isLockedItem, lockedRequirement, membershipProduct, membershipStepRequired,
  mergeProperty, minimumAgeOf, propertyTokens, purchasesEnabled, showsLockBadge, signedOutBrowsable, signupCodesEnabled, validateAccessForm,
  validateProperty
} from '../src/index.js';

const base = JSON.parse(readFileSync(new URL('./fixtures/dojo.json', import.meta.url), 'utf8'));

const open = { ...base, membership: null, minimumAge: 16, access: { model: 'Open', anonymousBrowsing: true, gatedFeatures: [], defaultAudience: 'SignedIn', purchasesEnabled: false, signupCodesEnabled: false, revision: 2 } };
const partial = { ...base, access: { model: 'PartiallyGated', anonymousBrowsing: true, gatedFeatures: ['forums', 'members', 'assistants'], defaultAudience: 'SignedIn', purchasesEnabled: true, signupCodesEnabled: true, revision: 3 } };
const full = { ...base, access: { model: 'FullyGated', anonymousBrowsing: false, gatedFeatures: [], defaultAudience: 'Members', purchasesEnabled: true, signupCodesEnabled: true, revision: 1 } };

const signedIn = { id: 'usr_1', roles: [], membership: { isActive: false }, accessTier: 'SignedIn', membershipRequired: false };
const member = { id: 'usr_2', roles: [], membership: { isActive: true }, accessTier: 'Member', membershipRequired: false };
const admin = { id: 'usr_3', roles: [{ role: 'SystemAdmin' }], membership: { isActive: false }, accessTier: 'Staff', membershipRequired: false };

test('accessOf normalizes server and property.json sections', () => {
  assert.equal(accessOf({}).model, AccessModel.FullyGated);
  assert.equal(accessOf({}).purchasesEnabled, true);
  assert.equal(accessOf({ access: { model: 'FullyGated', anonymousBrowsing: true, gatedFeatures: ['feed'] } }).anonymousBrowsing, false);
  assert.deepEqual(accessOf({ access: { model: 'Open', gatedFeatures: ['feed'] } }).gatedFeatures, []);
  assert.equal(accessOf({ access: { model: 'Open' } }).purchasesEnabled, false);
  assert.equal(accessOf({ access: { model: 'Open', defaultAudience: 'Members' } }).defaultAudience, 'SignedIn');
  assert.equal(accessOf({ access: { model: 'PartiallyGated' } }).defaultAudience, 'Members');
  assert.deepEqual(accessOf({ access: { model: 'PartiallyGated', gatedFeatures: ['members', 'bogus', 'members'] } }).gatedFeatures, ['members', 'directory']);
  assert.equal(accessOf({ access: { model: 'PartiallyGated' }, features: { signupCodes: false } }).signupCodesEnabled, false);
  assert.equal(accessOf(partial).revision, 3);
});

test('Open: no purchases, membership null, minimumAge from the top level', () => {
  assert.deepEqual(validateProperty(open), []);
  assert.equal(purchasesEnabled(open), false);
  assert.equal(signupCodesEnabled(open), false);
  assert.equal(membershipProduct(open), null);
  assert.equal(minimumAgeOf(open), 16);
  assert.equal(minimumAgeOf({}), 18);
  assert.equal(propertyTokens(open).minimumAge, '16');
  const merged = mergeProperty(base, { access: open.access, membership: null, minimumAge: 16 });
  assert.equal(merged.membership, null);
  assert.ok(validateProperty({ ...base, membership: null }).includes('membership is required'));
});

test('Open: everything needs only sign-in; nothing is locked', () => {
  assert.equal(isFeatureGated(open, 'feed'), false);
  assert.deepEqual(featureAccess(open, signedIn, 'forums', 'write'), { allowed: true, requires: null });
  assert.deepEqual(featureAccess(open, null, 'feed'), { allowed: true, requires: null });
  assert.deepEqual(featureAccess(open, null, 'feed', 'write'), { allowed: false, requires: 'SignIn' });
  assert.equal(featureAccess(open, null, 'members').requires, 'SignIn');
  assert.equal(showsLockBadge(open, signedIn, 'forums'), false);
  assert.equal(accessAction(open, 'Membership'), 'none');
  const picker = composerAudiences(open);
  assert.deepEqual(picker, { options: ['SignedIn'], defaultAudience: 'SignedIn', visible: false });
  assert.deepEqual(composerAudiences(open, { isSystemAdmin: true }).options, ['SignedIn', 'Public']);
});

test('PartiallyGated: gated features lock for non-members only', () => {
  assert.equal(isFeatureGated(partial, 'forums'), true);
  assert.equal(isFeatureGated(partial, 'directory'), true);
  assert.equal(isFeatureGated(partial, 'feed'), false);
  assert.equal(showsLockBadge(partial, signedIn, 'forums'), true);
  assert.equal(showsLockBadge(partial, member, 'forums'), false);
  assert.equal(showsLockBadge(partial, admin, 'forums'), false);
  assert.deepEqual(featureAccess(partial, signedIn, 'forums'), { allowed: false, requires: 'Membership' });
  assert.deepEqual(featureAccess(partial, signedIn, 'feed', 'write'), { allowed: true, requires: null });
  assert.deepEqual(featureAccess(partial, member, 'forums', 'write'), { allowed: true, requires: null });
  assert.equal(signedOutBrowsable(partial, 'feed'), true);
  assert.equal(signedOutBrowsable(partial, 'forums'), false);
  assert.equal(signedOutBrowsable(partial, 'assistants'), false);
  assert.equal(signedOutBrowsable({ ...partial, access: { ...partial.access, gatedFeatures: [] } }, 'forums'), true);
  const gatedFeed = { ...partial, access: { ...partial.access, gatedFeatures: ['feed'] } };
  assert.deepEqual(featureAccess(gatedFeed, signedIn, 'feed'), { allowed: true, requires: null });
  assert.deepEqual(featureAccess(gatedFeed, signedIn, 'feed', 'write'), { allowed: false, requires: 'Membership' });
  assert.equal(signedOutBrowsable(gatedFeed, 'feed'), true);
  assert.equal(accessAction(partial, 'Membership'), 'upgrade');
  assert.deepEqual(composerAudiences(partial, { feature: 'feed' }), { options: ['SignedIn', 'Members'], defaultAudience: 'SignedIn', visible: true });
  assert.equal(composerAudiences(partial, { feature: 'forums' }).defaultAudience, 'Members');
  const publicDefault = { ...partial, access: { ...partial.access, defaultAudience: 'Public' } };
  assert.equal(composerAudiences(publicDefault).defaultAudience, 'SignedIn');
  assert.equal(composerAudiences(publicDefault, { isSystemAdmin: true }).defaultAudience, 'Public');
});

test('FullyGated: membership required; picker only for SystemAdmin', () => {
  assert.equal(isFeatureGated(full, 'feed'), true);
  assert.equal(showsLockBadge(full, signedIn, 'feed'), false);
  assert.deepEqual(featureAccess(full, signedIn, 'feed'), { allowed: false, requires: 'Membership' });
  assert.equal(signedOutBrowsable(full, 'feed'), false);
  assert.deepEqual(composerAudiences(full), { options: ['Members'], defaultAudience: 'Members', visible: false });
  assert.deepEqual(composerAudiences(full, { isSystemAdmin: true }), { options: ['Members', 'Public'], defaultAudience: 'Members', visible: true });
});

test('caller tier and the paywall step follow Me', () => {
  assert.equal(callerTier(null), 'Anonymous');
  assert.equal(callerTier(member), 'Member');
  assert.equal(callerTier({ roles: [{ role: 'SystemAdmin' }], membership: {} }), 'Staff');
  assert.equal(callerTier({ roles: [], membership: { isActive: true } }), 'Member');
  assert.equal(membershipStepRequired(signedIn), false);
  assert.equal(membershipStepRequired({ ...signedIn, membershipRequired: true }), true);
  assert.equal(membershipStepRequired({ roles: [], membership: { isActive: false } }), true);
  assert.equal(membershipStepRequired({ roles: [], membership: { isActive: true } }), false);
  assert.equal(membershipStepRequired(null), false);
});

test('locked stubs and denials', () => {
  assert.equal(isLockedItem({ locked: true }), true);
  assert.equal(isLockedItem({ locked: false, viewer: { canOpen: false, requires: 'Membership' } }), true);
  assert.equal(isLockedItem({ locked: false, viewer: { canOpen: true } }), false);
  assert.equal(lockedRequirement({ viewer: { requires: 'SignIn' } }), 'SignIn');
  assert.equal(lockedRequirement({}), 'Membership');
  const paid = new ApiError('This is for members.', { status: 402, code: 'MembershipRequired', body: { required: 'Membership', feature: 'feed', targetType: 'FeedPost', targetId: 'fpo_1' } });
  assert.deepEqual(accessDenial(paid), { required: 'Membership', feature: 'feed', targetType: 'FeedPost', targetId: 'fpo_1' });
  assert.equal(accessDenial(new ApiError('x', { status: 402 })).required, 'Membership');
  assert.deepEqual(accessDenial(new ApiError('x', { status: 401, body: { required: 'SignIn', feature: 'forums' } })), { required: 'SignIn', feature: 'forums', targetType: null, targetId: null });
  assert.equal(accessDenial(new ApiError('x', { status: 401, code: 'Unauthorized' })), null);
  assert.equal(accessDenial(new ApiError('x', { status: 404 })), null);
  assert.equal(accessDenial(null), null);
});

test('admin access form helpers', () => {
  assert.deepEqual(validateAccessForm({ model: 'Open', gatedFeatures: [], defaultAudience: 'Members' }), { defaultAudience: 'membersInOpen' });
  assert.deepEqual(validateAccessForm({ model: 'Nope', gatedFeatures: ['feed', 'feed'], defaultAudience: null }), { model: 'modelInvalid', gatedFeatures: 'featureInvalid' });
  assert.deepEqual(validateAccessForm({ model: 'PartiallyGated', gatedFeatures: ['feed'], defaultAudience: '' }), {});
  assert.deepEqual(accessUpdateBody({ model: 'PartiallyGated', anonymousBrowsing: 1, gatedFeatures: ['messaging', 'feed'], defaultAudience: '' }), { model: 'PartiallyGated', anonymousBrowsing: true, gatedFeatures: ['feed', 'messaging'], defaultAudience: null });
  assert.equal(AccessFeatures.length, 11);
  assert.equal(AccessFeatures[8], 'search');
  assert.equal(AccessFeatures[9], 'ideas');
  assert.equal(AccessFeatures[10], 'training');
  assert.deepEqual(accessSwitchConsequences('FullyGated', 'Open'), ['purchasesOff']);
  assert.deepEqual(accessSwitchConsequences('Open', 'FullyGated'), ['purchasesOn', 'membershipRequired']);
  assert.deepEqual(accessSwitchConsequences('Open', 'Open'), []);
  assert.deepEqual(audienceChoices(['SignedIn'], 'Members'), ['SignedIn', 'Members']);
  assert.deepEqual(audienceChoices(['SignedIn'], 'SignedIn'), ['SignedIn']);
});

test('search is gated on its own key, independently of the forums', () => {
  assert.equal(featureAccess(partial, signedIn, 'search').allowed, true);
  const searchOnly = { ...base, access: { ...partial.access, gatedFeatures: ['search'] } };
  assert.deepEqual(accessOf(searchOnly).gatedFeatures, ['search']);
  assert.deepEqual(featureAccess(searchOnly, signedIn, 'search'), { allowed: false, requires: 'Membership' });
  assert.deepEqual(featureAccess(searchOnly, signedIn, 'forums'), { allowed: true, requires: null });
  assert.deepEqual(featureAccess(searchOnly, member, 'search'), { allowed: true, requires: null });
  assert.equal(featureAccess(open, null, 'search').requires, 'SignIn');
  assert.equal(featureAccess(full, signedIn, 'search').requires, 'Membership');
  assert.deepEqual(validateAccessForm({ model: 'PartiallyGated', gatedFeatures: ['search', 'forums'], defaultAudience: null }), {});
  assert.deepEqual(accessUpdateBody({ model: 'PartiallyGated', anonymousBrowsing: false, gatedFeatures: ['search', 'forums'], defaultAudience: null }).gatedFeatures, ['forums', 'search']);
  assert.equal(accessUpdateBody({ model: 'FullyGated', anonymousBrowsing: false, gatedFeatures: [], defaultAudience: 'SignedIn' }).defaultAudience, null);
});

test('accessOf keeps ideas in the gated features (Phase 5)', () => {
  const access = accessOf({ access: { model: 'PartiallyGated', gatedFeatures: ['ideas', 'bogus', 'forums'] } });
  assert.deepEqual(access.gatedFeatures, ['ideas', 'forums']);
  assert.equal(isFeatureGated({ access: { model: 'PartiallyGated', gatedFeatures: ['ideas'] } }, 'ideas'), true);
  assert.equal(isFeatureGated({ access: { model: 'PartiallyGated', gatedFeatures: [] } }, 'ideas'), false);
});
