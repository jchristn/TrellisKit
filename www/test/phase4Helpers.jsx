import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { AuthProvider, useAuth } from '../src/context/AuthContext.jsx';
import { ToastProvider } from '../src/context/ToastContext.jsx';
import { ThemeProvider } from '../src/context/ThemeContext.jsx';
import { PropertyProvider } from '../src/context/PropertyContext.jsx';
import { AccessPromptProvider } from '../src/components/AccessPrompt.jsx';
import { BUILD_SITE } from '../src/property/buildSite.js';
import { json, makeMe } from './helpers.jsx';
import { memberFetch } from './engagementHelpers.jsx';

/** The build-time site with extra property sections (`library`, `events`, `directory`, `access`, ...). */
export function siteWith(extra = {}) {
  return { ...BUILD_SITE, property: { ...BUILD_SITE.property, ...extra, features: { ...(BUILD_SITE.property.features || {}), ...(extra.features || {}) } } };
}

/** A Response for a fake's `{ status, body, contentType }` answer. */
export function fakeResponse(result) {
  if (!result) return undefined;
  if (result.status === 204) return new Response(null, { status: 204 });
  if (result.contentType) return new Response(String(result.body), { status: result.status, headers: { 'content-type': result.contentType } });
  return json(result.body, result.status);
}

/** A member session whose Phase 4 routes are served by the shared fakes (first answer wins). */
export function phase4Fetch(fakes, me = makeMe(), extra = null) {
  return memberFetch((call) => {
    if (extra) {
      const answer = extra(call);
      if (answer) return answer;
    }
    for (const fake of fakes) {
      const result = fake.handle({ method: call.method, path: call.path, query: call.query, body: call.body });
      if (result) return fakeResponse(result);
    }
    return undefined;
  }, me);
}

function SessionGate({ children }) {
  const auth = useAuth();
  if (!auth.isSignedIn) return null;
  return <><span data-testid="session-ready" hidden />{children}</>;
}

/** Render `ui` at `path` in a restored member session with a custom site. */
export async function renderP4(ui, { fetchImpl, path = '/test', routePath = '/test', site = siteWith(), extraRoutes = null } = {}) {
  const result = render(
    <PropertyProvider site={site}>
      <ThemeProvider>
        <MemoryRouter initialEntries={[path]}>
          <AuthProvider fetchImpl={fetchImpl} restoreOnLoad>
            <ToastProvider>
              <AccessPromptProvider>
                <SessionGate>
                  <Routes>
                    <Route path={routePath} element={ui} />
                    {extraRoutes}
                  </Routes>
                </SessionGate>
              </AccessPromptProvider>
            </ToastProvider>
          </AuthProvider>
        </MemoryRouter>
      </ThemeProvider>
    </PropertyProvider>
  );
  await screen.findByTestId('session-ready');
  return result;
}
