import { describe, expect, it } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route } from 'react-router-dom';
import strings from '../src/strings.js';
import NotificationSettingsView, { formatTimeOfDay, hoursSummary } from '../src/views/member/NotificationSettingsView.jsx';
import NotificationsView, { notificationHref } from '../src/views/member/NotificationsView.jsx';
import BlockedMembersView from '../src/views/member/BlockedMembersView.jsx';
import ReportDialog from '../src/components/feed/ReportDialog.jsx';
import BlockDialog from '../src/components/feed/BlockDialog.jsx';
import { webPathFor } from '../src/utils/engagement.js';
import { json } from './helpers.jsx';
import { enumPage, memberFetch, renderMember, user } from './engagementHelpers.jsx';

const DEVICE_ZONE = Intl.DateTimeFormat().resolvedOptions().timeZone;

function prefs(overrides = {}) {
  return {
    pushPaused: false,
    categories: [
      { category: 'Replies', inApp: true, push: true, email: false, editable: { inApp: true, push: true, email: false } },
      { category: 'Mentions', inApp: true, push: true, email: false, editable: { inApp: true, push: true, email: false } },
      { category: 'Reactions', inApp: true, push: true, email: false, editable: { inApp: true, push: true, email: false } },
      { category: 'FeedPosts', inApp: true, push: true, email: true, editable: { inApp: true, push: true, email: true } },
      { category: 'Moderation', inApp: true, push: true, email: false, editable: { inApp: false, push: true, email: false } },
      { category: 'Account', inApp: true, push: true, email: true, editable: { inApp: false, push: true, email: false } }
    ],
    notificationHours: { enabled: true, start: '07:00', end: '22:00', timeZone: DEVICE_ZONE },
    ...overrides
  };
}

describe('notification settings and hours', () => {
  it('formats hours and summaries', () => {
    expect(formatTimeOfDay('07:00')).toBe('7:00 AM');
    expect(formatTimeOfDay('22:30')).toBe('10:30 PM');
    expect(hoursSummary({ enabled: true, start: '07:00', end: '22:00', timeZone: 'UTC' })).toBe('Pushes arrive between 7:00 AM and 10:00 PM (UTC).');
    expect(hoursSummary({ enabled: true, start: '22:00', end: '06:00', timeZone: 'UTC' })).toMatch(/crosses midnight/);
    expect(hoursSummary({ enabled: true, start: '09:00', end: '09:00' })).toMatch(/any time/);
    expect(hoursSummary({ enabled: false })).toMatch(/any time/);
  });

  it('shows every category on by default, toggles optimistically, and explains fixed cells', async () => {
    let doc = prefs();
    const { fetchImpl, calls } = memberFetch((call) => {
      if (call.path === '/api/v1/me/notification-preferences/enumerate' && call.method === 'POST') return json({ ...doc, ...enumPage(doc.categories) });
      if (call.path === '/api/v1/me/notification-preferences' && call.method === 'PUT') {
        const edit = call.body.categories && call.body.categories[0];
        if (edit && edit.category === 'Mentions') return json({ error: 'ValidationFailed', message: 'Nope' }, 400);
        doc = { ...doc, categories: doc.categories.map((r) => (edit && r.category === edit.category ? { ...r, ...edit, push: edit.inApp === false ? false : (edit.push ?? r.push) } : r)), pushPaused: call.body.pushPaused ?? doc.pushPaused, notificationHours: { ...doc.notificationHours, ...(call.body.notificationHours || {}) } };
        return json(doc);
      }
      return undefined;
    });
    await renderMember(<NotificationSettingsView />, { fetchImpl });
    expect(await screen.findByRole('checkbox', { name: 'Replies: In app' })).toBeChecked();
    for (const name of ['Replies: Push', 'Mentions: Push', 'Reactions: Push', 'New feed posts: Email', 'New feed posts: Push']) expect(screen.getByRole('checkbox', { name })).toBeChecked();
    expect(screen.queryByRole('checkbox', { name: 'Moderation: In app' })).toBeNull();
    expect(screen.getAllByText('Always on').length).toBeGreaterThanOrEqual(2);
    expect(screen.getAllByText('Not available yet').length).toBeGreaterThanOrEqual(3);
    // Same time zone as the device: nothing is sent on load.
    expect(calls.filter((c) => c.method === 'PUT')).toHaveLength(0);

    await userEvent.click(screen.getByRole('checkbox', { name: 'Replies: In app' }));
    await waitFor(() => expect(calls.filter((c) => c.method === 'PUT').at(-1).body).toEqual({ categories: [{ category: 'Replies', inApp: false }] }));
    expect(screen.getByRole('checkbox', { name: 'Replies: Push' })).toBeDisabled();

    await userEvent.click(screen.getByRole('checkbox', { name: 'Mentions: Push' }));
    expect(await screen.findByText('Nope')).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: 'Mentions: Push' })).toBeChecked();

    await userEvent.click(screen.getByRole('checkbox', { name: 'Pause all push notifications' }));
    await waitFor(() => expect(calls.filter((c) => c.method === 'PUT').at(-1).body).toEqual({ pushPaused: true }));
  });

  it('offers the Following and Forums email digest when the server allows it', async () => {
    const base = prefs();
    const doc = { ...base, categories: [...base.categories,
      { category: 'Following', inApp: true, push: true, email: false, editable: { inApp: true, push: true, email: true } },
      { category: 'Forums', inApp: true, push: true, email: false, editable: { inApp: true, push: true, email: true } }] };
    const { fetchImpl, calls } = memberFetch((call) => {
      if (call.path === '/api/v1/me/notification-preferences/enumerate' && call.method === 'POST') return json({ ...doc, ...enumPage(doc.categories) });
      if (call.path === '/api/v1/me/notification-preferences' && call.method === 'PUT') return json({ ...doc, categories: doc.categories.map((r) => (r.category === 'Forums' ? { ...r, email: true } : r)) });
      return undefined;
    });
    await renderMember(<NotificationSettingsView />, { fetchImpl });
    const digest = strings.notificationSettings.digest;
    const following = await screen.findByRole('checkbox', { name: 'Threads you follow: Email (' + digest + ')' });
    expect(following).not.toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'New feed posts: Email' })).toBeChecked();
    expect(screen.getAllByText(digest)).toHaveLength(2);
    await userEvent.click(screen.getByRole('checkbox', { name: 'Forums: Email (' + digest + ')' }));
    await waitFor(() => expect(calls.filter((c) => c.method === 'PUT').at(-1).body).toEqual({ categories: [{ category: 'Forums', email: true }] }));
  });

  it('edits notification hours in a time zone and sends the device zone the first time', async () => {
    const { fetchImpl, calls } = memberFetch((call) => {
      if (call.path === '/api/v1/me/notification-preferences/enumerate' && call.method === 'POST') { const doc = prefs({ notificationHours: { enabled: true, start: '07:00', end: '22:00', timeZone: 'Etc/GMT+12' } }); return json({ ...doc, ...enumPage(doc.categories) }); };
      if (call.method === 'PUT') return json(prefs({ notificationHours: { enabled: true, start: '07:00', end: '22:00', timeZone: DEVICE_ZONE, ...(call.body.notificationHours || {}) } }));
      return undefined;
    });
    await renderMember(<NotificationSettingsView />, { fetchImpl });
    await waitFor(() => expect(calls.filter((c) => c.method === 'PUT')[0].body).toEqual({ notificationHours: { timeZone: DEVICE_ZONE } }));
    const start = await screen.findByLabelText('From');
    expect(start).toHaveValue('07:00');
    await userEvent.clear(start);
    await userEvent.type(start, '08:30');
    await userEvent.click(screen.getByRole('button', { name: 'Save hours' }));
    await waitFor(() => expect(calls.filter((c) => c.method === 'PUT').at(-1).body).toEqual({ notificationHours: { enabled: true, start: '08:30', end: '22:00', timeZone: DEVICE_ZONE } }));
    expect(await screen.findByText('Notification hours saved.')).toBeInTheDocument();
  });
});

describe('notification center', () => {
  it('maps paths to website routes and ignores unknown types', () => {
    expect(webPathFor('/feed/fpo_1?comment=cmt_2')).toBe('/app/feed/fpo_1?comment=cmt_2');
    expect(webPathFor('/account/membership')).toBe('/app/membership');
    expect(webPathFor('//evil.test')).toBeNull();
    expect(notificationHref({ type: 'DirectMessage', path: '/messages/1' })).toBe('/app/messages/1');
    expect(notificationHref({ type: 'FutureType', path: '/future/1' })).toBeNull();
    expect(notificationHref({ type: 'ContentFlagged', path: '/admin/content-review' })).toBe('/admin/content-review');
    expect(notificationHref({ type: 'MetricAlert', path: '/admin/metrics/health' })).toBe('/admin/metrics/health');
    expect(notificationHref({ type: 'MetricAlert', path: '//evil.test' })).toBeNull();
    expect(notificationHref({ type: 'ThreadReply', path: '/threads/thr_1?comment=cmt_1' })).toBe('/app/threads/thr_1?comment=cmt_1');
    expect(notificationHref({ type: 'AnswerAccepted', path: '/threads/thr_1' })).toBe('/app/threads/thr_1');
    expect(notificationHref({ type: 'EventWaitlistPromoted', path: '/events/evt_1' })).toBe('/app/events/evt_1');
    expect(notificationHref({ type: 'NewLessonInSeries', path: '/lessons/les_1' })).toBe('/app/lessons/les_1');
    expect(notificationHref({ type: 'SubmissionDecision', path: '/me/directory/submissions' })).toBe('/app/directory/submissions');
    expect(notificationHref({ type: 'EventProposalDecision', path: '/me/events/proposals' })).toBe('/app/events/proposals');
  });

  it('marks a notification read before opening it and marks all read', async () => {
    const { fetchImpl, calls } = memberFetch((call) => {
      if (call.path === '/api/v1/notifications') return json(enumPage([
        { id: 'ntf_1', type: 'CommentReply', category: 'Replies', title: 'Alex Chen replied to your comment', body: 'Hi', actor: user('usr_2', 'Alex Chen'), path: '/feed/fpo_1?comment=cmt_2', isRead: false, createdUtc: '2026-11-10T00:00:00Z', updatedUtc: '2026-11-10T00:00:00Z' },
        { id: 'ntf_2', type: 'FutureType', category: 'Account', title: 'A future type', body: null, path: '/broadcasts/1', isRead: true, createdUtc: '2026-11-09T00:00:00Z', updatedUtc: '2026-11-09T00:00:00Z' }
      ], { unreadCount: 1 }));
      if (call.path === '/api/v1/notifications/read') return json({ unreadCount: 0 });
      if (call.path === '/api/v1/notifications/read-all') return json({ unreadCount: 0 });
      return undefined;
    });
    await renderMember(<NotificationsView />, { fetchImpl, path: '/app/notifications', routePath: '/app/notifications', extraRoutes: <Route path="/app/feed/:id" element={<p>Post page</p>} /> });
    expect(await screen.findByText('A future type')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /A future type/ })).toBeNull();
    await userEvent.click(screen.getByRole('button', { name: 'Mark all read' }));
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/notifications/read-all')).toBe(true));
    await userEvent.click(screen.getByRole('link', { name: /Alex Chen replied/ }));
    expect(await screen.findByText('Post page')).toBeInTheDocument();
  });
});

describe('report and block', () => {
  it('requires details for "Something else", sends the report, thanks, and offers to block', async () => {
    const { fetchImpl, calls } = memberFetch((call) => {
      if (call.path === '/api/v1/reports') return json({ id: 'rpt_1', status: 'Open', createdUtc: '2026-11-10T00:00:00Z' }, 201);
      if (call.path === '/api/v1/users/usr_2/block') return json({ blocked: true });
      return undefined;
    });
    await renderMember(<ReportDialog target={{ type: 'Comment', id: 'cmt_1' }} author={user('usr_2', 'Alex Chen', { handle: 'alexchen' })} onClose={() => {}} />, { fetchImpl });
    await userEvent.click(screen.getByRole('button', { name: 'Send report' }));
    expect(screen.getByText('Choose a reason.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('radio', { name: /Something else/ }));
    await userEvent.type(screen.getByLabelText('Details'), 'short');
    await userEvent.click(screen.getByRole('button', { name: 'Send report' }));
    expect(screen.getByText(/at least 10 characters/)).toBeInTheDocument();
    await userEvent.type(screen.getByLabelText('Details'), ' but now long enough');
    await userEvent.click(screen.getByRole('button', { name: 'Send report' }));
    expect(await screen.findByText('Thanks. A moderator will review this.')).toBeInTheDocument();
    expect(calls.find((c) => c.path === '/api/v1/reports').body).toEqual({ targetType: 'Comment', targetId: 'cmt_1', reason: 'Other', details: 'short but now long enough' });
    await userEvent.click(screen.getByRole('button', { name: 'Block @alexchen' }));
    await userEvent.click(screen.getByRole('button', { name: 'Block' }));
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/users/usr_2/block' && c.method === 'POST')).toBe(true));
  });

  it('never offers to block staff and explains why', async () => {
    const { fetchImpl, calls } = memberFetch(() => undefined);
    await renderMember(<BlockDialog user={user('usr_9', 'Mod Person', { isStaff: true })} onClose={() => {}} />, { fetchImpl });
    expect(screen.getByRole('heading', { name: 'Staff cannot be blocked' })).toBeInTheDocument();
    expect(screen.getByText(/report it instead/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Block' })).toBeNull();
    expect(calls.some((c) => c.path.endsWith('/block'))).toBe(false);
  });

  it('switches to the staff explanation when the server answers CannotBlockStaff', async () => {
    const { fetchImpl } = memberFetch((call) => (call.path === '/api/v1/users/usr_8/block' ? json({ error: 'CannotBlockStaff', message: 'x' }, 400) : undefined));
    await renderMember(<BlockDialog user={user('usr_8', 'New Moderator')} onClose={() => {}} />, { fetchImpl });
    await userEvent.click(screen.getByRole('button', { name: 'Block' }));
    expect(await screen.findByRole('heading', { name: 'Staff cannot be blocked' })).toBeInTheDocument();
  });

  it('lists blocked members and unblocks', async () => {
    const { fetchImpl, calls } = memberFetch((call) => {
      if (call.path === '/api/v1/me/blocks') return json(enumPage([{ user: user('usr_2', 'Alex Chen'), createdUtc: '2026-11-01T00:00:00Z' }]));
      if (call.path === '/api/v1/users/usr_2/block' && call.method === 'DELETE') return json({ blocked: false });
      return undefined;
    });
    await renderMember(<BlockedMembersView />, { fetchImpl });
    const row = (await screen.findByText('Alex Chen')).closest('li');
    await userEvent.click(within(row).getByRole('button', { name: 'Unblock' }));
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Unblock' }));
    await waitFor(() => expect(screen.queryByText('Alex Chen')).toBeNull());
    expect(calls.some((c) => c.method === 'DELETE' && c.path === '/api/v1/users/usr_2/block')).toBe(true);
    expect(await screen.findByText('You have not blocked anyone.')).toBeInTheDocument();
  });
});
