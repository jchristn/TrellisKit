import { describe, expect, it } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import SettingsView from '../src/views/admin/SettingsView.jsx';
import strings from '../src/strings.js';
import { computeChanges, initialDrafts, validateField } from '../src/utils/settings.js';
import { createFetch, json, renderAdmin } from './helpers.jsx';

const FIELDS = [
  { key: 'requestHistory.retentionDays', section: 'Request history', label: 'Retention (days)', description: 'How long to keep requests.', type: 'Integer', value: 30, options: null, min: 1, max: 3650, applyMode: 'Live', overriddenByEnvironment: false, isSet: null },
  { key: 'requestHistory.enabled', section: 'Request history', label: 'Capture requests', description: 'Record API requests.', type: 'Boolean', value: true, options: null, min: null, max: null, applyMode: 'Live', overriddenByEnvironment: false, isSet: null },
  { key: 'database.provider', section: 'Database', label: 'Provider', description: 'Database engine.', type: 'Enum', value: 'Sqlite', options: ['Sqlite', 'Postgresql'], min: null, max: null, applyMode: 'Restart', overriddenByEnvironment: true, isSet: null },
  { key: 'database.connectionString', section: 'Database', label: 'Connection string', description: 'Connection string.', type: 'Secret', value: null, options: null, min: null, max: null, applyMode: 'Restart', overriddenByEnvironment: false, isSet: true },
  { key: 'logging.level', section: 'Logging', label: 'Level', description: 'Minimum level.', type: 'String', value: 'Info', options: null, min: null, max: null, applyMode: 'Live', overriddenByEnvironment: false, isSet: null }
];

function backend({ putResponse } = {}) {
  return createFetch((call) => {
    if (call.path === '/api/v1/settings/enumerate' && call.method === 'POST') return json({ objects: FIELDS, endOfResults: true });
    if (call.path === '/api/v1/settings' && call.method === 'PUT') {
      if (putResponse) return putResponse(call);
      const fields = FIELDS.map((f) => (f.key in call.body.values && f.type !== 'Secret' ? { ...f, value: call.body.values[f.key] } : f));
      return json({ fields });
    }
    return undefined;
  });
}

const puts = (calls) => calls.filter((c) => c.method === 'PUT');

describe('settings collection traversal', () => {
  it('loads continuation pages before rendering the editable settings', async () => {
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path !== '/api/v1/settings/enumerate') return undefined;
      const next = Boolean(call.body.continuationToken);
      return json({ success: true, objects: next ? FIELDS.slice(1) : FIELDS.slice(0, 1), endOfResults: next, continuationToken: next ? null : 'settings-next', totalRecords: FIELDS.length });
    });
    renderAdmin(<SettingsView />, { fetchImpl });
    expect(await screen.findByLabelText('Level')).toBeInTheDocument();
    expect(screen.getByLabelText('Retention (days)')).toBeInTheDocument();
    expect(calls.some((call) => call.body?.continuationToken === 'settings-next')).toBe(true);
  });
});

describe('settings helpers', () => {
  it('computes only changed keys and never sends blank secrets or overridden fields', () => {
    const drafts = initialDrafts(FIELDS);
    expect(drafts['database.connectionString']).toBe('');
    expect(computeChanges(FIELDS, drafts)).toEqual({});
    drafts['requestHistory.retentionDays'] = '60';
    drafts['database.provider'] = 'Postgresql';
    expect(computeChanges(FIELDS, drafts)).toEqual({ 'requestHistory.retentionDays': 60 });
    drafts['database.connectionString'] = 'Host=db';
    expect(computeChanges(FIELDS, drafts)).toEqual({ 'requestHistory.retentionDays': 60, 'database.connectionString': 'Host=db' });
  });

  it('validates integer range', () => {
    expect(validateField(FIELDS[0], '0')).toBe('Enter a value from 1 to 3650.');
    expect(validateField(FIELDS[0], '1.5')).toBe('Enter a whole number.');
    expect(validateField(FIELDS[0], '100')).toBeNull();
  });
});

describe('SettingsView', () => {
  it('renders fields with live, restart, overridden, and secret indicators', async () => {
    const { fetchImpl } = backend();
    renderAdmin(<SettingsView />, { fetchImpl });
    expect(await screen.findByLabelText('Retention (days)')).toHaveValue(30);
    const provider = screen.getByTestId('setting-database.provider');
    expect(within(provider).getByText('Restart required')).toBeInTheDocument();
    expect(within(provider).getByText('Set by environment')).toBeInTheDocument();
    expect(within(provider).getByRole('combobox')).toBeDisabled();
    const secret = screen.getByTestId('setting-database.connectionString');
    expect(within(secret).getByText('Set')).toBeInTheDocument();
    expect(within(secret).getByLabelText('Connection string')).toHaveValue('');
    expect(within(screen.getByTestId('setting-logging.level')).getByText('Live')).toBeInTheDocument();
  });

  it('sends only changed keys and omits an untouched secret', async () => {
    const { fetchImpl, calls } = backend();
    renderAdmin(<SettingsView />, { fetchImpl });
    const user = userEvent.setup();
    const retention = await screen.findByLabelText('Retention (days)');
    await user.clear(retention);
    await user.type(retention, '60');
    await user.click(screen.getByRole('switch'));
    await user.click(screen.getByRole('button', { name: 'Save changes' }));
    await waitFor(() => expect(puts(calls)).toHaveLength(1));
    expect(puts(calls)[0].body).toEqual({ values: { 'requestHistory.retentionDays': 60, 'requestHistory.enabled': false } });
    expect(await screen.findByText(/Settings saved/)).toBeInTheDocument();
  });

  it('sends a secret only when a new value is typed and shows the restart banner', async () => {
    const { fetchImpl, calls } = backend();
    renderAdmin(<SettingsView />, { fetchImpl });
    const user = userEvent.setup();
    await user.type(await screen.findByLabelText('Connection string'), 'Host=db;Password=x');
    await user.click(screen.getByRole('button', { name: 'Save changes' }));
    await waitFor(() => expect(puts(calls)).toHaveLength(1));
    expect(puts(calls)[0].body).toEqual({ values: { 'database.connectionString': 'Host=db;Password=x' } });
    expect(await screen.findByText('Some saved settings take effect only after a restart.')).toBeInTheDocument();
  });

  it('shows per-field validation errors from the server', async () => {
    const { fetchImpl } = backend({
      putResponse: () => json({ error: 'ValidationFailed', message: 'Invalid settings.', fieldErrors: { 'logging.level': 'Must be one of Debug, Info, Warn, Error.' } }, 400)
    });
    renderAdmin(<SettingsView />, { fetchImpl });
    const user = userEvent.setup();
    const level = await screen.findByLabelText('Level');
    await user.clear(level);
    await user.type(level, 'Loud');
    await user.click(screen.getByRole('button', { name: 'Save changes' }));
    const row = screen.getByTestId('setting-logging.level');
    expect(await within(row).findByText('Must be one of Debug, Info, Warn, Error.')).toBeInTheDocument();
    expect(screen.getByText('Some values were rejected. Fix the highlighted fields and save again.')).toBeInTheDocument();
  });

  it('blocks saving when an integer is out of range', async () => {
    const { fetchImpl, calls } = backend();
    renderAdmin(<SettingsView />, { fetchImpl });
    const user = userEvent.setup();
    const retention = await screen.findByLabelText('Retention (days)');
    await user.clear(retention);
    await user.type(retention, '5000');
    expect(screen.getByText('Enter a value from 1 to 3650.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Save changes' }));
    expect(puts(calls)).toHaveLength(0);
  });

  it('explains platform-managed settings instead of an error in MultiProperty (403), without a restart button', async () => {
    const { fetchImpl } = createFetch((call) => {
      // Refused as for a MultiProperty property administrator.
      if (call.path === '/api/v1/settings/enumerate') return json({ error: 'Forbidden', message: 'In MultiProperty mode deployment settings are managed by platform administrators at /api/v1/platform/settings.' }, 403);
      return undefined;
    });
    renderAdmin(<SettingsView />, { fetchImpl });
    const s = strings.admin.settings;
    expect(await screen.findByRole('heading', { name: s.platformManagedTitle })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: s.platformManagedIntegrations })).toHaveAttribute('href', '/admin/integrations');
    expect(screen.queryByRole('button', { name: 'Restart server' })).toBeNull();
    expect(screen.queryByText(new RegExp(s.loadError))).toBeNull();
  });

  it('asks for confirmation before restarting', async () => {
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/settings/enumerate') return json({ objects: FIELDS, endOfResults: true });
      if (call.path === '/api/v1/health') return json({ status: 'Healthy', version: '1.0.0', startedUtc: '2026-11-11T00:00:00Z', uptimeSeconds: 1, database: 'Sqlite' });
      if (call.path === '/api/v1/settings/restart') return json({ message: 'Restarting.' }, 202);
      return undefined;
    });
    renderAdmin(<SettingsView />, { fetchImpl });
    const user = userEvent.setup();
    await screen.findByLabelText('Retention (days)');
    await user.click(screen.getByRole('button', { name: 'Restart server' }));
    const dialog = await screen.findByRole('dialog');
    expect(calls.some((c) => c.path === '/api/v1/settings/restart')).toBe(false);
    await user.click(within(dialog).getByRole('button', { name: 'Restart server' }));
    await waitFor(() => expect(calls.some((c) => c.method === 'POST' && c.path === '/api/v1/settings/restart')).toBe(true));
    expect(await screen.findByText('Restarting the server. Waiting for it to come back...')).toBeInTheDocument();
  });
});
