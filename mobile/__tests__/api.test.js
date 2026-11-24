import * as SecureStore from 'expo-secure-store';
import { isRefreshableRoute, MobileApiClient } from '../src/api/MobileApiClient';
import { RefreshTokenKey, tokenStore } from '../src/api/tokenStore';
import { authResponse, installFakeServer, makeUser, resetEnvironment } from '../test-utils/fakeServer';

function makeClient(options = {}) {
  return new MobileApiClient({ baseUrl: 'http://api.test', appVersion: '1.0.0', tokenStore, ...options });
}

describe('MobileApiClient', () => {
  beforeEach(() => resetEnvironment());
  afterEach(() => delete global.fetch);

  test('sends X-Client: mobile, X-Client-Version, and the bearer token', async () => {
    const server = installFakeServer({ 'GET /auth/me': makeUser() });
    const api = makeClient();
    await api.setSession(authResponse(makeUser(), 'r1'));
    await api.get('/auth/me');
    const { headers } = server.calls[0];
    expect(headers['X-Client']).toBe('mobile');
    expect(headers['X-Client-Version']).toBe('1.0.0');
    expect(headers.Authorization).toBe('Bearer access-r1');
  });

  test('402 MembershipRequired and 403 AgeAttestationRequired run the access listeners; other errors do not', async () => {
    installFakeServer({
      'POST /feed/enumerate': { status: 402, body: { error: 'MembershipRequired', message: 'An active membership is required.' } },
      'GET /feed/fpo_1': { status: 403, body: { error: 'AgeAttestationRequired', message: 'Confirm your age.' } },
      'DELETE /feed/fpo_1': { status: 403, body: { error: 'Forbidden', message: 'No.' } }
    });
    const api = makeClient();
    const seen = [];
    const off = api.onAccessRequired((error) => seen.push(error.code));
    await expect(api.post('/feed/enumerate', { maxResults: 20 })).rejects.toMatchObject({ status: 402, code: 'MembershipRequired' });
    await expect(api.get('/feed/fpo_1')).rejects.toMatchObject({ status: 403 });
    await expect(api.delete('/feed/fpo_1')).rejects.toMatchObject({ code: 'Forbidden' });
    expect(seen).toEqual(['MembershipRequired', 'AgeAttestationRequired']);
    off();
    await expect(api.post('/feed/enumerate', {})).rejects.toMatchObject({ status: 402 });
    expect(seen).toHaveLength(2);
  });

  test('stores the refresh token in secure storage and keeps the access token in memory only', async () => {
    const api = makeClient();
    await api.setSession(authResponse(makeUser(), 'refresh-abc'));
    expect(SecureStore.setItemAsync).toHaveBeenCalledWith(RefreshTokenKey, 'refresh-abc', expect.objectContaining({ keychainAccessible: SecureStore.AFTER_FIRST_UNLOCK }));
    expect(SecureStore.__store.get(RefreshTokenKey)).toBe('refresh-abc');
    expect([...SecureStore.__store.values()]).not.toContain('access-refresh-abc');
    expect(api.accessToken).toBe('access-refresh-abc');

    await api.clearSession();
    expect(SecureStore.__store.has(RefreshTokenKey)).toBe(false);
    expect(api.accessToken).toBeNull();
  });

  test('a 401 refreshes once with the stored token, then retries the request', async () => {
    let meCalls = 0;
    const server = installFakeServer({
      'GET /me/export': (call) => {
        meCalls += 1;
        return call.headers.Authorization === 'Bearer access-r2' ? { ok: true } : { status: 401, body: { error: 'Unauthorized', message: 'Expired' } };
      },
      'POST /auth/refresh': (call) => (call.body.refreshToken === 'r1' ? authResponse(makeUser(), 'r2') : { status: 401, body: { error: 'InvalidRefreshToken' } })
    });
    const api = makeClient();
    await api.setSession(authResponse(makeUser(), 'r1'));

    await expect(api.get('/me/export')).resolves.toEqual({ ok: true });
    expect(meCalls).toBe(2);
    expect(server.called('POST /auth/refresh')).toHaveLength(1);
    expect(server.called('POST /auth/refresh')[0].headers.Authorization).toBeUndefined();
    expect(SecureStore.__store.get(RefreshTokenKey)).toBe('r2');
  });

  test('concurrent 401s share a single in-flight refresh', async () => {
    let release;
    const gate = new Promise((resolve) => {
      release = resolve;
    });
    const server = installFakeServer({
      'GET /auth/me': (call) => (call.headers.Authorization === 'Bearer access-r2' ? makeUser() : { status: 401, body: { error: 'Unauthorized' } }),
      'POST /auth/refresh': async () => {
        await gate;
        return authResponse(makeUser(), 'r2');
      }
    });
    const api = makeClient();
    await api.setSession(authResponse(makeUser(), 'r1'));

    const requests = [api.get('/auth/me'), api.get('/auth/me'), api.get('/auth/me')];
    await new Promise((resolve) => setTimeout(resolve, 0));
    release();
    const results = await Promise.all(requests);
    expect(results.map((r) => r.id)).toEqual(['usr_1', 'usr_1', 'usr_1']);
    expect(server.called('POST /auth/refresh')).toHaveLength(1);
  });

  test('a rejected refresh clears the session and notifies listeners', async () => {
    installFakeServer({
      'GET /auth/me': { status: 401, body: { error: 'Unauthorized' } },
      'POST /auth/refresh': { status: 401, body: { error: 'InvalidRefreshToken', message: 'Revoked' } }
    });
    const api = makeClient();
    const expired = jest.fn();
    api.onSessionExpired(expired);
    await api.setSession(authResponse(makeUser(), 'r1'));

    await expect(api.get('/auth/me')).rejects.toMatchObject({ status: 401 });
    expect(expired).toHaveBeenCalledTimes(1);
    expect(SecureStore.__store.has(RefreshTokenKey)).toBe(false);
    expect(api.accessToken).toBeNull();
  });

  test('a 401 from an anonymous auth route (bad password) never triggers a refresh', async () => {
    const server = installFakeServer({ 'POST /auth/login': { status: 401, body: { error: 'InvalidCredentials', message: 'No match' } } });
    SecureStore.__store.set(RefreshTokenKey, 'stale');
    const api = makeClient();
    await expect(api.post('/auth/login', { email: 'a@b.co', password: 'x' })).rejects.toMatchObject({ code: 'InvalidCredentials' });
    expect(server.called('POST /auth/refresh')).toHaveLength(0);
    expect(isRefreshableRoute('/auth/login')).toBe(false);
    expect(isRefreshableRoute('/auth/me')).toBe(true);
    expect(isRefreshableRoute('/auth/email/resend')).toBe(true);
    expect(isRefreshableRoute('/me/profile')).toBe(true);
  });

  test('raw uploads send the bytes with their content type', async () => {
    const server = installFakeServer({ 'PUT /me/avatar': makeUser({ avatarUrl: '/api/v1/media/avatars/a.jpg' }) });
    const api = makeClient();
    const bytes = new Uint8Array([1, 2, 3]);
    const me = await api.request('PUT', '/me/avatar', { rawBody: bytes, contentType: 'image/jpeg' });
    expect(server.calls[0].headers['Content-Type']).toBe('image/jpeg');
    expect(server.calls[0].body).toBe(bytes);
    expect(api.url(me.avatarUrl)).toBe('http://api.test/api/v1/media/avatars/a.jpg');
  });

  test('network failures become status 0 errors', async () => {
    global.fetch = jest.fn(async () => {
      throw new TypeError('Network request failed');
    });
    await expect(makeClient().get('/health')).rejects.toMatchObject({ status: 0 });
  });
});
