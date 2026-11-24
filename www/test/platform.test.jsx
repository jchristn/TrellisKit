import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { ApiClient } from '@trelliskit/shared';
import { AppRoutes } from '../src/App.jsx';
import { ThemeProvider } from '../src/context/ThemeContext.jsx';
import { PropertyProvider } from '../src/context/PropertyContext.jsx';
import { CLIENT_HEADERS } from '../src/context/AuthContext.jsx';
import { browser } from '../src/utils/browser.js';
import { DELEGATION_KEY, PLATFORM_TOKEN_KEY, readDelegation } from '../src/utils/delegation.js';
import { buildPlatformAuditQuery, buildPropertiesQuery, recordStripeWebhookUrl } from '../src/utils/platformQuery.js';
import { buildCreateBody } from '../src/views/platform/PropertyCreateView.jsx';
import { stripeWebhookUrl } from '../src/views/admin/PropertyConfigView.jsx';
import { createFetch, json, loadFixtureSite } from './helpers.jsx';

const SITE = loadFixtureSite('dojo');

const ADMIN = { id: 'pad_1', email: 'ops@example.test', displayName: 'Ops', invitationPending: false, lastLoginUtc: null, createdUtc: '2026-11-01T00:00:00Z', labels: [], tags: {} };

function record(overrides = {}) {
  return {
    id: 'app_mwd1j5o5_FlHVU4CoaxDMHyBFZoJ',
    slug: 'second',
    name: 'Second Example',
    status: 'Enabled',
    domains: [
      { id: 'pdm_1', host: 'second.test', kind: 'Web', verifiedUtc: '2026-11-11T20:00:00Z' },
      { id: 'pdm_2', host: 'api.second.test', kind: 'Api', verifiedUtc: null }
    ],
    apps: [{ id: 'ppa_1', platform: 'Ios', appId: 'test.second.trelliskit', storeAppId: null, enabled: true }],
    memberCount: 3,
    createdUtc: '2026-11-11T20:00:00Z',
    updatedUtc: '2026-11-11T20:01:00Z',
    enabledUtc: '2026-11-11T20:01:00Z',
    disabledUtc: null,
    disabledReason: null,
    deleteScheduledUtc: null,
    purgeStartedUtc: null,
    labels: ['beta'],
    tags: { tier: 'gold' },
    ...overrides
  };
}

function page(objects) {
  return { success: true, maxResults: 50, skip: 0, continuationToken: null, endOfResults: true, totalRecords: objects.length, recordsRemaining: 0, timestampUtc: '2026-11-11T00:00:00Z', objects };
}

function renderAt(path, fetchImpl) {
  return render(
    <PropertyProvider site={SITE}>
      <ThemeProvider>
        <MemoryRouter initialEntries={[path]}>
          <AppRoutes fetchImpl={fetchImpl} restoreOnLoad={false} />
        </MemoryRouter>
      </ThemeProvider>
    </PropertyProvider>
  );
}

/** A platform host backend: signed in when the request carries the platform token. */
function platformFetch(handler = () => undefined, { signedIn = true } = {}) {
  return createFetch((call) => {
    const custom = handler(call);
    if (custom) return custom;
    if (call.path === '/api/v1/health') return json({ status: 'Healthy', version: '1.0.0', startedUtc: '2026-11-11T00:00:00Z' });
    if (call.path === '/api/v1/platform/auth/me') {
      return signedIn && call.headers.Authorization === 'Bearer tkp_1' ? json(ADMIN) : json({ error: 'Unauthorized', message: 'A valid platform administrator token is required.' }, 401);
    }
    return undefined;
  });
}

beforeEach(() => {
  window.sessionStorage.clear();
  window.localStorage.clear();
});
afterEach(() => vi.restoreAllMocks());

describe('platform console availability', () => {
  it('shows only an explanation when the backend has no platform routes on this host', async () => {
    const { fetchImpl, calls } = createFetch((call) => (call.path === '/api/v1/platform/auth/me' ? json({ error: 'NotAvailable', message: 'Platform routes exist only in MultiProperty mode.' }, 404) : undefined));
    renderAt('/platform/properties', fetchImpl);
    expect(await screen.findByText('The platform console is not available here')).toBeInTheDocument();
    expect(screen.getByText('Server answer: 404 NotAvailable')).toBeInTheDocument();
    expect(calls.map((c) => c.path)).toEqual(['/api/v1/platform/auth/me']);
    expect(screen.queryByRole('navigation', { name: 'Platform navigation' })).not.toBeInTheDocument();
  });

  it('signs in with a platform account and keeps the tkp_ token in session storage', async () => {
    const { fetchImpl, calls } = platformFetch((call) => {
      if (call.path === '/api/v1/platform/auth/login') {
        return call.body.password === 'right' ? json({ accessToken: 'tkp_1', expiresUtc: '2026-11-11T22:00:00Z', admin: ADMIN }) : json({ error: 'InvalidCredentials', message: 'no' }, 401);
      }
      if (call.path === '/api/v1/platform/properties/enumerate') return json(page([record()]));
      return undefined;
    });
    renderAt('/platform', fetchImpl);
    const user = userEvent.setup();
    await user.type(await screen.findByLabelText('Email'), 'ops@example.test');
    await user.type(screen.getByLabelText('Password'), 'wrong');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(await screen.findByText('That email and password were not accepted.')).toBeInTheDocument();
    await user.clear(screen.getByLabelText('Password'));
    await user.type(screen.getByLabelText('Password'), 'right');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(await screen.findByRole('link', { name: 'Second Example' })).toBeInTheDocument();
    expect(window.sessionStorage.getItem(PLATFORM_TOKEN_KEY)).toBe('tkp_1');
    const list = calls.find((c) => c.path === '/api/v1/platform/properties/enumerate');
    expect(list.headers.Authorization).toBe('Bearer tkp_1');
    expect(list.headers['X-TK-Property']).toBeUndefined();
    expect(screen.getByText('Ops')).toBeInTheDocument();
  });

  it('accepts an invitation and returns to sign-in', async () => {
    const { fetchImpl, calls } = platformFetch((call) => (call.path === '/api/v1/platform/auth/accept-invite' ? new Response(null, { status: 204 }) : undefined), { signedIn: false });
    renderAt('/platform', fetchImpl);
    const user = userEvent.setup();
    await user.click(await screen.findByRole('button', { name: 'I have an invitation token' }));
    await user.type(screen.getByLabelText('Invitation token'), 'invite-abc');
    await user.type(screen.getByLabelText('New password'), 'Correct-Horse-9');
    await user.click(screen.getByRole('button', { name: 'Set password' }));
    expect(await screen.findByText('Password set. Sign in with your email and new password.')).toBeInTheDocument();
    expect(calls.find((c) => c.path === '/api/v1/platform/auth/accept-invite').body).toEqual({ token: 'invite-abc', password: 'Correct-Horse-9' });
  });
});

describe('platform query builders', () => {
  it('builds bounded property enumerations with filters, ordering, labels, and tags', () => {
    expect(buildPropertiesQuery({ search: ' sec ', status: 'Enabled', host: 'second.test', ordering: 'NameAscending' }, { pageNumber: 2, pageSize: 25, meta: { labels: ['beta'], labelMatch: 'Any', tags: { tier: '*' } } })).toEqual({
      maxResults: 25, skip: 25, ordering: 'NameAscending', search: 'sec', createdAfterUtc: undefined, createdBeforeUtc: undefined,
      labels: ['beta'], labelMatch: 'Any', tags: { tier: '*' }, filters: { status: 'Enabled', host: 'second.test' }
    });
    expect(buildPropertiesQuery({}, { pageSize: 9000 }).maxResults).toBe(500);
    expect(buildPlatformAuditQuery({ propertyId: 'app_a', action: 'platform.delegate' }, {}).filters).toEqual({ propertyId: 'app_a', action: 'platform.delegate' });
  });

  it('builds the create body from a document or a clone', () => {
    const doc = { schemaVersion: 2, name: 'Second Example' };
    expect(buildCreateBody({ source: 'document', document: doc, name: ' Renamed ', slug: '', customDomains: false, meta: { labels: [], tags: {} } })).toEqual({ document: doc, name: 'Renamed' });
    expect(buildCreateBody({
      source: 'clone', cloneFromPropertyId: 'app_a', slug: 'copy', customDomains: true,
      domains: [{ host: 'Copy.Test', kind: 'Web' }, { host: '', kind: 'Api' }], apps: [{ platform: 'Android', appId: 'test.copy.app' }],
      meta: { labels: ['beta'], tags: {} }
    })).toEqual({
      cloneFromPropertyId: 'app_a', slug: 'copy', domains: [{ host: 'copy.test', kind: 'Web' }],
      apps: [{ platform: 'Android', appId: 'test.copy.app', storeAppId: null, enabled: true }], labels: ['beta'], tags: {}
    });
  });

  it('builds the per-property Stripe webhook URL from the Api domain', () => {
    expect(recordStripeWebhookUrl(record())).toBe('https://api.second.test/api/v1/webhooks/stripe/app_mwd1j5o5_FlHVU4CoaxDMHyBFZoJ');
    expect(recordStripeWebhookUrl(record({ domains: [] }), 'http://127.0.0.1:5173')).toBe('http://127.0.0.1:5173/api/v1/webhooks/stripe/app_mwd1j5o5_FlHVU4CoaxDMHyBFZoJ');
    expect(stripeWebhookUrl({ id: 'app_x_y', domains: { api: 'api.example.test' } })).toBe('https://api.example.test/api/v1/webhooks/stripe/app_x_y');
  });
});

describe('platform console pages', () => {
  beforeEach(() => window.sessionStorage.setItem(PLATFORM_TOKEN_KEY, 'tkp_1'));

  it('filters the property list on the server with label and tag filters', async () => {
    const { fetchImpl, calls } = platformFetch((call) => (call.path === '/api/v1/platform/properties/enumerate' ? json(page([record()])) : undefined));
    renderAt('/platform/properties?status=Enabled&labels=beta&tag.tier=gold', fetchImpl);
    expect(await screen.findByRole('link', { name: 'Second Example' })).toBeInTheDocument();
    const body = calls.find((c) => c.path === '/api/v1/platform/properties/enumerate').body;
    expect(body.filters).toEqual({ status: 'Enabled' });
    expect(body.labels).toEqual(['beta']);
    expect(body.tags).toEqual({ tier: 'gold' });
    expect(screen.getByText('second.test')).toBeInTheDocument();
    expect(screen.getByText('tier=gold')).toBeInTheDocument();
  });

  it('creates a property from a pasted property.json', async () => {
    const { fetchImpl, calls } = platformFetch((call) => {
      if (call.method === 'POST' && call.path === '/api/v1/platform/properties') return json(record({ status: 'Disabled' }), 201);
      if (call.path === '/api/v1/platform/properties/app_mwd1j5o5_FlHVU4CoaxDMHyBFZoJ') return json(record({ status: 'Disabled' }));
      return undefined;
    });
    renderAt('/platform/properties/new', fetchImpl);
    const user = userEvent.setup();
    const area = await screen.findByLabelText('Or paste the document');
    await user.type(area, '{{nope');
    expect(screen.getByText('This is not valid JSON.')).toBeInTheDocument();
    await user.clear(area);
    await user.click(area);
    await user.paste('{"schemaVersion":2,"name":"Second Example"}');
    await user.click(screen.getByRole('button', { name: 'Create property' }));
    expect(await screen.findByRole('heading', { name: 'Second Example' })).toBeInTheDocument();
    expect(calls.find((c) => c.method === 'POST' && c.path === '/api/v1/platform/properties').body).toEqual({ document: { schemaVersion: 2, name: 'Second Example' } });
  });

  it('enables with readiness problems, disables with a reason, and edits domains with verification state', async () => {
    let current = record({ status: 'Disabled', enabledUtc: null });
    const { fetchImpl, calls } = platformFetch((call) => {
      const base = '/api/v1/platform/properties/' + current.id;
      if (call.path === base && call.method === 'GET') return json(current);
      if (call.path === base + '/enable') return json({ error: 'PropertyNotReady', message: 'The property is not ready to be enabled.', fieldErrors: { domains: 'Add at least one Web domain.' } }, 409);
      if (call.path === base && call.method === 'PUT') {
        if (call.body.domains && call.body.domains.some((d) => d.host === 'taken.test')) return json({ error: 'DomainInUse', message: 'taken.test is registered to another property.' }, 409);
        current = { ...current, domains: call.body.domains.map((d, i) => ({ id: 'pdm_n' + i, ...d, verifiedUtc: null })), apps: current.apps, updatedUtc: '2026-11-11T21:00:00Z' };
        return json(current);
      }
      return undefined;
    });
    renderAt('/platform/properties/app_mwd1j5o5_FlHVU4CoaxDMHyBFZoJ', fetchImpl);
    const user = userEvent.setup();
    await user.click(await screen.findByRole('button', { name: 'Enable' }));
    expect(await screen.findByText(/Add at least one Web domain\./)).toBeInTheDocument();

    await user.click(screen.getByRole('tab', { name: 'Domains and apps' }));
    expect(screen.getByText('Verified')).toBeInTheDocument();
    expect(screen.getByText('Pending verification')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Add domain' }));
    await user.type(screen.getByLabelText('Host 3'), 'second.test');
    await user.click(screen.getByRole('button', { name: 'Save domains and apps' }));
    expect(await screen.findByText('Listed twice.')).toBeInTheDocument();
    await user.clear(screen.getByLabelText('Host 3'));
    await user.type(screen.getByLabelText('Host 3'), 'taken.test');
    await user.click(screen.getByRole('button', { name: 'Save domains and apps' }));
    expect(await screen.findByText('taken.test is registered to another property.')).toBeInTheDocument();
    await user.clear(screen.getByLabelText('Host 3'));
    await user.type(screen.getByLabelText('Host 3'), 'www.second.test');
    await user.click(screen.getByRole('button', { name: 'Save domains and apps' }));
    expect(await screen.findByText('Domains and apps saved.')).toBeInTheDocument();
    const put = calls.filter((c) => c.method === 'PUT').pop();
    expect(put.body.domains).toEqual([{ host: 'second.test', kind: 'Web' }, { host: 'api.second.test', kind: 'Api' }, { host: 'www.second.test', kind: 'Api' }]);
    expect(put.body.apps).toEqual([{ platform: 'Ios', appId: 'test.second.trelliskit', storeAppId: null, enabled: true }]);
  });

  it('deletes only after the property id is typed, then shows the 30-day restore window', async () => {
    let current = record({ status: 'Disabled' });
    const { fetchImpl, calls } = platformFetch((call) => {
      const base = '/api/v1/platform/properties/' + current.id;
      if (call.path === base && call.method === 'GET') return json(current);
      if (call.path === base && call.method === 'DELETE') {
        current = { ...current, status: 'Deleting', deleteScheduledUtc: new Date(Date.now() + 30 * 86400000 - 60000).toISOString(), updatedUtc: '2026-11-11T21:00:01Z' };
        return json(current, 202);
      }
      if (call.path === base + '/restore') {
        current = { ...current, status: 'Disabled', deleteScheduledUtc: null, updatedUtc: '2026-11-11T21:00:02Z' };
        return json(current);
      }
      return undefined;
    });
    renderAt('/platform/properties/app_mwd1j5o5_FlHVU4CoaxDMHyBFZoJ?tab=danger', fetchImpl);
    const user = userEvent.setup();
    await user.click(await screen.findByRole('button', { name: 'Delete property' }));
    const dialog = await screen.findByRole('dialog');
    const confirm = within(dialog).getByRole('button', { name: 'Delete property' });
    expect(confirm).toBeDisabled();
    await user.type(within(dialog).getByLabelText('Property id'), 'app_wrong');
    expect(confirm).toBeDisabled();
    await user.clear(within(dialog).getByLabelText('Property id'));
    await user.type(within(dialog).getByLabelText('Property id'), current.id);
    await user.click(confirm);
    expect(await screen.findByText(/Scheduled for permanent deletion on .* \(30 days left\)/)).toBeInTheDocument();
    expect(calls.find((c) => c.method === 'DELETE').body).toEqual({ confirm: 'app_mwd1j5o5_FlHVU4CoaxDMHyBFZoJ' });
    expect(screen.getByText('Purging runs automatically when the 30-day restore window ends. This server does not offer an immediate purge.')).toBeInTheDocument();
    await user.click(screen.getAllByRole('button', { name: 'Restore property' }).pop());
    const restoreDialog = await screen.findByRole('dialog');
    await user.click(within(restoreDialog).getByRole('button', { name: 'Restore property' }));
    await waitFor(() => expect(screen.queryByText(/Scheduled for permanent deletion/)).not.toBeInTheDocument());
    expect(calls.some((c) => c.path.endsWith('/restore'))).toBe(true);
  });

  it('"Act in this app" starts a delegated session and opens the admin console', async () => {
    const assign = vi.spyOn(browser, 'assign').mockImplementation(() => {});
    const session = { accessToken: 'delegated-1', accessTokenExpiresUtc: new Date(Date.now() + 3600000).toISOString(), propertyId: record().id };
    const { fetchImpl, calls } = platformFetch((call) => {
      if (call.path === '/api/v1/platform/properties/' + record().id) return json(record());
      if (call.path.endsWith('/admin-session')) return json(session);
      return undefined;
    });
    renderAt('/platform/properties/' + record().id, fetchImpl);
    const user = userEvent.setup();
    await user.click(await screen.findByRole('button', { name: 'Act in this app' }));
    await waitFor(() => expect(assign).toHaveBeenCalledWith('/admin/home'));
    expect(calls.find((c) => c.path.endsWith('/admin-session')).method).toBe('POST');
    expect(readDelegation()).toEqual({ ...session, propertyName: 'Second Example' });
  });

  it('invites a platform admin and shows the one-time token', async () => {
    const { fetchImpl, calls } = platformFetch((call) => {
      if (call.path === '/api/v1/platform/admins/enumerate') return json(page([ADMIN]));
      if (call.path === '/api/v1/platform/admins' && call.method === 'POST') {
        return json({ admin: { ...ADMIN, id: 'pad_2', email: 'new@example.test', invitationPending: true }, inviteToken: 'invite-xyz', inviteExpiresUtc: '2026-11-18T00:00:00Z' }, 201);
      }
      return undefined;
    });
    renderAt('/platform/admins', fetchImpl);
    const user = userEvent.setup();
    expect(await screen.findByText('ops@example.test')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Invite admin' }));
    const dialog = await screen.findByRole('dialog');
    await user.type(within(dialog).getByLabelText('Email'), 'new@example.test');
    await user.type(within(dialog).getByLabelText('Display name'), 'New Admin');
    await user.click(within(dialog).getByRole('button', { name: 'Create invitation' }));
    expect(await within(dialog).findByText('invite-xyz')).toBeInTheDocument();
    expect(calls.find((c) => c.method === 'POST' && c.path === '/api/v1/platform/admins').body).toEqual({ email: 'new@example.test', displayName: 'New Admin' });
  });

  it('reads the platform audit, or one property audit through filters.propertyId', async () => {
    const { fetchImpl, calls } = platformFetch((call) => (call.path === '/api/v1/platform/audit/enumerate' ? json(page([{ id: 'aud_1', actorName: 'Ops', actorUserId: 'pad_1', action: 'platform.delegate', targetType: 'Property', targetId: 'app_a', summary: null, createdUtc: '2026-11-11T20:00:00Z' }])) : undefined));
    renderAt('/platform/audit?propertyId=app_a', fetchImpl);
    expect(await screen.findByText('platform.delegate')).toBeInTheDocument();
    expect(calls.find((c) => c.path === '/api/v1/platform/audit/enumerate').body.filters).toEqual({ propertyId: 'app_a' });
  });

  it('platform settings use the platform settings route', async () => {
    const { fetchImpl, calls } = platformFetch((call) => (call.path === '/api/v1/platform/settings/enumerate' ? json(page([{ key: 'platform.inviteDays', section: 'Platform', label: 'Invite days', type: 'Integer', value: 7, applyMode: 'Live', overriddenByEnvironment: false, min: 1, max: 30 }])) : undefined));
    renderAt('/platform/settings', fetchImpl);
    expect(await screen.findByRole('heading', { name: 'Platform settings' })).toBeInTheDocument();
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/platform/settings/enumerate' && c.headers.Authorization === 'Bearer tkp_1')).toBe(true));
    expect(calls.some((c) => c.path === '/api/v1/settings/enumerate')).toBe(false);
  });
});

describe('delegated admin console', () => {
  const PROPERTY_ID = 'app_mwd1j5o5_FlHVU4CoaxDMHyBFZoJ';

  function delegate(msFromNow = 3600000) {
    window.sessionStorage.setItem(DELEGATION_KEY, JSON.stringify({ accessToken: 'delegated-1', accessTokenExpiresUtc: new Date(Date.now() + msFromNow).toISOString(), propertyId: PROPERTY_ID, propertyName: 'Second Example' }));
  }

  function adminFetch(handler = () => undefined) {
    return createFetch((call) => {
      const custom = handler(call);
      if (custom) return custom;
      if (call.path === '/api/v1/health') return json({ status: 'Healthy', version: '1.0.0', startedUtc: '2026-11-11T00:00:00Z' });
      if (call.path === '/api/v1/property') return json({ id: PROPERTY_ID, slug: 'second', name: 'Second Example', domains: { web: 'second.test', api: 'api.second.test', media: 'media.second.test', redirects: [] }, mobile: { ...SITE.property.mobile, bundleId: 'test.second.trelliskit', androidPackage: 'test.second.trelliskit' } });
      return undefined;
    });
  }

  it('sends the delegated token with X-TK-Property, shows the banner with time left, and ends the delegation', async () => {
    delegate(59 * 60000 + 30000);
    const assign = vi.spyOn(browser, 'assign').mockImplementation(() => {});
    window.sessionStorage.setItem(PLATFORM_TOKEN_KEY, 'tkp_1');
    const { fetchImpl, calls } = adminFetch((call) => (call.path === '/api/v1/admin/property/domains/enumerate' ? json({ ...page(record().domains), propertyId: PROPERTY_ID }) : call.path === '/api/v1/admin/property/apps/enumerate' ? json({ ...page(record().apps), propertyId: PROPERTY_ID }) : undefined));
    renderAt('/admin/property', fetchImpl);
    expect(await screen.findByText('You are acting in Second Example as a platform administrator (delegated session).')).toBeInTheDocument();
    expect(screen.getByText(/^(59:[0-9]{2}|60:00) left$/)).toBeInTheDocument();
    expect(screen.getByText('Delegated SystemAdmin')).toBeInTheDocument();
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/property')).toBe(true));
    const propertyCall = calls.find((c) => c.path === '/api/v1/property');
    expect(propertyCall.headers['X-TK-Property']).toBe(PROPERTY_ID);
    expect(propertyCall.headers.Authorization).toBe('Bearer delegated-1');
    // A delegated platform admin edits the domains through the property admin route, with verification state.
    expect(await screen.findByText('Pending verification')).toBeInTheDocument();
    const domainsCall = calls.find((c) => c.path === '/api/v1/admin/property/domains/enumerate');
    expect(domainsCall.headers.Authorization).toBe('Bearer delegated-1');
    expect(domainsCall.headers['X-TK-Property']).toBe(PROPERTY_ID);
    expect(calls.some((c) => c.path.startsWith('/api/v1/platform/properties'))).toBe(false);
    expect(await screen.findByText('https://api.second.test/api/v1/webhooks/stripe/' + PROPERTY_ID)).toBeInTheDocument();

    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: 'End delegation' }));
    expect(assign).toHaveBeenCalledWith('/platform/properties/' + PROPERTY_ID);
    expect(window.sessionStorage.getItem(DELEGATION_KEY)).toBeNull();
  });

  it('a 401 ends the delegated session and the login page says so', async () => {
    delegate();
    const { fetchImpl } = adminFetch((call) => (call.path === '/api/v1/request-history' ? json({ error: 'Unauthorized', message: 'A valid bearer token is required.' }, 401) : undefined));
    renderAt('/admin/requests', fetchImpl);
    expect(await screen.findByText(/The delegated session ended/)).toBeInTheDocument();
    expect(window.sessionStorage.getItem(DELEGATION_KEY)).toBeNull();
  });

  it('ignores an expired delegation', () => {
    delegate(-1000);
    expect(readDelegation()).toBeNull();
    expect(window.sessionStorage.getItem(DELEGATION_KEY)).toBeNull();
  });

  it('property admins verify domains and edit domains and app ids, with 409 conflicts on the offending row', async () => {
    window.sessionStorage.setItem('tk.adminToken', 'test-token');
    const pending = { id: 'pdm_3', host: 'www.second.test', kind: 'Redirect', verifiedUtc: null, source: 'Property', verification: { recordName: '_trelliskit-verify.www.second.test', recordValue: 'trelliskit-verify=abc123', lastCheckedUtc: '2026-11-11T20:05:00Z', error: 'No TXT record found.' } };
    const view = { propertyId: PROPERTY_ID, domains: [{ id: 'pdm_1', host: 'second.test', kind: 'Web', verifiedUtc: '2026-11-11T20:00:00Z', source: 'Platform', verification: null }, pending], apps: record().apps };
    const { fetchImpl, calls } = adminFetch((call) => {
      if (call.path === '/api/v1/settings/enumerate') return json({ fields: [] });
      if (call.path === '/api/v1/admin/property/domains/enumerate') return json({ ...page(view.domains), propertyId: PROPERTY_ID });
      if (call.path === '/api/v1/admin/property/apps/enumerate') return json({ ...page(view.apps), propertyId: PROPERTY_ID });
      if (call.path === '/api/v1/admin/property/domains/pdm_3/verify') return json({ ...pending, verifiedUtc: '2026-11-11T20:10:00Z' });
      if (call.path === '/api/v1/admin/property/domains' && call.method === 'PUT') return json({ error: 'DomainInUse', message: 'taken.example.test is registered to another property.' }, 409);
      return undefined;
    });
    renderAt('/admin/property', fetchImpl);
    expect(await screen.findByText('https://api.second.test/api/v1/webhooks/stripe/' + PROPERTY_ID)).toBeInTheDocument();
    expect(await screen.findByText('_trelliskit-verify.www.second.test')).toBeInTheDocument();
    expect(screen.getByText('trelliskit-verify=abc123')).toBeInTheDocument();
    expect(screen.getByText('No TXT record found.')).toBeInTheDocument();
    expect(screen.getByText('1 domain is waiting for DNS verification and not routed yet.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Copy Value' })).toBeInTheDocument();
    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: 'Verify www.second.test' }));
    expect(await screen.findByText('Every domain is verified.')).toBeInTheDocument();
    expect(calls.find((c) => c.path === '/api/v1/admin/property/domains/pdm_3/verify').method).toBe('POST');

    await user.click(screen.getByRole('button', { name: 'Add domain' }));
    await user.type(screen.getByLabelText('Host 3'), 'taken.example.test');
    await user.click(screen.getByRole('button', { name: 'Save domains and apps' }));
    const put = await waitFor(() => {
      const found = calls.find((c) => c.path === '/api/v1/admin/property/domains' && c.method === 'PUT');
      expect(found).toBeTruthy();
      return found;
    });
    expect(put.body.domains.map((d) => d.host)).toEqual(['second.test', 'www.second.test', 'taken.example.test']);
    expect(screen.getByLabelText('Host 3')).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getAllByText('taken.example.test is registered to another property.').length).toBeGreaterThan(0);
    expect(screen.queryByText('You are acting in Second Example as a platform administrator (delegated session).')).not.toBeInTheDocument();
    expect(calls.some((c) => c.path.startsWith('/api/v1/platform'))).toBe(false);
    expect(calls.find((c) => c.path === '/api/v1/property').headers['X-TK-Property']).toBeUndefined();
  });
});

describe('website requests in MultiProperty deployments', () => {
  it('the web client sends no X-TK-Property or X-TK-App: the backend resolves the property by host', async () => {
    const seen = [];
    const client = new ApiClient({ fetchImpl: async (url, init) => { seen.push(init.headers); return json({}); }, headers: CLIENT_HEADERS });
    await client.get('/property');
    await client.post('/auth/login', { email: 'a@example.test', password: 'x' });
    for (const headers of seen) {
      expect(Object.keys(headers).filter((k) => k.toLowerCase().startsWith('x-tk-'))).toEqual([]);
      expect(headers['X-Client']).toBe('web');
    }
  });
});

describe('platform metrics', () => {
  it('compares bounded property series without property-admin authentication and preserves URL state', async () => {
    window.sessionStorage.setItem(PLATFORM_TOKEN_KEY, 'tkp_1');
    const start = '2026-11-01T00:00:00Z';
    const data = { ...page([{ id: 'point_1', period: 'Current', seriesKey: 'total', startUtc: start, endUtc: '2026-11-01T01:00:00Z', value: 25, complete: true }]), unit: 'count', timeZone: 'UTC' };
    const { fetchImpl, calls } = platformFetch((call) => call.path === '/api/v1/platform/metrics/comparisons/enumerate' ? json(page([{ propertyId: 'app_example', name: 'Example North', value: 25, unit: 'count', aggregation: 'Sum', complete: true }])) : call.path === '/api/v1/platform/properties/app_example/metrics/query' ? json(data) : undefined);
    renderAt('/platform/health?range=custom&from=2026-11-01T00:00:00Z&to=2026-11-02T00:00:00Z&metric=requests.count&compare=false', fetchImpl);
    expect(await screen.findByRole('link', { name: 'Example North' })).toHaveAttribute('href', '/platform/properties/app_example');
    expect(screen.getByRole('heading', { name: 'Property trends' })).toBeInTheDocument();
    const request = calls.find((c) => c.path === '/api/v1/platform/metrics/comparisons/enumerate');
    expect(request.headers.Authorization).toBe('Bearer tkp_1');
    expect(request.headers['X-TK-Property']).toBeUndefined();
    expect(request.body.skip || 0).toBe(0);
    expect(request.body).toMatchObject({ maxResults: 10, includeSeries: false, query: { metric: 'requests.count', bucket: 'Hour', compare: false } });
  });
});
