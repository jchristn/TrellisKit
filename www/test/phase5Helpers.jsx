import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { AuthProvider, useAuth } from '../src/context/AuthContext.jsx';
import { ToastProvider } from '../src/context/ToastContext.jsx';
import { ThemeProvider } from '../src/context/ThemeContext.jsx';
import { PropertyProvider, useProperty } from '../src/context/PropertyContext.jsx';
import { NotificationsProvider, useNotifications } from '../src/context/NotificationsContext.jsx';
import { RealtimeConfigContext, RealtimeProvider } from '../src/context/RealtimeContext.jsx';
import { AccessPromptProvider } from '../src/components/AccessPrompt.jsx';
import { BUILD_SITE } from '../src/property/buildSite.js';
import { createFetch, json, makeMe } from './helpers.jsx';
import { memberFetch } from './engagementHelpers.jsx';

/** A Response from a shared fake result (`{ status, body, contentType? }`). */
export function fakeResponse(result) {
  if (!result) return undefined;
  if (result.status === 204) return new Response(null, { status: 204 });
  if (typeof result.body === 'string') return new Response(result.body, { status: result.status, headers: { 'content-type': result.contentType || 'text/plain' } });
  return json(result.body, result.status);
}

/** A member session (`/auth/refresh` returns `me`) whose other routes are served by shared fakes. */
export function fakeMemberFetch(fakes, me = makeMe()) {
  const list = Array.isArray(fakes) ? fakes : [fakes];
  return memberFetch((call) => {
    for (const fake of list) {
      const result = fake.handle({ method: call.method, path: call.path, query: call.query, body: call.body, headers: call.headers });
      if (result) return fakeResponse(result);
    }
    return undefined;
  }, me);
}

/** An admin fetch (token mode) served by shared fakes. */
export function fakeAdminFetch(fakes) {
  const list = Array.isArray(fakes) ? fakes : [fakes];
  return createFetch((call) => {
    if (call.path === '/api/v1/settings/enumerate') return json({ objects: [], endOfResults: true });
    if (call.path === '/api/v1/labels/enumerate' || call.path === '/api/v1/tags/keys/enumerate') return json({ success: true, objects: [], continuationToken: null, endOfResults: true });
    for (const fake of list) {
      const result = fake.handle({ method: call.method, path: call.path, query: call.query, body: call.body, headers: call.headers });
      if (result) return fakeResponse(result);
    }
    return undefined;
  });
}

/** A site bundle with property overrides (features, access, ideas, domains). */
export function siteWith(overrides = {}) {
  const base = BUILD_SITE.property;
  return {
    ...BUILD_SITE,
    property: {
      ...base,
      ...overrides,
      features: { ...(base.features || {}), messaging: true, support: true, ideas: true, broadcasts: true, ...(overrides.features || {}) }
    }
  };
}

function SessionRealtime({ children }) {
  const auth = useAuth();
  const { property } = useProperty();
  if (!auth.isSignedIn) return null;
  return (
    <NotificationsProvider client={auth.client} enabled intervalMs={600000}>
      <RealtimeProvider client={auth.client} me={auth.me} property={property} enabled refreshSession={auth.refresh}>
        <span data-testid="session-ready" hidden />
        <BellProbe />
        {children}
      </RealtimeProvider>
    </NotificationsProvider>
  );
}

function BellProbe() {
  const { unreadCount } = useNotifications();
  return <span data-testid="bell-count" hidden>{unreadCount}</span>;
}

/** Shows the current location for navigation assertions. */
export function LocationProbe() {
  const location = useLocation();
  return <span data-testid="location">{location.pathname + location.search}</span>;
}

/**
 * Render `ui` at `path` inside a signed-in member session with the session's RealtimeProvider
 * (realtime config injects the fake WebSocket and timers). Waits for the session.
 */
export async function renderPhase5(ui, { fetchImpl, path = '/test', routePath = '/test', extraRoutes = null, site = siteWith(), realtime = {} } = {}) {
  const result = render(
    <PropertyProvider site={site}>
      <ThemeProvider>
        <RealtimeConfigContext.Provider value={realtime}>
          <MemoryRouter initialEntries={[path]}>
            <AuthProvider fetchImpl={fetchImpl} restoreOnLoad>
              <ToastProvider>
                <AccessPromptProvider>
                  <SessionRealtime>
                    <Routes>
                      <Route path={routePath} element={ui} />
                      {extraRoutes}
                    </Routes>
                    <LocationProbe />
                  </SessionRealtime>
                </AccessPromptProvider>
              </ToastProvider>
            </AuthProvider>
          </MemoryRouter>
        </RealtimeConfigContext.Provider>
      </ThemeProvider>
    </PropertyProvider>
  );
  await screen.findByTestId('session-ready');
  return result;
}
