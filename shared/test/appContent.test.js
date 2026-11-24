import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  ApiClient, applyAppManifest, activeBanners, appContentHeaders, assetsToEvict, assetsToFetch, bannerProblem, createAppContentStore,
  dismissBanner, featureDisabledOf, findRemoteAsset, invalidStringTokens, isBannerDismissible, isReleaseManager, canManageSigningKeys,
  remoteStringOverlay, remoteStringValue, setRemoteStrings, stringTokens, subscribeRemoteStrings, APP_CONTENT_STORAGE_KEY, FLAG_ATTRIBUTES, Role
} from '../src/index.js';
import { buildCatalog, entriesOf } from '../scripts/strings-catalog.mjs';

const here = dirname(fileURLToPath(import.meta.url));

function memoryStorage(seed = {}) {
  const data = { ...seed };
  return { data, getItem: async (k) => (k in data ? data[k] : null), setItem: async (k, v) => { data[k] = v; } };
}

const banner = (id, extra = {}) => ({ id, title: 'T ' + id, body: '', style: 'Info', placement: 'Global', dismissible: true, priority: 0, startsUtc: null, endsUtc: null, contentHash: 'h-' + id, ...extra });

test('remote string overlay replaces string leaves by catalog key and restores them when removed', () => {
  const tree = { common: { retry: 'Retry', greet: 'Hi {name}', fn: (x) => 'v' + x, list: ['a'] }, top: 'Top' };
  const view = remoteStringOverlay(tree, 'web', { fill: (v) => v.replace('{name}', 'Example Community') });
  assert.equal(view.common.retry, 'Retry');
  setRemoteStrings({ 'web.common.retry': 'Try again', 'web.common.greet': 'Hello {name}', 'mobile.common.retry': 'Nope' });
  assert.equal(view.common.retry, 'Try again');
  assert.equal(view.common.greet, 'Hello Example Community');
  assert.equal(view.common.fn(1), 'v1');
  assert.deepEqual(view.common.list, ['a']);
  assert.equal(view.top, 'Top');
  assert.deepEqual(Object.keys(view.common), ['retry', 'greet', 'fn', 'list']);
  assert.equal({ ...view.common }.retry, 'Try again');
  assert.equal(JSON.parse(JSON.stringify(view)).common.retry, 'Try again');
  assert.equal('retry' in view.common, true);
  assert.equal(remoteStringValue('web.common.retry'), 'Try again');
  setRemoteStrings({});
  assert.equal(view.common.retry, 'Retry');
  assert.equal(tree.common.retry, 'Retry', 'the module object is never modified');
});

test('overlay works over frozen trees and notifies subscribers', () => {
  const tree = Object.freeze({ a: Object.freeze({ b: 'B' }) });
  const view = remoteStringOverlay(tree, 'mobile');
  let calls = 0;
  const off = subscribeRemoteStrings(() => { calls += 1; });
  setRemoteStrings({ 'mobile.a.b': 'Override' });
  assert.equal(view.a.b, 'Override');
  off();
  setRemoteStrings({});
  assert.equal(calls, 1);
  assert.equal(view.a.b, 'B');
});

test('applyAppManifest applies full snapshots, deltas, and complete sets', () => {
  let state = applyAppManifest(null, {
    contentVersion: 4, flags: { 'a.b': true }, killSwitches: { feed: { killed: false } },
    strings: { upserts: { 'web.x': 'X', 'web.y': 'Y' }, deletes: [] },
    banners: { objects: [banner('ban_1')], deletes: [] },
    assets: { upserts: [{ id: 'ras_1', key: 'hero', sha256: 'aa', url: 'u1' }], deletes: [] },
    checkIntervalMinutes: 5
  }, { sentSince: null, now: 10 });
  assert.equal(state.contentVersion, 4);
  assert.equal(state.checkIntervalMinutes, 5);
  assert.deepEqual(state.strings, { 'web.x': 'X', 'web.y': 'Y' });
  state = applyAppManifest(state, {
    contentVersion: 5, flags: {}, killSwitches: { feed: { killed: true, message: 'Paused' } },
    strings: { upserts: { 'web.z': 'Z' }, deletes: ['web.x'] },
    banners: { objects: [], deletes: ['ban_1'] },
    assets: { upserts: [{ id: 'ras_2', key: 'logo', sha256: 'bb', url: 'u2' }], deletes: ['ras_1'] }
  }, { sentSince: 4 });
  assert.deepEqual(state.strings, { 'web.y': 'Y', 'web.z': 'Z' });
  assert.deepEqual(state.banners, []);
  assert.deepEqual(Object.keys(state.assets), ['ras_2']);
  assert.deepEqual(state.flags, {});
  assert.equal(state.killSwitches.feed.killed, true);
  const refreshed = applyAppManifest(state, { contentVersion: 900, fullRefresh: true, flags: {}, killSwitches: {}, strings: null, banners: null, assets: null },
    { sentSince: 5, enumerated: { strings: [{ key: 'web.q', value: 'Q' }], banners: [banner('ban_9')], assets: [] } });
  assert.deepEqual(refreshed.strings, { 'web.q': 'Q' });
  assert.equal(refreshed.banners[0].id, 'ban_9');
  assert.equal(refreshed.contentVersion, 900);
});

test('the store sends since, throttles, re-enumerates on fullRefresh, persists, and survives failures', async () => {
  const sent = [];
  let reply = { contentVersion: 2, flags: { 'feed.v': 'v2' }, killSwitches: {}, strings: { upserts: { 'web.a': 'A' }, deletes: [] }, banners: { objects: [], deletes: [] }, assets: { upserts: [], deletes: [] }, checkIntervalMinutes: 15 };
  let clock = 1000;
  const storage = memoryStorage();
  const enumerations = [];
  const store = createAppContentStore({
    client: 'web',
    fetchManifest: async (since) => { sent.push(since); if (reply instanceof Error) throw reply; return reply; },
    enumerate: async (kind, query) => { enumerations.push([kind, query.filters]); return { objects: kind === 'strings' ? [{ key: 'web.r', value: 'R' }] : [], endOfResults: true }; },
    storage,
    now: () => clock
  });
  const seen = [];
  store.subscribe((s) => seen.push(s.contentVersion));
  await store.refresh();
  assert.deepEqual(sent, [null]);
  assert.equal(store.getFlag('feed.v', 'v1'), 'v2');
  assert.equal(store.getFlag('missing', 'fallback'), 'fallback');
  assert.equal(remoteStringValue('web.a'), 'A');
  assert.ok(JSON.parse(storage.data[APP_CONTENT_STORAGE_KEY]).contentVersion === 2);
  await store.refresh();
  assert.deepEqual(sent, [null], 'throttled inside checkIntervalMinutes');
  reply = { contentVersion: 3, fullRefresh: true, flags: {}, killSwitches: {}, strings: null, banners: null, assets: null };
  await store.refresh({ force: true });
  assert.deepEqual(sent, [null, 2]);
  assert.deepEqual(enumerations[0], ['strings', { client: 'web' }]);
  assert.deepEqual(store.getState().strings, { 'web.r': 'R' });
  reply = new Error('offline');
  clock += 60 * 60000;
  await store.refresh();
  assert.equal(store.getState().contentVersion, 3, 'a failed fetch keeps the last good state');
  store.noteFeatureDisabled('messaging', 'Paused');
  assert.equal(store.isKilled('messaging'), true);
  assert.equal(store.killMessage('messaging'), 'Paused');
  assert.ok(seen.length >= 3);
  setRemoteStrings({});
});

test('the store starts from the cached state, else the bundled fallback', async () => {
  const cached = createAppContentStore({ fetchManifest: async () => null, storage: memoryStorage({ [APP_CONTENT_STORAGE_KEY]: JSON.stringify({ contentVersion: 7, flags: { x: 1 } }) }) });
  await cached.load();
  assert.equal(cached.getState().contentVersion, 7);
  assert.equal(cached.getFlag('x'), 1);
  const bundled = createAppContentStore({ fetchManifest: async () => null, initial: { contentVersion: 1, flags: { y: true }, killSwitches: {}, strings: { upserts: {}, deletes: [] } } });
  await bundled.load();
  assert.equal(bundled.getFlag('y'), true);
  assert.throws(() => createAppContentStore({}), TypeError);
  setRemoteStrings({});
});

test('banners: placement, schedule, priority, and dismissal by content hash', () => {
  const now = Date.parse('2026-12-05T12:00:00Z');
  const list = [
    banner('ban_a', { priority: 1 }),
    banner('ban_b', { priority: 5 }),
    banner('ban_c', { placement: 'Feed' }),
    banner('ban_d', { startsUtc: '2026-12-06T00:00:00Z' }),
    banner('ban_e', { endsUtc: '2026-12-05T00:00:00Z' }),
    banner('ban_f', { style: 'Critical' })
  ];
  assert.deepEqual(activeBanners(list, { placement: 'Global', now }).map((b) => b.id), ['ban_b', 'ban_a', 'ban_f']);
  assert.deepEqual(activeBanners(list, { placement: 'Feed', now }).map((b) => b.id), ['ban_c']);
  let dismissed = dismissBanner({}, list[0]);
  dismissed = dismissBanner(dismissed, list[5]);
  assert.deepEqual(dismissed, { ban_a: 'h-ban_a' });
  assert.deepEqual(activeBanners(list, { placement: 'Global', now, dismissed }).map((b) => b.id), ['ban_b', 'ban_f']);
  const edited = list.map((b) => (b.id === 'ban_a' ? { ...b, contentHash: 'changed' } : b));
  assert.ok(activeBanners(edited, { placement: 'Global', now, dismissed }).some((b) => b.id === 'ban_a'), 'shows again after a title or body change');
  assert.equal(isBannerDismissible(list[5]), false);
});

test('asset hash bookkeeping and lookups', () => {
  const assets = { ras_1: { id: 'ras_1', key: 'hero', sha256: 'aa', url: 'u/aa', darkVariant: { sha256: 'bb', url: 'u/bb' } }, ras_2: { id: 'ras_2', key: 'logo', sha256: 'aa', url: 'u/aa' } };
  assert.deepEqual(assetsToFetch(assets, ['bb']).map((f) => f.sha256), ['aa']);
  assert.deepEqual(assetsToEvict(assets, ['aa', 'zz']), ['zz']);
  assert.equal(findRemoteAsset(assets, 'logo').id, 'ras_2');
  assert.equal(findRemoteAsset(assets, 'nope'), null);
});

test('headers, feature disabled errors, tokens, and banner validation', () => {
  assert.deepEqual(appContentHeaders({ client: 'mobile', platform: 'ios', appVersion: '1.2.0', installId: 'i', channel: 'preview' }),
    { 'X-Client': 'mobile', 'X-Client-Platform': 'ios', 'X-Client-Version': '1.2.0', 'X-TK-Install-Id': 'i', 'X-Client-Channel': 'preview' });
  assert.deepEqual(featureDisabledOf({ status: 503, code: 'FeatureDisabled', body: { error: 'FeatureDisabled', message: 'Paused', feature: 'feed' } }), { feature: 'feed', message: 'Paused' });
  assert.equal(featureDisabledOf({ status: 500 }), null);
  assert.deepEqual(stringTokens('Hi {name}, {name} and {owner}'), ['name', 'owner']);
  assert.deepEqual(invalidStringTokens('Hi {name} {price}', ['name']), ['price']);
  assert.equal(bannerProblem({ title: '' }), 'title');
  assert.equal(bannerProblem({ title: 'x', link: { path: 'support', label: 'Go' } }), 'link.path');
  assert.equal(bannerProblem({ title: 'x', link: { url: 'http://a', label: 'Go' } }), 'link.url');
  assert.equal(bannerProblem({ title: 'x', link: { path: '/a', url: 'https://a', label: 'Go' } }), 'link');
  assert.equal(bannerProblem({ title: 'x', link: { path: '/a' } }), 'link.label');
  assert.equal(bannerProblem({ title: 'x', link: { path: '/a', label: 'Go' } }), null);
  assert.deepEqual(FLAG_ATTRIBUTES.profileField, ['Equals', 'In']);
});

test('ReleaseManager helpers', () => {
  const me = (role) => ({ roles: [{ role, resourceType: 'Global', resourceId: '*' }] });
  assert.equal(Role.ReleaseManager, 'ReleaseManager');
  assert.equal(isReleaseManager(me('ReleaseManager')), true);
  assert.equal(isReleaseManager(me('SystemAdmin')), true);
  assert.equal(isReleaseManager(me('Communicator')), false);
  assert.equal(canManageSigningKeys(me('ReleaseManager')), false);
  assert.equal(canManageSigningKeys(me('SystemAdmin')), true);
});

test('content and symbolication API methods map to their routes', async () => {
  const calls = [];
  const fetchImpl = async (url, init) => {
    calls.push({ url: String(url), method: init.method, body: init.body, headers: init.headers });
    return new Response(JSON.stringify({ ok: true }), { status: 200, headers: { 'content-type': 'application/json' } });
  };
  const client = new ApiClient({ fetchImpl, getAccessToken: () => 'tok' });
  await client.enumerateRemoteStrings({ filters: { client: 'web', overridden: true } });
  await client.putRemoteString('web.common.retry', { value: 'Try again' });
  await client.deleteRemoteString('web.common.retry');
  await client.getRemoteString('web.common.retry');
  await client.createBanner({ title: 'Hi' });
  await client.updateBanner('ban_1', { title: 'Yo' }, '"bnv_1"');
  await client.archiveBanner('ban_1');
  await client.createRemoteAsset({ key: 'hero' });
  await client.uploadRemoteAssetContent('ras_1', new Uint8Array([1, 2]), 'image/png', { dark: true });
  await client.enumerateUpdateFailures('upd_1', { filters: { type: 'LaunchFailed' } });
  await client.symbolicateUpdate('upd_1', { stackTrace: 'at x (index.bundle:1:2)' });
  await client.symbolicateUpdate('upd_1', { eventId: 'uev_1' });
  await client.enumerateAppBanners({}, { headers: { 'X-Client': 'web' } });
  const routes = calls.map((c) => c.method + ' ' + c.url.replace(/^.*\/api\/v1/, ''));
  assert.deepEqual(routes, [
    'POST /admin/app/strings/enumerate',
    'PUT /admin/app/strings/web.common.retry',
    'DELETE /admin/app/strings/web.common.retry',
    'GET /admin/app/strings/web.common.retry',
    'POST /admin/app/banners',
    'PUT /admin/app/banners/ban_1',
    'POST /admin/app/banners/ban_1/archive',
    'POST /admin/app/assets',
    'PUT /admin/app/assets/ras_1/dark-content',
    'POST /admin/updates/upd_1/failures/enumerate',
    'POST /admin/updates/upd_1/symbolicate',
    'POST /admin/updates/upd_1/symbolicate',
    'POST /app/banners/enumerate'
  ]);
  assert.equal(JSON.parse(calls[0].body).filters.client, 'web');
  assert.equal(calls[5].headers['If-Match'], '"bnv_1"');
  assert.equal(calls[8].headers['Content-Type'], 'image/png');
  assert.deepEqual(JSON.parse(calls[11].body), { eventId: 'uev_1' });
});

test('the string catalog generator reads string leaves without running the module', () => {
  const source = "import x from 'y';\nconst strings = { a: { b: 'B {name}', c: (n) => 'f' + n, d: `T`, e: ['x'], f: `t${1}` }, g: 'G' };\nexport default strings;";
  const entries = entriesOf(source, { binding: 'strings', prefix: 'web', client: 'web' });
  assert.deepEqual(entries.map((e) => e.key), ['web.a.b', 'web.a.d', 'web.g']);
  assert.deepEqual(entries[0].tokens, ['name']);
  assert.equal(entries[0].maxLength, 200);
  const frozen = entriesOf("export const stringTemplates = Object.freeze({ k: 'V' });", { binding: 'stringTemplates', prefix: 'mobile', client: 'mobile' });
  assert.deepEqual(frozen.map((e) => e.key), ['mobile.k']);
});

test('the committed catalog has the documented shape', () => {
  const catalog = JSON.parse(readFileSync(join(here, '..', 'src', 'strings', 'catalog.json'), 'utf8'));
  assert.equal(catalog.version, 1);
  assert.match(catalog.sha256, /^[0-9a-f]{64}$/);
  assert.ok(catalog.entries.length > 100);
  const keys = new Set();
  for (const e of catalog.entries) {
    assert.match(e.key, /^(web|mobile)\./);
    assert.ok(!keys.has(e.key), 'unique ' + e.key);
    keys.add(e.key);
    assert.ok(e.key.startsWith(e.client + '.'));
    assert.ok(Array.isArray(e.tokens));
    assert.ok(e.maxLength >= 200 && e.maxLength <= 2000);
  }
  assert.ok(buildCatalog().entries.length > 100);
});


test('full content refresh drains banner pages and preserves the last complete version after incomplete paging', async () => {
  let broken = false;
  const calls = [];
  const store = createAppContentStore({
    initial: { contentVersion: 1, flags: {}, killSwitches: {}, banners: { objects: [banner('old')] } },
    fetchManifest: async () => ({ contentVersion: 2, fullRefresh: true }),
    enumerate: async (kind, query) => {
      if (kind !== 'banners') return { objects: [], endOfResults: true };
      calls.push(query);
      if (broken) return { objects: [banner('partial')], endOfResults: false, continuationToken: null };
      return query.continuationToken ? { objects: [banner('second')], endOfResults: true, continuationToken: null }
        : { objects: [banner('first')], endOfResults: false, continuationToken: 'banners2' };
    }
  });
  await store.refresh({ force: true });
  assert.deepEqual(store.getState().banners.map(item => item.id), ['first', 'second']);
  assert.equal(calls[1].continuationToken, 'banners2');
  broken = true;
  await store.refresh({ force: true });
  assert.deepEqual(store.getState().banners.map(item => item.id), ['first', 'second']);
  assert.equal(store.getState().contentVersion, 2);
  setRemoteStrings({});
});
