import { test } from 'node:test';
import assert from 'node:assert/strict';
import { ApiClient } from '../src/index.js';

function recorder() {
  const calls = [];
  const fetchImpl = async (url, init) => {
    calls.push({ method: init.method, url: url.replace('/api/v1', ''), body: init.body ? JSON.parse(init.body) : undefined });
    return new Response(JSON.stringify({ ok: true }), { status: 200, headers: { 'content-type': 'application/json' } });
  };
  return { calls, client: new ApiClient({ fetchImpl }) };
}

test('palette versions: list every kept version and restore any one (admin and platform)', async () => {
  const { calls, client } = recorder();
  await client.enumeratePropertyBrandingVersions({ maxResults: 10, continuationToken: 'v2' });
  await client.restorePropertyBrandingVersion('7', 'Back to autumn');
  await client.restorePropertyBrandingVersion(3);
  await client.enumeratePlatformPropertyBrandingVersions('app_1', { maxResults: 10 });
  await client.restorePlatformPropertyBrandingVersion('app_1', 4);
  assert.deepEqual(calls.map((c) => c.method + ' ' + c.url), [
    'POST /admin/property/branding/versions/enumerate', 'POST /admin/property/branding/revert', 'POST /admin/property/branding/revert',
    'POST /platform/properties/app_1/branding/versions/enumerate', 'POST /platform/properties/app_1/branding/revert'
  ]);
  assert.deepEqual(calls[0].body, { maxResults: 10, continuationToken: 'v2' });
  assert.deepEqual(calls[1].body, { to: 'Version', version: 7, note: 'Back to autumn' });
  assert.deepEqual(calls[2].body, { to: 'Version', version: 3 });
  assert.deepEqual(calls[4].body, { to: 'Version', version: 4 });
});
