import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { DefaultMobileShortcuts, MobileDestinationIds, MobileDestinations, loadProperty, mergeProperty, mobileNavigationOf, resolveMobileNavigation, validateMobileNavigation } from '../src/index.js';

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
  const nav = mobileNavigationOf({ mobile: { navigation: { drawerItems: ['members', 'bogus', 'members'], shortcuts: ['feed', 'feed', 'forums', 'library', 'members', 'messages'], accountMenu: ['profile', 'profile', 'bogus'], labels: { feed: '  Home  ', bogus: 'X', forums: 42, library: '' } } } });
  assert.deepEqual(nav.shortcuts, ['feed', 'forums', 'library', 'members']);
  assert.deepEqual(nav.accountMenu, ['profile']);
  assert.deepEqual(nav.labels, { feed: 'Home' });
  assert.equal(new Set(nav.drawerItems).size, nav.drawerItems.length);
  assert.equal(nav.drawerItems[0], 'members');
  for (const id of ['feed', 'forums', 'library', 'profile', 'notifications']) assert.ok(!nav.drawerItems.includes(id), id);
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

test('account menu and shortcuts count as placements; header destinations are not appended', () => {
  const property = { features: { ai: true }, mobile: { navigation: {
    drawerItems: ['forums', 'members'], shortcuts: ['feed', 'directory', 'events', 'messages'],
    accountMenu: ['profile', 'settings', 'support', 'about'], labels: { directory: 'Explore' }
  } } };
  const nav = resolveMobileNavigation(property, member);
  assert.deepEqual(ids(nav.shortcuts), ['feed', 'directory', 'events', 'messages']);
  assert.deepEqual(ids(nav.accountMenu), ['profile', 'settings', 'support', 'about']);
  assert.equal(nav.shortcuts.find((entry) => entry.id === 'directory').label, 'Explore');
  const drawer = ids(nav.drawerItems);
  assert.deepEqual(drawer.slice(0, 2), ['forums', 'members']);
  // Notifications lives in the header, so it is not appended.
  for (const id of ['notifications', 'feed', 'directory', 'events', 'messages', 'profile', 'settings', 'support', 'about']) assert.ok(!drawer.includes(id), id);
  // Everything else stays reachable.
  for (const id of ['library', 'assistants', 'announcements', 'ideas']) assert.ok(drawer.includes(id), id);
  // An explicitly placed header destination stays where the property put it.
  assert.ok(mobileNavigationOf({ mobile: { navigation: { drawerItems: ['feed', 'notifications'] } } }).drawerItems.includes('notifications'));
  // Missing configuration keeps every destination in the drawer and no account menu.
  assert.deepEqual(mobileNavigationOf({}).drawerItems, MobileDestinationIds);
  assert.deepEqual(mobileNavigationOf({}).accountMenu, []);
  // Guests never see signed-in account destinations.
  assert.deepEqual(ids(resolveMobileNavigation({ ...property, access: { model: 'Open', anonymousBrowsing: true } }).accountMenu), ['profile', 'about']);
});

test('a destination covered by a placed destination is not appended, and returns when its cover is unplaced', () => {
  const covering = MobileDestinations.find((entry) => entry.coveredBy);
  if (!covering) return;
  const placed = mobileNavigationOf({ mobile: { navigation: { drawerItems: [covering.coveredBy], shortcuts: [] } } });
  assert.ok(!placed.drawerItems.includes(covering.id));
  const unplaced = mobileNavigationOf({ mobile: { navigation: { drawerItems: ['feed'], shortcuts: [] } } });
  assert.ok(unplaced.drawerItems.includes(covering.id));
});

test('account menu and label configuration is validated and merged from the server', () => {
  for (const config of [{ accountMenu: 'profile' }, { accountMenu: ['profile', 'profile'] }, { accountMenu: ['nowhere'] }, { labels: [] }, { labels: { nowhere: 'X' } }, { labels: { feed: '' } }, { labels: { feed: 'x'.repeat(25) } }, { labels: { feed: 7 } }]) {
    assert.ok(validateMobileNavigation(config).length > 0, JSON.stringify(config));
  }
  assert.deepEqual(validateMobileNavigation({ accountMenu: [], labels: {} }), []);
  assert.deepEqual(validateMobileNavigation({ accountMenu: ['profile'], labels: { directory: 'Explore' } }), []);
  const bundled = { ...sample, mobile: { ...sample.mobile, navigation: { drawerItems: ['feed'], shortcuts: [], accountMenu: ['profile'], labels: { feed: 'Home' } } } };
  const merged = mergeProperty(bundled, { ...sample, mobile: { ...sample.mobile, navigation: { drawerItems: ['forums'], shortcuts: ['feed'], accountMenu: null } } });
  assert.deepEqual(merged.mobile.navigation, { drawerItems: ['forums'], shortcuts: ['feed'], accountMenu: ['profile'], labels: { feed: 'Home' } });
});
