import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  ApiClient, ROLLOUT_STEPS, nextRolloutStep, parseRolloutTemplate, rolloutPlanProblem, canPromote, hasRollout, rotationSteps,
  UpdateGroupStatus, UpdateGroupKind, FLAG_ATTRIBUTES
} from '../src/index.js';

function jsonResponse(body, status = 200) {
  return new Response(body === null ? null : JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });
}

function recordingClient() {
  const calls = [];
  const fetchImpl = async (url, init) => {
    calls.push({ url, method: init.method, body: init.body ? JSON.parse(init.body) : undefined, headers: init.headers });
    return jsonResponse({ ok: true });
  };
  return { client: new ApiClient({ fetchImpl, getAccessToken: () => 'tok' }), calls };
}

test('live update methods map to the admin routes with the exact bodies', async () => {
  const { client, calls } = recordingClient();
  await client.enumerateUpdateGroups({ filters: { channel: 'preview', platform: 'ios' }, labels: ['hotfix'], maxResults: 900 });
  await client.getUpdateGroup('upg_1');
  await client.getUpdateGroupAdoption('upg_1', { filters: { days: 7 } });
  await client.enumerateUpdateAssets('upd_1', { maxResults: 20 });
  await client.publishUpdateGroup('upg_1', { rolloutPercentage: 10 });
  await client.setUpdateRollout('upg_1', 25, 'ios');
  await client.pauseUpdateGroup('upg_1', 'crash');
  await client.resumeUpdateGroup('upg_1');
  await client.setUpdateCritical('upg_1', true, 'Restart now');
  await client.rollbackUpdateGroup('upg_2', { targetUpdateGroupId: 'upg_1', rolloutPercentage: 100, message: 'Back' });
  await client.rollbackToEmbedded({ platform: 'all', runtimeVersion: 'rt', channel: 'production' });
  await client.promoteUpdateGroup('upg_1', { channel: 'production', rolloutPercentage: 5 });
  await client.setUpdateRolloutPlan('upg_1', { useTemplate: true });
  await client.deleteUpdateGroup('upg_3');
  await client.enumerateUpdateChannels();
  await client.createUpdateChannel('staff');
  await client.deleteUpdateChannel('staff');
  await client.enumerateRuntimeVersions({ filters: { platform: 'android' } });
  await client.enumerateDeployKeys();
  await client.createDeployKey({ name: 'ci', channels: ['preview'] });
  await client.revokeDeployKey('udk_1');
  await client.enumerateSigningKeys();
  await client.registerSigningKey({ keyId: 'next', certificatePem: 'PEM' });
  await client.activateSigningKey('usk_1');
  await client.retireSigningKey('usk_1');
  await client.getInstallsByKeyId();
  await client.getUpdatesHealth();
  const lines = calls.map((c) => c.method + ' ' + c.url.replace('/api/v1', ''));
  assert.deepEqual(lines, [
    'POST /admin/update-groups/enumerate', 'GET /admin/update-groups/upg_1', 'POST /admin/update-groups/upg_1/adoption/enumerate',
    'POST /admin/updates/upd_1/assets/enumerate', 'POST /admin/update-groups/upg_1/publish', 'POST /admin/update-groups/upg_1/rollout',
    'POST /admin/update-groups/upg_1/pause', 'POST /admin/update-groups/upg_1/resume', 'POST /admin/update-groups/upg_1/critical',
    'POST /admin/update-groups/upg_2/rollback', 'POST /admin/update-groups/rollback-to-embedded', 'POST /admin/update-groups/upg_1/promote',
    'PUT /admin/update-groups/upg_1/rollout-plan', 'DELETE /admin/update-groups/upg_3',
    'POST /admin/updates/channels/enumerate', 'POST /admin/updates/channels', 'DELETE /admin/updates/channels/staff',
    'POST /admin/updates/runtime-versions/enumerate', 'POST /admin/updates/deploy-keys/enumerate', 'POST /admin/updates/deploy-keys',
    'POST /admin/updates/deploy-keys/udk_1/revoke', 'POST /admin/updates/signing-keys/enumerate', 'POST /admin/updates/signing-keys',
    'POST /admin/updates/signing-keys/usk_1/activate', 'POST /admin/updates/signing-keys/usk_1/retire', 'POST /admin/updates/signing-keys/installs/enumerate',
    'GET /admin/updates/health'
  ]);
  assert.equal(calls[0].body.maxResults, 500, 'Admin page size is clamped to 500');
  assert.deepEqual(calls[0].body.filters, { channel: 'preview', platform: 'ios' });
  assert.deepEqual(calls[0].body.labels, ['hotfix']);
  assert.deepEqual(calls[5].body, { rolloutPercentage: 25, platform: 'ios' });
  assert.deepEqual(calls[6].body, { reason: 'crash' });
  assert.deepEqual(calls[8].body, { critical: true, criticalMessage: 'Restart now' });
  assert.deepEqual(calls[9].body, { targetUpdateGroupId: 'upg_1', rolloutPercentage: 100, message: 'Back' });
  assert.deepEqual(calls[11].body, { channel: 'production', rolloutPercentage: 5, supersede: false });
  assert.equal(calls[0].headers.Authorization, 'Bearer tok');
});

test('flag, content, and kill switch methods map to the app configuration routes', async () => {
  const { client, calls } = recordingClient();
  await client.enumerateFlags({ filters: { state: 'Published' } });
  await client.createFlag({ key: 'feed.v2', name: 'V2', kind: 'Boolean', defaultValue: false, offValue: false });
  await client.getFlag('ffl_1');
  await client.updateFlag('ffl_1', { enabled: false }, '"ffv_1"');
  await client.archiveFlag('ffl_1');
  await client.restoreFlag('ffl_1');
  await client.deleteFlag('ffl_1');
  await client.evaluateFlag('ffl_1', { platform: 'ios' }, false);
  await client.enumerateFlagHistory('ffl_1');
  await client.getContentDrafts();
  await client.discardContentDrafts(['ffl_1']);
  await client.publishContent('Ship', 4);
  await client.enumerateContentVersions();
  await client.getContentVersion(5);
  await client.restoreContentVersion(4, 'Back');
  await client.enumerateKillSwitches({ filters: { killed: true } });
  await client.setKillSwitch('feed', { killed: true, message: 'Paused', reason: 'Incident' });
  await client.getAppManifest({ since: 3, headers: { 'X-Client': 'web' } });
  const lines = calls.map((c) => c.method + ' ' + c.url.replace('/api/v1', ''));
  assert.deepEqual(lines, [
    'POST /admin/app/flags/enumerate', 'POST /admin/app/flags', 'GET /admin/app/flags/ffl_1', 'PUT /admin/app/flags/ffl_1',
    'POST /admin/app/flags/ffl_1/archive', 'POST /admin/app/flags/ffl_1/restore', 'DELETE /admin/app/flags/ffl_1', 'POST /admin/app/flags/ffl_1/evaluate',
    'POST /admin/app/flags/ffl_1/history/enumerate', 'POST /admin/app/drafts/enumerate', 'POST /admin/app/drafts/discard', 'POST /admin/app/publish',
    'POST /admin/app/versions/enumerate', 'GET /admin/app/versions/5', 'POST /admin/app/versions/4/restore',
    'POST /admin/app/kill-switches/enumerate', 'PUT /admin/app/kill-switches/feed', 'GET /app/manifest?since=3'
  ]);
  assert.equal(calls[3].headers['If-Match'], '"ffv_1"');
  assert.deepEqual(calls[7].body, { as: { platform: 'ios' }, useDraft: false });
  assert.deepEqual(calls[10].body, { objectIds: ['ffl_1'] });
  assert.deepEqual(calls[11].body, { message: 'Ship', expectedVersion: 4 });
  assert.deepEqual(calls[16].body, { killed: true, message: 'Paused', reason: 'Incident' });
  assert.equal(calls[17].headers['X-Client'], 'web');
});

test('rollout helpers: steps, templates, plan validation', () => {
  assert.deepEqual([...ROLLOUT_STEPS], [1, 5, 10, 25, 50, 100]);
  assert.equal(nextRolloutStep(0), 1);
  assert.equal(nextRolloutStep(10), 25);
  assert.equal(nextRolloutStep(100), null);
  assert.deepEqual(parseRolloutTemplate('5@0, 25@24,100@72'), [{ percentage: 5, hoursAfterPublish: 0 }, { percentage: 25, hoursAfterPublish: 24 }, { percentage: 100, hoursAfterPublish: 72 }]);
  assert.throws(() => parseRolloutTemplate('5@x'));
  assert.equal(rolloutPlanProblem(parseRolloutTemplate('5@0,25@24')), null);
  assert.equal(rolloutPlanProblem([{ percentage: 50, hoursAfterPublish: 0 }, { percentage: 25, hoursAfterPublish: 1 }]), 'Percentages must increase.');
  assert.equal(rolloutPlanProblem([{ percentage: 5, startsUtc: '2026-12-02T00:00:00Z' }, { percentage: 25, startsUtc: '2026-12-01T00:00:00Z' }]), 'Stages must be in time order.');
  assert.equal(rolloutPlanProblem(Array.from({ length: 11 }, (_, i) => ({ percentage: i + 1, hoursAfterPublish: i }))), 'At most 10 stages.');
});

test('promotion rules and rollout state', () => {
  assert.equal(canPromote({ channel: 'preview', kind: UpdateGroupKind.Publish, status: UpdateGroupStatus.Published }), true);
  assert.equal(canPromote({ channel: 'development', kind: UpdateGroupKind.Publish, status: UpdateGroupStatus.Published }), false, 'never from development');
  assert.equal(canPromote({ channel: 'preview', kind: UpdateGroupKind.RollBackToEmbedded, status: UpdateGroupStatus.Published }), false);
  assert.equal(canPromote({ channel: 'preview', kind: UpdateGroupKind.Publish, status: UpdateGroupStatus.Draft }), false);
  assert.equal(hasRollout({ status: 'Paused' }), true);
  assert.equal(hasRollout({ status: 'RolledBack' }), false);
  assert.ok(FLAG_ATTRIBUTES.appVersion.includes('SemverRange'));
});

test('rotation checklist follows the key ring', () => {
  const ready = [
    { keyId: 'main', status: 'Active', isCertificateAuthority: true, issuedByKeyId: null, privateKeyAvailable: true },
    { keyId: 'standby', status: 'Standby', isCertificateAuthority: true, issuedByKeyId: 'main', privateKeyAvailable: true }
  ];
  assert.deepEqual(rotationSteps(ready).map((s) => s.done), [true, true, false, false, false]);
  const switched = [
    { keyId: 'main', status: 'Retired', isCertificateAuthority: true, issuedByKeyId: null, privateKeyAvailable: true },
    { keyId: 'standby', status: 'Active', isCertificateAuthority: true, issuedByKeyId: 'main', privateKeyAvailable: true }
  ];
  assert.deepEqual(rotationSteps(switched, { objects: [{ key: 'standby', count: 10 }] }).map((s) => s.done), [true, true, true, false, true]);
  assert.deepEqual(rotationSteps(switched, { objects: [{ key: 'main', count: 3 }] }).map((s) => s.done)[4], false, 'not drained');
  assert.deepEqual(rotationSteps([]).map((s) => s.done), [false, false, false, false, false]);
});
