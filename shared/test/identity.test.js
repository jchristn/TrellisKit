import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  ApiClient, ApiError, singleFlight, passwordProblems, isDisplayName, normalizeSignupCode, isSignupCode, parseDateOnly,
  SignInMethod, SubscriptionStatus
} from '../src/index.js';

function jsonResponse(body, status = 200) {
  return new Response(body === null ? null : JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });
}

test('ApiClient sends configured default headers (object or function)', async () => {
  const seen = [];
  const fetchImpl = async (url, init) => { seen.push(init.headers); return jsonResponse({ ok: true }); };
  await new ApiClient({ fetchImpl, headers: { 'X-Client': 'web', 'X-Client-Version': '1.2.3' } }).get('/x');
  await new ApiClient({ fetchImpl, headers: () => ({ 'X-Client': 'mobile' }) }).get('/x');
  assert.equal(seen[0]['X-Client'], 'web');
  assert.equal(seen[0]['X-Client-Version'], '1.2.3');
  assert.equal(seen[1]['X-Client'], 'mobile');
});

test('ApiClient retries once after onUnauthorized resolves true', async () => {
  let token = 'old';
  const tokens = [];
  const fetchImpl = async (url, init) => {
    tokens.push(init.headers.Authorization);
    return init.headers.Authorization === 'Bearer new' ? jsonResponse({ ok: true }) : jsonResponse({ error: 'Unauthorized', message: 'no' }, 401);
  };
  let hookCalls = 0;
  const client = new ApiClient({
    fetchImpl,
    getAccessToken: () => token,
    onUnauthorized: async ({ path }) => { hookCalls += 1; assert.equal(path, '/me'); token = 'new'; return true; }
  });
  const body = await client.get('/me');
  assert.deepEqual(body, { ok: true });
  assert.equal(hookCalls, 1);
  assert.deepEqual(tokens, ['Bearer old', 'Bearer new']);
});

test('ApiClient does not retry more than once and honors retryOnUnauthorized false', async () => {
  let calls = 0;
  let hookCalls = 0;
  const fetchImpl = async () => { calls += 1; return jsonResponse({ error: 'Unauthorized', message: 'no' }, 401); };
  const client = new ApiClient({ fetchImpl, onUnauthorized: async () => { hookCalls += 1; return true; } });
  await assert.rejects(client.get('/me'), (err) => err instanceof ApiError && err.status === 401);
  assert.equal(calls, 2);
  assert.equal(hookCalls, 1);
  await assert.rejects(client.post('/auth/refresh', undefined, { retryOnUnauthorized: false }), (err) => err.status === 401);
  assert.equal(hookCalls, 1);
});

test('ApiClient rethrows the original 401 when the hook declines or throws', async () => {
  const fetchImpl = async () => jsonResponse({ error: 'Unauthorized', message: 'Sign in.' }, 401);
  await assert.rejects(new ApiClient({ fetchImpl, onUnauthorized: () => false }).get('/me'), (err) => err.message === 'Sign in.');
  await assert.rejects(new ApiClient({ fetchImpl, onUnauthorized: () => { throw new Error('boom'); } }).get('/me'), (err) => err.status === 401);
});

test('ApiClient sends raw bodies with a content type and reads text and blobs', async () => {
  let seen = null;
  const fetchImpl = async (url, init) => {
    seen = init;
    return new Response('a,b\n1,2', { status: 200, headers: { 'content-type': 'text/csv' } });
  };
  const client = new ApiClient({ fetchImpl });
  const bytes = new Uint8Array([1, 2, 3]);
  const text = await client.request('PUT', '/me/avatar', { rawBody: bytes, contentType: 'image/png' });
  assert.equal(seen.body, bytes);
  assert.equal(seen.headers['Content-Type'], 'image/png');
  assert.equal(text, 'a,b\n1,2');
  const blob = await client.get('/export.csv', { responseType: 'blob' });
  assert.equal(await blob.text(), 'a,b\n1,2');
  const page = await client.get('/export.csv', { responseType: 'textWithHeaders' });
  assert.equal(page.text, 'a,b\n1,2');
  assert.equal(page.headers.get('content-type'), 'text/csv');
});

test('singleFlight shares one in-flight call and starts a new one after it settles', async () => {
  let runs = 0;
  let release;
  const fn = singleFlight(() => { runs += 1; return new Promise((resolve) => { release = resolve; }); });
  const a = fn();
  const b = fn();
  await Promise.resolve();
  release('done');
  assert.equal(await a, 'done');
  assert.equal(await b, 'done');
  assert.equal(runs, 1);
  const c = fn();
  await Promise.resolve();
  release('again');
  assert.equal(await c, 'again');
  assert.equal(runs, 2);
});

test('passwordProblems applies the password policy', () => {
  assert.deepEqual(passwordProblems('short'), ['tooShort']);
  assert.deepEqual(passwordProblems('x'.repeat(129)), ['tooLong']);
  assert.deepEqual(passwordProblems('member@example.com', 'Member@Example.com'), ['matchesEmail']);
  assert.deepEqual(passwordProblems('correct horse battery', 'member@example.com'), []);
});

test('identity validators', () => {
  assert.ok(isDisplayName('Jo'));
  assert.ok(!isDisplayName(' J '));
  assert.ok(!isDisplayName('x'.repeat(61)));
  assert.equal(normalizeSignupCode('  seminar2026 '), 'SEMINAR2026');
  assert.ok(isSignupCode('seminar-2026'));
  assert.ok(!isSignupCode('abc'));
  assert.ok(!isSignupCode('BAD CODE!'));
  const d = parseDateOnly('2000-02-29');
  assert.equal(d.getFullYear(), 2000);
  assert.equal(d.getMonth(), 1);
  assert.equal(parseDateOnly('2001-02-29'), null);
  assert.equal(parseDateOnly('nope'), null);
});

test('identity enums', () => {
  assert.equal(SignInMethod.Google, 'Google');
  assert.equal(SubscriptionStatus.InBillingRetry, 'InBillingRetry');
});
