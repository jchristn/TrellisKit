import { afterEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import NotificationsView from '../src/views/member/NotificationsView.jsx';
import ReportDialog from '../src/components/feed/ReportDialog.jsx';
import VerifyEmailPage from '../src/views/auth/VerifyEmailPage.jsx';
import MemberPublicProfileView from '../src/views/member/MemberPublicProfileView.jsx';
import { json, makeMe } from './helpers.jsx';
import { enumPage, memberFetch, renderMember, user } from './engagementHelpers.jsx';

describe('notification filter in the URL', () => {
  it('reads ?show=unread on load and writes the All/Unread choice back to the URL', async () => {
    const { fetchImpl, calls } = memberFetch((call) => (call.path === '/api/v1/notifications' ? json(enumPage([], { unreadCount: 0 })) : undefined));
    await renderMember(<NotificationsView />, { fetchImpl, path: '/app/notifications?show=unread', routePath: '/app/notifications' });
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/notifications')).toBe(true));
    const lists = () => calls.filter((c) => c.path === '/api/v1/notifications');
    expect(lists()[0].search).toMatch(/unreadOnly=true/);
    expect(screen.getByRole('radio', { name: 'Unread' })).toHaveAttribute('aria-checked', 'true');
    const before = lists().length;
    await userEvent.click(screen.getByRole('radio', { name: 'All' }));
    await waitFor(() => expect(lists().length).toBeGreaterThan(before));
    expect(lists()[lists().length - 1].search).not.toMatch(/unreadOnly=true/);
    expect(screen.getByRole('radio', { name: 'All' })).toHaveAttribute('aria-checked', 'true');
  });
});

describe('report dialog validation', () => {
  afterEach(() => {
    delete Element.prototype.scrollIntoView;
  });

  it('scrolls the first validation error into view', async () => {
    const scrolled = vi.fn();
    Element.prototype.scrollIntoView = scrolled;
    const { fetchImpl } = memberFetch(() => undefined);
    await renderMember(<ReportDialog target={{ type: 'Comment', id: 'cmt_1' }} onClose={() => {}} />, { fetchImpl });
    await userEvent.click(screen.getByRole('button', { name: 'Send report' }));
    expect(screen.getByText('Choose a reason.')).toBeInTheDocument();
    await waitFor(() => expect(scrolled).toHaveBeenCalled());
    expect(scrolled.mock.contexts[0]).toHaveClass('field-error');
  });
});

describe('used verification link', () => {
  it('says the address is already verified when the signed-in account is verified', async () => {
    const me = makeMe({ emailVerified: true });
    const { fetchImpl } = memberFetch((call) => {
      if (call.path === '/api/v1/auth/email/verify') return json({ error: 'InvalidToken', message: 'Used.' }, 400);
      if (call.path === '/api/v1/auth/me') return json(me);
      return undefined;
    }, me);
    await renderMember(<VerifyEmailPage />, { fetchImpl, path: '/verify-email?token=used', routePath: '/verify-email' });
    expect(await screen.findByText(/already verified/)).toBeInTheDocument();
    expect(screen.queryByText(/invalid or has expired/)).toBeNull();
  });

  it('keeps the invalid-link message for an unverified account', async () => {
    const me = makeMe({ emailVerified: false });
    const { fetchImpl } = memberFetch((call) => {
      if (call.path === '/api/v1/auth/email/verify') return json({ error: 'InvalidToken', message: 'Used.' }, 400);
      if (call.path === '/api/v1/auth/me') return json(me);
      return undefined;
    }, me);
    await renderMember(<VerifyEmailPage />, { fetchImpl, path: '/verify-email?token=bad', routePath: '/verify-email' });
    expect(await screen.findByText(/invalid or has expired/)).toBeInTheDocument();
  });
});

describe('member profile safety actions', () => {
  it.each([
    [true, false],
    [false, true]
  ])('staff %s: Block shown %s', async (isStaff, blockShown) => {
    const { fetchImpl } = memberFetch((call) => (call.path === '/api/v1/users/usr_7' ? json({ ...user('usr_7', 'Robin Staff', { isStaff }), isBlockedByMe: false }) : undefined));
    await renderMember(<MemberPublicProfileView />, { fetchImpl, path: '/app/members/usr_7', routePath: '/app/members/:id' });
    expect(await screen.findByRole('button', { name: /Report/ })).toBeInTheDocument();
    expect(Boolean(screen.queryByRole('button', { name: /^Block/ }))).toBe(blockShown);
  });
});
