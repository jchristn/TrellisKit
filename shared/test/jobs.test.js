import { test } from 'node:test';
import assert from 'node:assert/strict';
import { ApiClient, JobIntervalTypes, JobItemStatuses, jobsMethods } from '../src/index.js';

function recorder() {
  const calls = [];
  const fetchImpl = async (url, init) => {
    calls.push({ method: init.method, url: url.replace('/api/v1', ''), body: init.body ? JSON.parse(init.body) : undefined });
    return new Response(JSON.stringify({ ok: true }), { status: 200, headers: { 'content-type': 'application/json' } });
  };
  return { calls, client: new ApiClient({ fetchImpl }) };
}

test('job engine ApiClient methods exist and map to their routes', async () => {
  const names = ['adminListJobs', 'adminJobOverview', 'adminGetJob', 'adminUpdateJob', 'adminRunJob', 'adminEnumerateJobRuns', 'adminGetJobRun', 'adminCancelJobRun',
    'adminEnumerateJobRunItems', 'adminEnumerateJobItems', 'adminRetryJobItem'];
  for (const name of names) assert.equal(typeof ApiClient.prototype[name], 'function', name);
  assert.equal(Object.keys(jobsMethods).length, names.length);
  assert.ok(JobItemStatuses.includes('Dead'));
  assert.ok(JobIntervalTypes.includes('Manual'));

  const { calls, client } = recorder();
  await client.adminListJobs();
  await client.adminRunJob('jobs.prune', true);
  await client.adminUpdateJob('jobs.prune', { enabled: false });
  await client.adminEnumerateJobItems({ filters: { status: ['Dead'] } });
  await client.adminRetryJobItem('jbi_1');
  await client.adminCancelJobRun('jbr_1');
  await client.adminEnumerateJobRunItems('jbr_1', {});
  assert.deepEqual(calls.map((c) => c.method + ' ' + c.url), [
    'POST /admin/jobs/enumerate', 'POST /admin/jobs/jobs.prune/run', 'PUT /admin/jobs/jobs.prune', 'POST /admin/jobs/items/enumerate', 'POST /admin/jobs/items/jbi_1/retry',
    'POST /admin/jobs/runs/jbr_1/cancel', 'POST /admin/jobs/runs/jbr_1/items/enumerate'
  ]);
  assert.deepEqual(calls[1].body, { dryRun: true });
  assert.deepEqual(calls[3].body.filters, { status: ['Dead'] });
});
