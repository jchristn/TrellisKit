import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { render } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { AdminAuthProvider, ADMIN_TOKEN_KEY } from '../src/context/AdminAuthContext.jsx';
import { AuthProvider } from '../src/context/AuthContext.jsx';
import { ToastProvider } from '../src/context/ToastContext.jsx';
import { ThemeProvider } from '../src/context/ThemeContext.jsx';
import { PropertyProvider } from '../src/context/PropertyContext.jsx';
import { AppRoutes } from '../src/App.jsx';
import { loadSite } from '../scripts/property.mjs';

/** JSON Response helper. */
export function json(body, status = 200) {
  return new Response(body === null ? null : JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' }
  });
}

/**
 * Build a fetch mock from a handler `(method, url, init) => Response | undefined`.
 * Records every call as { method, url, path, query, body, headers }.
 */
export function createFetch(handler) {
  const calls = [];
  const fetchImpl = async (url, init = {}) => {
    const method = (init.method || 'GET').toUpperCase();
    const parsed = new URL(url, 'http://localhost');
    const call = {
      method,
      url,
      path: parsed.pathname,
      query: Object.fromEntries(parsed.searchParams.entries()),
      search: parsed.search,
      body: typeof init.body === 'string' ? JSON.parse(init.body) : init.body,
      headers: init.headers || {}
    };
    calls.push(call);
    const result = await handler(call);
    return result || json({ error: 'NotFound', message: 'No mock for ' + method + ' ' + parsed.pathname }, 404);
  };
  return { fetchImpl, calls };
}

/** Render admin UI with providers, a stored token, and a router at `path`. */
export function renderAdmin(ui, { fetchImpl, path = '/admin/test', routePath = '/admin/test', token = 'test-token', site = null } = {}) {
  if (token) window.sessionStorage.setItem(ADMIN_TOKEN_KEY, token);
  const tree = (
    <ThemeProvider>
      <MemoryRouter initialEntries={[path]}>
        <AuthProvider fetchImpl={fetchImpl} restoreOnLoad={false}>
          <AdminAuthProvider fetchImpl={fetchImpl}>
            <ToastProvider>
              <Routes>
                <Route path={routePath} element={ui} />
              </Routes>
            </ToastProvider>
          </AdminAuthProvider>
        </AuthProvider>
      </MemoryRouter>
    </ThemeProvider>
  );
  return render(site ? <PropertyProvider site={site}>{tree}</PropertyProvider> : tree);
}

/** A complete, onboarded member (`Me`) for tests; override any field. */
export function makeMe(overrides = {}) {
  return {
    id: 'usr_1',
    email: 'sam@example.com',
    emailVerified: true,
    displayName: 'Sam Rivera',
    handle: 'sam',
    avatarUrl: null,
    bio: null,
    rank: 'Gold',
    rankLevel: 2,
    affiliation: null,
    city: null,
    region: null,
    country: 'US',
    goals: [],
    goalsOther: null,
    rankHistory: [],
    profile: { rank: { key: 'Gold', level: 2 }, affiliation: null, city: null, region: null, country: 'US', goals: null, rankHistory: null },
    profileMissing: [],
    ageAttested: true,
    profileComplete: true,
    signInMethods: ['Email'],
    roles: [],
    flags: { canPostToFeed: false, canPromoteToFeed: false, canUploadVideo: false, canSendDirectMessages: true, isMuted: false, mutedUntilUtc: null },
    membership: { isActive: true, source: 'Subscription', store: 'Stripe', expiresUtc: '2027-11-11T00:00:00Z', autoRenew: true, willConvertToPaid: false },
    createdUtc: '2026-11-11T20:00:00Z',
    ...overrides
  };
}

/** An AuthResponse wrapping `me`. */
export function authResponse(me, { isNewAccount = false, accessToken = 'access-1' } = {}) {
  return { accessToken, accessTokenExpiresUtc: '2026-11-11T21:00:00Z', refreshToken: null, isNewAccount, user: me };
}

/** Absolute folder of a test fixture property (test/fixtures/<name>). */
export function fixtureDir(name) {
  return path.resolve(path.dirname(fileURLToPath(import.meta.url)), 'fixtures', name);
}

/** Load a fixture property folder into a site bundle, exactly as the build does. */
export function loadFixtureSite(name) {
  return loadSite(fixtureDir(name)).site;
}

/** Render the whole app at `path` for a site bundle (default: the build-time fixture). */
export function renderSite(path, { site, fetchImpl = null, extra = null } = {}) {
  return render(
    <PropertyProvider site={site}>
      <ThemeProvider>
        <MemoryRouter initialEntries={[path]}>
          <AppRoutes fetchImpl={fetchImpl} />
          {extra}
        </MemoryRouter>
      </ThemeProvider>
    </PropertyProvider>
  );
}
