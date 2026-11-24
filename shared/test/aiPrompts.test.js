import { test } from 'node:test';
import assert from 'node:assert/strict';
import { ApiClient } from '../src/index.js';

test('AI prompt draft, benchmark, and publish methods map to their routes', async () => {
  const calls = [];
  const fetchImpl = async (url, init) => {
    calls.push({ method: init.method, url: url.replace('/api/v1', ''), body: init.body ? JSON.parse(init.body) : undefined });
    return new Response(JSON.stringify({ ok: true }), { status: 200, headers: { 'content-type': 'application/json' } });
  };
  const client = new ApiClient({ fetchImpl });
  await client.adminSavePrompt('platform.scopeGuardrail', 'Scope: training only.');
  await client.adminBenchmarkPrompt('platform.scopeGuardrail', { dryRun: true });
  await client.adminBenchmarkPrompt('platform.scopeGuardrail');
  await client.adminPublishPrompt('platform.scopeGuardrail', 'apv_1');
  await client.adminPublishPrompt('platform.scopeGuardrail');
  assert.deepEqual(calls.map((c) => c.method + ' ' + c.url), [
    'PUT /admin/ai/prompts/platform.scopeGuardrail',
    'POST /admin/ai/prompts/platform.scopeGuardrail/benchmark',
    'POST /admin/ai/prompts/platform.scopeGuardrail/benchmark',
    'POST /admin/ai/prompts/platform.scopeGuardrail/publish',
    'POST /admin/ai/prompts/platform.scopeGuardrail/publish'
  ]);
  assert.deepEqual(calls[0].body, { content: 'Scope: training only.' });
  assert.deepEqual(calls[1].body, { dryRun: true });
  assert.deepEqual(calls[2].body, {});
  assert.deepEqual(calls[3].body, { versionId: 'apv_1' });
  assert.deepEqual(calls[4].body, {});
});
