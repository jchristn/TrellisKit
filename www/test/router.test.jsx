import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import userEvent from '@testing-library/user-event';
import { AppRoutes } from '../src/App.jsx';
import { ThemeProvider } from '../src/context/ThemeContext.jsx';
import { ADMIN_TOKEN_KEY } from '../src/context/AdminAuthContext.jsx';
import { SESSION_HINT_KEY } from '../src/context/AuthContext.jsx';
import { PropertyProvider } from '../src/context/PropertyContext.jsx';
import { authResponse, createFetch, json, makeMe } from './helpers.jsx';

function renderAt(path, fetchImpl = null) {
  return render(
    <ThemeProvider>
      <MemoryRouter initialEntries={[path]}>
        <AppRoutes fetchImpl={fetchImpl} />
      </MemoryRouter>
    </ThemeProvider>
  );
}

// Unit tests run against the test fixture property (test/fixtures/dojo).
const MARKETING = [
  ['/', 'Practice together', 'Fixture Dojo | Practice together'],
  ['/guide', 'The Fixture Guide', 'The Fixture Guide | Fixture Dojo'],
  ['/about', 'About the owner', 'About the owner | Fixture Dojo'],
  ['/membership', 'Membership', 'Membership | Fixture Dojo'],
  ['/privacy', 'Privacy policy', 'Privacy Policy | Fixture Dojo'],
  ['/terms', 'Terms of service', 'Terms of Service | Fixture Dojo'],
  ['/community-guidelines', 'Community guidelines', 'Community Guidelines | Fixture Dojo'],
  ['/support', 'Support', 'Support | Fixture Dojo'],
  ['/delete-account', 'Delete your account', 'Delete Your Account | Fixture Dojo']
];

describe('router', () => {
  it.each(MARKETING)('renders %s with its heading, title, and meta description', async (path, heading, title) => {
    renderAt(path);
    expect(screen.getByRole('heading', { level: 1, name: heading })).toBeInTheDocument();
    await waitFor(() => expect(document.title).toBe(title));
    expect(document.querySelector('meta[name="description"]').getAttribute('content')).toBeTruthy();
  });

  it.each(['/privacy', '/terms', '/community-guidelines', '/delete-account'])('shows the draft banner on %s', (path) => {
    renderAt(path);
    expect(screen.getByText('Draft, pending legal review. This page is not yet in effect.')).toBeInTheDocument();
  });

  it('links the external buy button from the property content', () => {
    renderAt('/guide');
    const links = screen.getAllByRole('link', { name: /Buy the guide/ });
    expect(links[0]).toHaveAttribute('href', 'https://shop.dojo.test/guide');
  });

  it('renders a not found page for unknown routes', () => {
    renderAt('/nope');
    expect(screen.getByRole('heading', { name: 'Page not found' })).toBeInTheDocument();
  });

  it('sends signed-out visitors from /app to the sign-in page', async () => {
    const { fetchImpl } = createFetch(() => undefined);
    renderAt('/app', fetchImpl);
    expect(await screen.findByRole('heading', { level: 1, name: 'Sign in' })).toBeInTheDocument();
  });

  it('lets signed-out visitors browse when the server turned anonymous browsing on after the build', async () => {
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/property') return json({ access: { model: 'PartiallyGated', anonymousBrowsing: true, gatedFeatures: [], defaultAudience: 'SignedIn', purchasesEnabled: true, signupCodesEnabled: true } });
      return undefined;
    });
    render(
      <PropertyProvider>
        <ThemeProvider>
          <MemoryRouter initialEntries={['/app']}>
            <AppRoutes fetchImpl={fetchImpl} />
          </MemoryRouter>
        </ThemeProvider>
      </PropertyProvider>
    );
    expect(await screen.findByRole('heading', { level: 1, name: 'Welcome to Fixture Dojo' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { level: 1, name: 'Sign in' })).toBeNull();
    expect(calls.some((c) => c.path === '/api/v1/property')).toBe(true);
  });

  it('restores a session and renders a live section (every member section is open) with every section in the navigation', async () => {
    window.localStorage.setItem(SESSION_HINT_KEY, '1');
    const { fetchImpl } = createFetch((call) => {
      if (call.path === '/api/v1/auth/refresh') return json(authResponse(makeMe()));
      return undefined;
    });
    renderAt('/app/messages', fetchImpl);
    expect((await screen.findAllByRole('heading', { name: 'Messages' })).length).toBeGreaterThan(0);
    expect(screen.queryByText('Coming soon')).toBeNull();
    expect(screen.queryByText(/Phase/)).toBeNull();
    for (const name of ['Feed', 'Forums', 'Library', 'Events', 'Directory', 'Messages', 'Profile']) {
      expect(screen.getAllByRole('link', { name: new RegExp(name) }).length).toBeGreaterThan(0);
    }
    // The navigation collapses to an icon rail and the choice is remembered.
    const toggle = screen.getByTestId('member-nav-toggle');
    expect(document.querySelector('.member-app')).not.toHaveClass('nav-collapsed');
    fireEvent.click(toggle);
    expect(document.querySelector('.member-app')).toHaveClass('nav-collapsed');
    expect(window.localStorage.getItem('tk.member.navCollapsed')).toBe('true');
    expect(screen.getAllByRole('link', { name: /Feed/ })[0]).toHaveAttribute('title');
    fireEvent.click(toggle);
    expect(window.localStorage.getItem('tk.member.navCollapsed')).toBe('false');
  });

  it('shows Membership disabled with an explanation in the onboarding sidebar until the profile is complete', async () => {
    window.localStorage.setItem(SESSION_HINT_KEY, '1');
    const { fetchImpl } = createFetch((call) => {
      if (call.path === '/api/v1/auth/refresh') return json(authResponse(makeMe({ profileComplete: false, membership: null })));
      return undefined;
    });
    renderAt('/app/onboarding/profile', fetchImpl);
    const nav = await screen.findByRole('navigation', { name: /member/i });
    const membership = await within(nav).findByText('Membership');
    expect(membership.closest('[aria-disabled="true"]')).not.toBeNull();
    expect(within(nav).queryByRole('link', { name: 'Membership' })).toBeNull();
    expect(within(nav).getByText('Available after you finish your profile.')).toBeInTheDocument();
    expect(within(nav).getByRole('link', { name: 'Continue setup' })).toHaveAttribute('href', '/app/onboarding/profile');
  });

  it.each([
    ['/app/forums', 'coming-soon-forums'],
    ['/app/forums/general', 'coming-soon-forums'],
    ['/app/threads/thr_1', 'coming-soon-forums'],
    ['/app/search', 'coming-soon-search']
  ])('shows %s as coming soon without calling the API when the server lacks the capability', async (path, testId) => {
    window.localStorage.setItem(SESSION_HINT_KEY, '1');
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/auth/refresh') return json(authResponse(makeMe()));
      if (call.path === '/api/v1/property') return json({ features: { forums: false } });
      return undefined;
    });
    renderAt(path, fetchImpl);
    expect(await screen.findByTestId(testId)).toBeInTheDocument();
    expect(screen.getAllByText('Coming soon').length).toBeGreaterThan(0);
    // The entries stay in the navigation.
    expect(screen.getAllByRole('link', { name: /Forums/ }).length).toBeGreaterThan(0);
    // Search is hidden from the navigation while the server does not report it.
    expect(screen.queryAllByRole('link', { name: /^Search$/ })).toHaveLength(0);
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(calls.filter((c) => /^\/api\/v1\/(forums|threads|search)/.test(c.path))).toEqual([]);
  });

  it('opens the real forum and search screens once the server reports the capabilities', async () => {
    window.localStorage.setItem(SESSION_HINT_KEY, '1');
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/auth/refresh') return json(authResponse(makeMe()));
      if (call.path === '/api/v1/property') return json({ features: { forums: true, search: true } });
      return undefined;
    });
    const at = (path) => render(
      <PropertyProvider>
        <ThemeProvider>
          <MemoryRouter initialEntries={[path]}>
            <AppRoutes fetchImpl={fetchImpl} />
          </MemoryRouter>
        </ThemeProvider>
      </PropertyProvider>
    );
    const forums = at('/app/forums');
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/forums/enumerate')).toBe(true));
    expect(screen.queryByTestId('coming-soon-forums')).toBeNull();
    forums.unmount();
    at('/app/search');
    await waitFor(() => expect(calls.filter((c) => c.path === '/api/v1/property').length).toBeGreaterThan(1));
    await waitFor(() => expect(within(screen.getByRole('main')).getByRole('searchbox')).toBeInTheDocument());
    expect(screen.queryByTestId('coming-soon-search')).toBeNull();
  });

  it('searches from the top bar and links to the full search page', async () => {
    window.localStorage.setItem(SESSION_HINT_KEY, '1');
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/auth/refresh') return json(authResponse(makeMe()));
      if (call.path === '/api/v1/property') return json({ features: { forums: true, search: true } });
      return undefined;
    });
    render(
      <PropertyProvider>
        <ThemeProvider>
          <MemoryRouter initialEntries={['/app/forums']}>
            <AppRoutes fetchImpl={fetchImpl} />
          </MemoryRouter>
        </ThemeProvider>
      </PropertyProvider>
    );
    const form = await screen.findByRole('search', { name: 'Search the community' });
    expect(within(form).getByRole('link', { name: 'Advanced search' })).toHaveAttribute('href', '/app/search');
    await userEvent.type(within(form).getByRole('searchbox'), 'guard retention');
    await userEvent.click(within(form).getByRole('button', { name: 'Search' }));
    await waitFor(() => expect(calls.some((c) => /search/.test(c.path) && JSON.stringify(c.body || {}).includes('guard retention'))).toBe(true));
  });

  it('redirects admin routes to the login page with email sign-in and a token option', async () => {
    const { fetchImpl, calls } = createFetch(() => undefined);
    renderAt('/admin/requests', fetchImpl);
    expect(await screen.findByLabelText('Email')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Fixture Dojo Admin' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Use admin token' }));
    expect(screen.getByLabelText('Admin token')).toBeInTheDocument();
    // Only the anonymous runtime property load; no authenticated calls before sign in.
    expect(calls.map((c) => c.path)).toEqual(['/api/v1/property']);
  });

  it('opens the admin shell when a stored token validates', async () => {
    window.sessionStorage.setItem(ADMIN_TOKEN_KEY, 'tok');
    const { fetchImpl } = createFetch((call) => {
      if (call.path === '/api/v1/settings/enumerate') return json({ objects: [], endOfResults: true });
      if (call.path === '/api/v1/health') return json({ status: 'Healthy', version: '1.0.0', startedUtc: '2026-11-11T00:00:00Z', uptimeSeconds: 5, database: 'Sqlite' });
      if (call.path === '/api/v1/request-history/summary') return json({ totalCount: 0, totalSuccess: 0, totalFailure: 0, averageDurationMs: 0, buckets: [] });
      return undefined;
    });
    renderAt('/admin', fetchImpl);
    expect(await screen.findByRole('heading', { level: 1, name: 'Home' })).toBeInTheDocument();
    const nav = screen.getByRole('navigation', { name: 'Admin navigation' });
    for (const name of ['Members', 'Request History', 'API Explorer', 'Settings', 'Observability', 'Audit Log']) {
      expect(within(nav).getByRole('link', { name: new RegExp(name) })).toBeInTheDocument();
    }
    expect(screen.getByText('Powered by TrellisKit')).toBeInTheDocument();
    expect(screen.getByText('Fixture Dojo Admin')).toBeInTheDocument();
    await waitFor(() => expect(document.title).toBe('Home | Fixture Dojo Admin'));
  });

  it.each(['forums', 'threads', 'trending'])('admin %s opens a coming-soon page with no forum calls until the server reports forums', async (id) => {
    window.sessionStorage.setItem(ADMIN_TOKEN_KEY, 'tok');
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/settings/enumerate') return json({ objects: [], endOfResults: true });
      if (call.path === '/api/v1/health') return json({ status: 'Healthy', version: '1.0.0' });
      return undefined;
    });
    renderAt('/admin/' + id, fetchImpl);
    expect(await screen.findByTestId('admin-coming-soon-' + id)).toBeInTheDocument();
    expect(screen.getAllByText('Coming soon').length).toBeGreaterThan(0);
    const nav = screen.getByRole('navigation', { name: 'Admin navigation' });
    expect(within(nav).getByRole('link', { name: /Forums/ })).toBeInTheDocument();
    expect(screen.queryByText(/Phase/)).toBeNull();
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(calls.filter((c) => /\/(forums|threads|trending)\b/.test(c.path))).toEqual([]);
  });

  it('admin AI pages say the feature is turned off when the server reports it off', async () => {
    window.sessionStorage.setItem(ADMIN_TOKEN_KEY, 'tok');
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/settings/enumerate') return json({ objects: [], endOfResults: true });
      if (call.path === '/api/v1/health') return json({ status: 'Healthy', version: '1.0.0' });
      if (call.path === '/api/v1/property') return json({ features: { forums: true, ai: false } });
      return undefined;
    });
    render(
      <PropertyProvider>
        <ThemeProvider>
          <MemoryRouter initialEntries={['/admin/ai-endpoints']}>
            <AppRoutes fetchImpl={fetchImpl} />
          </MemoryRouter>
        </ThemeProvider>
      </PropertyProvider>
    );
    expect(await screen.findByTestId('admin-turned-off-ai-endpoints')).toBeInTheDocument();
    expect(screen.getByText('Model endpoints is turned off')).toBeInTheDocument();
    expect(screen.queryByText('Coming soon')).toBeNull();
    expect(calls.filter((c) => c.path.includes('/admin/ai/'))).toEqual([]);
  });

  it('admin forums opens the real page once the server reports the forums capability', async () => {
    window.sessionStorage.setItem(ADMIN_TOKEN_KEY, 'tok');
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/settings/enumerate') return json({ objects: [], endOfResults: true });
      if (call.path === '/api/v1/health') return json({ status: 'Healthy', version: '1.0.0' });
      if (call.path === '/api/v1/property') return json({ features: { forums: true } });
      return undefined;
    });
    render(
      <PropertyProvider>
        <ThemeProvider>
          <MemoryRouter initialEntries={['/admin/forums']}>
            <AppRoutes fetchImpl={fetchImpl} />
          </MemoryRouter>
        </ThemeProvider>
      </PropertyProvider>
    );
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/admin/forums/enumerate')).toBe(true));
    expect(screen.queryByTestId('admin-coming-soon-forums')).toBeNull();
  });

  it('shows the observability URLs in plain sight', async () => {
    window.sessionStorage.setItem(ADMIN_TOKEN_KEY, 'tok');
    const { fetchImpl } = createFetch((call) => (call.path === '/api/v1/settings' ? json({ fields: [] }) : json({ status: 'Healthy', version: '1.0.0' })));
    renderAt('/admin/observability', fetchImpl);
    expect(await screen.findByText(/http:\/\/127\.0\.0\.1:3000/)).toBeInTheDocument();
    expect(screen.getByText(/http:\/\/127\.0\.0\.1:9090/)).toBeInTheDocument();
    expect(screen.getByText(/http:\/\/127\.0\.0\.1:3200/)).toBeInTheDocument();
    expect(screen.getByText('Username admin, password admin')).toBeInTheDocument();
  });
});
