import { describe, expect, it } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import EmailView, { buildEmailMessagesQuery, parseRecipients } from '../src/views/admin/EmailView.jsx';
import { templateBody, validateTemplate } from '../src/views/admin/EmailTemplateEditor.jsx';
import IntegrationsView, { buildSettingsUpdate, initialDrafts } from '../src/views/admin/IntegrationsView.jsx';
import MemberDetailDrawer from '../src/views/admin/MemberDetailDrawer.jsx';
import { createFetch, json, makeMe, renderAdmin } from './helpers.jsx';

const enumeration = (objects) => ({ success: true, maxResults: 50, skip: 0, continuationToken: null, endOfResults: true, totalRecords: objects.length, recordsRemaining: 0, objects });

const WELCOME = {
  key: 'welcome', category: 'Essential', recipient: 'Member', description: 'Sent after sign-up.', tokens: ['name', 'community'],
  defaultSubject: 'Welcome to {community}', defaultPreheader: 'Glad you are here', defaultHeading: 'Welcome, {name}', defaultBody: 'Thanks for joining.', override: null
};
const DIGEST = {
  ...WELCOME, key: 'forum_digest', category: 'Optional', recipient: 'Member', description: "Digest of Following or Forums notifications (members who turned the category's email on).",
  tokens: ['name', 'memberName', 'digestTitle', 'count', 'items', 'link', 'unsubscribeLink'], defaultSubject: '{count} new in {digestTitle} on {name}'
};
const STAFF = { ...WELCOME, key: 'staff_new_member', category: 'Staff', recipient: 'Staff', description: 'A member joined.', override: { template: 'staff_new_member', subject: 'New: {name}', preheader: null, heading: null, body: null, updatedUtc: '2026-11-10T00:00:00Z' } };

function emailFetch(handler = () => undefined) {
  return createFetch((call) => {
    const custom = handler(call);
    if (custom) return custom;
    if (call.path === '/api/v1/settings/enumerate') return json({ objects: [], endOfResults: true });
    if (call.path === '/api/v1/admin/email/templates/enumerate') return json(enumeration([WELCOME, DIGEST, STAFF]));
    if (call.path.endsWith('/enumerate')) return json(enumeration([]));
    return undefined;
  });
}

const renderEmail = (fetchImpl, path = '/admin/email') => renderAdmin(<EmailView />, { fetchImpl, path, routePath: '/admin/email' });

describe('email helpers', () => {
  it('validates template parts and builds the override body', () => {
    expect(validateTemplate({ subject: 'Hi {name} {bogus}', preheader: '', heading: 'a\nb', body: '' }, ['name'])).toEqual({
      subject: 'Unknown tokens: {bogus}. Use only the tokens listed above.',
      heading: 'Use one line.'
    });
    expect(templateBody({ subject: ' Hi ', preheader: '', heading: '', body: 'One\r\n\r\nTwo' })).toEqual({ subject: 'Hi', preheader: null, heading: null, body: 'One\n\nTwo' });
    expect(parseRecipients('a@example.test, b@example.test\nc@example.test')).toEqual(['a@example.test', 'b@example.test', 'c@example.test']);
    expect(buildEmailMessagesQuery({ status: 'Failed', order: 'UpdatedDescending' }, { pageNumber: 2, pageSize: 25 })).toMatchObject({ ordering: 'UpdatedDescending', skip: 25, filters: { status: 'Failed' } });
  });
});

describe('email admin', () => {
  it('edits a template with token chips, client token checks, preview, save, and test send', async () => {
    const { fetchImpl, calls } = emailFetch((call) => {
      if (call.path === '/api/v1/admin/email/templates/welcome/preview') return json({ subject: 'Hello Sam', preheader: 'Glad you are here', html: '<html><body><p>Rendered</p></body></html>', text: 'Rendered text' });
      if (call.path === '/api/v1/admin/email/templates/welcome' && call.method === 'PUT') return json({ ...WELCOME, override: { subject: call.body.subject } });
      if (call.path === '/api/v1/admin/email/test') return json({ ok: true, message: 'Sent to admin@example.test.' });
      return undefined;
    });
    renderEmail(fetchImpl);
    const user = userEvent.setup();
    expect(await screen.findByText('staff_new_member')).toBeInTheDocument();
    expect(screen.getByText('Customized')).toBeInTheDocument();
    await user.click(screen.getByText('welcome'));
    const dialog = await screen.findByRole('dialog');
    const subject = within(dialog).getByLabelText('Subject');
    expect(subject).toHaveAttribute('placeholder', 'Welcome to {community}');
    await user.type(subject, 'Hello {{bad}');
    expect(within(dialog).getByText('Unknown tokens: {bad}. Use only the tokens listed above.')).toBeInTheDocument();
    await user.clear(subject);
    await user.type(subject, 'Hello ');
    await user.click(within(dialog).getByRole('button', { name: '{name}' }));
    await waitFor(() => expect(subject).toHaveValue('Hello {name}'));

    await user.click(within(dialog).getByRole('button', { name: 'Preview' }));
    const frame = await within(dialog).findByTitle('Rendered email welcome');
    expect(frame).toHaveAttribute('sandbox', '');
    expect(frame.getAttribute('srcdoc')).toContain('Rendered');
    expect(calls.find((c) => c.path.endsWith('/preview')).body).toEqual({ subject: 'Hello {name}', preheader: null, heading: null, body: null });
    await user.click(within(dialog).getByRole('tab', { name: 'Plain text' }));
    expect(within(dialog).getByText('Rendered text')).toBeInTheDocument();

    await user.type(within(dialog).getByLabelText('Send to'), 'qa@example.test');
    await user.click(within(dialog).getByRole('button', { name: 'Send test' }));
    expect(await within(dialog).findByText('Sent to admin@example.test.')).toBeInTheDocument();
    expect(calls.find((c) => c.path === '/api/v1/admin/email/test').body).toEqual({ template: 'welcome', to: 'qa@example.test' });

    await user.click(within(dialog).getByRole('button', { name: 'Save template' }));
    expect(await screen.findByText('Template saved.')).toBeInTheDocument();
    expect(calls.find((c) => c.method === 'PUT').body).toEqual({ subject: 'Hello {name}', preheader: null, heading: null, body: null });
  });

  it('sends the catalog only when selected and shows every template outcome', async () => {
    const { fetchImpl, calls } = emailFetch((call) => call.path === '/api/v1/admin/email/test/catalog' ? json({ sent: 1, failed: 1, results: [{ template: 'welcome', ok: true, message: 'Sent.' }, { template: 'forum_digest', ok: false, message: 'Suppressed.' }] }) : undefined);
    renderEmail(fetchImpl);
    const user = userEvent.setup();
    await screen.findByText('staff_new_member');
    await user.click(screen.getByRole('button', { name: 'Send test email' }));
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByRole('button', { name: 'Send test' })).toBeInTheDocument();
    await user.selectOptions(within(dialog).getByLabelText('Template'), '__catalog');
    expect(within(dialog).getByText(/each of the 3 templates/)).toBeInTheDocument();
    await user.type(within(dialog).getByLabelText('Send to'), 'qa@example.test');
    expect(calls.some((call) => call.path.endsWith('/test/catalog'))).toBe(false);
    await user.click(within(dialog).getByRole('button', { name: 'Send catalog test' }));
    expect(await within(dialog).findByText('1 sent; 1 not sent.')).toBeInTheDocument();
    const table = within(dialog).getByRole('table', { name: 'Catalog test results' });
    expect(within(table).getByText('Suppressed.')).toBeInTheDocument();
    expect(calls.find((call) => call.path.endsWith('/test/catalog')).body).toEqual({ to: 'qa@example.test' });
    expect(calls.some((call) => call.path === '/api/v1/admin/email/test')).toBe(false);
  });

  it('lists the forum digest template with its Optional category and tokens', async () => {
    const { fetchImpl } = emailFetch();
    renderEmail(fetchImpl);
    const user = userEvent.setup();
    const cell = await screen.findByText('forum_digest');
    const row = cell.closest('tr');
    expect(within(row).getByText(/Digest of Following or Forums notifications/)).toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText('Category'), 'Optional');
    expect(screen.getByText('forum_digest')).toBeInTheDocument();
    expect(screen.queryByText('welcome')).toBeNull();
    await user.click(screen.getByText('forum_digest'));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByRole('button', { name: '{digestTitle}' })).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: '{unsubscribeLink}' })).toBeInTheDocument();
  });

  it('resets an overridden template after confirmation', async () => {
    const { fetchImpl, calls } = emailFetch((call) => (call.path === '/api/v1/admin/email/templates/staff_new_member' && call.method === 'DELETE' ? json(null, 204) : undefined));
    renderEmail(fetchImpl);
    const user = userEvent.setup();
    await user.click(await screen.findByText('staff_new_member'));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByLabelText('Subject')).toHaveValue('New: {name}');
    await user.click(within(dialog).getByRole('button', { name: 'Reset to default' }));
    const confirm = screen.getAllByRole('dialog').at(-1);
    expect(within(confirm).getByText('Reset staff_new_member to the default?')).toBeInTheDocument();
    expect(calls.some((c) => c.method === 'DELETE')).toBe(false);
    await user.click(within(confirm).getByRole('button', { name: 'Reset to default' }));
    expect(await screen.findByText('Template reset to the default.')).toBeInTheDocument();
  });

  it('switches staff events on and off and validates recipients', async () => {
    const events = [{ template: 'staff_new_member', description: 'A member joined.', defaultRecipients: 'Staff', enabled: true, recipients: [] }];
    const { fetchImpl, calls } = emailFetch((call) => {
      if (call.path === '/api/v1/admin/email/staff-events/enumerate') return json(enumeration(events));
      if (call.path === '/api/v1/admin/email/staff-events/staff_new_member') return json({ ...events[0], ...call.body });
      return undefined;
    });
    renderEmail(fetchImpl, '/admin/email?tab=staffEvents');
    const user = userEvent.setup();
    expect(await screen.findByText('Using the defaults (administrators).')).toBeInTheDocument();
    await user.click(screen.getByRole('switch', { name: 'Send staff_new_member' }));
    await waitFor(() => expect(calls.find((c) => c.method === 'PUT').body).toEqual({ enabled: false }));
    const box = screen.getByLabelText('Recipients of staff_new_member');
    await user.type(box, 'ops@example.test, nope');
    await user.click(screen.getByRole('button', { name: 'Save recipients' }));
    expect(await screen.findByText('Not valid email addresses: nope.')).toBeInTheDocument();
    await user.clear(box);
    await user.type(box, 'ops@example.test');
    await user.click(screen.getByRole('button', { name: 'Save recipients' }));
    await waitFor(() => expect(calls.filter((c) => c.method === 'PUT').at(-1).body).toEqual({ recipients: ['ops@example.test'] }));
  });

  it('filters the message log and opens a message', async () => {
    const message = { id: 'eml_1', template: 'welcome', category: 'Essential', recipient: 'sam@example.test', userId: 'usr_1', subject: 'Welcome Sam', status: 'Failed', provider: 'Smtp', providerMessageId: null, attempts: 3, nextAttemptUtc: null, lastError: 'Connection refused', createdUtc: '2026-11-11T00:00:00Z', updatedUtc: '2026-11-11T00:01:00Z', labels: [], tags: {} };
    const { fetchImpl, calls } = emailFetch((call) => {
      if (call.path === '/api/v1/admin/email/messages/enumerate') return json(enumeration([message]));
      if (call.path === '/api/v1/admin/email/messages/eml_1') return json(message);
      return undefined;
    });
    renderEmail(fetchImpl, '/admin/email?tab=messages');
    const user = userEvent.setup();
    expect(await screen.findByText('Welcome Sam')).toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText('Status'), 'Failed');
    await user.selectOptions(screen.getByLabelText('Template'), 'welcome');
    await user.selectOptions(screen.getByLabelText('Order'), 'CreatedAscending');
    await user.click(screen.getByRole('button', { name: 'Apply filters' }));
    await waitFor(() => {
      const last = calls.filter((c) => c.path === '/api/v1/admin/email/messages/enumerate').at(-1);
      expect(last.body).toEqual({ maxResults: 50, ordering: 'CreatedAscending', filters: { status: 'Failed', template: 'welcome' } });
    });
    await user.click(screen.getByText('Welcome Sam'));
    const dialog = await screen.findByRole('dialog');
    expect(await within(dialog).findByText('Connection refused')).toBeInTheDocument();
  });

  it('adds and removes suppressions', async () => {
    const row = { id: 'esp_1', email: 'gone@example.test', reason: 'Bounce', source: 'mailgun', note: null, createdUtc: '2026-11-11T00:00:00Z', labels: [], tags: {} };
    const { fetchImpl, calls } = emailFetch((call) => {
      if (call.path === '/api/v1/admin/email/suppressions/enumerate') return json(enumeration([row]));
      if (call.path === '/api/v1/admin/email/suppressions' && call.method === 'POST') return json({ ...row, id: 'esp_2', email: call.body.email, reason: 'Manual' }, 201);
      if (call.path === '/api/v1/admin/email/suppressions/esp_1' && call.method === 'DELETE') return json(null, 204);
      return undefined;
    });
    renderEmail(fetchImpl, '/admin/email?tab=suppressions');
    const user = userEvent.setup();
    expect(await screen.findByText('gone@example.test')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Add suppression' }));
    const dialog = await screen.findByRole('dialog');
    await user.type(within(dialog).getByLabelText('Email'), 'bad');
    await user.click(within(dialog).getByRole('button', { name: 'Add suppression' }));
    expect(within(dialog).getByText('Enter a valid email address.')).toBeInTheDocument();
    await user.clear(within(dialog).getByLabelText('Email'));
    await user.type(within(dialog).getByLabelText('Email'), 'stop@example.test');
    await user.type(within(dialog).getByLabelText('Note'), 'Asked by phone');
    await user.click(within(dialog).getByRole('button', { name: 'Add suppression' }));
    await waitFor(() => expect(calls.find((c) => c.method === 'POST' && c.path === '/api/v1/admin/email/suppressions').body).toEqual({ email: 'stop@example.test', note: 'Asked by phone', labels: [], tags: {} }));

    await user.click(screen.getByRole('button', { name: 'Actions for gone@example.test' }));
    await user.click(screen.getByRole('menuitem', { name: 'Remove' }));
    const confirm = await screen.findByRole('dialog');
    await user.click(within(confirm).getByRole('button', { name: 'Remove' }));
    await waitFor(() => expect(calls.some((c) => c.method === 'DELETE' && c.path === '/api/v1/admin/email/suppressions/esp_1')).toBe(true));
  });
});

const SETTINGS = {
  propertyId: 'app_1',
  fields: [
    { key: 'email.provider', section: 'Email', label: 'Provider', description: 'Which provider sends.', type: 'Enum', value: 'Auto', options: ['Auto', 'Mailgun', 'Smtp', 'Log'], applyMode: 'Live', isSet: null, scope: 'Property', inherited: false },
    { key: 'email.fromName', section: 'Email', label: 'From name', description: '', type: 'String', value: '', applyMode: 'Live', isSet: null, scope: 'Property', inherited: true },
    { key: 'email.mailgunApiKey', section: 'Email', label: 'Mailgun API key', description: '', type: 'Secret', value: null, applyMode: 'Live', isSet: true, scope: 'Property', inherited: false },
    { key: 'email.smtpPassword', section: 'Email', label: 'SMTP password', description: '', type: 'Secret', value: null, applyMode: 'Live', isSet: false, scope: 'Property', inherited: false },
    { key: 'email.smtpPort', section: 'Email', label: 'SMTP port', description: '', type: 'Integer', value: 587, min: 1, max: 65535, applyMode: 'Live', isSet: null, scope: 'Property', inherited: false }
  ]
};

describe('property integrations', () => {
  it('builds updates from changed keys only, with secrets write-only', () => {
    const drafts = initialDrafts(SETTINGS.fields);
    expect(drafts['email.mailgunApiKey']).toEqual({ value: '', replace: false, clear: false });
    expect(buildSettingsUpdate(SETTINGS.fields, drafts)).toEqual({ values: {}, clear: [] });
    const changed = { ...drafts, 'email.provider': { ...drafts['email.provider'], value: 'Mailgun' }, 'email.mailgunApiKey': { value: '', replace: false, clear: true }, 'email.smtpPassword': { value: 'pw', replace: true, clear: false }, 'email.smtpPort': { value: '', replace: false, clear: false } };
    expect(buildSettingsUpdate(SETTINGS.fields, changed)).toEqual({ values: { 'email.provider': 'Mailgun', 'email.smtpPassword': 'pw', 'email.smtpPort': null }, clear: ['email.mailgunApiKey'] });
  });

  it('replaces and clears secrets, shows inherited values, maps field errors, and tests email', async () => {
    let puts = 0;
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/settings/enumerate') return json({ objects: [], endOfResults: true });
      if (call.path === '/api/v1/admin/property/settings/enumerate' && call.method === 'POST') return json({ ...enumeration(SETTINGS.fields), propertyId: SETTINGS.propertyId });
      if (call.path === '/api/v1/admin/property/settings' && call.method === 'PUT') {
        puts += 1;
        if (puts === 1) return json({ error: 'ValidationFailed', message: 'Invalid.', fieldErrors: { 'values.email.smtpPassword': 'Too short.' } }, 400);
        return json(SETTINGS);
      }
      if (call.path === '/api/v1/admin/property/integrations/email/test') return json({ ok: false, message: 'Mailgun rejected the key.' });
      return undefined;
    });
    renderAdmin(<IntegrationsView />, { fetchImpl, path: '/admin/integrations', routePath: '/admin/integrations' });
    const user = userEvent.setup();
    const secretRow = await screen.findByTestId('psetting-email.mailgunApiKey');
    expect(within(secretRow).getByText('Set')).toBeInTheDocument();
    expect(within(screen.getByTestId('psetting-email.fromName')).getByText('Deployment value')).toBeInTheDocument();
    expect(screen.queryAllByDisplayValue(/./).some((el) => el.type === 'password')).toBe(false);
    await user.click(within(secretRow).getByRole('button', { name: 'Clear' }));
    expect(within(secretRow).getByText('Will be cleared on save')).toBeInTheDocument();
    const smtp = screen.getByTestId('psetting-email.smtpPassword');
    expect(within(smtp).getByText('Not set')).toBeInTheDocument();
    await user.click(within(smtp).getByRole('button', { name: 'Set value' }));
    await user.type(within(smtp).getByLabelText('SMTP password'), 'abc');
    await user.click(screen.getByRole('button', { name: 'Save settings' }));
    expect(await within(smtp).findByText('Too short.')).toBeInTheDocument();
    expect(calls.find((c) => c.method === 'PUT').body).toEqual({ values: { 'email.smtpPassword': 'abc' }, clear: ['email.mailgunApiKey'] });

    await user.type(screen.getByLabelText('Send to'), 'qa@example.test');
    await user.click(screen.getByRole('button', { name: 'Send test email' }));
    expect(await screen.findByText('Mailgun rejected the key.')).toBeInTheDocument();
    expect(calls.find((c) => c.path.endsWith('/integrations/email/test')).body).toEqual({ to: 'qa@example.test' });
  });
});

describe('admin member profile', () => {
  const fields = [
    { id: 'pfd_dn', key: 'displayName', builtIn: true, label: 'Name', type: 'Text', options: [], subFields: [], validation: { required: true }, collectAt: 'SignUp', editableBy: 'Member', visibility: 'Public', showOnProfile: true, showOnCard: true, enabled: true, profileOrder: 0 },
    { id: 'pfd_club', key: 'club', builtIn: false, label: 'Club', type: 'Text', options: [], subFields: [], validation: { required: false, maxLength: 100 }, collectAt: 'Profile', editableBy: 'Member', visibility: 'Public', showOnProfile: true, showOnCard: false, enabled: true, profileOrder: 20 },
    { id: 'pfd_note', key: 'staffNote', builtIn: false, label: 'Staff note', type: 'Text', options: [], subFields: [], validation: { required: false }, collectAt: 'AdminOnly', editableBy: 'Staff', visibility: 'Staff', showOnProfile: true, showOnCard: false, enabled: true, profileOrder: 30 }
  ];
  const member = {
    ...makeMe({ id: 'usr_2', displayName: 'Ana Silva', email: 'ana@example.com' }),
    profile: { club: 'North', staffNote: null },
    isBanned: false, bannedReason: null, lastSeenUtc: null,
    subscriptions: { objects: [], totalRecords: 0 }, redemptions: { objects: [], totalRecords: 0 }, comps: { objects: [], totalRecords: 0 }, sessions: { objects: [], totalRecords: 0 }
  };

  it('shows every profile field and saves a staff edit with only changed keys', async () => {
    let saves = 0;
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/settings/enumerate') return json({ objects: [], endOfResults: true });
      if (call.path === '/api/v1/admin/profile-fields/enumerate') return json(enumeration(fields));
      if (call.path === '/api/v1/admin/members/usr_2' && call.method === 'GET') return json(member);
      if (call.path === '/api/v1/admin/members/usr_2/profile') {
        saves += 1;
        if (saves === 1) return json({ error: 'ValidationFailed', message: 'Invalid.', fieldErrors: { 'profile.staffNote': 'Server rejected this.' } }, 400);
        return json({ ...member, profile: { ...member.profile, ...call.body.profile } });
      }
      if (call.path.endsWith('/enumerate')) return json(enumeration([]));
      return undefined;
    });
    renderAdmin(<MemberDetailDrawer id="usr_2" onClose={() => {}} onChanged={() => {}} />, { fetchImpl });
    const drawer = await screen.findByRole('dialog');
    expect(await within(drawer).findByText('North')).toBeInTheDocument();
    expect(await within(drawer).findByText('Staff note')).toBeInTheDocument();
    const user = userEvent.setup();
    await user.click(within(drawer).getByRole('button', { name: 'Edit profile' }));
    const dialog = screen.getAllByRole('dialog').at(-1);
    await user.type(within(dialog).getByLabelText(/Staff note/), 'Founding member');
    await user.click(within(dialog).getByRole('button', { name: 'Save profile' }));
    expect(await within(dialog).findByText('Server rejected this.')).toBeInTheDocument();
    expect(calls.find((c) => c.path === '/api/v1/admin/members/usr_2/profile').body).toEqual({ profile: { staffNote: 'Founding member' } });
    await user.click(within(dialog).getByRole('button', { name: 'Save profile' }));
    expect(await screen.findByText('Profile saved.')).toBeInTheDocument();
  });
});
