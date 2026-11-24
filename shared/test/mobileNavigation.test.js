import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { DefaultMobileShortcuts, MobileDestinationIds, loadProperty, mergeProperty, mobileNavigationOf, resolveMobileNavigation, validateMobileNavigation } from '../src/index.js';

const sample = JSON.parse(readFileSync(new URL('../../samples/example/property.json', import.meta.url)));
const member = { id: 'member-test', accessTier: 'Member' };
const ids = (entries) => entries.map((entry) => entry.id);

test('neutral default navigation provides all destinations and four shortcuts without property literals', () => {
  assert.deepEqual(mobileNavigationOf({}).shortcuts, DefaultMobileShortcuts);
  const nav = resolveMobileNavigation({ features: { ai: true } }, member);
  assert.deepEqual(ids(nav.drawerItems), MobileDestinationIds);
  assert.equal(nav.drawerItems.find((entry) => entry.id === 'members').section, 'community');
  assert.equal(nav.drawerItems.find((entry) => entry.id === 'settings').href, '/account');
});

test('properties reorder destinations and choose shortcuts without stranding omitted features', () => {
  const property = { mobile: { navigation: { drawerItems: ['members', 'events'], shortcuts: ['members', 'messages'] } } };
  const nav = resolveMobileNavigation(property, member);
  assert.deepEqual(ids(nav.drawerItems).slice(0, 2), ['members', 'events']);
  assert.equal(new Set(ids(nav.drawerItems)).size, nav.drawerItems.length);
  assert.ok(nav.drawerItems.some((item) => item.id === 'settings'));
  assert.deepEqual(ids(nav.shortcuts), ['members', 'messages']);
  assert.deepEqual(mobileNavigationOf({ mobile: { navigation: { shortcuts: [] } } }).shortcuts, []);
});

test('disabled features and unavailable AI disappear from both surfaces', () => {
  const property = { features: { library: false, events: false, directory: false, messaging: false, ideas: false, support: false }, mobile: { navigation: { shortcuts: ['library', 'events', 'messages', 'assistants'] } } };
  const nav = resolveMobileNavigation(property, member);
  assert.deepEqual(nav.shortcuts, []);
  for (const id of ['library', 'events', 'directory', 'messages', 'assistants', 'ideas', 'support']) assert.ok(!ids(nav.drawerItems).includes(id), id);
  assert.ok(ids(nav.drawerItems).includes('members'));
  assert.ok(ids(resolveMobileNavigation({ features: { ai: true } }, member).drawerItems).includes('assistants'));
});

test('guest navigation honors anonymous browsing and excludes signed-in account actions', () => {
  const property = { access: { model: 'Open', anonymousBrowsing: true }, features: { ai: true } };
  const nav = resolveMobileNavigation(property);
  for (const id of ['feed', 'forums', 'library', 'events', 'directory', 'assistants', 'profile', 'about']) assert.ok(ids(nav.drawerItems).includes(id), id);
  for (const id of ['members', 'messages', 'notifications', 'settings', 'announcements', 'support', 'ideas']) assert.ok(!ids(nav.drawerItems).includes(id), id);
  assert.deepEqual(ids(resolveMobileNavigation({ access: { model: 'FullyGated' } }).drawerItems), ['profile', 'about']);
});

test('gated signed-in features stay discoverable and locked; members and staff have no locks', () => {
  const property = { access: { model: 'PartiallyGated', gatedFeatures: ['members', 'messaging'] } };
  const nav = resolveMobileNavigation(property, { id: 'free', accessTier: 'SignedIn' });
  assert.equal(nav.drawerItems.find((entry) => entry.id === 'members').locked, true);
  assert.equal(nav.shortcuts.find((entry) => entry.id === 'messages').locked, true);
  assert.equal(nav.drawerItems.find((entry) => entry.id === 'settings').locked, false);
  assert.ok(resolveMobileNavigation(property, member).drawerItems.every((entry) => !entry.locked));
  assert.ok(resolveMobileNavigation(property, { accessTier: 'Staff' }).drawerItems.every((entry) => !entry.locked));
});

test('labels come from property terminology and app string catalog; events and venues remain distinct', () => {
  const property = { terminology: { members: 'participants' }, directory: { venueLabelPlural: 'Studios' } };
  const nav = resolveMobileNavigation(property, member, { feed: 'Community feed' });
  assert.equal(nav.drawerItems.find((entry) => entry.id === 'members').label, 'Participants');
  assert.equal(nav.drawerItems.find((entry) => entry.id === 'directory').label, 'Studios');
  assert.equal(nav.drawerItems.find((entry) => entry.id === 'feed').label, 'Community feed');
  assert.equal(nav.drawerItems.find((entry) => entry.id === 'directory').href, '/map?section=places');
  assert.equal(nav.drawerItems.find((entry) => entry.id === 'events').href, '/map?section=events');
});

test('configuration rejects unknown routes, duplicates, invalid arrays and excessive shortcuts', () => {
  for (const config of [[], 'bad', { shortcuts: ['members', 'members'] }, { shortcuts: ['../../admin'] }, { shortcuts: ['feed', 'forums', 'library', 'members', 'messages'] }, { drawerItems: {} }, { drawerItems: [null] }]) {
    assert.ok(validateMobileNavigation(config).length > 0, JSON.stringify(config));
    assert.throws(() => loadProperty({ ...sample, mobile: { navigation: config } }), /mobile.navigation/);
  }
  assert.deepEqual(validateMobileNavigation(null), []);
  assert.deepEqual(validateMobileNavigation({ drawerItems: [], shortcuts: [] }), []);
});

test('malformed cached config is bounded and deduplicated defensively', () => {
  const nav = mobileNavigationOf({ mobile: { navigation: { drawerItems: ['members', 'bogus', 'members'], shortcuts: ['feed', 'feed', 'forums', 'library', 'members', 'messages'] } } });
  assert.deepEqual(nav.shortcuts, ['feed', 'forums', 'library', 'members']);
  assert.equal(nav.drawerItems.length, MobileDestinationIds.length);
  assert.equal(nav.drawerItems[0], 'members');
});

test('older server mobile identity preserves bundled navigation; newer fields override independently', () => {
  const base = { ...sample, mobile: { ...sample.mobile, navigation: { drawerItems: ['members', 'feed'], shortcuts: ['members', 'messages'] } } };
  const legacy = mergeProperty(base, { mobile: { bundleId: 'test.example.app', navigation: null } });
  assert.equal(legacy.mobile.bundleId, 'test.example.app');
  assert.deepEqual(legacy.mobile.navigation, base.mobile.navigation);
  const updated = mergeProperty(base, { mobile: { navigation: { shortcuts: [] } } });
  assert.deepEqual(updated.mobile.navigation, { drawerItems: ['members', 'feed'], shortcuts: [] });
  assert.deepEqual(mergeProperty(base, { mobile: { navigation: { drawerItems: null, shortcuts: ['feed'] } } }).mobile.navigation, { drawerItems: ['members', 'feed'], shortcuts: ['feed'] });
  assert.equal(mergeProperty(base, { mobile: { navigation: { shortcuts: ['invalid'] } } }), base);
  assert.deepEqual(base.mobile.navigation.shortcuts, ['members', 'messages']);
});
