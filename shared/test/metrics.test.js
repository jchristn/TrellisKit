import { test } from 'node:test';
import assert from 'node:assert/strict';
import { ApiClient, metricsMethods } from '../src/index.js';

function recorder(result = { objects: [], totalRecords: 0 }) {
  const calls = [];
  const fetchImpl = async (url, init) => {
    calls.push({ method: init.method, url, signal: init.signal, body: init.body ? JSON.parse(init.body) : undefined });
    return new Response(JSON.stringify(result), { headers: { 'content-type': 'application/json' } });
  };
  return { calls, client: new ApiClient({ fetchImpl }) };
}

const query = Object.freeze({ metric: 'requests.total', from: '2026-11-01T00:00:00Z', to: '2026-11-08T00:00:00Z', bucket: 'Day', compare: true, filters: { status: '5xx' }, percentile: .95 });

test('property metrics helpers preserve query semantics, envelope data, gaps, and cancellation', async () => {
  const result = { metric: query.metric, series: [{ key: 'total', points: [{ value: null, complete: false, suppressed: true }] }], previous: [], annotations: [{ kind: 'Deploy', label: 'Server started' }], completeThroughUtc: null };
  const { client, calls } = recorder(result);
  const signal = new AbortController().signal;
  for (const name of Object.keys(metricsMethods)) assert.equal(typeof ApiClient.prototype[name], 'function', name);
  await client.adminMetricRegistry({}, signal);
  const data = await client.adminQueryMetrics({ query }, signal);
  await client.adminMetricPages({}, signal);
  await client.adminMetricWorkers({ createdAfterUtc: query.from, createdBeforeUtc: query.to }, signal);
  assert.deepEqual(data, result);
  assert.deepEqual(calls.map((c) => c.method + ' ' + c.url.split('?')[0]), ['POST /api/v1/admin/metrics/enumerate', 'POST /api/v1/admin/metrics/query', 'POST /api/v1/admin/metrics/pages/enumerate', 'POST /api/v1/admin/metrics/workers/enumerate']);
  assert.deepEqual(calls[1].body, { maxResults: 25, query });
  assert.deepEqual(calls[3].body, { maxResults: 25, createdAfterUtc: query.from, createdBeforeUtc: query.to });
  assert.ok(calls.every((c) => c.signal instanceof AbortSignal));
});

test('platform comparison helpers use the dedicated route and bounded enumeration without losing the metric query', async () => {
  const { client, calls } = recorder();
  const signal = new AbortController().signal;
  const request = Object.freeze({ maxResults: 900, skip: 20, ordering: 'ValueAscending', labels: ['region:west'], tags: { tier: 'standard' }, filters: { status: 'Disabled' }, search: ' North ', query, includeSeries: true });
  await client.platformCompareMetrics(request, signal);
  await client.platformQueryMetrics('app /?test', { query }, signal);
  assert.equal(calls[0].url, '/api/v1/platform/metrics/comparisons/enumerate');
  assert.deepEqual(calls[0].body, { maxResults: 20, skip: 20, ordering: 'ValueAscending', search: 'North', labels: ['region:west'], labelMatch: 'All', tags: { tier: 'standard' }, filters: { status: 'Disabled' }, query, includeSeries: true });
  assert.equal(calls[1].url, '/api/v1/platform/properties/app%20%2F%3Ftest/metrics/query');
  assert.deepEqual(calls[1].body, { maxResults: 25, query });
  assert.ok(calls.every((c) => c.signal instanceof AbortSignal));
  assert.equal(request.maxResults, 900);
});

test('platform comparisons default to 20 rows and preserve continuation tokens for explicit server validation', async () => {
  const { client, calls } = recorder();
  await client.platformCompareMetrics();
  await client.platformCompareMetrics({ maxResults: 'invalid', continuationToken: 'unsupported-token', query });
  assert.deepEqual(calls[0].body, { maxResults: 20, ordering: 'ValueDescending', query: {}, includeSeries: false });
  assert.equal(calls[1].body.maxResults, 20);
  assert.equal(calls[1].body.continuationToken, 'unsupported-token');
});


test('every metrics helper forwards the supplied cancellation signal to the API transport', async () => {
  const signal = new AbortController().signal;
  const options = [];
  const transport = { ...metricsMethods, get: (_path, init) => options.push(init), post: (_path, _body, init) => options.push(init) };
  await transport.adminMetricRegistry({}, signal);
  await transport.adminQueryMetrics({ query }, signal);
  await transport.adminMetricPages({}, signal);
  await transport.adminMetricWorkers({}, signal);
  await transport.platformCompareMetrics({ query }, signal);
  await transport.platformQueryMetrics('app_example', { query }, signal);
  assert.equal(options.length, 6);
  assert.ok(options.every((init) => init.signal === signal));
});
