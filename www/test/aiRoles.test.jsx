import { describe, expect, it } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import strings from '../src/strings.js';
import { groupedAdminRoutes } from '../src/views/admin/adminRoutes.js';
import AiRolesView, { roleBody } from '../src/views/admin/ai/AiRolesView.jsx';
import { CALL_DETAIL_GROUPS, callDetailValue } from '../src/views/admin/ai/AiCallDetail.jsx';
import { harnessBody, harnessForm, harnessProblem } from '../src/views/admin/ai/AiHarnessPanel.jsx';
import { createFetch, json, renderAdmin } from './helpers.jsx';
import { enumPage } from './engagementHelpers.jsx';

const s = strings.ai.rolesAdmin;

function role(overrides = {}) {
  return {
    role: 'Extraction', title: 'Extraction', description: 'Per-page extraction.', kind: 'Completion', allowsHarness: false, interactive: false,
    suggested: 'API open model, then claude-sonnet-5-5', reasoningEffort: 'Low', availabilityPolicy: 'FallThrough', exhaustionPolicy: 'Fail',
    executors: [{ position: 0, runtime: 'ApiEndpoint', endpointId: 'mep_1', endpointName: 'Local gpt-oss', model: 'gpt-oss', health: 'Healthy', enabled: true, problem: null }],
    ready: true, updatedByUserId: null, updatedUtc: null, ...overrides
  };
}

function harness(overrides = {}) {
  return {
    runtime: 'ClaudeCode', supportsJobTools: true, credentialTypes: ['Environment', 'ApiKey', 'OAuthToken', 'AuthToken'], baseUrlVariable: 'ANTHROPIC_BASE_URL',
    credentialVariables: [], credentialVariablesByType: { ApiKey: ['ANTHROPIC_API_KEY'], OAuthToken: ['CLAUDE_CODE_OAUTH_TOKEN'], AuthToken: ['ANTHROPIC_AUTH_TOKEN'] },
    environmentVariables: ['ANTHROPIC_API_KEY', 'ANTHROPIC_AUTH_TOKEN', 'CLAUDE_CODE_OAUTH_TOKEN'], model: null, permissionMode: null,
    timeoutSeconds: null, maxToolCalls: null, allowWebResearch: true, baseUrl: null, credentialType: 'Environment', hasCredential: false,
    defaultTimeoutSeconds: 600, defaultMaxToolCalls: 50, runsOnThisInstance: false, saved: false, updatedByUserId: null, updatedUtc: null, ...overrides
  };
}

const ledgerCall = {
  id: 'aic_1', createdUtc: '2026-11-21T10:00:00Z', purpose: 'Task', role: 'Extraction', runtime: 'ApiEndpoint', model: 'gpt-oss', providerModel: 'gpt-oss:20b', endpointId: 'mep_1', assistantId: null,
  executorPosition: 1, escalationReason: '#0 ApiEndpoint mep_0: ModelError', outcome: 'Ok', errorCode: null, isFallback: false, isTest: false,
  promptTokens: 120, completionTokens: 30, reasoningTokens: 0, inputTokens: 120, outputTokens: 30, totalTokens: 150, cachedReadTokens: 80, cacheCreationTokens: null,
  latencyMs: 640, queueMs: 3, timeToFirstTokenMs: 210, timeToLastTokenMs: 630, streamingMs: 420, tokensPerSecond: 71.4, loadMs: null, promptEvalMs: null, evalMs: null,
  toolCalls: 2, toolExecMs: 95, retries: 0, statusCode: 200, finishReason: 'stop', streamed: true, chunks: 12, requestBytes: 2048, responseBytes: null, responseId: 'resp_9', reasoningEffort: 'low', exitCode: null,
  costUsd: 0.0012, jobKey: 'discovery.organizations', jobRunId: 'jbr_1', jobItemId: 'jbi_1'
};

describe('AI call detail', () => {
  it('names every stored telemetry field once and formats each kind of value', () => {
    const fields = CALL_DETAIL_GROUPS.flatMap(([, list]) => list);
    expect(new Set(fields).size).toBe(fields.length);
    for (const field of ['timeToFirstTokenMs', 'timeToLastTokenMs', 'streamingMs', 'latencyMs', 'queueMs', 'tokensPerSecond', 'inputTokens', 'outputTokens', 'totalTokens', 'reasoningTokens', 'cachedReadTokens',
      'cacheCreationTokens', 'inputTextTokens', 'inputImageTokens', 'inputAudioTokens', 'inputVideoTokens', 'toolCalls', 'toolExecMs', 'retries', 'statusCode', 'finishReason', 'streamed', 'chunks', 'requestBytes',
      'responseBytes', 'responseId', 'providerModel', 'reasoningEffort', 'loadMs', 'promptEvalMs', 'evalMs', 'exitCode', 'costUsd']) {
      expect(fields).toContain(field);
      expect(s.detail.fields[field]).toBeTruthy();
    }
    expect(callDetailValue('timeToFirstTokenMs', ledgerCall)).toBe(s.detail.ms(210));
    expect(callDetailValue('requestBytes', ledgerCall)).toBe(s.detail.bytes(2048));
    expect(callDetailValue('tokensPerSecond', ledgerCall)).toBe(s.detail.perSecond(71.4));
    expect(callDetailValue('streamed', ledgerCall)).toBe(s.detail.yes);
    expect(callDetailValue('isTest', ledgerCall)).toBe(s.detail.no);
    expect(callDetailValue('costUsd', ledgerCall)).toBe('$0.0012');
    expect(callDetailValue('executorPosition', ledgerCall)).toBe('#1');
    expect(callDetailValue('retries', ledgerCall)).toBe('0');
    expect(callDetailValue('responseBytes', ledgerCall)).toBe(s.detail.notReported);
    expect(callDetailValue('cacheCreationTokens', ledgerCall)).toBe(s.detail.notReported);
    expect(callDetailValue('exitCode', {})).toBe(s.detail.notReported);
  });
});

describe('Harness profiles', () => {
  it('maps the editor to the API body and keeps the credential write-only', () => {
    expect(harnessForm(null)).toMatchObject({ credentialType: 'Environment', allowWebResearch: true, credentialMode: 'replace', storedType: null });
    const environment = harnessBody({ ...harnessForm(harness()), model: ' sonnet ', timeoutSeconds: '120', maxToolCalls: '', permissionMode: 'ReadOnly' });
    expect(environment).toEqual({ model: 'sonnet', permissionMode: 'ReadOnly', timeoutSeconds: 120, maxToolCalls: null, allowWebResearch: true, baseUrl: null, credentialType: 'Environment' });
    expect(environment).not.toHaveProperty('credential');

    const stored = harnessForm(harness({ credentialType: 'OAuthToken', hasCredential: true, credentialVariables: ['CLAUDE_CODE_OAUTH_TOKEN'] }));
    expect(stored.credentialMode).toBe('keep');
    expect(harnessBody(stored)).not.toHaveProperty('credential');
    expect(harnessProblem(stored)).toBeNull();
    expect(harnessBody({ ...stored, credentialMode: 'replace', credential: 'new-value-not-real' }).credential).toBe('new-value-not-real');
    // Changing the type never reuses the value stored for the other type.
    const changed = { ...stored, credentialType: 'ApiKey', credential: '' };
    expect(harnessProblem(changed)).toBe('credential');
    expect(harnessBody({ ...changed, credential: 'key-not-real' }).credential).toBe('key-not-real');
    expect(harnessProblem({ ...stored, credentialType: 'AuthToken', credential: 'token-not-real', credentialMode: 'replace', baseUrl: ' ' })).toBe('baseUrl');
    expect(harnessBody({ ...stored, credentialType: 'Environment', credential: 'ignored' })).not.toHaveProperty('credential');
  });
});

describe('AI task roles page', () => {
  it('is in the AI navigation group', () => {
    const ids = groupedAdminRoutes({ features: {} }).flatMap((g) => g.routes.map((r) => r.id));
    expect(ids).toContain('ai-roles');
  });

  it('maps the editor to the API body', () => {
    expect(roleBody({ reasoningEffort: '', availabilityPolicy: 'FallThrough', exhaustionPolicy: 'Defer', executors: [
      { runtime: 'ApiEndpoint', endpointId: 'mep_1', model: 'ignored', enabled: true },
      { runtime: 'ClaudeCode', endpointId: 'ignored', model: 'sonnet', enabled: false }
    ] })).toEqual({ reasoningEffort: null, availabilityPolicy: 'FallThrough', exhaustionPolicy: 'Defer', executors: [
      { runtime: 'ApiEndpoint', endpointId: 'mep_1', model: null, enabled: true },
      { runtime: 'ClaudeCode', endpointId: null, model: 'sonnet', enabled: false }
    ] });
  });

  it('lists roles and the ledger, saves a hierarchy, and tests a role', async () => {
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/admin/ai/roles/enumerate') return json(enumPage([role(), role({ role: 'DiscoveryResearch', title: 'Discovery research', description: 'Agentic research.', allowsHarness: true, executors: [{ position: 0, runtime: 'ClaudeCode', model: 'sonnet', enabled: true, problem: 'Harnesses run only on worker instances' }], ready: false })]));
      if (call.path === '/api/v1/admin/ai/calls/enumerate') return json(enumPage([{ id: 'aic_1', createdUtc: '2026-11-21T10:00:00Z', purpose: 'Task', role: 'Extraction', model: 'gpt-oss', executorPosition: 1, escalationReason: '#0 ApiEndpoint mep_0: ModelError', outcome: 'Ok', promptTokens: 120, completionTokens: 30, latencyMs: 640, costUsd: 0.0012, jobKey: 'discovery.organizations' }]));
      if (call.path === '/api/v1/admin/ai/harnesses/enumerate') return json(enumPage([harness()]));
      if (call.path === '/api/v1/admin/ai/endpoints/enumerate') return json(enumPage([{ id: 'mep_1', name: 'Local gpt-oss', model: 'gpt-oss', kind: 'Completion' }, { id: 'mep_2', name: 'Claude', model: 'claude-sonnet-5-5', kind: 'Completion' }]));
      if (call.path === '/api/v1/admin/ai/roles/Extraction' && call.method === 'PUT') return json(role());
      if (call.path === '/api/v1/admin/ai/roles/Extraction/test') return json({ success: true, text: 'Iron Temple Gym', executorPosition: 0, endpointId: 'mep_1', model: 'gpt-oss', escalations: [], errorCode: null, latencyMs: 410, costUsd: 0 });
      return undefined;
    });
    const user = userEvent.setup();
    renderAdmin(<AiRolesView />, { fetchImpl });
    const row = (await screen.findByText('Per-page extraction.')).closest('tr');
    expect(within(row).getByText(/Local gpt-oss/)).toBeInTheDocument();
    expect(within(row).getByText(s.ready)).toBeInTheDocument();
    const research = screen.getByText('Discovery research').closest('tr');
    expect(within(research).getByText('Harnesses run only on worker instances')).toBeInTheDocument();
    expect(screen.getByText('discovery.organizations')).toBeInTheDocument();

    await user.click(within(row).getByRole('button', { name: s.edit }));
    await user.click(await screen.findByRole('button', { name: s.form.add }));
    await user.selectOptions(screen.getByLabelText(s.form.endpoint + ' 2'), 'mep_2');
    await user.click(screen.getByRole('button', { name: s.form.save }));
    await waitFor(() => expect(calls.some((c) => c.method === 'PUT' && c.body.executors.length === 2 && c.body.executors[1].endpointId === 'mep_2')).toBe(true));

    await user.click(within(row).getByRole('button', { name: s.test }));
    await user.type(await screen.findByLabelText(s.testForm.prompt), 'Extract the name');
    await user.click(screen.getByRole('button', { name: s.testForm.send }));
    expect(await screen.findByText('Iron Temple Gym')).toBeInTheDocument();
  });

  it('shows a call\'s full telemetry, with unreported values named as such', async () => {
    const { fetchImpl } = createFetch((call) => {
      if (call.path === '/api/v1/admin/ai/roles/enumerate') return json(enumPage([role()]));
      if (call.path === '/api/v1/admin/ai/calls/enumerate') return json(enumPage([ledgerCall]));
      if (call.path === '/api/v1/admin/ai/endpoints/enumerate') return json(enumPage([]));
      if (call.path === '/api/v1/admin/ai/harnesses/enumerate') return json(enumPage([]));
      return undefined;
    });
    const user = userEvent.setup();
    renderAdmin(<AiRolesView />, { fetchImpl });
    const ledgerRow = (await screen.findByText('discovery.organizations')).closest('tr');
    expect(within(ledgerRow).getByText('150')).toBeInTheDocument();
    expect(within(ledgerRow).getByText('210 ms')).toBeInTheDocument();
    await user.click(within(ledgerRow).getByRole('button', { name: s.detailsFor('aic_1') }));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByTestId('call-timeToFirstTokenMs')).toHaveTextContent(s.detail.ms(210));
    expect(within(dialog).getByTestId('call-streamingMs')).toHaveTextContent(s.detail.ms(420));
    expect(within(dialog).getByTestId('call-cachedReadTokens')).toHaveTextContent('80');
    expect(within(dialog).getByTestId('call-toolCalls')).toHaveTextContent('2');
    expect(within(dialog).getByTestId('call-toolExecMs')).toHaveTextContent(s.detail.ms(95));
    expect(within(dialog).getByTestId('call-finishReason')).toHaveTextContent('stop');
    expect(within(dialog).getByTestId('call-responseId')).toHaveTextContent('resp_9');
    expect(within(dialog).getByTestId('call-responseBytes')).toHaveTextContent(s.detail.notReported);
    expect(within(dialog).getByTestId('call-cacheCreationTokens')).toHaveTextContent(s.detail.notReported);
    expect(within(dialog).getByTestId('call-costUsd')).toHaveTextContent('$0.0012');
  });

  it('lists harness profiles, stores a credential write-only, and reveals it only on request', async () => {
    let saved = harness();
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/admin/ai/roles/enumerate') return json(enumPage([]));
      if (call.path === '/api/v1/admin/ai/calls/enumerate') return json(enumPage([]));
      if (call.path === '/api/v1/admin/ai/endpoints/enumerate') return json(enumPage([]));
      if (call.path === '/api/v1/admin/ai/harnesses/enumerate' && call.method === 'POST') return json(enumPage([saved, harness({ runtime: 'Mux', credentialTypes: ['Environment'], baseUrlVariable: null, credentialVariablesByType: {}, environmentVariables: ['MUX_CONFIG_DIR'] })]));
      if (call.path === '/api/v1/admin/ai/harnesses/ClaudeCode' && call.method === 'PUT') {
        saved = harness({ model: call.body.model, permissionMode: call.body.permissionMode, credentialType: call.body.credentialType, hasCredential: true, credentialVariables: ['CLAUDE_CODE_OAUTH_TOKEN'], saved: true });
        return json(saved);
      }
      if (call.path === '/api/v1/admin/ai/harnesses/ClaudeCode/reveal') return json({ field: 'credential', headerName: null, value: 'revealed-value-not-real' });
      return undefined;
    });
    const user = userEvent.setup();
    const h = s.harness;
    renderAdmin(<AiRolesView />, { fetchImpl });
    const row = (await screen.findByText('ClaudeCode')).closest('tr');
    expect(within(row).getByText(h.credentialTypes.Environment)).toBeInTheDocument();
    expect(within(row).getByText(h.notHere)).toBeInTheDocument();
    expect(within(row).getByText(h.limits(600, 50))).toBeInTheDocument();
    expect(within(row).queryByRole('button', { name: h.reveal + ' ClaudeCode' })).toBeNull();

    await user.click(within(row).getByRole('button', { name: h.edit + ' ClaudeCode' }));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText(h.form.baseUrlHint('ANTHROPIC_BASE_URL'))).toBeInTheDocument();
    await user.type(within(dialog).getByLabelText(h.form.model, { exact: false }), 'sonnet');
    await user.selectOptions(within(dialog).getByLabelText(h.form.mode, { exact: false }), 'ReadOnly');
    expect(within(dialog).queryByRole('option', { name: 'Unrestricted' })).toBeNull();
    await user.selectOptions(within(dialog).getByLabelText(h.form.credentialType, { exact: false }), 'OAuthToken');
    expect(within(dialog).getByText(h.form.credentialHints.OAuthToken('CLAUDE_CODE_OAUTH_TOKEN'))).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: h.form.save }));
    expect(await within(dialog).findByText(h.form.needsCredential)).toBeInTheDocument();
    expect(calls.some((c) => c.method === 'PUT')).toBe(false);
    const secret = within(dialog).getByLabelText(h.form.credential);
    expect(secret).toHaveAttribute('type', 'password');
    await user.type(secret, 'oauth-value-not-real');
    await user.click(within(dialog).getByRole('button', { name: h.form.save }));
    await waitFor(() => expect(calls.some((c) => c.method === 'PUT' && c.path.endsWith('/harnesses/ClaudeCode'))).toBe(true));
    expect(calls.find((c) => c.method === 'PUT').body).toEqual({ model: 'sonnet', permissionMode: 'ReadOnly', timeoutSeconds: null, maxToolCalls: null, allowWebResearch: true, baseUrl: null, credentialType: 'OAuthToken', credential: 'oauth-value-not-real' });

    const stored = (await screen.findByText(h.credentialTypes.OAuthToken)).closest('tr');
    expect(within(stored).getByText(h.stored)).toBeInTheDocument();
    expect(screen.queryByText('oauth-value-not-real')).toBeNull();
    await user.click(within(stored).getByRole('button', { name: h.reveal + ' ClaudeCode' }));
    const reveal = await screen.findByRole('dialog');
    expect(within(reveal).getByRole('button', { name: h.revealForm.submit })).toBeDisabled();
    await user.type(within(reveal).getByLabelText(h.revealForm.reason), 'Rotating the harness token');
    await user.click(within(reveal).getByRole('button', { name: h.revealForm.submit }));
    expect(await within(reveal).findByDisplayValue('revealed-value-not-real')).toBeInTheDocument();
    expect(calls.find((c) => c.path.endsWith('/reveal')).body).toEqual({ reason: 'Rotating the harness token' });

    const mux = screen.getByText('Mux').closest('tr');
    await user.click(within(reveal).getByRole('button', { name: /close/i }));
    await user.click(within(mux).getByRole('button', { name: h.edit + ' Mux' }));
    const muxDialog = await screen.findByRole('dialog');
    expect(within(muxDialog).queryByLabelText(h.form.baseUrl, { exact: false })).toBeNull();
    expect(within(within(muxDialog).getByLabelText(h.form.credentialType, { exact: false })).getAllByRole('option')).toHaveLength(1);
  });
});
