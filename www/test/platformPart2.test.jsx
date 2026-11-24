import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { AppRoutes } from '../src/App.jsx';
import { ThemeProvider } from '../src/context/ThemeContext.jsx';
import { PropertyProvider } from '../src/context/PropertyContext.jsx';
import { PLATFORM_TOKEN_KEY } from '../src/utils/delegation.js';
import { buildPlatformRequestHistoryQuery, buildPropertyMetricsQuery, platformInviteLink } from '../src/utils/platformQuery.js';
import { metricsTotals } from '../src/views/platform/PropertyMetricsView.jsx';
import { buildIntegrationUpdate } from '../src/views/platform/PropertyIntegrationsPanel.jsx';
import { slugInUseMessage } from '../src/views/platform/PropertyCreateView.jsx';
import { createFetch, json, loadFixtureSite } from './helpers.jsx';
import { brandPalette, checkPaletteContrast } from '@trelliskit/shared';
import strings from '../src/strings.js';

const SITE = loadFixtureSite('dojo');
const ADMIN = { id: 'pad_1', email: 'ops@example.test', displayName: 'Ops', invitationPending: false, lastLoginUtc: null, createdUtc: '2026-11-01T00:00:00Z', labels: [], tags: {} };
const PID = 'app_mwd1j5o5_FlHVU4CoaxDMHyBFZoJ';

function record(overrides = {}) {
  return {
    id: PID,
    slug: 'second',
    name: 'Second Example',
    status: 'Enabled',
    domains: [
      { id: 'pdm_1', host: 'second.test', kind: 'Web', verifiedUtc: '2026-11-11T20:00:00Z', source: 'Platform', verification: null },
      { id: 'pdm_2', host: 'shop.second.test', kind: 'Alias', verifiedUtc: null, source: 'Property', verification: { recordName: '_trelliskit-verify.shop.second.test', recordValue: 'trelliskit-verify=tok123', lastCheckedUtc: '2026-11-11T21:00:00Z', error: 'No TXT record found.' } }
    ],
    apps: [],
    memberCount: 3,
    createdUtc: '2026-11-11T20:00:00Z',
    updatedUtc: '2026-11-11T20:01:00Z',
    enabledUtc: '2026-11-11T20:01:00Z',
    disabledUtc: null,
    disabledReason: null,
    deleteScheduledUtc: null,
    purgeStartedUtc: null,
    labels: [],
    tags: {},
    ...overrides
  };
}

function metrics(overrides = {}) {
  return { propertyId: PID, slug: 'second', name: 'Second Example', status: 'Enabled', members: 10, activeMembers: 4, requests24h: 120, errors24h: 0, storageBytes: 1048576, labels: [], tags: {}, ...overrides };
}

function page(objects, total = objects.length) {
  return { success: true, maxResults: 50, skip: 0, continuationToken: null, endOfResults: total <= objects.length, totalRecords: total, recordsRemaining: Math.max(0, total - objects.length), timestampUtc: '2026-11-11T00:00:00Z', objects };
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

function platformFetch(handler = () => undefined, { signedIn = true } = {}) {
  return createFetch((call) => {
    const custom = handler(call);
    if (custom) return custom;
    if (call.path === '/api/v1/health') return json({ status: 'Healthy', version: '1.0.0', startedUtc: '2026-11-11T00:00:00Z' });
    if (call.path === '/api/v1/platform/auth/me') {
      return signedIn && call.headers.Authorization === 'Bearer tkp_1' ? json(ADMIN) : json({ error: 'Unauthorized', message: 'no' }, 401);
    }
    return undefined;
  });
}

beforeEach(() => {
  window.sessionStorage.clear();
  window.localStorage.clear();
});
afterEach(() => vi.restoreAllMocks());

describe('platform part 2 helpers', () => {
  it('scopes request history to the platform by default and keeps a chosen property or unresolved', () => {
    expect(buildPlatformRequestHistoryQuery({}, { pageNumber: 1, pageSize: 25 }).filters).toEqual({ propertyId: 'platform' });
    const q = buildPlatformRequestHistoryQuery({ propertyId: PID, method: 'POST', statusCode: '500', pathContains: '/api', userId: 'usr_1', search: 'login' }, { pageNumber: 2, pageSize: 25, meta: { labels: ['x'], labelMatch: 'All', tags: {} } });
    expect(q.filters).toEqual({ propertyId: PID, method: 'POST', statusCode: '500', pathContains: '/api', userId: 'usr_1' });
    expect(q.search).toBe('login');
    expect(q.skip).toBe(25);
    expect(q.labels).toEqual(['x']);
    expect(buildPlatformRequestHistoryQuery({ propertyId: 'unresolved' }, {}).filters.propertyId).toBe('unresolved');
  });

  it('builds the metrics query with status and ordering, and sums totals', () => {
    const q = buildPropertyMetricsQuery({ status: 'Enabled', ordering: 'NameAscending', search: 'sec' }, { pageNumber: 1, pageSize: 50 });
    expect(q.filters).toEqual({ status: 'Enabled' });
    expect(q.ordering).toBe('NameAscending');
    expect(q.search).toBe('sec');
    expect(metricsTotals([metrics(), metrics({ members: 5, errors24h: 3, storageBytes: 1 })])).toEqual({ properties: 2, members: 15, activeMembers: 8, requests24h: 240, errors24h: 3, storageBytes: 1048577 });
  });

  it('sends secrets only when typed, changed values, and cleared keys', () => {
    const fields = [
      { key: 'email.provider', type: 'Enum', value: 'Auto', options: ['Auto', 'Smtp'] },
      { key: 'email.smtpPort', type: 'Integer', value: 587 },
      { key: 'email.smtpPassword', type: 'Secret', value: null, isSet: true },
      { key: 'email.mailgunApiKey', type: 'Secret', value: null, isSet: false },
      { key: 'email.smtpUseTls', type: 'Boolean', value: true }
    ];
    expect(buildIntegrationUpdate(fields, { 'email.provider': 'Auto', 'email.smtpPort': '587', 'email.smtpPassword': '', 'email.mailgunApiKey': '', 'email.smtpUseTls': true }, [])).toEqual({ values: {}, clear: [] });
    expect(buildIntegrationUpdate(fields, { 'email.provider': 'Smtp', 'email.smtpPort': '2525', 'email.smtpPassword': '', 'email.mailgunApiKey': 'key-1', 'email.smtpUseTls': false }, ['email.smtpPassword']))
      .toEqual({ values: { 'email.provider': 'Smtp', 'email.smtpPort': 2525, 'email.mailgunApiKey': 'key-1', 'email.smtpUseTls': false }, clear: ['email.smtpPassword'] });
  });

  it('recognizes SlugInUse by code or slug field error, and builds the invite link', () => {
    expect(slugInUseMessage({ code: 'SlugInUse', status: 409, body: { fieldErrors: { slug: 'In use.' } } }, true)).toMatch(/Another property already uses this slug/);
    expect(slugInUseMessage({ code: 'Conflict', status: 409, body: { fieldErrors: { slug: 'In use.' } } }, false)).toMatch(/slug from the document/);
    expect(slugInUseMessage({ code: 'DomainInUse', status: 409, body: {} }, true)).toBe('');
    expect(platformInviteLink('a b', 'https://platform.example.test')).toBe('https://platform.example.test/platform/accept-invite?token=a%20b');
  });
});

describe('platform part 2 views', () => {
  beforeEach(() => window.sessionStorage.setItem(PLATFORM_TOKEN_KEY, 'tkp_1'));

  it('property metrics: KPI totals, error highlighting, filters, and a link to the property', async () => {
    const rows = [metrics(), metrics({ propertyId: 'app_b', slug: 'third', name: 'Third', members: 2, activeMembers: 1, requests24h: 30, errors24h: 7, storageBytes: 0 })];
    const { fetchImpl, calls } = platformFetch((call) => (call.path === '/api/v1/platform/metrics/properties/enumerate' ? json(page(rows)) : undefined));
    renderAt('/platform/metrics?status=Enabled&labels=beta', fetchImpl);
    expect(await screen.findByRole('link', { name: 'Third' })).toHaveAttribute('href', '/platform/properties/app_b');
    expect(screen.getByRole('navigation', { name: 'Platform navigation' })).toHaveTextContent('Property metrics');
    const body = calls.find((c) => c.path === '/api/v1/platform/metrics/properties/enumerate').body;
    expect(body.filters).toEqual({ status: 'Enabled' });
    expect(body.labels).toEqual(['beta']);
    await waitFor(() => expect(screen.getByText('Total members').parentElement).toHaveTextContent('12'));
    expect(screen.getByText('Errors (24h)', { selector: '.kpi-label' }).parentElement).toHaveTextContent('7');
    expect(screen.getByText('Storage', { selector: '.kpi-label' }).parentElement).toHaveTextContent('1');
    const errorCell = within(screen.getByRole('table')).getByText('7', { selector: '.tone-danger' });
    expect(errorCell.closest('tr')).toHaveClass('row-failed');
  });

  it('request history: scope picker, filters, and an inspector that explains absent bodies', async () => {
    const entry = { id: 'req_1', userId: null, principalName: null, method: 'GET', path: '/api/v1/health', url: '/api/v1/health', statusCode: 503, durationMs: 1.5, sourceIp: '10.0.0.1', requestBodyBytes: 0, responseBodyBytes: 120, createdUtc: '2026-11-11T19:35:18.721Z', completedUtc: '2026-11-11T19:35:18.723Z', labels: [], tags: {} };
    const { fetchImpl, calls } = platformFetch((call) => {
      if (call.path === '/api/v1/platform/request-history/enumerate') return json(page([entry]));
      if (call.path === '/api/v1/platform/properties/enumerate') return json(page([record()]));
      return undefined;
    });
    renderAt('/platform/requests', fetchImpl);
    const user = userEvent.setup();
    expect(await screen.findByText('/api/v1/health')).toBeInTheDocument();
    const reqs = () => calls.filter((c) => c.path === '/api/v1/platform/request-history/enumerate');
    expect(reqs()[0].body.filters).toEqual({ propertyId: 'platform' });

    const scope = screen.getByLabelText('Scope');
    await within(scope).findByRole('option', { name: 'Second Example (second)' });
    await user.selectOptions(scope, PID);
    await waitFor(() => expect(reqs().pop().body.filters).toEqual({ propertyId: PID }));

    await user.type(screen.getByLabelText('Status code'), 'abc');
    await user.click(screen.getByRole('button', { name: 'Apply filters' }));
    expect(await screen.findByText('Status code must be a number from 100 to 599.')).toBeInTheDocument();
    await user.clear(screen.getByLabelText('Status code'));
    await user.type(screen.getByLabelText('Status code'), '503');
    await user.selectOptions(screen.getByLabelText('Method'), 'GET');
    await user.type(screen.getByLabelText('User id'), 'usr_9');
    await user.click(screen.getByRole('button', { name: 'Apply filters' }));
    await waitFor(() => expect(reqs().pop().body.filters).toEqual({ propertyId: PID, method: 'GET', statusCode: '503', userId: 'usr_9' }));

    await user.selectOptions(screen.getByLabelText('Scope'), 'unresolved');
    await waitFor(() => expect(reqs().pop().body.filters).toEqual({ propertyId: 'unresolved', method: 'GET', statusCode: '503', userId: 'usr_9' }));

    await user.click(await screen.findByText('/api/v1/health'));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText(/carries no headers or bodies/)).toBeInTheDocument();
    expect(within(dialog).getByText('10.0.0.1')).toBeInTheDocument();
  });

  it('autocompletes property labels from the platform discovery route', async () => {
    const { fetchImpl, calls } = platformFetch((call) => {
      if (call.path === '/api/v1/platform/properties/enumerate') return json(page([record()]));
      if (call.path === '/api/v1/platform/labels/enumerate') return json(page([{ label: 'beta', count: 2 }]));
      return undefined;
    });
    renderAt('/platform/properties', fetchImpl);
    const user = userEvent.setup();
    expect(await screen.findByRole('link', { name: 'Second Example' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Labels and tags' }));
    await user.type(screen.getByRole('combobox', { name: 'Labels' }), 'be');
    expect(await screen.findByRole('option', { name: 'beta' })).toBeInTheDocument();
    const call = calls.find((c) => c.path === '/api/v1/platform/labels/enumerate');
    expect(call.body).toMatchObject({ targetType: 'Property', prefix: 'be' });
    expect(call.headers.Authorization).toBe('Bearer tkp_1');
  });

  it('autocompletes platform admin labels with targetType PlatformAdmin', async () => {
    const { fetchImpl, calls } = platformFetch((call) => {
      if (call.path === '/api/v1/platform/admins/enumerate') return json(page([ADMIN]));
      if (call.path === '/api/v1/platform/labels/enumerate') return json(page([{ label: 'oncall', count: 1 }]));
      return undefined;
    });
    renderAt('/platform/admins', fetchImpl);
    const user = userEvent.setup();
    expect(await screen.findByText('ops@example.test')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Labels and tags' }));
    await user.type(screen.getByRole('combobox', { name: 'Labels' }), 'on');
    expect(await screen.findByRole('option', { name: 'oncall' })).toBeInTheDocument();
    expect(calls.find((c) => c.path === '/api/v1/platform/labels/enumerate').body).toMatchObject({ targetType: 'PlatformAdmin', prefix: 'on' });
  });

  it('shows SlugInUse inline on the slug field and never changes the slug', async () => {
    const { fetchImpl, calls } = platformFetch((call) => (call.method === 'POST' && call.path === '/api/v1/platform/properties'
      ? json({ error: 'SlugInUse', message: 'The slug is used by another property.', fieldErrors: { slug: 'The slug is used by another property.' } }, 409)
      : undefined));
    renderAt('/platform/properties/new', fetchImpl);
    const user = userEvent.setup();
    const area = await screen.findByLabelText('Or paste the document');
    await user.click(area);
    await user.paste('{"schemaVersion":2,"name":"Second Example"}');
    await user.type(screen.getByLabelText(/^Slug/), 'second');
    await user.click(screen.getByRole('button', { name: 'Create property' }));
    const message = await screen.findByText('Another property already uses this slug. Enter a different slug; it is never changed automatically.');
    expect(message).toHaveAttribute('id', 'property-slug-error');
    expect(screen.getByLabelText(/^Slug/)).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByLabelText(/^Slug/)).toHaveValue('second');
    expect(calls.filter((c) => c.method === 'POST' && c.path === '/api/v1/platform/properties')).toHaveLength(1);
    await user.type(screen.getByLabelText(/^Slug/), '-2');
    expect(screen.queryByText(/Another property already uses this slug/)).not.toBeInTheDocument();
  });

  it('invite delivered by email shows no token', async () => {
    const { fetchImpl } = platformFetch((call) => {
      if (call.path === '/api/v1/platform/admins/enumerate') return json(page([ADMIN]));
      if (call.path === '/api/v1/platform/admins' && call.method === 'POST') return json({ admin: { ...ADMIN, id: 'pad_2', email: 'new@example.test', invitationPending: true }, inviteToken: null, inviteDelivery: 'Email', inviteExpiresUtc: '2026-11-18T00:00:00Z' }, 201);
      return undefined;
    });
    renderAt('/platform/admins', fetchImpl);
    const user = userEvent.setup();
    await user.click(await screen.findByRole('button', { name: 'Invite admin' }));
    const dialog = await screen.findByRole('dialog');
    await user.type(within(dialog).getByLabelText('Email'), 'new@example.test');
    await user.type(within(dialog).getByLabelText('Display name'), 'New Admin');
    await user.click(within(dialog).getByRole('button', { name: 'Create invitation' }));
    expect(await within(dialog).findByText('Invitation emailed to new@example.test.')).toBeInTheDocument();
    expect(within(dialog).queryByText('Invitation token')).not.toBeInTheDocument();
  });

  it('invite with Log delivery shows the one-time token, the no-provider note, and the accept link', async () => {
    const { fetchImpl } = platformFetch((call) => {
      if (call.path === '/api/v1/platform/admins/enumerate') return json(page([ADMIN]));
      if (call.path === '/api/v1/platform/admins' && call.method === 'POST') return json({ admin: { ...ADMIN, id: 'pad_2', email: 'new@example.test', invitationPending: true }, inviteToken: 'invite-xyz', inviteDelivery: 'Log', inviteExpiresUtc: '2026-11-18T00:00:00Z' }, 201);
      return undefined;
    });
    renderAt('/platform/admins', fetchImpl);
    const user = userEvent.setup();
    await user.click(await screen.findByRole('button', { name: 'Invite admin' }));
    const dialog = await screen.findByRole('dialog');
    await user.type(within(dialog).getByLabelText('Email'), 'new@example.test');
    await user.type(within(dialog).getByLabelText('Display name'), 'New Admin');
    await user.click(within(dialog).getByRole('button', { name: 'Create invitation' }));
    expect(await within(dialog).findByText('invite-xyz')).toBeInTheDocument();
    expect(within(dialog).getByText(/Shown once; no email provider is configured for the deployment/)).toBeInTheDocument();
    expect(within(dialog).getByText(window.location.origin + '/platform/accept-invite?token=invite-xyz')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Copy Invitation token' })).toBeInTheDocument();
  });

  it('the emailed accept-invite link opens the invitation form with the token', async () => {
    window.sessionStorage.clear();
    const { fetchImpl } = platformFetch(() => undefined, { signedIn: false });
    renderAt('/platform/accept-invite?token=invite-abc', fetchImpl);
    expect(await screen.findByLabelText('Invitation token')).toHaveValue('invite-abc');
  });

  it('shows each domain source and verification state with the TXT record to copy', async () => {
    const { fetchImpl } = platformFetch((call) => (call.path === '/api/v1/platform/properties/' + PID ? json(record()) : undefined));
    renderAt('/platform/properties/' + PID + '?tab=domains', fetchImpl);
    const panel = (await screen.findByRole('heading', { name: 'Domain verification' })).closest('section');
    expect(within(panel).getByText('Platform')).toBeInTheDocument();
    expect(within(panel).getByText('Property admin')).toBeInTheDocument();
    expect(within(panel).getByText(/^Verified /)).toBeInTheDocument();
    expect(within(panel).getByText('Awaiting TXT record')).toBeInTheDocument();
    expect(within(panel).getByText('_trelliskit-verify.shop.second.test')).toBeInTheDocument();
    expect(within(panel).getByText('trelliskit-verify=tok123')).toBeInTheDocument();
    expect(within(panel).getByRole('button', { name: 'Copy Value shop.second.test' })).toBeInTheDocument();
    expect(within(panel).getByText('Last check: No TXT record found.')).toBeInTheDocument();
  });

  it('integration settings: secrets are write-only, replaced or cleared', async () => {
    let settings = {
      propertyId: PID,
      fields: [
        { key: 'email.fromName', section: 'Email', label: 'From name', description: 'Sender name', type: 'String', value: 'Ops', options: null, applyMode: 'Live', isSet: true, scope: 'Property', inherited: false },
        { key: 'email.smtpPassword', section: 'Email', label: 'SMTP password', description: 'Secret', type: 'Secret', value: null, options: null, applyMode: 'Live', isSet: true, scope: 'Property', inherited: false },
        { key: 'email.mailgunApiKey', section: 'Email', label: 'Mailgun API key', description: 'Secret', type: 'Secret', value: null, options: null, applyMode: 'Live', isSet: false, scope: 'Property', inherited: false }
      ]
    };
    const { fetchImpl, calls } = platformFetch((call) => {
      if (call.path === '/api/v1/platform/properties/' + PID) return json(record());
      if (call.path === '/api/v1/platform/properties/' + PID + '/settings/enumerate' && call.method === 'POST') return json({ ...page(settings.fields), propertyId: settings.propertyId });
      if (call.path === '/api/v1/platform/properties/' + PID + '/settings' && call.method === 'PUT') {
        settings = { ...settings, fields: settings.fields.map((f) => (f.key === 'email.smtpPassword' ? { ...f, isSet: false } : f.key === 'email.mailgunApiKey' ? { ...f, isSet: true } : f)) };
        return json(settings);
      }
      return undefined;
    });
    renderAt('/platform/properties/' + PID + '?tab=integrations', fetchImpl);
    const user = userEvent.setup();
    const password = await screen.findByLabelText('SMTP password');
    expect(password).toHaveValue('');
    expect(password).toHaveAttribute('type', 'password');
    expect(within(screen.getByTestId('integration-email.smtpPassword')).getByText('Set')).toBeInTheDocument();
    expect(within(screen.getByTestId('integration-email.mailgunApiKey')).getByText('Not set')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Clear SMTP password' }));
    await user.type(screen.getByLabelText('Mailgun API key'), 'key-new');
    await user.click(screen.getByRole('button', { name: 'Save integration settings' }));
    await waitFor(() => expect(calls.some((c) => c.method === 'PUT')).toBe(true));
    expect(calls.find((c) => c.method === 'PUT').body).toEqual({ values: { 'email.mailgunApiKey': 'key-new' }, clear: ['email.smtpPassword'] });
    await waitFor(() => expect(within(screen.getByTestId('integration-email.mailgunApiKey')).getByText('Set')).toBeInTheDocument());
    expect(screen.getByLabelText('Mailgun API key')).toHaveValue('');
  });
  it('branding and AI tabs edit the property through the platform routes', async () => {
    const palette = brandPalette(loadFixtureSite('dojo').property);
    const view = (version, light = palette.light) => ({
      propertyId: PID, version, source: 'Edit', note: null, light, dark: palette.dark, keys: Object.keys(light), backgrounds: { light: '#FFFFFF', dark: '#0E141D' },
      contrast: checkPaletteContrast({ light, dark: palette.dark }), overrides: [], seed: { version: null, light: palette.light, dark: palette.dark }, previous: null,
      syncsFromFolder: false, createdByUserId: null, createdUtc: '2026-11-12T00:00:00Z'
    });
    const { fetchImpl, calls } = platformFetch((call) => {
      if (call.path === '/api/v1/platform/properties/' + PID) return json(record());
      if (call.path === '/api/v1/platform/properties/' + PID + '/branding' && call.method === 'GET') return json(view(1));
      if (call.path === '/api/v1/platform/properties/' + PID + '/branding' && call.method === 'PUT') return json(view(2, { ...palette.light, ...call.body.light }));
      if (call.path === '/api/v1/platform/properties/' + PID + '/ai') return json({ propertyId: PID, enabled: false, deploymentEnabled: true, available: false, seedEnabled: false, syncsFromFolder: false, revision: 1, updatedByUserId: null, updatedUtc: '2026-11-12T00:00:00Z' });
      return undefined;
    });
    renderAt('/platform/properties/' + PID + '?tab=branding', fetchImpl);
    const user = userEvent.setup();
    const b = strings.admin.branding;
    const input = await screen.findByLabelText(b.hexLabel('primary', 'light'));
    await user.clear(input);
    await user.type(input, '#0B3D91');
    await user.click(screen.getByRole('button', { name: b.save }));
    await waitFor(() => expect(calls.some((c) => c.method === 'PUT')).toBe(true));
    expect(calls.find((c) => c.method === 'PUT').body).toEqual({ light: { primary: '#0B3D91' } });
    await user.click(screen.getByRole('tab', { name: strings.platform.detail.tabs.ai }));
    expect(await screen.findByRole('switch')).not.toBeChecked();
  });
});
