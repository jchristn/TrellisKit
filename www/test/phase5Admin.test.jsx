import { describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createBroadcastsFake, createIdeasFake, createSupportFake } from '@trelliskit/shared/testing';
import SupportInboxView, { buildTicketQuery } from '../src/views/admin/SupportInboxView.jsx';
import IdeasAdminView from '../src/views/admin/IdeasAdminView.jsx';
import BroadcastsView from '../src/views/admin/BroadcastsView.jsx';
import { broadcastBody, segmentFilters, validateBroadcast } from '../src/views/admin/BroadcastEditor.jsx';
import MessageSnapshotPanel, { snapshotMessages } from '../src/views/admin/MessageSnapshotPanel.jsx';
import { queueFilterOptions } from '../src/views/admin/ModerationQueue.jsx';
import ModerationView from '../src/views/admin/ModerationView.jsx';
import { groupedAdminRoutes } from '../src/views/admin/adminRoutes.js';
import { validateTicketType } from '../src/views/admin/TicketTypesTab.jsx';
import { browser } from '../src/utils/browser.js';
import { createFetch, json, makeMe, renderAdmin, renderSite } from './helpers.jsx';
import { fakeAdminFetch, fakeMemberFetch, siteWith } from './phase5Helpers.jsx';
import { render } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import appStrings from '../src/strings.js';

const ids = (groups) => groups.flatMap((g) => g.routes.map((r) => r.id));

function seedTickets(support) {
  const create = (body) => support.handle({ method: 'POST', path: '/api/v1/tickets', body, headers: { 'x-client': 'web' } }).body;
  const a = create({ type: 'Billing', title: 'Charged twice', body: 'I was charged twice this month.', includeDiagnostics: true });
  const b = create({ type: 'Enhancement', title: 'Add dark mode', body: 'Please add a dark theme option.' });
  return { a, b };
}

describe('admin navigation and roles', () => {
  it('shows each staff role only its sections and hides sections whose feature is off', () => {
    const property = { features: {} };
    expect(ids(groupedAdminRoutes(property))).toEqual(expect.arrayContaining(['support', 'ideas', 'broadcasts']));
    expect(ids(groupedAdminRoutes(property, { moderatorOnly: true, moderator: false, supportStaff: true }))).toEqual(['home', 'support', 'ideas']);
    expect(ids(groupedAdminRoutes(property, { moderatorOnly: true, moderator: false, communicator: true }))).toEqual(['home', 'broadcasts']);
    expect(ids(groupedAdminRoutes(property, { moderatorOnly: true }))).toEqual(['home', 'moderation']);
    expect(ids(groupedAdminRoutes({ features: { support: false, ideas: false, broadcasts: false } }))).not.toEqual(expect.arrayContaining(['support']));
    expect(ids(groupedAdminRoutes({ features: { broadcasts: false } }))).not.toContain('broadcasts');
  });

  it('lets a SupportAgent sign in to the console and see the support sections only', async () => {
    window.localStorage.setItem('tk.hasSession', '1');
    const support = createSupportFake();
    const me = makeMe({ roles: [{ role: 'SupportAgent', resourceType: 'Global', resourceId: '*' }] });
    const { fetchImpl, calls } = fakeMemberFetch(support, me);
    renderSite('/admin/support', { fetchImpl, site: siteWith() });
    const nav = await screen.findByRole('navigation', { name: 'Admin navigation' }, { timeout: 5000 });
    expect(within(nav).getAllByRole('link').map((a) => a.getAttribute('href'))).toEqual(['/admin/home', '/admin/support', '/admin/ideas']);
    expect(await screen.findByRole('heading', { name: 'Support inbox' })).toBeInTheDocument();
    // SystemAdmin-only tabs stay hidden.
    expect(screen.queryByRole('tab', { name: 'Ticket types' })).toBeNull();
    // Ticket types come from the member route: the admin type route is SystemAdmin only (it answered 403).
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/support/ticket-types/enumerate')).toBe(true));
    expect(calls.some((c) => c.path === '/api/v1/admin/support/ticket-types/enumerate')).toBe(false);
  });

  it('gives a Communicator a home without moderation counts (no 403 from the queue)', async () => {
    window.localStorage.setItem('tk.hasSession', '1');
    const fake = createBroadcastsFake();
    const me = makeMe({ roles: [{ role: 'Communicator', resourceType: 'Global', resourceId: '*' }] });
    const { fetchImpl, calls } = fakeMemberFetch(fake, me);
    renderSite('/admin/home', { fetchImpl, site: siteWith() });
    expect(await screen.findByText(appStrings.admin.moderation.staffHome, {}, { timeout: 5000 })).toBeInTheDocument();
    expect(calls.some((c) => c.path.startsWith('/api/v1/admin/moderation'))).toBe(false);
  });

  it('accepts the Phase 5 roles in the member role editor options', async () => {
    const { default: strings } = await import('../src/strings.js');
    expect(strings.enums.role.SupportAgent).toBe('Support agent');
    expect(strings.enums.role.Communicator).toBe('Communicator');
    expect(strings.enums.resourceType.Messaging).toBe('Direct messages');
  });
});

describe('support inbox', () => {
  it('builds the admin query with boolean filters and metadata', () => {
    const q = buildTicketQuery({ status: 'Open', unassigned: 'true', search: ' charge ' }, { pageNumber: 2, pageSize: 25, sort: '-lastActivityUtc', meta: { labels: ['vip'], labelMatch: 'All', tags: {} } });
    expect(q).toMatchObject({ maxResults: 25, skip: 25, ordering: 'LastActivityDescending', search: 'charge', labels: ['vip'], filters: { status: 'Open', unassigned: true } });
  });

  it('lists tickets with metrics, filters, assigns, replies, writes notes, and exports CSV', async () => {
    const ideas = createIdeasFake();
    const support = createSupportFake({ ideasFake: ideas });
    const { a, b } = seedTickets(support);
    const staff = { handle: (call) => (call.path === '/api/v1/admin/members/enumerate' ? { status: 200, body: { objects: [{ id: 'usr_9', displayName: 'Sam Staff', email: 'sam@example.test' }], endOfResults: true } } : null) };
    const { fetchImpl, calls } = fakeAdminFetch([support, ideas, staff]);
    const download = vi.spyOn(browser, 'download').mockImplementation(() => {});
    renderAdmin(<SupportInboxView />, { fetchImpl, path: '/admin/support', routePath: '/admin/support' });
    expect(await screen.findByText('Charged twice')).toBeInTheDocument();
    expect(screen.getByText('Opened (30 days)')).toBeInTheDocument();
    expect(calls.find((c) => c.path === '/api/v1/admin/tickets/enumerate').body).toMatchObject({ maxResults: 50, ordering: 'LastActivityDescending' });
    expect(screen.getByRole('tab', { name: 'Ticket types' })).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /Export CSV/ }));
    await waitFor(() => expect(download).toHaveBeenCalled());
    expect(download.mock.calls[0][0]).toContain('number,type,status,priority,title');

    await userEvent.click(screen.getByText('Charged twice'));
    const drawer = await screen.findByRole('dialog', { name: 'Ticket #' + a.number });
    expect(within(drawer).getByText(/web/)).toBeInTheDocument();
    await userEvent.selectOptions(within(drawer).getByLabelText('Priority'), 'Urgent');
    await waitFor(() => expect(calls.find((c) => c.method === 'PUT' && c.path === '/api/v1/admin/tickets/' + a.id).body).toEqual({ priority: 'Urgent' }));
    // The assignee is picked by name (EntityPicker), never typed as a raw id.
    await userEvent.type(within(drawer).getByLabelText('Assignee'), 'Sam');
    await userEvent.click(await within(drawer).findByRole('option', { name: /Sam Staff/ }));
    await userEvent.click(within(drawer).getByRole('button', { name: 'Assign' }));
    await waitFor(() => expect(calls.filter((c) => c.method === 'PUT').at(-1).body).toEqual({ assigneeUserId: 'usr_9' }));

    await userEvent.click(within(drawer).getByRole('radio', { name: 'Internal note' }));
    await userEvent.type(within(drawer).getByLabelText('Message'), 'Check the payment provider');
    await userEvent.click(within(drawer).getByRole('button', { name: 'Send' }));
    await waitFor(() => expect(calls.find((c) => c.method === 'POST' && c.path.endsWith('/messages')).body).toMatchObject({ kind: 'Note', body: 'Check the payment provider' }));
    const note = (await within(drawer).findByText('Check the payment provider')).closest('li');
    expect(note).toHaveClass('ticket-note');
    expect(within(note).getByText('Internal note')).toBeInTheDocument();

    await userEvent.click(within(drawer).getByRole('radio', { name: 'Reply to member' }));
    await userEvent.type(within(drawer).getByLabelText('Message'), 'Refunded.');
    await userEvent.selectOptions(within(drawer).getByLabelText('Then set status'), 'Resolved');
    await userEvent.click(within(drawer).getByRole('button', { name: 'Send' }));
    await waitFor(() => expect(calls.filter((c) => c.method === 'POST' && c.path.endsWith('/messages')).at(-1).body).toMatchObject({ kind: 'Reply', setStatus: 'Resolved' }));
    expect((await within(drawer).findByText('Refunded.')).closest('li')).not.toHaveClass('ticket-note');
    void b;
  });

  it('publishes an enhancement ticket to the ideas board', async () => {
    const ideas = createIdeasFake();
    const support = createSupportFake({ ideasFake: ideas });
    const { b } = seedTickets(support);
    const { fetchImpl, calls } = fakeAdminFetch([support, ideas]);
    renderAdmin(<SupportInboxView />, { fetchImpl, path: '/admin/support?ticket=' + b.id, routePath: '/admin/support' });
    const drawer = await screen.findByRole('dialog', { name: 'Ticket #' + b.number });
    await userEvent.click(within(drawer).getByRole('button', { name: 'Publish to ideas' }));
    const dialog = await screen.findByRole('dialog', { name: 'Publish this ticket as an idea' });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Publish to ideas' }));
    await waitFor(() => expect(calls.find((c) => c.path.endsWith('/publish-idea')).body).toMatchObject({ title: 'Add dark mode', showRequester: false }));
    expect(await within(drawer).findByText('Linked idea: Add dark mode')).toBeInTheDocument();
    expect(ideas.state.ideas).toHaveLength(1);
  });

  it('manages ticket types for SystemAdmin', async () => {
    expect(validateTicketType({ key: '1x', label: '' })).toEqual({ key: 'Keys are letters and digits, 2 to 40 characters.', label: 'Labels are 1 to 60 characters.' });
    const support = createSupportFake();
    const { fetchImpl, calls } = fakeAdminFetch(support);
    renderAdmin(<SupportInboxView />, { fetchImpl, path: '/admin/support?tab=types', routePath: '/admin/support' });
    expect((await screen.findAllByText('Billing')).length).toBeGreaterThan(0);
    await userEvent.click(screen.getByRole('button', { name: 'Move Bug up' }));
    await userEvent.click(screen.getByRole('button', { name: 'Save order' }));
    await waitFor(() => expect(calls.find((c) => c.path === '/api/v1/admin/support/ticket-types/order').body).toEqual({ ticketTypeIds: ['tty_bug', 'tty_support', 'tty_enhancement', 'tty_billing'] }));
    await userEvent.click(screen.getByRole('button', { name: 'New ticket type' }));
    const dialog = await screen.findByRole('dialog', { name: 'New ticket type' });
    await userEvent.type(within(dialog).getByLabelText('Key'), 'Account');
    await userEvent.type(within(dialog).getByLabelText('Label'), 'Account');
    await userEvent.type(within(dialog).getByLabelText('Help text shown above the form'), 'Sign-in and profile problems.');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(calls.find((c) => c.method === 'POST' && c.path === '/api/v1/admin/support/ticket-types').body).toMatchObject({ key: 'Account', helpText: 'Sign-in and profile problems.' }));
  });
});

describe('ideas management', () => {
  it('sets a status with a note and notify voters, merges, and unmerges', async () => {
    const ideas = createIdeasFake();
    const target = ideas.addIdea({ title: 'Dark theme', voters: ['usr_5'] });
    ideas.addIdea({ title: 'Night mode', voters: ['usr_6'] });
    const { fetchImpl, calls } = fakeAdminFetch(ideas);
    renderAdmin(<IdeasAdminView />, { fetchImpl, path: '/admin/ideas', routePath: '/admin/ideas' });
    await userEvent.click(await screen.findByText('Night mode'));
    const drawer = await screen.findByRole('dialog', { name: 'Night mode' });
    await userEvent.selectOptions(within(drawer).getByRole('combobox', { name: 'Status' }), 'Planned');
    await userEvent.type(within(drawer).getByLabelText('Note to voters (optional)'), 'Next release.');
    await userEvent.click(within(drawer).getByRole('checkbox', { name: 'Notify voters and followers' }));
    await userEvent.click(within(drawer).getByRole('button', { name: 'Update status' }));
    await waitFor(() => expect(calls.find((c) => c.path.endsWith('/status')).body).toEqual({ status: 'Planned', statusNote: 'Next release.', notifyVoters: false }));
    await userEvent.type(within(drawer).getByLabelText('Merge into idea'), 'Dark');
    await userEvent.click(await within(drawer).findByRole('option', { name: /Dark theme/ }));
    await userEvent.click(within(drawer).getByRole('button', { name: 'Merge' }));
    await waitFor(() => expect(calls.find((c) => c.path.endsWith('/merge')).body).toEqual({ intoIdeaId: target.id }));
    expect(await within(drawer).findByText('Merged into Dark theme')).toBeInTheDocument();
    await userEvent.click(within(drawer).getByRole('button', { name: 'Unmerge' }));
    await waitFor(() => expect(calls.some((c) => c.path.endsWith('/unmerge'))).toBe(true));
  });

  it('adds and orders statuses for SystemAdmin', async () => {
    const ideas = createIdeasFake();
    const { fetchImpl, calls } = fakeAdminFetch(ideas);
    renderAdmin(<IdeasAdminView />, { fetchImpl, path: '/admin/ideas?tab=statuses', routePath: '/admin/ideas' });
    expect(await screen.findByText('UnderReview')).toBeInTheDocument();
    await userEvent.type(screen.getByLabelText('Key'), 'Testing');
    await userEvent.type(screen.getByLabelText('Label'), 'In testing');
    await userEvent.click(screen.getByRole('button', { name: 'New status' }));
    await waitFor(() => expect(calls.find((c) => c.method === 'POST' && c.path === '/api/v1/admin/ideas/statuses').body).toMatchObject({ key: 'Testing', label: 'In testing' }));
    await userEvent.click(screen.getByRole('button', { name: 'Move Planned up' }));
    await userEvent.click(screen.getByRole('button', { name: 'Save order' }));
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/admin/ideas/statuses/order')).toBe(true));
  });
});

describe('broadcasts', () => {
  const form = () => ({
    title: 'Holiday', channels: { inApp: true, push: true, email: false }, inApp: { title: 'Holiday hours', body: 'Closed Monday.' }, push: { title: 'Holiday', body: 'Closed Monday' },
    email: { subject: '', preheader: '', body: '', ctaLabel: '' }, segmentMode: 'inline', segmentId: '', rows: [{ key: 'a', field: 'membership', operator: 'eq', value: 'Active' }, { key: 'b', field: 'tags', operator: 'exists', value: 'plan' }, { key: 'c', field: 'store', operator: 'in', value: '' }],
    category: 'Optional', essentialReason: '', labels: [], tags: {}, sendAt: ''
  });

  it('validates and builds broadcast bodies', () => {
    expect(segmentFilters(form().rows)).toEqual([{ field: 'membership', operator: 'eq', value: 'Active' }, { field: 'tags.plan', operator: 'exists', value: true }]);
    // Server value types: booleans, whole days, string lists, hasValue true, tags.<key> from key=value.
    expect(segmentFilters([
      { key: 'd', field: 'hasVerifiedEmail', operator: 'eq', value: 'true' },
      { key: 'e', field: 'activeWithinDays', operator: 'eq', value: ' 30 ' },
      { key: 'f', field: 'membershipSource', operator: 'in', value: 'SignupCode, AdminComp' },
      { key: 'g', field: 'labels', operator: 'any', value: 'vip' },
      { key: 'h', field: 'profile.rank', operator: 'hasValue', value: '' },
      { key: 'i', field: 'tags', operator: 'eq', value: 'plan=gold' },
      { key: 'j', field: 'eventRsvp', operator: 'going', value: 'evt_1' }
    ])).toEqual([
      { field: 'hasVerifiedEmail', operator: 'eq', value: true },
      { field: 'activeWithinDays', operator: 'eq', value: 30 },
      { field: 'membershipSource', operator: 'in', value: ['SignupCode', 'AdminComp'] },
      { field: 'labels', operator: 'any', value: ['vip'] },
      { field: 'profile.rank', operator: 'hasValue', value: true },
      { field: 'tags.plan', operator: 'eq', value: 'gold' },
      { field: 'eventRsvp', operator: 'going', value: 'evt_1' }
    ]);
    expect(validateBroadcast(form())).toEqual({});
    expect(validateBroadcast({ ...form(), category: 'Essential', channels: { inApp: false, push: false, email: false } })).toEqual({ channels: 'Choose at least one channel.', essentialReason: 'Essential broadcasts need a reason.' });
    expect(validateBroadcast(form(), { requireSendAt: true, now: Date.now() }).sendAt).toBe('Choose a time at least 5 minutes ahead.');
    const body = broadcastBody(form());
    expect(body).toMatchObject({ channels: { inApp: true, push: true, email: false }, category: 'Optional', essentialReason: null, segment: { filters: [{ field: 'membership' }, { field: 'tags.plan' }] } });
    expect(body.email).toBeUndefined();
    expect(broadcastBody({ ...form(), segmentMode: 'saved', segmentId: 'bsg_1' })).toMatchObject({ segmentId: 'bsg_1' });
  });

  it('builds a segment with a live count, previews push, saves, tests, and sends with confirmCount', async () => {
    const fake = createBroadcastsFake({ audienceCount: 42 });
    const { fetchImpl, calls } = fakeAdminFetch(fake);
    renderAdmin(<BroadcastsView />, { fetchImpl, path: '/admin/broadcasts', routePath: '/admin/broadcasts' });
    expect(await screen.findByText('No broadcasts yet.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'New broadcast' }));
    const drawer = await screen.findByRole('dialog', { name: 'New broadcast' });
    expect(await within(drawer).findByText('42 recipients')).toBeInTheDocument();
    await userEvent.click(within(drawer).getByRole('button', { name: 'Add filter' }));
    await userEvent.selectOptions(within(drawer).getByLabelText('Value'), 'Active');
    await waitFor(() => expect(calls.filter((c) => c.path === '/api/v1/admin/broadcasts/segments/preview').at(-1).body).toEqual({ segment: { filters: [{ field: 'membership', operator: 'eq', value: 'Active' }] } }));

    await userEvent.type(within(drawer).getByLabelText('Internal title'), 'Holiday');
    await userEvent.type(within(drawer).getByLabelText('Title'), 'Holiday hours');
    await userEvent.type(within(drawer).getByLabelText('Body'), 'Closed Monday.');
    await userEvent.click(within(drawer).getByRole('checkbox', { name: 'Push' }));
    const pushTitle = within(drawer).getAllByLabelText('Title')[1];
    await userEvent.type(pushTitle, 'We are closed');
    await userEvent.type(within(drawer).getAllByLabelText('Body')[1], 'See you Tuesday');
    expect(within(drawer).getByLabelText('Push preview')).toHaveTextContent('We are closed');
    // Essential is offered to SystemAdmin with a required reason.
    await userEvent.click(within(drawer).getByRole('checkbox', { name: /Essential/ }));
    await userEvent.click(within(drawer).getByRole('button', { name: 'Save draft' }));
    expect(within(drawer).getByText('Essential broadcasts need a reason.')).toBeInTheDocument();
    await userEvent.click(within(drawer).getByRole('checkbox', { name: /Essential/ }));
    await userEvent.click(within(drawer).getByRole('button', { name: 'Save draft' }));
    await waitFor(() => expect(calls.find((c) => c.method === 'POST' && c.path === '/api/v1/admin/broadcasts').body).toMatchObject({ title: 'Holiday', channels: { inApp: true, push: true, email: false }, push: { title: 'We are closed' } }));

    await userEvent.click(within(drawer).getByRole('button', { name: 'Send a test to me' }));
    await waitFor(() => expect(calls.find((c) => c.path.endsWith('/test')).body).toEqual({ channels: ['inApp', 'push'] }));

    await userEvent.click(within(drawer).getByRole('button', { name: 'Send now' }));
    const confirm = await screen.findByRole('dialog', { name: 'Send this broadcast?' });
    expect(confirm).toHaveTextContent('42 recipients by In app, Push');
    await userEvent.click(within(confirm).getByRole('button', { name: 'Send to 42' }));
    await waitFor(() => expect(calls.find((c) => c.path.endsWith('/send')).body).toEqual({ confirmCount: 42 }));
    expect(await within(drawer).findByText('This broadcast can no longer be edited.')).toBeInTheDocument();
    expect(within(drawer).getByRole('heading', { name: 'Recipients' })).toBeInTheDocument();
  });

  it('deletes a saved segment from the audience picker', async () => {
    const fake = createBroadcastsFake({ audienceCount: 5 });
    const saved = fake.handle({ method: 'POST', path: '/api/v1/admin/broadcasts/segments', body: { name: 'Black belts', filters: [] } }).body;
    const { fetchImpl, calls } = fakeAdminFetch(fake);
    renderAdmin(<BroadcastsView />, { fetchImpl, path: '/admin/broadcasts?broadcast=new', routePath: '/admin/broadcasts' });
    const drawer = await screen.findByRole('dialog', { name: 'New broadcast' });
    await userEvent.click(within(drawer).getByRole('radio', { name: 'Saved segment' }));
    await userEvent.selectOptions(await within(drawer).findByLabelText('Saved segment'), saved.id);
    await userEvent.click(within(drawer).getByRole('button', { name: 'Delete segment' }));
    const confirm = await screen.findByRole('dialog', { name: 'Delete segment Black belts' });
    await userEvent.click(within(confirm).getByRole('button', { name: 'Delete segment' }));
    await waitFor(() => expect(calls.some((c) => c.method === 'DELETE' && c.path === '/api/v1/admin/broadcasts/segments/' + saved.id)).toBe(true));
    expect(await screen.findByText('Segment deleted.')).toBeInTheDocument();
    await waitFor(() => expect(within(drawer).queryByRole('option', { name: 'Black belts' })).toBeNull());
  });

  it('schedules and cancels a broadcast', async () => {
    const fake = createBroadcastsFake();
    const draft = fake.handle({ method: 'POST', path: '/api/v1/admin/broadcasts', body: { title: 'Later', channels: { inApp: true }, inApp: { title: 'Later', body: 'Soon.' }, segment: { filters: [] } } }).body;
    const { fetchImpl, calls } = fakeAdminFetch(fake);
    renderAdmin(<BroadcastsView />, { fetchImpl, path: '/admin/broadcasts?broadcast=' + draft.id, routePath: '/admin/broadcasts' });
    const drawer = await screen.findByRole('dialog', { name: 'Edit Later' });
    await userEvent.click(within(drawer).getByRole('button', { name: 'Schedule' }));
    expect(within(drawer).getByText('Choose a time at least 5 minutes ahead.')).toBeInTheDocument();
    const later = new Date(Date.now() + 3 * 3600 * 1000);
    const pad = (n) => String(n).padStart(2, '0');
    const local = later.getFullYear() + '-' + pad(later.getMonth() + 1) + '-' + pad(later.getDate()) + 'T' + pad(later.getHours()) + ':' + pad(later.getMinutes());
    await userEvent.type(within(drawer).getByLabelText('Send at'), local);
    await userEvent.click(within(drawer).getByRole('button', { name: 'Schedule' }));
    await waitFor(() => expect(calls.find((c) => c.path.endsWith('/schedule')).body.sendAtUtc).toMatch(/Z$/));
    await userEvent.click(await within(drawer).findByRole('button', { name: 'Cancel broadcast' }));
    await waitFor(() => expect(calls.some((c) => c.path.endsWith('/cancel'))).toBe(true));
  });

  it('hides the Essential option from a Communicator', async () => {
    window.localStorage.setItem('tk.hasSession', '1');
    const fake = createBroadcastsFake();
    const me = makeMe({ roles: [{ role: 'Communicator', resourceType: 'Global', resourceId: '*' }] });
    const { fetchImpl } = fakeMemberFetch(fake, me);
    renderSite('/admin/broadcasts?broadcast=new', { fetchImpl, site: siteWith() });
    const drawer = await screen.findByRole('dialog', { name: 'New broadcast' }, { timeout: 5000 });
    expect(within(drawer).queryByRole('checkbox', { name: /Essential/ })).toBeNull();
    expect(within(drawer).getByText(/Only a system administrator can send an essential broadcast/)).toBeInTheDocument();
  });
});

describe('message report snapshots', () => {
  it('limits the Messaging scope to SystemAdmin and messaging moderators', () => {
    expect(queueFilterOptions(false).scopeTypes.map((o) => o.value)).not.toContain('Messaging');
    expect(queueFilterOptions(false, { messagingModerator: true }).scopeTypes.map((o) => o.value)).toContain('Messaging');
    expect(queueFilterOptions(true).targetTypes.map((o) => o.value)).toEqual(expect.arrayContaining(['Message', 'Conversation']));
  });

  it('renders the frozen snapshot with the reported message marked and no conversation link', () => {
    const target = { reportedMessageId: 'msg_2', messages: { objects: [
      { id: 'msg_2', seq: 2, author: { displayName: 'Alex Chen' }, body: 'Rude words', state: 'Visible', createdUtc: '2026-11-12T10:01:00Z' },
      { id: 'msg_1', seq: 1, author: { displayName: 'Sam Rivera' }, body: '', state: 'Deleted', createdUtc: '2026-11-12T10:00:00Z', attachments: [{ id: 'med_1' }] }
    ] } };
    expect(snapshotMessages(target).map((m) => m.id)).toEqual(['msg_1', 'msg_2']);
    const { container } = render(<MemoryRouter><MessageSnapshotPanel target={target} reportedId="msg_2" /></MemoryRouter>);
    expect(screen.getByText('Rude words').closest('li')).toHaveClass('snapshot-reported');
    expect(screen.getByText('Message deleted')).toBeInTheDocument();
    expect(screen.getByText('1 attachment')).toBeInTheDocument();
    expect(container.querySelector('a')).toBeNull();
  });

  it('opens a reported direct message from the queue as a snapshot only', async () => {
    const { fetchImpl } = createFetch((call) => {
      if (call.path === '/api/v1/settings/enumerate') return json({ objects: [], endOfResults: true });
      if (call.path === '/api/v1/admin/moderation/queue/Message/msg_9') {
        return json({ target: { reportedMessageId: 'msg_9', messages: [{ id: 'msg_9', seq: 4, author: { displayName: 'Alex Chen' }, body: 'Reported text', state: 'Visible', createdUtc: '2026-11-12T10:00:00Z' }] }, reports: [], actions: [] });
      }
      if (call.path.startsWith('/api/v1/admin/moderation/queue')) return json({ success: true, objects: [], totalRecords: 0, endOfResults: true });
      return undefined;
    });
    renderAdmin(<ModerationView />, { fetchImpl, path: '/admin/moderation?target=Message:msg_9', routePath: '/admin/moderation' });
    const snapshot = await screen.findByTestId('message-snapshot');
    expect(within(snapshot).getByText('Reported text')).toBeInTheDocument();
    const drawer = snapshot.closest('[role="dialog"]');
    expect(within(drawer).queryByRole('link', { name: /Open/ })).toBeNull();
  });
});
