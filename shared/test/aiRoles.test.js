import { test } from 'node:test';
import assert from 'node:assert/strict';
import { ApiClient } from '../src/index.js';

test('AI task role and call ledger methods map to their routes', async () => {
  const calls = [];
  const fetchImpl = async (url, init) => {
    calls.push({ method: init.method, url: url.replace('/api/v1', ''), body: init.body ? JSON.parse(init.body) : undefined });
    return new Response(JSON.stringify({ ok: true }), { status: 200, headers: { 'content-type': 'application/json' } });
  };
  const client = new ApiClient({ fetchImpl });
  await client.adminListRoles();
  await client.adminSaveRole('Extraction', { executors: [{ runtime: 'ApiEndpoint', endpointId: 'mep_1' }] });
  await client.adminTestRole('Extraction', 'Hello');
  await client.adminEnumerateCalls({ filters: { role: ['Extraction'] } });
  assert.deepEqual(calls.map((c) => c.method + ' ' + c.url), ['POST /admin/ai/roles/enumerate', 'PUT /admin/ai/roles/Extraction', 'POST /admin/ai/roles/Extraction/test', 'POST /admin/ai/calls/enumerate']);
  assert.deepEqual(calls[2].body, { prompt: 'Hello' });
  assert.deepEqual(calls[3].body.filters, { role: ['Extraction'] });
});

test('harness profile methods map to their routes and never retry a reveal', async () => {
  const calls = [];
  const fetchImpl = async (url, init) => {
    calls.push({ method: init.method, url: url.replace('/api/v1', ''), body: init.body ? JSON.parse(init.body) : undefined });
    return new Response(JSON.stringify({ ok: true }), { status: 200, headers: { 'content-type': 'application/json' } });
  };
  const client = new ApiClient({ fetchImpl });
  await client.adminListHarnesses();
  await client.adminGetHarness('ClaudeCode');
  await client.adminSaveHarness('ClaudeCode', { model: 'sonnet', permissionMode: 'ReadOnly', credentialType: 'OAuthToken', credential: 'value-not-real' });
  await client.adminSaveHarness('Gemini Cli');
  await client.revealHarnessCredential('ClaudeCode', { reason: 'Rotating the harness token' });
  assert.deepEqual(calls.map((c) => c.method + ' ' + c.url), [
    'POST /admin/ai/harnesses/enumerate', 'GET /admin/ai/harnesses/ClaudeCode', 'PUT /admin/ai/harnesses/ClaudeCode', 'PUT /admin/ai/harnesses/Gemini%20Cli', 'POST /admin/ai/harnesses/ClaudeCode/reveal'
  ]);
  assert.deepEqual(calls[2].body, { model: 'sonnet', permissionMode: 'ReadOnly', credentialType: 'OAuthToken', credential: 'value-not-real' });
  assert.deepEqual(calls[3].body, {});
  assert.deepEqual(calls[4].body, { reason: 'Rotating the harness token' });
});
