import { test } from 'node:test';
import assert from 'node:assert/strict';
import { ApiClient, ApiError, isAdult, isEmail, isHandle, formatCount, formatRelativeTime, formatRetryAfter, formatTimeUntil } from '../src/index.js';

test('ApiClient builds /api/v1 URLs and omits empty query values', () => {
  const client = new ApiClient({ baseUrl: 'https://api.example.com/', fetchImpl: async () => null });
  assert.equal(client.url('/health'), 'https://api.example.com/api/v1/health');
  assert.equal(client.url('feed', { limit: 20, cursor: null, q: '' }), 'https://api.example.com/api/v1/feed?limit=20');
});

test('ApiClient sends bearer token and parses JSON', async () => {
  let seen = null;
  const fetchImpl = async (url, init) => {
    seen = { url, init };
    return new Response(JSON.stringify({ status: 'Healthy' }), { status: 200, headers: { 'content-type': 'application/json' } });
  };
  const client = new ApiClient({ fetchImpl, getAccessToken: () => 'abc' });
  const body = await client.health();
  assert.equal(body.status, 'Healthy');
  assert.equal(seen.init.headers.Authorization, 'Bearer abc');
  assert.equal(seen.url, '/api/v1/health');
});

test('ApiClient throws ApiError with server message on failure', async () => {
  const fetchImpl = async () => new Response(JSON.stringify({ error: 'NotFound', message: 'Thread not found.' }), { status: 404, headers: { 'content-type': 'application/json' } });
  const client = new ApiClient({ fetchImpl });
  await assert.rejects(client.get('/threads/x'), (err) => err instanceof ApiError && err.status === 404 && err.code === 'NotFound' && err.message === 'Thread not found.');
});

test('ApiClient maps network failure to status 0', async () => {
  const client = new ApiClient({ fetchImpl: async () => { throw new TypeError('fetch failed'); } });
  await assert.rejects(client.get('/health'), (err) => err instanceof ApiError && err.status === 0);
});

test('validators', () => {
  assert.ok(isEmail('member@example.com'));
  assert.ok(!isEmail('nope'));
  assert.ok(isHandle('member_c'));
  assert.ok(!isHandle('a'));
  const today = new Date(2026, 10, 11);
  assert.ok(isAdult(new Date(2008, 10, 11), today));
  assert.ok(!isAdult(new Date(2008, 10, 12), today));
});

test('formatters', () => {
  assert.equal(formatCount(999), '999');
  assert.equal(formatCount(1200), '1.2K');
  assert.equal(formatCount(2500000), '2.5M');
  const now = new Date('2026-11-11T12:00:00Z');
  assert.equal(formatRelativeTime('2026-11-11T11:55:00Z', now), '5m');
  assert.equal(formatRelativeTime('2026-11-11T09:00:00Z', now), '3h');
  assert.equal(formatTimeUntil('2026-11-12T00:00:00Z', now), 'in 12h');
  assert.equal(formatTimeUntil('2026-11-11T12:30:00Z', now), 'in 30m');
  assert.equal(formatTimeUntil('2026-11-14T12:00:00Z', now), 'in 3d');
  assert.equal(formatTimeUntil('2026-11-11T11:00:00Z', now), 'now');
});

test('formatRetryAfter rounds a Retry-After wait up into words', () => {
  assert.equal(formatRetryAfter(null), null);
  assert.equal(formatRetryAfter(0), null);
  assert.equal(formatRetryAfter(1), '1 second');
  assert.equal(formatRetryAfter(45), '45 seconds');
  assert.equal(formatRetryAfter(60), '1 minute');
  assert.equal(formatRetryAfter(3412), '57 minutes');
  assert.equal(formatRetryAfter(3600), 'about 1 hour');
  assert.equal(formatRetryAfter(3601), 'about 2 hours');
  assert.equal(formatRetryAfter(86400), 'about 1 day');
  assert.equal(formatRetryAfter(90000), 'about 2 days');
});
