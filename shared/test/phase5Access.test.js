import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  canSendEssentialBroadcast, canUseBroadcasts, canUseIdeas, canUseMessaging, canUseSupport, isCommunicator, isMessagingModerator, isStaffUser, isSupportStaff,
  isSystemAdmin, Phase5Feature, propertyFeatureEnabled, Role
} from '../src/index.js';

const open = { access: { model: 'Open', anonymousBrowsing: true } };
const partial = { access: { model: 'PartiallyGated', anonymousBrowsing: true, gatedFeatures: ['messaging', 'ideas'] } };
const partialOpenIdeas = { access: { model: 'PartiallyGated', anonymousBrowsing: true, gatedFeatures: ['messaging'] } };
const full = { access: { model: 'FullyGated' } };

const signedIn = { id: 'usr_1', roles: [], accessTier: 'SignedIn' };
const member = { id: 'usr_2', roles: [], accessTier: 'Member' };
const lapsed = { id: 'usr_3', roles: [], accessTier: 'SignedIn', membership: { isActive: false } };
const agent = { id: 'usr_4', roles: [{ role: 'SupportAgent', resourceType: 'Global', resourceId: '*' }], accessTier: 'Staff' };
const communicator = { id: 'usr_5', roles: [{ role: 'Communicator', resourceType: 'Global', resourceId: '*' }], accessTier: 'Staff' };
const admin = { id: 'usr_6', roles: [{ role: 'SystemAdmin' }], accessTier: 'Staff' };
const dmModerator = { id: 'usr_7', roles: [{ role: 'Moderator', resourceType: 'Messaging', resourceId: '*' }], accessTier: 'Staff' };

test('support is reachable by every signed-in account in every model and never needs a membership', () => {
  for (const property of [open, partial, full]) {
    for (const me of [signedIn, member, lapsed, agent]) assert.deepEqual(canUseSupport(property, me), { allowed: true, requires: null, reason: null });
    assert.equal(canUseSupport(property, null).requires, 'SignIn');
  }
  assert.equal(canUseSupport({ ...full, features: { support: false } }, member).reason, 'FeatureOff');
});

test('messaging is gated, never signed out, and checks the send flags for writes', () => {
  assert.equal(canUseMessaging(open, signedIn).allowed, true);
  assert.equal(canUseMessaging(open, null).requires, 'SignIn');
  assert.equal(canUseMessaging(partial, signedIn).requires, 'Membership');
  assert.equal(canUseMessaging(partialOpenIdeas, signedIn, 'read').requires, 'Membership');
  assert.equal(canUseMessaging(full, lapsed, 'read').requires, 'Membership');
  assert.equal(canUseMessaging(full, member, 'write').allowed, true);
  assert.equal(canUseMessaging(full, { ...member, flags: { canSendDirectMessages: false } }, 'write').reason, 'MessagingNotAllowed');
  assert.equal(canUseMessaging(full, { ...member, flags: { canSendDirectMessages: false } }, 'read').allowed, true);
  assert.equal(canUseMessaging(full, { ...member, isMuted: true }, 'write').reason, 'MessagingRestricted');
  assert.equal(canUseMessaging({ ...open, features: { messaging: false } }, member).reason, 'FeatureOff');
});

test('ideas are gatable; signed-out reads only with anonymous browsing when not gated', () => {
  assert.equal(canUseIdeas(open, null, 'read').allowed, true);
  assert.equal(canUseIdeas(open, null, 'write').requires, 'SignIn');
  assert.equal(canUseIdeas(open, signedIn, 'write').allowed, true);
  assert.equal(canUseIdeas(partial, signedIn, 'read').requires, 'Membership');
  assert.equal(canUseIdeas(partial, null, 'read').requires, 'SignIn');
  assert.equal(canUseIdeas(partialOpenIdeas, signedIn, 'write').allowed, true);
  assert.equal(canUseIdeas(full, signedIn).requires, 'Membership');
  assert.equal(canUseIdeas(full, member, 'write').allowed, true);
  assert.equal(canUseIdeas(full, agent, 'write').allowed, true);
  assert.equal(canUseIdeas({ features: { ideas: false } }, member).reason, 'FeatureOff');
});

test('role helpers for support staff, communicators, and messaging moderators', () => {
  assert.equal(Role.SupportAgent, 'SupportAgent');
  assert.equal(Role.Communicator, 'Communicator');
  assert.equal(isSupportStaff(agent), true);
  assert.equal(isSupportStaff(admin), true);
  assert.equal(isSupportStaff(communicator), false);
  assert.equal(isCommunicator(communicator), true);
  assert.equal(isCommunicator(admin), true);
  assert.equal(isCommunicator(agent), false);
  assert.equal(canSendEssentialBroadcast(communicator), false);
  assert.equal(canSendEssentialBroadcast(admin), true);
  assert.equal(isMessagingModerator(dmModerator), true);
  assert.equal(isMessagingModerator({ roles: [{ role: 'Moderator', resourceType: 'Forum', resourceId: '*' }] }), false);
  assert.equal(isSystemAdmin(admin), true);
  assert.equal(isSystemAdmin(null), false);
  assert.equal(isStaffUser({ roles: ['SupportAgent'] }), true);
  assert.equal(canUseBroadcasts(open, communicator), true);
  assert.equal(canUseBroadcasts({ features: { broadcasts: false } }, admin), false);
  assert.equal(canUseBroadcasts(open, member), false);
  assert.equal(propertyFeatureEnabled({}, Phase5Feature.Support), true);
});
