import { describe, expect, it } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import strings from '../src/strings.js';
import { AppRoutes } from '../src/App.jsx';
import { ThemeProvider } from '../src/context/ThemeContext.jsx';
import ModerationView from '../src/views/admin/ModerationView.jsx';
import ReviewRequestsTab from '../src/views/admin/ReviewRequestsTab.jsx';
import FeedAdminView from '../src/views/admin/FeedAdminView.jsx';
import MediaLibraryView from '../src/views/admin/MediaLibraryView.jsx';
import MemberDetailDrawer from '../src/views/admin/MemberDetailDrawer.jsx';
import { validateModerationForm } from '../src/views/admin/ModerationActionDialog.jsx';
import { highlightMatches, parseImportTerms } from '../src/views/admin/WordFilterTab.jsx';
import { mostCommonReason, isOverdue, queueFilterOptions } from '../src/views/admin/ModerationQueue.jsx';
import { groupedAdminRoutes } from '../src/views/admin/adminRoutes.js';
import { authResponse, createFetch, json, makeMe, renderAdmin } from './helpers.jsx';
import { enumPage, makeComment, user } from './engagementHelpers.jsx';

function adminFetch(handler) {
  return createFetch((call) => {
    if (call.path === '/api/v1/settings/enumerate') return json({ objects: [], endOfResults: true });
    return handler(call);
  });
}

const queueRow = {
  targetType: 'Comment', targetId: 'cmt_1', targetState: 'Hidden', preview: { title: null, excerpt: 'Buy cheap stuff', hasMedia: false }, author: user('usr_2', 'Spammy Sam'),
  reportCount: 3, reporterCount: 3, reasons: { Spam: 3 }, firstReportedUtc: '2026-11-10T00:00:00Z', lastReportedUtc: '2026-11-10T01:00:00Z', dueUtc: '2026-11-10T12:00:00Z', isOverdue: true, status: 'Open'
};

describe('moderation helpers', () => {
  it('validates reasons, detects overdue rows, parses imports, and highlights matches', () => {
    expect(validateModerationForm({ action: 'Remove', reason: 'ok' })).toHaveProperty('reason');
    expect(validateModerationForm({ action: 'Remove', reason: 'Spam' })).toEqual({});
    expect(validateModerationForm({ action: 'Dismiss', reason: '' })).toEqual({});
    expect(mostCommonReason([{ reason: 'Spam' }, { reason: 'Harassment' }, { reason: 'Harassment' }])).toBe('Harassment');
    expect(mostCommonReason([{ reason: 'Spam' }, { reason: 'Other' }])).toBe('Spam');
    expect(mostCommonReason([])).toBeNull();
    expect(isOverdue({ status: 'Open', dueUtc: '2000-01-01T00:00:00Z' })).toBe(true);
    expect(isOverdue({ status: 'Open', dueUtc: '2999-01-01T00:00:00Z' })).toBe(false);
    expect(isOverdue({ isOverdue: false, dueUtc: '2000-01-01T00:00:00Z' })).toBe(false);
    expect(parseImportTerms('a\n A \n\nb\r\nc')).toEqual(['a', 'b', 'c']);
    expect(highlightMatches('one bad word', [{ start: 4, length: 3 }])).toEqual([{ text: 'one ', match: false }, { text: 'bad', match: true }, { text: ' word', match: false }]);
  });

  it('moderators see only Home and Moderation in the sidebar', () => {
    const ids = groupedAdminRoutes({ features: {} }, { moderatorOnly: true }).flatMap((g) => g.routes.map((r) => r.id));
    expect(ids).toEqual(['home', 'moderation']);
  });
});

describe('moderation queue', () => {
  it('shows how long until a pending report is due instead of "Due now"', async () => {
    const due = new Date(Date.now() + 5 * 3600 * 1000 + 120000).toISOString();
    const { fetchImpl } = adminFetch((call) => (call.path === '/api/v1/admin/moderation/queue' ? json(enumPage([{ ...queueRow, targetState: 'Visible', dueUtc: due, isOverdue: false }, { ...queueRow, targetType: 'AssistantMessage', targetId: 'acm_1', preview: { title: 'Helper Bot', excerpt: 'A reply', hasMedia: false }, author: { id: null, displayName: 'Deleted member', isDeleted: true }, scope: { type: 'Assistant', id: 'ast_1', name: 'Helper Bot' }, dueUtc: due, isOverdue: false }], { totalRecords: 2 })) : undefined));
    renderAdmin(<ModerationView />, { fetchImpl, path: '/admin/moderation', routePath: '/admin/moderation' });
    expect((await screen.findAllByText('Due in 5h')).length).toBe(2);
    expect(screen.queryByText('[deleted]')).toBeNull();
    expect(screen.queryByText('Deleted member')).toBeNull();
    expect(screen.getAllByText('Helper Bot').length).toBeGreaterThan(0);
    expect(screen.queryByText('Due now')).toBeNull();
  });

  it('queries the queue as an enumeration, flags overdue and auto-hidden rows, and removes with a reason', async () => {
    const { fetchImpl, calls } = adminFetch((call) => {
      if (call.path === '/api/v1/admin/moderation/queue') return json(enumPage([queueRow], { totalRecords: 1 }));
      if (call.path === '/api/v1/admin/moderation/queue/Comment/cmt_1') {
        return json({ target: makeComment('cmt_1', { state: 'Hidden', body: 'Buy cheap stuff', author: user('usr_2', 'Spammy Sam') }), context: { feedPostId: 'fpo_1' }, revisions: [], reports: [{ id: 'rpt_1', reporter: user('usr_5', 'Kim'), reason: 'Spam', details: 'Links', status: 'Open', createdUtc: '2026-11-10T00:00:00Z' }], actions: enumPage([]), authorHistory: { removedCount90d: 1, hiddenCount90d: 2, reportsAgainst90d: 5, isMuted: false } });
      }
      if (call.path === '/api/v1/admin/moderation/actions' && call.method === 'POST') return json({ action: { id: 'mac_1' }, target: {} });
      return undefined;
    });
    renderAdmin(<ModerationView />, { fetchImpl, path: '/admin/moderation', routePath: '/admin/moderation' });
    expect(await screen.findByText('Buy cheap stuff')).toBeInTheDocument();
    const listCall = calls.find((c) => c.path === '/api/v1/admin/moderation/queue');
    expect(listCall.query).toEqual({ maxResults: '50', ordering: 'DueAscending', status: 'Open' });
    expect(screen.getByText('Overdue')).toBeInTheDocument();
    expect(screen.getByText('Auto-hidden')).toBeInTheDocument();

    await userEvent.click(screen.getByText('Buy cheap stuff'));
    const drawer = await screen.findByRole('dialog', { name: /Report on a comment/ });
    expect(await within(drawer).findByText('Links')).toBeInTheDocument();
    expect(within(drawer).getByRole('button', { name: 'Unhide' })).toBeInTheDocument();
    await userEvent.click(within(drawer).getByRole('button', { name: 'Remove' }));
    const dialog = await screen.findByRole('dialog', { name: 'Remove this comment' });
    expect(within(dialog).getByLabelText('Reason')).toHaveValue('Spam');
    expect(within(dialog).getByLabelText('Notify the author')).toBeChecked();
    await userEvent.type(within(dialog).getByLabelText('Internal note (optional)'), 'Third time');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Remove' }));
    await waitFor(() => expect(calls.find((c) => c.path === '/api/v1/admin/moderation/actions' && c.method === 'POST').body).toEqual({ targetType: 'Comment', targetId: 'cmt_1', action: 'Remove', reason: 'Spam', note: 'Third time', notifyAuthor: true, resolveReports: true }));
  });

  it('dismisses open reports and shows the admin-only tabs to administrators', async () => {
    const { fetchImpl, calls } = adminFetch((call) => {
      if (call.path === '/api/v1/admin/moderation/queue') return json(enumPage([{ ...queueRow, targetState: 'Visible', isOverdue: false }]));
      if (call.path === '/api/v1/admin/moderation/queue/Comment/cmt_1') return json({ target: makeComment('cmt_1'), reports: [{ id: 'rpt_1', reason: 'Spam', status: 'Open', createdUtc: '2026-11-10T00:00:00Z' }], actions: [] });
      if (call.path.endsWith('/dismiss')) return json({});
      return undefined;
    });
    renderAdmin(<ModerationView />, { fetchImpl, path: '/admin/moderation?target=Comment:cmt_1', routePath: '/admin/moderation' });
    expect(await screen.findByRole('tab', { name: 'Word filter' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Moderators' })).toBeInTheDocument();
    const drawer = await screen.findByRole('dialog', { name: /Report on a comment/ });
    await userEvent.click(await within(drawer).findByRole('button', { name: 'Dismiss reports' }));
    const dialog = await screen.findByRole('dialog', { name: /Dismiss reports this comment/ });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Dismiss reports' }));
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/admin/moderation/queue/Comment/cmt_1/dismiss')).toBe(true));
  });
});

describe('assistant replies in the moderation queue', () => {
  const snapshotMessage = (position, role, content, extra = {}) => ({ id: 'acm_' + position, position, role, content, status: 'Completed', isReported: false, createdUtc: '2026-11-10T00:00:0' + position + 'Z', labels: [], tags: {}, ...extra });
  const snapshot = {
    id: 'ams_1', messageId: 'acm_2', reportId: 'rpt_9', reason: 'Report', conversationId: 'acv_1', conversationDeleted: true,
    assistant: { id: 'ast_1', name: 'Helper', deleted: false }, owner: user('usr_7', 'Riley'),
    reportedMessage: snapshotMessage(2, 'Assistant', 'A bad reply', { isReported: true }),
    model: { endpointId: 'mep_1', endpointName: 'Hosted chat', model: 'example-chat-model', usedFallback: false },
    citations: [{ index: 1, targetType: 'FeedPost', targetId: 'fpo_1', title: 'Schedule', path: '/feed/fpo_1' }],
    messages: { objects: [snapshotMessage(1, 'User', 'A question'), snapshotMessage(2, 'Assistant', 'A bad reply', { isReported: true })], totalRecords: 3, enumerate: '/api/v1/admin/moderation/queue/AssistantMessage/acm_2/snapshot/enumerate' },
    createdUtc: '2026-11-10T00:00:00Z', expiresUtc: '2027-11-10T00:00:00Z', labels: [], tags: {}
  };

  it('offers the Assistant scope and target type to SystemAdmin only', () => {
    expect(queueFilterOptions(true).scopeTypes.map((o) => o.value)).toContain('Assistant');
    expect(queueFilterOptions(true).targetTypes.map((o) => o.value)).toContain('AssistantMessage');
    expect(queueFilterOptions(false).scopeTypes.map((o) => o.value)).not.toContain('Assistant');
    expect(queueFilterOptions(false).targetTypes.map((o) => o.value)).not.toContain('AssistantMessage');
  });

  it('shows the conversation snapshot, loads more messages, and only offers Dismiss', async () => {
    const { fetchImpl, calls } = adminFetch((call) => {
      if (call.path === '/api/v1/admin/moderation/queue') return json(enumPage([{ ...queueRow, targetType: 'AssistantMessage', targetId: 'acm_2', targetState: 'Completed', preview: { title: 'Helper', excerpt: 'A bad reply', hasMedia: false }, scope: { type: 'Assistant', id: 'ast_1', name: 'Helper' }, isOverdue: false }]));
      if (call.path === '/api/v1/admin/moderation/queue/AssistantMessage/acm_2') return json({ targetType: 'AssistantMessage', targetId: 'acm_2', target: snapshot, reports: [{ id: 'rpt_9', reporter: user('usr_7', 'Riley'), reason: 'Harassment', status: 'Open', createdUtc: '2026-11-10T00:00:00Z' }], actions: enumPage([]), authorHistory: { removedCount90d: 0, hiddenCount90d: 0, reportsAgainst90d: 0, isMuted: false } });
      if (call.path === '/api/v1/admin/moderation/queue/AssistantMessage/acm_2/snapshot/enumerate') return json(enumPage([snapshotMessage(1, 'User', 'A question'), snapshotMessage(2, 'Assistant', 'A bad reply', { isReported: true }), snapshotMessage(3, 'User', 'A follow-up')], { totalRecords: 3 }));
      return undefined;
    });
    renderAdmin(<ModerationView />, { fetchImpl, path: '/admin/moderation?target=AssistantMessage:acm_2', routePath: '/admin/moderation' });
    const drawer = await screen.findByRole('dialog', { name: /Report on an assistant reply/ });
    const panel = await within(drawer).findByTestId('assistant-snapshot');
    expect(within(panel).getByText('Riley')).toBeInTheDocument();
    expect(within(panel).getByText('Hosted chat / example-chat-model')).toBeInTheDocument();
    expect(within(panel).getByText(strings.admin.moderation.snapshot.conversationDeleted)).toBeInTheDocument();
    expect(within(panel).getByTestId('snapshot-message-2')).toHaveTextContent(strings.admin.moderation.snapshot.reported);
    await userEvent.click(within(panel).getByRole('button', { name: strings.admin.moderation.snapshot.loadMore }));
    expect(await within(panel).findByText('A follow-up')).toBeInTheDocument();
    expect(calls.find((c) => c.path.endsWith('/snapshot/enumerate')).body).toEqual({ maxResults: 50 });
    expect(within(drawer).getByRole('button', { name: 'Dismiss reports' })).toBeInTheDocument();
    expect(within(drawer).queryByRole('button', { name: 'Remove' })).toBeNull();
    expect(within(drawer).queryByRole('button', { name: 'Hide' })).toBeNull();
    expect(within(drawer).queryByText(strings.admin.moderation.authorHistoryTitle)).toBeNull();
  });
});

describe('review requests', () => {
  it('shows the reviewer from feedback.reviewer', async () => {
    const { fetchImpl } = adminFetch((call) => {
      if (call.path === '/api/v1/admin/threads/enumerate') {
        return json(enumPage([
          { id: 'thr_1', title: 'Add dark mode', forum: { id: 'frm_1', name: 'Ideas' }, author: user('usr_2', 'Sam'), feedback: { status: 'InReview', reviewer: user('usr_9', 'Morgan Mod') }, createdUtc: '2026-11-10T00:00:00Z' },
          { id: 'thr_2', title: 'Fix the map', forum: { id: 'frm_1', name: 'Ideas' }, author: user('usr_3', 'Ash'), feedback: { status: 'Open', reviewer: null }, createdUtc: '2026-11-10T01:00:00Z' }
        ]));
      }
      return undefined;
    });
    renderAdmin(<ReviewRequestsTab />, { fetchImpl });
    expect(await screen.findByRole('columnheader', { name: strings.admin.reviewRequests.columns.reviewer })).toBeInTheDocument();
    const rows = screen.getAllByRole('row');
    expect(within(rows.find((r) => r.textContent.includes('Add dark mode'))).getByText('Morgan Mod')).toBeInTheDocument();
    expect(within(rows.find((r) => r.textContent.includes('Fix the map'))).getAllByText('-').length).toBeGreaterThan(0);
  });
});

describe('word filter editor', () => {
  it('adds, imports, tests, and deletes terms', async () => {
    const { fetchImpl, calls } = adminFetch((call) => {
      if (call.path === '/api/v1/admin/moderation/word-filter' && call.method === 'GET') return json(enumPage([{ id: 'wft_1', term: 'badword', matchType: 'Word', action: 'Block', note: null, createdByName: 'Admin', createdUtc: '2026-11-01T00:00:00Z', updatedUtc: '2026-11-01T00:00:00Z', hitCount30d: 4 }], { totalRecords: 1 }));
      if (call.path === '/api/v1/admin/moderation/word-filter' && call.method === 'POST') return call.body.term === 'badword' ? json({ error: 'Conflict', message: 'dup' }, 409) : json({ id: 'wft_2' }, 201);
      if (call.path === '/api/v1/admin/moderation/word-filter/import') return json({ added: 2, skipped: 1 });
      if (call.path === '/api/v1/admin/moderation/word-filter/test') return json({ matches: [{ termId: 'wft_1', term: 'badword', action: 'Block', start: 5, length: 7 }] });
      if (call.method === 'DELETE') return json(null, 204);
      return undefined;
    });
    renderAdmin(<ModerationView />, { fetchImpl, path: '/admin/moderation?tab=wordFilter', routePath: '/admin/moderation' });
    expect(await screen.findByText('badword')).toBeInTheDocument();
    expect(calls.find((c) => c.path === '/api/v1/admin/moderation/word-filter').query).toMatchObject({ ordering: 'TermAscending' });

    await userEvent.click(screen.getByRole('button', { name: 'Add term' }));
    let dialog = await screen.findByRole('dialog', { name: 'Add a term' });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Save term' }));
    expect(within(dialog).getByText('Enter a term.')).toBeInTheDocument();
    await userEvent.type(within(dialog).getByLabelText('Term'), 'badword');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Save term' }));
    expect(await within(dialog).findByText('That term is already on the list.')).toBeInTheDocument();
    await userEvent.clear(within(dialog).getByLabelText('Term'));
    await userEvent.type(within(dialog).getByLabelText('Term'), 'newterm');
    await userEvent.selectOptions(within(dialog).getByLabelText('Action'), 'Flag');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Save term' }));
    await waitFor(() => expect(calls.filter((c) => c.method === 'POST' && c.path === '/api/v1/admin/moderation/word-filter').at(-1).body).toEqual({ term: 'newterm', matchType: 'Word', action: 'Flag', note: null }));

    await userEvent.click(screen.getByRole('button', { name: 'Import' }));
    dialog = await screen.findByRole('dialog', { name: 'Import terms' });
    await userEvent.type(within(dialog).getByLabelText(/Terms, one per line/), 'one{enter}two{enter}one');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Import terms' }));
    expect(await screen.findByText('2 added, 1 skipped as duplicates.')).toBeInTheDocument();
    expect(calls.find((c) => c.path.endsWith('/import')).body).toEqual({ terms: ['one', 'two'], matchType: 'Word', action: 'Block' });

    await userEvent.type(screen.getByLabelText('Text to check'), 'some badword here');
    await userEvent.click(screen.getByRole('button', { name: 'Test' }));
    expect(await screen.findByText('1 match')).toBeInTheDocument();
    expect(document.querySelector('mark')).toHaveTextContent('badword');

    await userEvent.click(screen.getByRole('button', { name: 'Actions for badword' }));
    await userEvent.click(screen.getByRole('menuitem', { name: 'Delete' }));
    await userEvent.click(within(await screen.findByRole('dialog', { name: 'Delete "badword"?' })).getByRole('button', { name: 'Delete' }));
    await waitFor(() => expect(calls.some((c) => c.method === 'DELETE' && c.path === '/api/v1/admin/moderation/word-filter/wft_1')).toBe(true));
  });
});

describe('admin feed and media', () => {
  it('lists feed posts as an enumeration with label filters from the URL', async () => {
    const { fetchImpl, calls } = adminFetch((call) => {
      if (call.path === '/api/v1/admin/feed') return json(enumPage([{ id: 'fpo_1', kind: 'Original', title: 'Welcome', excerpt: 'Hi', author: user('usr_1', 'Owner'), state: 'Scheduled', visibility: 'Public', isPinned: false, publishAtUtc: '2026-12-01T00:00:00Z', publishedUtc: null, createdUtc: '2026-11-10T00:00:00Z', attachmentCount: 0, hasVideo: false, commentCount: 0, reactionTotal: 0, openReportCount: 0 }], { totalRecords: 1 }));
      if (call.path === '/api/v1/admin/feed/fpo_1/publish-now') return json({});
      return undefined;
    });
    renderAdmin(<FeedAdminView />, { fetchImpl, path: '/admin/feed?labels=news', routePath: '/admin/feed' });
    expect(await screen.findByText('Welcome')).toBeInTheDocument();
    expect(calls.find((c) => c.path === '/api/v1/admin/feed').search).toBe('?maxResults=50&ordering=PublishedDescending&labels=news&labelMatch=All');
    expect(screen.getByText('Public')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Actions for Welcome' }));
    await userEvent.click(screen.getByRole('menuitem', { name: 'Publish now' }));
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/admin/feed/fpo_1/publish-now')).toBe(true));
  });

  it('shows the media library summary and filters by kind and status', async () => {
    const { fetchImpl, calls } = adminFetch((call) => {
      if (call.path === '/api/v1/admin/media/summary') return json({ totalCount: 3, totalStoredBytes: 2048, byKind: { Image: 2, Video: 1 }, byStatus: { Processing: 1 }, failed24h: 1 });
      if (call.path === '/api/v1/admin/media') return json(enumPage([{ media: { id: 'med_1', kind: 'Video', status: 'Failed', failureReason: 'VideoTooLong', sizeBytes: 100 }, owner: user('usr_2', 'Alex'), attachedTo: null, storedBytes: 100, fileName: 'a.mov', createdUtc: '2026-11-10T00:00:00Z' }], { totalRecords: 1 }));
      return undefined;
    });
    renderAdmin(<MediaLibraryView />, { fetchImpl, path: '/admin/media', routePath: '/admin/media' });
    expect(await screen.findByText('Failed (24 hours)')).toBeInTheDocument();
    await userEvent.selectOptions(screen.getByLabelText('Kind'), 'Video');
    await userEvent.selectOptions(screen.getByLabelText('Status'), 'Failed');
    await userEvent.click(screen.getByRole('button', { name: 'Apply filters' }));
    await waitFor(() => expect(calls.filter((c) => c.path === '/api/v1/admin/media').at(-1).query).toEqual({ maxResults: '50', ordering: 'CreatedDescending', kind: 'Video', status: 'Failed' }));
    await userEvent.click(screen.getByRole('button', { name: 'Actions for med_1' }));
    expect(screen.getByRole('menuitem', { name: 'Reprocess' })).toBeInTheDocument();
  });
});

describe('member posting permissions', () => {
  it('shows the Posting group with defaults and saves only changed flags', async () => {
    const member = { id: 'usr_2', displayName: 'Alex', email: 'a@example.test', flags: { canPostToFeed: false, canUploadVideo: true, isMuted: false }, roles: [], labels: [], tags: {} };
    const { fetchImpl, calls } = adminFetch((call) => {
      if (call.path === '/api/v1/admin/members/usr_2' && call.method === 'GET') return json(member);
      if (call.path === '/api/v1/admin/members/usr_2/flags') return json({ ...member, flags: { ...member.flags, ...call.body } });
      if (call.path === '/api/v1/admin/moderators') return json({ user: user('usr_2', 'Alex'), roles: [] });
      return undefined;
    });
    renderAdmin(<MemberDetailDrawer id="usr_2" onClose={() => {}} onChanged={() => {}} />, { fetchImpl });
    const posting = (await screen.findByRole('heading', { name: 'Posting' })).closest('section');
    expect(within(posting).getByRole('checkbox', { name: /Can post \(posts/ })).toBeChecked();
    expect(within(posting).getByRole('checkbox', { name: /Can attach photos and video/ })).toBeChecked();
    expect(within(posting).getByRole('checkbox', { name: /Can create feed posts/ })).not.toBeChecked();
    expect(within(posting).getByRole('button', { name: 'Mute' })).toBeInTheDocument();
    await userEvent.click(within(posting).getByRole('checkbox', { name: /Can create feed posts/ }));
    await userEvent.click(within(posting).getByRole('checkbox', { name: /Can attach photos and video/ }));
    await userEvent.click(within(posting).getByRole('button', { name: 'Save permissions' }));
    await waitFor(() => expect(calls.find((c) => c.path === '/api/v1/admin/members/usr_2/flags').body).toEqual({ canPostToFeed: true, canPostMedia: false }));
    await userEvent.click(screen.getByRole('checkbox', { name: /Feed moderator/ }));
    await waitFor(() => expect(calls.find((c) => c.path === '/api/v1/admin/moderators' && c.method === 'POST').body).toEqual({ userId: 'usr_2', resourceType: 'Feed' }));
    // Without the server's forums capability the forum-mutes section is a coming-soon note with no calls.
    expect(screen.getByTestId('forum-mutes-coming-soon')).toHaveTextContent('Forum mutes are coming soon');
    expect(calls.some((c) => c.path.includes('forum-mutes') || c.path.includes('/forums'))).toBe(false);
  });
});

describe('moderator console access', () => {
  it('lets a feed moderator in, shows only Home and Moderation, and redirects other sections', async () => {
    const me = makeMe({ roles: [{ role: 'FeedModerator', resourceType: 'Feed', resourceId: '*' }] });
    const { fetchImpl } = createFetch((call) => {
      if (call.path === '/api/v1/auth/refresh') return json(authResponse(me));
      if (call.path === '/api/v1/health') return json({ status: 'Healthy', version: '1.0.0' });
      if (call.path === '/api/v1/admin/moderation/queue') return json(enumPage([], { totalRecords: 0 }));
      if (call.path === '/api/v1/media/grant') return json({ expiresUtc: '2099-01-01T00:00:00Z' });
      return undefined;
    });
    render(<ThemeProvider><MemoryRouter initialEntries={['/admin/members']}><AppRoutes fetchImpl={fetchImpl} restoreOnLoad /></MemoryRouter></ThemeProvider>);
    expect(await screen.findByText('The queue is clear. New reports appear here.')).toBeInTheDocument();
    const nav = screen.getByRole('navigation', { name: 'Admin navigation' });
    expect(within(nav).getAllByRole('link').map((a) => a.textContent)).toEqual(['Home', 'Moderation']);
    expect(screen.queryByRole('tab', { name: 'Word filter' })).toBeNull();
  });
});
