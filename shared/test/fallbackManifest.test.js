import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createAppContentStore, EMPTY_FALLBACK_MANIFEST, fallbackManifestOf, setRemoteStrings } from '../src/index.js';
import { buildFallbackManifest } from '../scripts/app-manifest.mjs';

const here = dirname(fileURLToPath(import.meta.url));

function memoryStorage(seed = {}) {
  const data = { ...seed };
  return { data, getItem: async (k) => (k in data ? data[k] : null), setItem: async (k, v) => { data[k] = v; } };
}

const banner = (id) => ({ id, title: 'T ' + id, body: '', style: 'Info', placement: 'Global', dismissible: true, priority: 0, startsUtc: null, endsUtc: null, contentHash: 'h-' + id });

test('the bundled fallback manifest is a full snapshot of the anonymous manifest and starts a store offline', async () => {
  const manifest = {
    contentVersion: 12,
    checkIntervalMinutes: 30,
    fullRefresh: false,
    flags: { 'feed.composer': 'v2' },
    killSwitches: { messaging: { killed: true, message: 'Back soon' } },
    strings: { upserts: { 'web.common.retry': 'Try again', bad: 3 }, deletes: ['web.old'] },
    banners: { objects: [banner('ban_1'), null] },
    assets: { upserts: [{ id: 'ras_1', key: 'hero', sha256: 'a'.repeat(64) }, { key: 'no id' }], deletes: ['ras_2'] },
    binary: { updateRequired: true }
  };
  const fallback = fallbackManifestOf(manifest);
  assert.equal(fallback.fallback, true);
  assert.equal(fallback.contentVersion, 12);
  assert.equal(fallback.checkIntervalMinutes, 30);
  assert.deepEqual(fallback.strings, { upserts: { 'web.common.retry': 'Try again' }, deletes: [] });
  assert.equal(fallback.banners.objects.length, 1);
  assert.deepEqual(fallback.assets.upserts.map((a) => a.id), ['ras_1']);
  assert.equal(fallback.binary, undefined, 'the binary policy is resolved per install, never bundled');
  assert.deepEqual(fallbackManifestOf({ fullRefresh: true, contentVersion: 3 }), { ...EMPTY_FALLBACK_MANIFEST });
  assert.deepEqual(fallbackManifestOf(null), { ...EMPTY_FALLBACK_MANIFEST });

  const store = createAppContentStore({ client: 'web', storage: memoryStorage(), initial: fallback, fetchManifest: async () => { throw new Error('offline'); } });
  await store.refresh();
  assert.equal(store.isKilled('messaging'), true, 'kill switches apply on the first launch offline');
  assert.equal(store.killMessage('messaging'), 'Back soon');
  assert.equal(store.getFlag('feed.composer'), 'v2');
  assert.equal(store.getState().strings['web.common.retry'], 'Try again');
  assert.equal(store.getState().contentVersion, 12);

  let since = 'unset';
  const online = createAppContentStore({
    client: 'web',
    initial: fallback,
    fetchManifest: async (s) => {
      since = s;
      return { contentVersion: 13, flags: {}, killSwitches: {}, strings: { upserts: {}, deletes: [] }, banners: { objects: [] }, assets: { upserts: [], deletes: [] } };
    }
  });
  await online.refresh();
  assert.equal(since, 12, 'the first online fetch asks for changes since the bundled version');
  assert.equal(online.isKilled('messaging'), false, 'the live manifest replaces the bundled kill switches');
  const cached = createAppContentStore({ client: 'web', storage: memoryStorage({ 'tk.appContent.v1': JSON.stringify({ contentVersion: 20, flags: { cached: true } }) }), initial: fallback, fetchManifest: async () => null });
  await cached.load();
  assert.equal(cached.getFlag('cached'), true, 'a cached manifest wins over the bundled one');
  assert.equal(cached.getFlag('feed.composer'), null);
  setRemoteStrings({});
});

test('the fallback manifest script reads a file or the API and falls back to the empty manifest', async () => {
  const warnings = [];
  const warn = (m) => warnings.push(m);
  assert.deepEqual(await buildFallbackManifest({ env: {}, warn }), { ...EMPTY_FALLBACK_MANIFEST }, 'no source: empty');
  const fromFile = await buildFallbackManifest({ env: { TK_APP_MANIFEST_FILE: join(here, 'fixtures', 'app-manifest.json') }, warn });
  assert.equal(fromFile.contentVersion, 7);
  assert.equal(fromFile.killSwitches.feed.killed, true);
  let asked = null;
  const fetchImpl = async (url, init) => {
    asked = { url, client: init.headers['X-Client'] };
    return { ok: true, status: 200, json: async () => ({ contentVersion: 9, flags: { a: true }, killSwitches: {} }) };
  };
  const fromApi = await buildFallbackManifest({ client: 'mobile', env: { TK_APP_MANIFEST_URL: 'https://api.example.test/' }, fetchImpl, warn });
  assert.deepEqual(asked, { url: 'https://api.example.test/api/v1/app/manifest', client: 'mobile' });
  assert.equal(fromApi.contentVersion, 9);
  const failing = await buildFallbackManifest({ env: { TK_APP_MANIFEST_URL: 'https://api.example.test' }, fetchImpl: async () => ({ ok: false, status: 503 }), warn });
  assert.deepEqual(failing, { ...EMPTY_FALLBACK_MANIFEST }, 'an unreachable API never fails the build');
  assert.ok(warnings.some((w) => w.includes('HTTP 503')));
  const mobileBundle = JSON.parse(readFileSync(join(here, '..', '..', 'mobile', 'assets', 'app-manifest.json'), 'utf8'));
  assert.equal(mobileBundle.fallback, true, 'the mobile app bundles a fallback manifest');
});
