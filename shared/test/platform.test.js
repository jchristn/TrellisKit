import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  ApiClient, ApiError, PropertyStatus, propertyDeletionState, delegationTimeLeft, stripeWebhookPath, parsePropertyDocument,
  domainsAndAppsBody, domainsAndAppsProblems
} from '../src/index.js';

function jsonResponse(body, status = 200) {
  return new Response(body === null ? null : JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });
}

function recordingClient(respond = () => jsonResponse({ ok: true })) {
  const calls = [];
  const fetchImpl = async (url, init) => {
    calls.push({ url, method: init.method, body: init.body ? JSON.parse(init.body) : undefined, headers: init.headers });
    return respond(url, init);
  };
  return { client: new ApiClient({ fetchImpl, getAccessToken: () => 'tkp_abc' }), calls };
}

test('platform methods map to the platform routes with the exact bodies', async () => {
  const { client, calls } = recordingClient();
  await client.platformLogin('ops@example.test', 'pw');
  await client.platformAcceptInvite('tok', 'pw2');
  await client.platformLogout();
  await client.getPlatformMe();
  await client.listPlatformAdmins({ search: 'ops', maxResults: 10 });
  await client.invitePlatformAdmin({ email: 'a@example.test', displayName: 'A' });
  await client.removePlatformAdmin('pad_1');
  await client.listProperties({ search: 'x', filters: { status: 'Enabled' }, labels: ['beta'], tags: { tier: 'gold' }, maxResults: 900 });
  await client.createProperty({ cloneFromPropertyId: 'app_a', name: 'B' });
  await client.getProperty('app_a');
  await client.updateProperty('app_a', { domains: [{ host: 'b.test', kind: 'Web' }] });
  await client.enableProperty('app_a');
  await client.disableProperty('app_a', 'Billing');
  await client.disableProperty('app_a');
  await client.deleteProperty('app_a', 'app_a');
  await client.restoreProperty('app_a');
  await client.listPropertyAdmins('app_a', {});
  await client.grantPropertyAdmin('app_a', 'usr_1');
  await client.revokePropertyAdmin('app_a', 'usr_1');
  await client.createDelegatedSession('app_a');
  await client.listPlatformAudit({ filters: { propertyId: 'app_a' } });
  await client.getPlatformSettings();
  await client.updatePlatformSettings({ 'platform.inviteDays': 3 });
  await client.restartPlatformServer();
  const lines = calls.map((c) => c.method + ' ' + c.url.replace('/api/v1', ''));
  assert.deepEqual(lines, [
    'POST /platform/auth/login', 'POST /platform/auth/accept-invite', 'POST /platform/auth/logout', 'GET /platform/auth/me',
    'POST /platform/admins/enumerate', 'POST /platform/admins', 'DELETE /platform/admins/pad_1',
    'POST /platform/properties/enumerate', 'POST /platform/properties', 'GET /platform/properties/app_a', 'PUT /platform/properties/app_a',
    'POST /platform/properties/app_a/enable', 'POST /platform/properties/app_a/disable', 'POST /platform/properties/app_a/disable',
    'DELETE /platform/properties/app_a', 'POST /platform/properties/app_a/restore',
    'POST /platform/properties/app_a/admins/enumerate', 'POST /platform/properties/app_a/admins', 'DELETE /platform/properties/app_a/admins/usr_1',
    'POST /platform/properties/app_a/admin-session', 'POST /platform/audit/enumerate',
    'POST /platform/settings/enumerate', 'PUT /platform/settings', 'POST /platform/settings/restart'
  ]);
  assert.deepEqual(calls[0].body, { email: 'ops@example.test', password: 'pw' });
  assert.deepEqual(calls[1].body, { token: 'tok', password: 'pw2' });
  assert.equal(calls[3].headers.Authorization, 'Bearer tkp_abc');
  const enumerate = calls[7].body;
  assert.equal(enumerate.search, 'x');
  assert.deepEqual(enumerate.filters, { status: 'Enabled' });
  assert.deepEqual(enumerate.labels, ['beta']);
  assert.deepEqual(enumerate.tags, { tier: 'gold' });
  assert.equal(enumerate.maxResults, 500, 'admin collections clamp at 500');
  assert.deepEqual(calls[12].body, { reason: 'Billing' });
  assert.deepEqual(calls[13].body, {});
  assert.deepEqual(calls[14].body, { confirm: 'app_a' });
  assert.deepEqual(calls[22].body, { values: { 'platform.inviteDays': 3 } });
});

test('platformAvailability asks the backend: 404 means not a platform host, 401 means sign in', async () => {
  const answer = (status, body) => new ApiClient({ fetchImpl: async () => jsonResponse(body, status) });
  assert.deepEqual(await answer(404, { error: 'NotAvailable', message: 'no' }).platformAvailability(), { available: false, signedIn: false, admin: null, code: 'NotAvailable' });
  assert.deepEqual(await answer(404, { error: 'PropertyNotFound', message: 'no' }).platformAvailability(), { available: false, signedIn: false, admin: null, code: 'PropertyNotFound' });
  assert.deepEqual(await answer(401, { error: 'Unauthorized', message: 'no' }).platformAvailability(), { available: true, signedIn: false, admin: null, code: 'Unauthorized' });
  const me = { id: 'pad_1', email: 'ops@example.test' };
  assert.deepEqual(await answer(200, me).platformAvailability(), { available: true, signedIn: true, admin: me, code: null });
  await assert.rejects(answer(500, { error: 'InternalError', message: 'x' }).platformAvailability(), (err) => err instanceof ApiError && err.status === 500);
  // The probe never runs the refresh hook.
  let hook = 0;
  const client = new ApiClient({ fetchImpl: async () => jsonResponse({ error: 'Unauthorized' }, 401), onUnauthorized: () => { hook += 1; return true; } });
  await client.platformAvailability();
  assert.equal(hook, 0);
});

test('propertyDeletionState reports the 30-day restore window and the purge', () => {
  const now = Date.parse('2026-11-11T00:00:00Z');
  const deleting = propertyDeletionState({ status: PropertyStatus.Deleting, deleteScheduledUtc: '2026-12-11T00:00:00Z', purgeStartedUtc: null }, now);
  assert.equal(deleting.restorable, true);
  assert.equal(deleting.daysLeft, 30);
  assert.equal(deleting.purgeAtUtc, '2026-12-11T00:00:00Z');
  const purging = propertyDeletionState({ status: 'Deleting', deleteScheduledUtc: '2026-11-10T00:00:00Z', purgeStartedUtc: '2026-11-10T01:00:00Z' }, now);
  assert.equal(purging.restorable, false);
  assert.equal(purging.purging, true);
  assert.equal(purging.daysLeft, 0);
  assert.equal(propertyDeletionState({ status: 'Enabled' }, now).deleting, false);
  assert.equal(propertyDeletionState({ status: 'Deleted' }, now).deleted, true);
});

test('delegationTimeLeft counts down to the token expiry', () => {
  const now = Date.parse('2026-11-11T10:00:00Z');
  assert.deepEqual(delegationTimeLeft({ accessTokenExpiresUtc: '2026-11-11T10:59:30Z' }, now), { active: true, msLeft: 3570000, minutes: 59, seconds: 30 });
  assert.equal(delegationTimeLeft({ accessTokenExpiresUtc: '2026-11-11T09:00:00Z' }, now).active, false);
  assert.equal(delegationTimeLeft(null, now).active, false);
});

test('stripeWebhookPath is the per-property URL', () => {
  assert.equal(stripeWebhookPath('app_mwczw7mg_F8RsDcNidltPWZS1seB'), '/api/v1/webhooks/stripe/app_mwczw7mg_F8RsDcNidltPWZS1seB');
});

test('parsePropertyDocument accepts only a JSON object', () => {
  assert.deepEqual(parsePropertyDocument(''), { document: null, error: 'Empty' });
  assert.deepEqual(parsePropertyDocument('{nope'), { document: null, error: 'InvalidJson' });
  assert.deepEqual(parsePropertyDocument('[1]'), { document: null, error: 'NotObject' });
  assert.deepEqual(parsePropertyDocument(' {"name":"Example Community"} '), { document: { name: 'Example Community' }, error: null });
});

test('domainsAndAppsBody normalizes rows and domainsAndAppsProblems mirrors the server rules', () => {
  const body = domainsAndAppsBody(
    [{ host: ' WWW.Example.Test. ', kind: 'Web' }, { host: '', kind: 'Api' }, { id: 'pdm_1', host: 'api.example.test', kind: 'Api', verifiedUtc: null }],
    [{ id: 'ppa_1', platform: 'Ios', appId: ' test.example.app ', storeAppId: '', enabled: true }, { platform: 'Android', appId: '' }, { platform: 'Android', appId: 'test.example.app', enabled: false }]
  );
  assert.deepEqual(body, {
    domains: [{ host: 'www.example.test', kind: 'Web' }, { host: 'api.example.test', kind: 'Api' }],
    apps: [{ platform: 'Ios', appId: 'test.example.app', storeAppId: null, enabled: true }, { platform: 'Android', appId: 'test.example.app', storeAppId: null, enabled: false }]
  });
  assert.deepEqual(domainsAndAppsProblems(body.domains, body.apps), {});
  const problems = domainsAndAppsProblems(
    [{ host: 'a.test', kind: 'Web' }, { host: 'a.test', kind: 'Alias' }, { host: 'bad host', kind: 'Web' }],
    [{ platform: 'Ios', appId: 'x.y' }, { platform: 'Ios', appId: 'X.Y' }, { platform: 'Android', appId: 'nodots' }]
  );
  assert.deepEqual(problems, { 'domains[1].host': 'Duplicate', 'domains[2].host': 'InvalidHost', 'apps[1].appId': 'Duplicate', 'apps[2].appId': 'InvalidAppId' });
  const many = Array.from({ length: 21 }, (_, i) => ({ host: 'h' + i + '.test', kind: 'Web' }));
  assert.equal(domainsAndAppsProblems(many, []).domains, 'TooMany');
});
