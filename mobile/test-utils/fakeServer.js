/**
 * A fake backend for tests: install it as global.fetch and register handlers by
 * "METHOD /path" (path relative to /api/v1). Handlers return { status, body } or a body.
 */
import * as Network from 'expo-network';
import * as SecureStore from 'expo-secure-store';
import { resetVisitorClient } from '../src/ai/visitor';
import { resetApiClient } from '../src/api/client';
import { resetMapSearchSessions } from '../src/components/maps/mapSearchSessions';
import { resetSessionFilters } from '../src/hooks/useListFilters';
import { resetPushRegistrationState } from '../src/notifications/push';
import { resetSaveState } from '../src/training/saves';
import { resetTrainingOverviewMemory } from '../src/training/useTrainingOverview';
import { defaultProfile, property } from './property';

/** The active property's store product id (membership.storeProductId). */
export const PRODUCT = property.membership.storeProductId;

export function jsonResponse(body, status = 200, headers = {}) {
  const extra = {};
  for (const [name, value] of Object.entries(headers || {})) extra[name.toLowerCase()] = value;
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: { get: (name) => (name.toLowerCase() === 'content-type' ? 'application/json; charset=utf-8' : extra[name.toLowerCase()] || null) },
    json: async () => body,
    text: async () => JSON.stringify(body)
  };
}

export const disabledProviders = {
  email: true,
  apple: { enabled: false, webClientId: null },
  google: { enabled: false, webClientId: null, iosClientId: null, androidClientId: null },
  facebook: { enabled: false, appId: null },
  stripe: { enabled: false },
  appleIap: { enabled: false, productId: PRODUCT },
  googlePlay: { enabled: false, productId: PRODUCT }
};

/** A complete, active member. Override fields per test. */
export function makeUser(overrides = {}) {
  return {
    id: 'usr_1',
    email: 'member@example.com',
    emailVerified: true,
    displayName: 'Test Member',
    handle: 'tester',
    avatarUrl: null,
    bio: null,
    profile: defaultProfile(),
    ageAttested: true,
    profileComplete: true,
    profileMissing: [],
    signInMethods: ['Email'],
    roles: [],
    flags: {},
    membership: { isActive: true, source: 'Subscription', store: 'Apple', expiresUtc: '2027-11-11T00:00:00Z', autoRenew: true, willConvertToPaid: false },
    createdUtc: '2026-11-11T20:00:00Z',
    ...overrides
  };
}

export function authResponse(user, refreshToken = 'refresh-2') {
  return { accessToken: 'access-' + refreshToken, accessTokenExpiresUtc: '2099-01-01T00:00:00Z', refreshToken, isNewAccount: false, user };
}

/**
 * Create and install the fake server.
 * @param {Record<string, any>} routes
 * @param {{ fallback?: (call: { method: string, path: string, query: object, body: any }) => any }} [options]
 *   `fallback` answers routes without a handler (for example the shared forums fake); returning
 *   undefined gives the usual 404.
 */
export function installFakeServer(routes = {}, { fallback } = {}) {
  const handlers = { 'GET /auth/providers': disabledProviders, ...routes };
  const calls = [];
  const fetch = jest.fn(async (url, init = {}) => {
    const parsed = new URL(url);
    const path = parsed.pathname.replace(/^\/api\/v1/, '');
    const method = init.method || 'GET';
    const key = method + ' ' + path;
    let body = init.body;
    if (typeof body === 'string') {
      try {
        body = JSON.parse(body);
      } catch {
        // Raw body.
      }
    }
    const call = { key, url, method, path, headers: init.headers || {}, body };
    calls.push(call);
    const handler = handlers[key];
    let result;
    if (handler === undefined) {
      result = fallback ? await fallback({ ...call, query: Object.fromEntries(parsed.searchParams.entries()) }) : undefined;
      if (result === undefined) return jsonResponse({ error: 'NotFound', message: 'No fake route for ' + key }, 404);
    } else result = typeof handler === 'function' ? await handler(call) : handler;
    if (result && result.__network) throw new TypeError('Network request failed');
    // { status, body, headers } answers with response headers (for example ETag); 304 has no body.
    if (result && typeof result === 'object' && 'status' in result && 'headers' in result && Object.keys(result).length <= 3) {
      if (result.status === 304) return { ok: false, status: 304, headers: jsonResponse(null, 304, result.headers).headers, json: async () => null, text: async () => '' };
      return jsonResponse(result.body, result.status, result.headers);
    }
    if (result && typeof result === 'object' && 'status' in result && 'body' in result && Object.keys(result).length === 2) {
      if (result.status === 204) return { ok: true, status: 204, headers: { get: () => null }, json: async () => null, text: async () => '' };
      return jsonResponse(result.body, result.status);
    }
    return jsonResponse(result, 200);
  });
  global.fetch = fetch;
  return {
    fetch,
    calls,
    handlers,
    called: (key) => calls.filter((c) => c.key === key)
  };
}

/** Reset the API client, secure storage, push registration, and network state between tests. */
export function resetEnvironment() {
  resetApiClient();
  resetVisitorClient();
  resetPushRegistrationState();
  resetSessionFilters();
  resetMapSearchSessions();
  resetTrainingOverviewMemory();
  resetSaveState();
  SecureStore.__store.clear();
  Network.__reset();
}

/**
 * Store a session that the app restores at launch: a refresh token plus the
 * POST /auth/refresh handler returning `user`.
 */
export function signedInAs(user, { notificationsPrompted = true } = {}) {
  SecureStore.__store.set('tk.refreshToken', 'refresh-1');
  if (notificationsPrompted) SecureStore.__store.set('tk.notificationsPrompted.' + user.id, '1');
  return { 'POST /auth/refresh': () => authResponse(user) };
}
