import { describe, expect, it } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import UpdatesView, { buildUpdateGroupsQuery, updateStatusTone } from '../src/views/admin/UpdatesView.jsx';
import UpdateDetailView, { planBody, stagesFromPlan } from '../src/views/admin/UpdateDetailView.jsx';
import UpdateChannelsView from '../src/views/admin/UpdateChannelsView.jsx';
import UpdateKeysView from '../src/views/admin/UpdateKeysView.jsx';
import FlagsView, { buildFlagsQuery, flagBody, flagForm, flagKeyProblem } from '../src/views/admin/FlagsView.jsx';
import KillSwitchesView, { killRules, killState } from '../src/views/admin/KillSwitchesView.jsx';
import { groupedAdminRoutes } from '../src/views/admin/adminRoutes.js';
import strings from '../src/strings.js';
import { createFetch, json, renderAdmin } from './helpers.jsx';
import { enumPage } from './engagementHelpers.jsx';

function adminFetch(handler) {
  return createFetch((call) => {
    if (call.path === '/api/v1/settings/enumerate') return json({ objects: [], endOfResults: true });
    const result = handler(call);
    if (result) return result;
    if (call.path === '/api/v1/admin/updates/channels/enumerate') return json(enumPage(Object.entries(channelCatalog).map(([name, latestByPlatform]) => ({ name, latestByPlatform }))));
    return undefined;
  });
}

const update = (platform, extra = {}) => ({ id: 'upd_' + platform, uuid: platform + '-uuid', platform, runtimeVersion: 'rt-abcdef0123456789', status: 'Published', createdAt: '2026-11-10T00:00:00Z', launchAssetSizeBytes: 2048, assetCount: 3, totalBytes: 9000, rolloutPercentage: 10, sourceMapCount: 1, ...extra });

const group = (extra = {}) => ({
  id: 'upg_1', kind: 'Publish', channel: 'preview', message: 'Fix composer', status: 'Published', rolloutPercentage: 10, critical: false, criticalMessage: null,
  gitCommit: 'abc1234', gitDirty: false, republishOf: null, promotedFrom: null, updateGroupUuid: null, platforms: ['ios', 'android'], updates: [update('ios'), update('android')],
  createdBy: { type: 'DeployKey', id: 'udk_1', name: 'deploy key ci' }, publishedUtc: '2026-11-10T00:00:00Z', pausedUtc: null, rolledBackUtc: null, supersededUtc: null,
  rolloutPlan: [{ position: 0, percentage: 25, startsUtc: '2026-11-11T00:00:00Z', appliedUtc: null }],
  autoHalt: { enabled: true, failurePercent: 5, minInstalls: 20, halted: false, haltedUtc: null, haltReason: null, failures: 1, successes: 99, failureRatePercent: 1 },
  adoption: { activeInstalls: 100, onThisUpdate: 99 }, assets: { objects: [], totalRecords: 0, enumerate: null }, labels: ['hotfix'], tags: {}, createdUtc: '2026-11-10T00:00:00Z', updatedUtc: '2026-11-10T00:00:00Z',
  ...extra
});

const health = { enabled: true, signing: 'Active', activeKeyId: 'main', signingProblem: null, storage: 'Ok', storageMode: 'Local', rolloutPlanTemplate: '5@0,25@24,100@72' };
const channelCatalog = { production: { ios: null, android: null }, preview: { ios: update('ios'), android: null } };

describe('live update helpers', () => {
  it('builds enumeration queries, plan bodies, flag bodies, and kill switch rules', () => {
    const q = buildUpdateGroupsQuery({ search: 'fix', channel: 'preview', critical: 'true' }, { pageNumber: 2, pageSize: 25, sort: '-createdUtc', meta: { labels: ['hotfix'], tags: {} } });
    expect(q).toMatchObject({ maxResults: 25, skip: 25, ordering: 'CreatedDescending', search: 'fix', labels: ['hotfix'], filters: { channel: 'preview', critical: true } });
    expect(updateStatusTone('Paused')).toBe('warning');
    const stages = stagesFromPlan([{ percentage: 25, startsUtc: '2026-11-11T00:00:00Z' }]);
    const body = planBody(stages, { enabled: true, failurePercent: '7', minInstalls: '50' });
    expect(body.stages[0].percentage).toBe(25);
    expect(body.stages[0].startsUtc).toBe('2026-11-11T00:00:00.000Z');
    expect(body).toMatchObject({ autoHaltEnabled: true, autoHaltFailurePercent: 7, autoHaltMinInstalls: 50 });
    const form = flagForm(null);
    form.key = 'feed.v2';
    form.name = 'V2';
    form.rules = [{ value: 'true', rolloutPercentage: '50', conditions: [{ attribute: 'appVersion', operator: 'SemverRange', values: '>=1.2.0 <2.0.0' }, { attribute: 'platform', operator: 'In', values: 'ios, android' }] }];
    expect(flagBody(form)).toMatchObject({ key: 'feed.v2', kind: 'Boolean', valueType: 'Boolean', defaultValue: false, rules: [{ value: true, rolloutPercentage: 50, conditions: [{ values: ['>=1.2.0 <2.0.0'] }, { values: ['ios', 'android'] }] }] });
    expect(buildFlagsQuery({ hasDraftChanges: 'false' }, { pageNumber: 1, pageSize: 50 })).toMatchObject({ ordering: 'KeyAscending', filters: { hasDraftChanges: false } });
    expect(killRules('', '')).toBeUndefined();
    expect(killRules('<2.0.0', 'ios')).toEqual([{ conditions: [{ attribute: 'appVersion', operator: 'SemverRange', values: ['<2.0.0'] }, { attribute: 'platform', operator: 'In', values: ['ios'] }] }]);
    expect(killState({ killed: true, killedForEveryone: false }).label).toBe('Off for some callers');
  });

  it('adds a Releases group to the admin sidebar', () => {
    const groups = groupedAdminRoutes({ features: {} });
    const releases = groups.find((g) => g.label === 'Releases');
    expect(releases.routes.map((r) => r.id)).toEqual(['updates', 'update-channels', 'update-keys', 'flags', 'kill-switches', 'strings', 'banners', 'assets']);
  });
});

describe('updates list', () => {
  it('enumerates update groups with filters and raises the rollout from the row menu', async () => {
    const { fetchImpl, calls } = adminFetch((call) => {
      if (call.path === '/api/v1/admin/update-groups/enumerate') return json(enumPage([group()], { totalRecords: 1 }));
      if (call.path === '/api/v1/admin/updates/health') return json(health);
      if (call.path === '/api/v1/admin/update-groups/upg_1/rollout') return json(group({ rolloutPercentage: 25 }));
      return undefined;
    });
    renderAdmin(<UpdatesView />, { fetchImpl, path: '/admin/updates', routePath: '/admin/updates' });
    expect(await screen.findByText('Fix composer')).toBeInTheDocument();
    expect(screen.getByText('Active (main)')).toBeInTheDocument();
    const list = calls.find((c) => c.path === '/api/v1/admin/update-groups/enumerate');
    expect(list.method).toBe('POST');
    expect(list.body.ordering).toBe('CreatedDescending');
    await userEvent.click(screen.getByRole('button', { name: 'Actions for Fix composer' }));
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Raise to 25%' }));
    await waitFor(() => expect(calls.find((c) => c.path === '/api/v1/admin/update-groups/upg_1/rollout').body).toEqual({ rolloutPercentage: 25 }));
  });
});

describe('update detail', () => {
  it('changes the rollout with confirmation, saves the schedule, and marks the update critical', async () => {
    const { fetchImpl, calls } = adminFetch((call) => {
      if (call.path === '/api/v1/admin/update-groups/upg_1' && call.method === 'GET') return json(group());
      if (call.path === '/api/v1/admin/updates/health') return json(health);
      if (call.path === '/api/v1/admin/update-groups/upg_1/adoption/binary-versions/enumerate') return json(enumPage([{ key: '1.0.0', count: 10 }]));
      if (call.path === '/api/v1/admin/update-groups/upg_1/adoption/enumerate') return json({ ...enumPage([]), days: 14, byPlatform: { ios: { activeInstalls: 10, onThisUpdate: 5, percentOnThisUpdate: 50, downloaded: 6, applied: 5, failed: 1, launchFailed: 0 } }, objects: [{ date: '2026-11-10', checks: 9, downloads: 6, applied: 5, failures: 1 }], byBinaryVersion: { objects: [], totalRecords: 0 } });
      if (call.path === '/api/v1/admin/update-groups/upg_1/rollout') return json(group({ rolloutPercentage: 50 }));
      if (call.path === '/api/v1/admin/update-groups/upg_1/rollout-plan') return json(group());
      if (call.path === '/api/v1/admin/update-groups/upg_1/critical') return json(group({ critical: true, criticalMessage: 'Restart now' }));
      if (call.path.startsWith('/api/v1/metadata')) return json({ targetType: 'UpdateGroup', targetId: 'upg_1', labels: ['hotfix'], tags: {} });
      return undefined;
    });
    renderAdmin(<UpdateDetailView />, { fetchImpl, path: '/admin/updates/upg_1', routePath: '/admin/updates/:id' });
    expect(await screen.findByRole('heading', { name: 'Fix composer' })).toBeInTheDocument();
    expect(screen.getByText('1 failed, 99 running (1% failure)')).toBeInTheDocument();
    expect(await screen.findByText('2026-11-10')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: '50%' }));
    const confirm = await screen.findByRole('dialog', { name: 'Set the rollout to 50%?' });
    await userEvent.click(within(confirm).getByRole('button', { name: /Confirm/ }));
    await waitFor(() => expect(calls.find((c) => c.path === '/api/v1/admin/update-groups/upg_1/rollout').body).toEqual({ rolloutPercentage: 50 }));
    await userEvent.click(screen.getByRole('button', { name: 'Save schedule' }));
    await waitFor(() => expect(calls.find((c) => c.path === '/api/v1/admin/update-groups/upg_1/rollout-plan').body).toMatchObject({ stages: [{ percentage: 25 }], autoHaltEnabled: true, autoHaltFailurePercent: 5 }));
    await userEvent.click(screen.getByRole('checkbox', { name: 'Critical update' }));
    await userEvent.type(screen.getByLabelText('Prompt text (optional, 200 characters)'), 'Restart now');
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(calls.find((c) => c.path === '/api/v1/admin/update-groups/upg_1/critical').body).toEqual({ critical: true, criticalMessage: 'Restart now' }));
  });
});

describe('channels, keys, flags, kill switches', () => {
  it('lists channels and runtime versions and adds a custom channel', async () => {
    const { fetchImpl, calls } = adminFetch((call) => {
      if (call.path === '/api/v1/admin/updates/channels/enumerate') return json(enumPage([{ name: 'production', builtIn: true, latestByPlatform: { ios: null, android: null }, activeInstalls30d: 12, promotable: true, labels: [], tags: {}, createdUtc: '2026-11-01T00:00:00Z' }]));
      if (call.path === '/api/v1/admin/updates/runtime-versions/enumerate') return json(enumPage([{ platform: 'ios', runtimeVersion: 'rt-1234567890abcdefXYZ', firstSeenUtc: '2026-11-01T00:00:00Z', lastSeenUtc: '2026-11-10T00:00:00Z', activeInstalls30d: 12, latestUpdate: null, binaryVersions: { objects: [{ key: '1.2.0', count: 12 }], totalRecords: 1 } }]));
      if (call.path === '/api/v1/admin/updates/channels' && call.method === 'POST') return json({ name: 'staff', builtIn: false }, 201);
      return undefined;
    });
    renderAdmin(<UpdateChannelsView />, { fetchImpl, path: '/admin/update-channels', routePath: '/admin/update-channels' });
    expect(await screen.findByText('production')).toBeInTheDocument();
    expect(await screen.findByText('1.2.0 (12)')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Add channel' }));
    const dialog = await screen.findByRole('dialog', { name: 'Add a custom channel' });
    await userEvent.type(within(dialog).getByLabelText('Name'), 'staff');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Add channel' }));
    await waitFor(() => expect(calls.find((c) => c.path === '/api/v1/admin/updates/channels' && c.method === 'POST').body).toEqual({ name: 'staff' }));
  });

  it('shows the key ring and rotation steps without private material and shows a new deploy key secret once', async () => {
    const keys = [
      { id: 'usk_1', keyId: 'main', algorithm: 'rsa-v1_5-sha256', keyBits: 2048, source: 'Environment', privateKeyAvailable: true, certificateSha256: 'aa', certificateNotAfterUtc: '2036-01-01T00:00:00Z', certificateSubject: 'CN=x', issuedByKeyId: null, isCertificateAuthority: true, status: 'Active', lastUsedUtc: null, labels: [], tags: {}, createdUtc: '2026-11-01T00:00:00Z' },
      { id: 'usk_2', keyId: 'standby', algorithm: 'rsa-v1_5-sha256', keyBits: 2048, source: 'Stored', privateKeyAvailable: true, certificateSha256: 'bb', certificateNotAfterUtc: '2036-01-01T00:00:00Z', certificateSubject: 'CN=y', issuedByKeyId: 'main', isCertificateAuthority: true, status: 'Standby', lastUsedUtc: null, labels: [], tags: {}, createdUtc: '2026-11-01T00:00:00Z' }
    ];
    const { fetchImpl, calls } = adminFetch((call) => {
      if (call.path === '/api/v1/admin/updates/signing-keys/enumerate') return json(enumPage(keys));
      if (call.path === '/api/v1/admin/updates/signing-keys/installs/enumerate') return json(enumPage([{ key: 'main', count: 40 }], { totalRecords: 1 }));
      if (call.path === '/api/v1/admin/updates/deploy-keys/enumerate') return json(enumPage([]));
      if (call.path === '/api/v1/admin/updates/deploy-keys' && call.method === 'POST') return json({ id: 'udk_9', name: 'ci', channels: ['preview'], active: true, secret: 'tkdk_secret_once', labels: [], tags: {} }, 201);
      if (call.path === '/api/v1/admin/updates/signing-keys/usk_2/activate') return json(keys[1]);
      return undefined;
    });
    renderAdmin(<UpdateKeysView />, { fetchImpl, path: '/admin/update-keys', routePath: '/admin/update-keys' });
    expect(await screen.findByText('standby')).toBeInTheDocument();
    expect(screen.getByText(/A standby key is registered/)).toBeInTheDocument();
    expect(document.body.textContent).not.toContain('PRIVATE KEY');
    await userEvent.click(screen.getByRole('button', { name: 'Make active' }));
    const confirm = await screen.findByRole('dialog', { name: 'Sign with standby?' });
    await userEvent.click(within(confirm).getByRole('button', { name: 'Make active' }));
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/admin/updates/signing-keys/usk_2/activate')).toBe(true));
    await userEvent.click(screen.getByRole('button', { name: 'Create deploy key' }));
    const dialog = await screen.findByRole('dialog', { name: 'Create a deploy key' });
    await userEvent.type(within(dialog).getByLabelText('Name'), 'ci');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Create deploy key' }));
    expect(await screen.findByText('tkdk_secret_once')).toBeInTheDocument();
    expect(calls.find((c) => c.path === '/api/v1/admin/updates/deploy-keys' && c.method === 'POST').body).toMatchObject({ name: 'ci', channels: ['preview'] });
  });

  it('creates a flag draft and publishes content with the expected version', async () => {
    const { fetchImpl, calls } = adminFetch((call) => {
      if (call.path === '/api/v1/admin/app/flags/enumerate') return json(enumPage([]));
      if (call.path === '/api/v1/admin/app/drafts/enumerate') return json({ ...enumPage([]), contentVersion: 4, totalRecords: 2, enumerate: '/api/v1/admin/app/drafts/enumerate' });
      if (call.path === '/api/v1/admin/app/versions/enumerate') return json(enumPage([]));
      if (call.path === '/api/v1/admin/app/flags' && call.method === 'POST') return json({ id: 'ffl_1', key: 'feed.v2' }, 201);
      if (call.path === '/api/v1/admin/app/publish') return json({ id: 'cve_1', version: 5 }, 201);
      return undefined;
    });
    renderAdmin(<FlagsView />, { fetchImpl, path: '/admin/flags', routePath: '/admin/flags' });
    expect(await screen.findByText('2 pending changes')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'New flag' }));
    const editor = await screen.findByRole('dialog', { name: 'New feature flag' });
    expect(within(editor).getByText(strings.admin.liveUpdates.flags.keyHint)).toBeInTheDocument();
    // A key with underscores gets a plain-language problem before any request, not a raw regex.
    await userEvent.type(within(editor).getByLabelText('Key'), 'sim_test_flag');
    await userEvent.type(within(editor).getByLabelText('Name'), 'V2');
    await userEvent.click(within(editor).getByRole('button', { name: 'Save' }));
    expect(within(editor).getByRole('alert')).toHaveTextContent('no spaces, dashes, or underscores');
    expect(within(editor).queryByText(/\^\[a-z\]/)).toBeNull();
    expect(calls.some((c) => c.path === '/api/v1/admin/app/flags' && c.method === 'POST')).toBe(false);
    expect(flagKeyProblem('feed.v2')).toBeNull();
    expect(flagKeyProblem('Feed')).toBe(strings.admin.liveUpdates.flags.keyInvalid);
    await userEvent.clear(within(editor).getByLabelText('Key'));
    await userEvent.type(within(editor).getByLabelText('Key'), 'feed.v2');
    await userEvent.click(within(editor).getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(calls.find((c) => c.path === '/api/v1/admin/app/flags' && c.method === 'POST').body).toMatchObject({ key: 'feed.v2', name: 'V2', kind: 'Boolean', defaultValue: false, clients: ['mobile', 'web'] }));
    await userEvent.click(screen.getByRole('button', { name: 'Publish content' }));
    const publish = await screen.findByRole('dialog', { name: 'Publish content' });
    await userEvent.type(within(publish).getByLabelText('Message'), 'Ship v2');
    await userEvent.click(within(publish).getByRole('button', { name: 'Publish content' }));
    await waitFor(() => expect(calls.find((c) => c.path === '/api/v1/admin/app/publish').body).toEqual({ message: 'Ship v2', expectedVersion: 4 }));
  });

  it('turns a feature off with a reason and a version range', async () => {
    const row = { id: 'ffl_k', featureKey: 'feed', killed: false, killedForEveryone: false, message: null, blocksServer: true, ruleCount: 0, rules: [], revision: 0, lastChangedBy: null, lastChangedReason: null, lastChangedUtc: null, labels: [], tags: {}, createdUtc: '2026-11-01T00:00:00Z' };
    const { fetchImpl, calls } = adminFetch((call) => {
      if (call.path === '/api/v1/admin/app/kill-switches/enumerate') return json(enumPage([row]));
      if (call.path === '/api/v1/admin/app/kill-switches/feed') return json({ ...row, killed: true });
      return undefined;
    });
    renderAdmin(<KillSwitchesView />, { fetchImpl, path: '/admin/kill-switches', routePath: '/admin/kill-switches' });
    await userEvent.click(await screen.findByRole('button', { name: 'Turn off feed' }));
    const dialog = await screen.findByRole('dialog', { name: 'Turn off feed?' });
    await userEvent.type(within(dialog).getByLabelText('Reason'), 'Incident');
    await userEvent.type(within(dialog).getByLabelText('Only app versions (semver range, optional)'), '<2.0.0');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Turn off' }));
    await waitFor(() => expect(calls.find((c) => c.path === '/api/v1/admin/app/kill-switches/feed').body).toEqual({ killed: true, reason: 'Incident', rules: [{ conditions: [{ attribute: 'appVersion', operator: 'SemverRange', values: ['<2.0.0'] }] }], blocksServer: true }));
  });
});
