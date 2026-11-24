import { describe, expect, it } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route } from 'react-router-dom';
import { AuthProvider } from '../src/context/AuthContext.jsx';
import { ToastProvider } from '../src/context/ToastContext.jsx';
import { createForumsFake, makeForum, makePoll, makeThread, makeThreadComment, moderatorViewer } from '@trelliskit/shared/testing';
import ForumsIndexView from '../src/views/member/ForumsIndexView.jsx';
import ForumView from '../src/views/member/ForumView.jsx';
import ThreadView from '../src/views/member/ThreadView.jsx';
import SearchView from '../src/views/member/SearchView.jsx';
import ForumsFollowingView from '../src/views/member/ForumsFollowingView.jsx';
import PostCard from '../src/components/feed/PostCard.jsx';
import PollCard from '../src/components/forums/PollCard.jsx';
import SnippetText from '../src/components/forums/SnippetText.jsx';
import ForumsAdminView, { forumBody } from '../src/views/admin/ForumsAdminView.jsx';
import ThreadsAdminView from '../src/views/admin/ThreadsAdminView.jsx';
import TrendingView from '../src/views/admin/TrendingView.jsx';
import ModerationView from '../src/views/admin/ModerationView.jsx';
import { groupedAdminRoutes } from '../src/views/admin/adminRoutes.js';
import { webPathFor } from '../src/utils/engagement.js';
import { appForumPath, appThreadPath } from '../src/utils/forums.js';
import { createFetch, json, makeMe, renderAdmin } from './helpers.jsx';
import { makePost, memberFetch, renderMember, siteWithFeatures, user } from './engagementHelpers.jsx';

const general = makeForum('frm_g', { slug: 'general', name: 'General', section: 'Community', sortOrder: 10, unreadThreadCount: 3, threadCount: 3, allowedThreadTypes: ['Discussion', 'Question', 'Poll'], defaultOrdering: 'Active' });
const help = makeForum('frm_h', { slug: 'help', name: 'Help', section: 'Support', sortOrder: 20 });
const reviews = makeForum('frm_r', { slug: 'reviews', name: 'Reviews', sortOrder: 30, viewer: { canCreateThread: true, canFollow: true, canModerate: false, allowedThreadTypes: ['Feedback'] }, allowedThreadTypes: ['Feedback'] });

function seed(extra = {}) {
  return {
    forums: [general, help, reviews],
    threads: [
      makeThread('thr_pin', general, { title: 'Welcome to the forum', isPinned: true, pinnedUtc: '2026-11-10T00:00:00Z' }),
      makeThread('thr_q', general, { type: 'Question', title: 'How do I reset my profile photo?', lastActivityUtc: '2026-11-10T20:00:00Z', isUnread: true, trendingScore: 2 }),
      makeThread('thr_v', general, { type: 'Poll', title: 'Which night works?', poll: makePoll('pol_1', 'thr_v', ['Tuesday', 'Thursday']) })
    ],
    comments: [makeThreadComment('cmt_1', 'thr_q', { body: 'Try the reset option under Account' })],
    ...extra
  };
}

/** A member session whose forum routes are served by the shared fake. */
function forumFetch(fake, me) {
  return memberFetch((call) => {
    const result = fake.handle({ method: call.method, path: call.path, query: call.query, body: call.body });
    if (!result) return undefined;
    return result.status === 204 ? new Response(null, { status: 204 }) : json(result.body, result.status);
  }, me);
}

describe('forum paths and helpers', () => {
  it('builds web paths and maps notification paths', () => {
    expect(appForumPath(general)).toBe('/app/forums/general');
    expect(appForumPath('frm_x')).toBe('/app/forums/frm_x');
    expect(appThreadPath('thr_1', 'cmt_2')).toBe('/app/threads/thr_1?comment=cmt_2');
    expect(webPathFor('/threads/thr_1?comment=cmt_2')).toBe('/app/threads/thr_1?comment=cmt_2');
    expect(webPathFor('/forums/frm_1')).toBe('/app/forums/frm_1');
  });

  it('renders search snippets as text with marks, never HTML', () => {
    const { container } = render(<SnippetText snippet={{ text: 'try <b>reset</b> now', highlights: [{ start: 4, length: 12 }] }} />);
    expect(container.querySelector('b')).toBeNull();
    expect(container.querySelector('mark')).toHaveTextContent('<b>reset</b>');
  });

  it('forum admin sections are visible to forum moderators only', () => {
    expect(groupedAdminRoutes({ features: {} }, { moderatorOnly: true, forumModerator: true }).flatMap((g) => g.routes.map((r) => r.id))).toEqual(['home', 'threads', 'trending', 'moderation']);
    // Forum sections stay in the sidebar whatever the server reports; without the capability they open a coming-soon page.
    expect(groupedAdminRoutes({ features: { forums: false } }).flatMap((g) => g.routes.map((r) => r.id))).toContain('forums');
    expect(forumBody({ name: ' Show ', slug: '', description: '', guidelines: '', iconEmoji: '', iconMediaId: 'med_1', color: '', section: '', audience: 'Members', threadCreation: 'Members', allowedThreadTypes: ['Discussion'], defaultOrdering: 'Hot', allowMedia: false, allowVideo: true, maxOpenFeedbackPerMember: '2', labels: [], tags: {} }))
      .toMatchObject({ name: 'Show', icon: { emoji: null, mediaId: 'med_1' }, allowVideo: false, maxOpenFeedbackPerMember: 2, color: null });
  });
});

describe('forum index', () => {
  it('groups forums by section with unread counts and follows a forum', async () => {
    const fake = createForumsFake(seed());
    const { fetchImpl, calls } = forumFetch(fake);
    await renderMember(<ForumsIndexView />, { fetchImpl, path: '/app/forums', routePath: '/app/forums' });
    expect(await screen.findByRole('heading', { level: 2, name: 'Community' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 2, name: 'Support' })).toBeInTheDocument();
    expect(screen.getByLabelText('3 threads with new activity')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'General' })).toHaveAttribute('href', '/app/forums/general');
    await userEvent.click(screen.getByRole('button', { name: 'Follow Help' }));
    await waitFor(() => expect(calls.some((c) => c.method === 'PUT' && c.path === '/api/v1/forums/frm_h/follow')).toBe(true));
    expect(await screen.findByRole('button', { name: 'Stop following Help' })).toHaveAttribute('aria-pressed', 'true');
    expect(calls.find((c) => c.path === '/api/v1/forums/enumerate').body.maxResults).toBe(100);
  });
});

describe('forum page', () => {
  it('shows pinned threads first, filters by type, orders by Hot, and searches with highlighted snippets', async () => {
    const fake = createForumsFake(seed());
    const { fetchImpl, calls } = forumFetch(fake);
    await renderMember(<ForumView />, { fetchImpl, path: '/app/forums/general', routePath: '/app/forums/:slug' });
    expect(await screen.findByRole('heading', { level: 1, name: 'General' })).toBeInTheDocument();
    const pinned = await screen.findByRole('region', { name: /Pinned/ });
    expect(within(pinned).getByRole('link', { name: 'Welcome to the forum' })).toHaveAttribute('href', '/app/threads/thr_pin');
    expect(screen.getByText('New activity:')).toBeInTheDocument();
    const enumerations = () => calls.filter((c) => c.path === '/api/v1/forums/frm_g/threads/enumerate');
    expect(enumerations()[0].body).toMatchObject({ maxResults: 20, ordering: 'Active' });

    await userEvent.click(screen.getByRole('button', { name: 'Question' }));
    await waitFor(() => expect(enumerations().at(-1).body.filters).toEqual({ type: ['Question'] }));
    expect(await screen.findByRole('link', { name: 'How do I reset my profile photo?' })).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByRole('link', { name: 'Which night works?' })).toBeNull());
    await userEvent.click(screen.getByRole('button', { name: 'Question' }));

    await userEvent.selectOptions(screen.getByLabelText('Order'), 'Hot');
    await waitFor(() => expect(enumerations().at(-1).body.ordering).toBe('Hot'));

    await userEvent.type(screen.getByPlaceholderText('Search threads'), 'Account{Enter}');
    await waitFor(() => expect(enumerations().at(-1).body.search).toBe('Account'));
    const mark = await screen.findByText('Account', { selector: 'mark' });
    expect(mark.closest('article').querySelector('a')).toHaveAttribute('href', '/app/threads/thr_q?comment=cmt_1');
  });

  it('creates a poll thread with the type picker limited to allowed types', async () => {
    const fake = createForumsFake(seed());
    const { fetchImpl, calls } = forumFetch(fake);
    await renderMember(<ForumView />, { fetchImpl, path: '/app/forums/general', routePath: '/app/forums/:slug', extraRoutes: <Route path="/app/threads/:id" element={<p>Thread page</p>} /> });
    await userEvent.click(await screen.findByRole('button', { name: 'New thread' }));
    const picker = screen.getByRole('group', { name: 'Thread type' });
    expect(within(picker).getAllByRole('radio').map((r) => r.value)).toEqual(['Discussion', 'Question', 'Poll']);
    await userEvent.click(within(picker).getByRole('radio', { name: /Poll/ }));
    await userEvent.type(screen.getByLabelText('Poll: Title'), 'Best day?');
    await userEvent.click(screen.getByRole('button', { name: 'Post thread' }));
    expect(await screen.findByText('Fill in every option or remove the empty ones.')).toBeInTheDocument();
    await userEvent.type(screen.getByLabelText('Option 1'), 'Monday');
    await userEvent.type(screen.getByLabelText('Option 2'), 'Friday');
    await userEvent.selectOptions(screen.getByLabelText('Show results'), 'Always');
    await userEvent.click(screen.getByRole('button', { name: 'Post thread' }));
    expect(await screen.findByText('Thread page')).toBeInTheDocument();
    const create = calls.find((c) => c.method === 'POST' && c.path === '/api/v1/forums/frm_g/threads');
    expect(create.body).toMatchObject({ type: 'Poll', title: 'Best day?', feedback: null, poll: { options: [{ label: 'Monday' }, { label: 'Friday' }], resultsVisibility: 'Always', multipleChoice: false } });
    expect(create.body.poll.voterVisibility).toBeUndefined();
  });

  it('says when to try again after 429 too many threads (Retry-After)', async () => {
    const fake = createForumsFake(seed());
    const { fetchImpl } = memberFetch((call) => {
      if (call.method === 'POST' && call.path === '/api/v1/forums/frm_g/threads') {
        return new Response(JSON.stringify({ error: 'RateLimited', message: 'Too many requests. Try again later.' }), { status: 429, headers: { 'content-type': 'application/json', 'retry-after': '1500' } });
      }
      const result = fake.handle({ method: call.method, path: call.path, query: call.query, body: call.body });
      if (!result) return undefined;
      return result.status === 204 ? new Response(null, { status: 204 }) : json(result.body, result.status);
    });
    await renderMember(<ForumView />, { fetchImpl, path: '/app/forums/general', routePath: '/app/forums/:slug' });
    await userEvent.click(await screen.findByRole('button', { name: 'New thread' }));
    await userEvent.type(screen.getByLabelText('Title'), 'One more thread');
    await userEvent.type(screen.getByRole('combobox', { name: 'Body' }), 'Hello there');
    await userEvent.click(screen.getByRole('button', { name: 'Post thread' }));
    expect(await screen.findByText('You are doing that too often. Try again in 25 minutes.')).toBeInTheDocument();
  });

  it('requires a video for Feedback threads', async () => {
    const fake = createForumsFake(seed());
    const { fetchImpl, calls } = forumFetch(fake, makeMe({ flags: { canUploadVideo: true } }));
    await renderMember(<ForumView />, { fetchImpl, path: '/app/forums/reviews', routePath: '/app/forums/:slug' });
    await userEvent.click(await screen.findByRole('button', { name: 'New thread' }));
    expect(screen.queryByRole('group', { name: 'Thread type' })).toBeNull();
    await userEvent.type(screen.getByLabelText('Title'), 'Review my round');
    await userEvent.click(screen.getByRole('button', { name: 'Post thread' }));
    expect(await screen.findAllByText('Attach a video to request feedback.')).toHaveLength(2);
    expect(calls.some((c) => c.method === 'POST' && c.path.endsWith('/threads'))).toBe(false);
  });

  it('shows a not found state for unknown forums', async () => {
    const fake = createForumsFake(seed());
    const { fetchImpl } = forumFetch(fake);
    await renderMember(<ForumView />, { fetchImpl, path: '/app/forums/nope', routePath: '/app/forums/:slug' });
    expect(await screen.findByText('This forum is not available.')).toBeInTheDocument();
  });
});

describe('thread page', () => {
  it('votes in a poll and shows results with text alternatives', async () => {
    const fake = createForumsFake(seed());
    const { fetchImpl, calls } = forumFetch(fake);
    await renderMember(<ThreadView />, { fetchImpl, path: '/app/threads/thr_v', routePath: '/app/threads/:id' });
    expect(await screen.findByRole('heading', { level: 1, name: 'Which night works?' })).toBeInTheDocument();
    expect(screen.getByText('Results show after you vote.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Vote' }));
    expect(await screen.findByText('Choose an option.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('radio', { name: 'Thursday' }));
    await userEvent.click(screen.getByRole('button', { name: 'Vote' }));
    const results = await screen.findByRole('list', { name: 'Poll results' });
    expect(within(results).getByText(/Thursday: 1 vote, 100 percent/)).toBeInTheDocument();
    expect(within(results).getByText(/Tuesday: 0 votes, 0 percent/)).toBeInTheDocument();
    expect(calls.find((c) => c.method === 'PUT' && c.path === '/api/v1/polls/pol_1/votes').body).toEqual({ optionIds: ['pop_1_2'] });
    expect(screen.getByText('1 voter')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Remove my vote' }));
    await waitFor(() => expect(calls.some((c) => c.method === 'DELETE' && c.path === '/api/v1/polls/pol_1/votes')).toBe(true));
  });

  it('lets the author edit poll options before the first vote (section 6.4)', async () => {
    const mine = makeThread('thr_pe', general, { type: 'Poll', title: 'Pick a night', author: user('usr_1', 'Sam Rivera'), poll: makePoll('pol_e', 'thr_pe', ['Tuesday', 'Thursday']), viewer: { canEdit: true, canDelete: true, canComment: true, canReact: true, canFollow: true, canClosePoll: true } });
    const fake = createForumsFake(seed({ threads: [mine] }));
    const { fetchImpl, calls } = forumFetch(fake);
    await renderMember(<ThreadView />, { fetchImpl, path: '/app/threads/thr_pe', routePath: '/app/threads/:id' });
    await screen.findByRole('heading', { level: 1, name: 'Pick a night' });
    await userEvent.click(screen.getByRole('button', { name: 'Thread actions' }));
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Edit thread' }));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByLabelText('Option 1')).toHaveValue('Tuesday');
    expect(within(dialog).queryByText(/options and settings are fixed/)).toBeNull();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Add an option' }));
    await userEvent.type(within(dialog).getByLabelText('Option 3'), 'Saturday');
    await userEvent.click(within(dialog).getByRole('checkbox', { name: 'Allow more than one choice' }));
    await userEvent.click(within(dialog).getByRole('button', { name: 'Save changes' }));
    await waitFor(() => expect(calls.some((c) => c.method === 'PUT' && c.path === '/api/v1/threads/thr_pe')).toBe(true));
    const put = calls.find((c) => c.method === 'PUT' && c.path === '/api/v1/threads/thr_pe');
    expect(put.body.poll).toMatchObject({ options: [{ label: 'Tuesday' }, { label: 'Thursday' }, { label: 'Saturday' }], multipleChoice: true, voterVisibility: 'Anonymous' });
    expect(await screen.findByText('Saturday')).toBeInTheDocument();
  });

  it('after the first vote only the close time is editable, and a late vote locks the form', async () => {
    const voted = makeThread('thr_pv', general, { type: 'Poll', title: 'Voted poll', author: user('usr_1', 'Sam Rivera'), poll: makePoll('pol_v', 'thr_pv', ['Tuesday', 'Thursday'], { totalVoters: 2 }), viewer: { canEdit: true, canComment: true, canReact: true, canFollow: true } });
    const fresh = makeThread('thr_pr', general, { type: 'Poll', title: 'Racing poll', author: user('usr_1', 'Sam Rivera'), poll: makePoll('pol_r', 'thr_pr', ['A', 'B']), viewer: { canEdit: true, canComment: true, canReact: true, canFollow: true } });
    const fake = createForumsFake(seed({ threads: [voted, fresh] }));
    const { fetchImpl, calls } = forumFetch(fake);
    const view = await renderMember(<ThreadView />, { fetchImpl, path: '/app/threads/thr_pv', routePath: '/app/threads/:id' });
    await screen.findByRole('heading', { level: 1, name: 'Voted poll' });
    await userEvent.click(screen.getByRole('button', { name: 'Thread actions' }));
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Edit thread' }));
    let dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText(/options and settings are fixed/)).toBeInTheDocument();
    expect(within(dialog).getByLabelText('Option 1')).toBeDisabled();
    expect(within(dialog).getByRole('checkbox', { name: 'Allow more than one choice' })).toBeDisabled();
    expect(within(dialog).getByLabelText('Close automatically at (optional)')).not.toBeDisabled();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Save changes' }));
    await waitFor(() => expect(calls.some((c) => c.method === 'PUT' && c.path === '/api/v1/threads/thr_pv')).toBe(true));
    expect(calls.find((c) => c.method === 'PUT' && c.path === '/api/v1/threads/thr_pv').body.poll).toBeUndefined();
    view.unmount();

    await renderMember(<ThreadView />, { fetchImpl, path: '/app/threads/thr_pr', routePath: '/app/threads/:id' });
    await screen.findByRole('heading', { level: 1, name: 'Racing poll' });
    await userEvent.click(screen.getByRole('button', { name: 'Thread actions' }));
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Edit thread' }));
    dialog = await screen.findByRole('dialog');
    fake.state.threads.find((t) => t.id === 'thr_pr').poll.totalVoters = 1;
    await userEvent.clear(within(dialog).getByLabelText('Option 2'));
    await userEvent.type(within(dialog).getByLabelText('Option 2'), 'C');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Save changes' }));
    expect(await within(dialog).findByText('Poll options cannot change after the first vote.')).toBeInTheDocument();
    expect(within(dialog).getByText(/options and settings are fixed/)).toBeInTheDocument();
    expect(within(dialog).getByLabelText('Option 2')).toHaveValue('B');
  });

  it('accepts an answer from the comment menu and shows it above the tree', async () => {
    const fake = createForumsFake(seed({ threads: [makeThread('thr_q', general, { type: 'Question', title: 'How do I reset?', author: user('usr_1', 'Sam Rivera'), viewer: { canEdit: true, canDelete: true, canComment: true, canReact: true, canAcceptAnswer: true, canFollow: true } })] }));
    const { fetchImpl, calls } = forumFetch(fake);
    await renderMember(<ThreadView />, { fetchImpl, path: '/app/threads/thr_q', routePath: '/app/threads/:id' });
    expect(await screen.findByText('Try the reset option under Account')).toBeInTheDocument();
    expect(calls.some((c) => c.path === '/api/v1/threads/thr_q/comments')).toBe(true);
    await userEvent.click(screen.getByRole('button', { name: 'Comment actions' }));
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Accept as answer' }));
    expect(await screen.findByRole('heading', { name: /Accepted answer/ })).toBeInTheDocument();
    expect(calls.find((c) => c.path === '/api/v1/threads/thr_q/accept-answer').body).toEqual({ commentId: 'cmt_1' });
    expect((await screen.findAllByText('Accepted answer')).length).toBeGreaterThan(0);
  });

  it('routes a linked thread conversation to the feed post and freezes locked threads', async () => {
    const post = { ...makePost('fpo_9', { title: 'Announcement', body: 'Big news' }) };
    const linked = makeThread('thr_l', general, { kind: 'LinkedFeedPost', title: 'Announcement', body: null, commentTarget: { type: 'FeedPost', id: 'fpo_9' }, source: { type: 'FeedPost', id: 'fpo_9', title: 'Announcement', author: user('usr_2', 'Alex Chen') } });
    const fake = createForumsFake(seed({ threads: [linked, makeThread('thr_lk', general, { title: 'Closed topic', isLocked: true, lockReason: 'Resolved', viewer: { canReact: true, canComment: true } })], feedPosts: [post], comments: [] }));
    const { fetchImpl, calls } = forumFetch(fake);
    const view = await renderMember(<ThreadView />, { fetchImpl, path: '/app/threads/thr_l', routePath: '/app/threads/:id' });
    expect(await screen.findByText('Big news')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open the feed post' })).toHaveAttribute('href', '/app/feed/fpo_9');
    await userEvent.type(await screen.findByPlaceholderText('Add a comment'), 'Shared conversation');
    await userEvent.click(screen.getByRole('button', { name: 'Comment' }));
    await waitFor(() => expect(calls.some((c) => c.method === 'POST' && c.path === '/api/v1/feed/fpo_9/comments')).toBe(true));
    expect(calls.some((c) => c.path.startsWith('/api/v1/threads/thr_l/comments'))).toBe(false);
    view.unmount();

    await renderMember(<ThreadView />, { fetchImpl, path: '/app/threads/thr_lk', routePath: '/app/threads/:id' });
    expect(await screen.findByText(/This thread is locked: Resolved/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Add a reaction' })).toBeNull();
    expect(screen.queryByPlaceholderText('Add a comment')).toBeNull();
  });

  it('drives the moderator menu from viewer: pin, lock with a reason, and promote to the feed', async () => {
    const fake = createForumsFake(seed({ threads: [makeThread('thr_m', general, { title: 'Moderate me', viewer: { ...moderatorViewer, canComment: true, canReact: true, canPromote: true, canReport: true } })] }));
    const { fetchImpl, calls } = forumFetch(fake, makeMe({ roles: [{ role: 'Moderator', resourceType: 'Forum', resourceId: '*' }] }));
    await renderMember(<ThreadView />, { fetchImpl, path: '/app/threads/thr_m', routePath: '/app/threads/:id' });
    await screen.findByRole('heading', { level: 1, name: 'Moderate me' });
    await userEvent.click(screen.getByRole('button', { name: 'Thread actions' }));
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Pin to the top' }));
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/threads/thr_m/pin')).toBe(true));
    expect(await screen.findByText('Pinned')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Thread actions' }));
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Lock thread' }));
    const dialog = await screen.findByRole('dialog', { name: 'Lock this thread?' });
    await userEvent.type(within(dialog).getByLabelText(/Reason/), 'Answered');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Lock thread' }));
    expect(await screen.findByText(/This thread is locked: Answered/)).toBeInTheDocument();
    expect(calls.find((c) => c.path === '/api/v1/threads/thr_m/lock').body).toEqual({ reason: 'Answered' });

    await userEvent.click(screen.getByRole('button', { name: 'Thread actions' }));
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Promote to the feed' }));
    const promote = await screen.findByRole('dialog', { name: 'Promote to the feed' });
    expect(within(promote).getByRole('checkbox', { name: 'Notify members about the new feed post' })).toBeChecked();
    await userEvent.click(within(promote).getByRole('checkbox', { name: 'Notify members about the new feed post' }));
    await userEvent.click(within(promote).getByRole('button', { name: 'Promote to the feed' }));
    await waitFor(() => expect(calls.find((c) => c.path === '/api/v1/threads/thr_m/promote').body).toMatchObject({ notifyMembers: false, audience: 'Members' }));
  });
});

describe('feed portability', () => {
  it('renders a promoted thread card linking to its thread and reports against the thread', async () => {
    const card = makePost('fpo_p', { kind: 'PromotedThread', title: 'Great thread', commentTarget: { type: 'Thread', id: 'thr_9' }, source: { type: 'Thread', id: 'thr_9', forumId: 'frm_g', forumName: 'General', title: 'Great thread' }, commentCount: 4 });
    const { fetchImpl, calls } = memberFetch((call) => (call.path === '/api/v1/reports' ? json({ id: 'rpt_1' }, 201) : undefined));
    await renderMember(<PostCard post={card} />, { fetchImpl });
    expect(screen.getByRole('link', { name: /From the General forum/ })).toHaveAttribute('href', '/app/threads/thr_9');
    expect(screen.getByRole('link', { name: 'Great thread' })).toHaveAttribute('href', '/app/threads/thr_9');
    expect(screen.getByRole('link', { name: '4 comments' })).toHaveAttribute('href', '/app/threads/thr_9#comments');
    await userEvent.click(screen.getByRole('button', { name: 'Post actions' }));
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Report post' }));
    await userEvent.click(await screen.findByRole('radio', { name: /Spam/ }));
    await userEvent.click(screen.getByRole('button', { name: 'Send report' }));
    await waitFor(() => expect(calls.find((c) => c.path === '/api/v1/reports').body).toMatchObject({ targetType: 'Thread', targetId: 'thr_9' }));
  });

  it('offers no forum discussion action while the server lacks the forums capability', async () => {
    const post = makePost('fpo_1', { title: 'Big news' });
    const { fetchImpl } = memberFetch(() => undefined, makeMe({ flags: { canPromoteToFeed: true } }));
    await renderMember(<PostCard post={post} />, { fetchImpl });
    await userEvent.click(screen.getByRole('button', { name: 'Post actions' }));
    await screen.findByRole('menuitem', { name: /Copy link/ });
    expect(screen.queryByRole('menuitem', { name: 'Open for discussion in a forum' })).toBeNull();
  });

  it('opens a feed post for discussion and links the discussion thread', async () => {
    const post = makePost('fpo_1', { title: 'Big news' });
    const fake = createForumsFake(seed({ feedPosts: [post] }));
    const { fetchImpl, calls } = forumFetch(fake, makeMe({ flags: { canPromoteToFeed: true } }));
    function Harness() {
      return <PostCard post={post} />;
    }
    await renderMember(<Harness />, { fetchImpl, features: { forums: true } });
    await userEvent.click(screen.getByRole('button', { name: 'Post actions' }));
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Open for discussion in a forum' }));
    const dialog = await screen.findByRole('dialog', { name: 'Open for discussion' });
    await userEvent.selectOptions(await within(dialog).findByLabelText('Forum'), 'frm_h');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Open for discussion in a forum' }));
    await waitFor(() => expect(calls.find((c) => c.path === '/api/v1/feed/fpo_1/discuss').body).toMatchObject({ forumId: 'frm_h' }));
  });
});

describe('following and search', () => {
  it('lists followed threads and forums by tab', async () => {
    const fake = createForumsFake(seed({ forums: [{ ...general, isFollowed: true }, help], threads: [makeThread('thr_f', general, { title: 'Followed thread', isFollowed: true })] }));
    const { fetchImpl, calls } = forumFetch(fake);
    await renderMember(<ForumsFollowingView />, { fetchImpl, path: '/app/forums/following', routePath: '/app/forums/following' });
    expect(await screen.findByRole('link', { name: 'Followed thread' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('tab', { name: 'Forums' }));
    expect(await screen.findByRole('link', { name: 'General' })).toBeInTheDocument();
    expect(calls.filter((c) => c.path === '/api/v1/me/follows/enumerate').map((c) => c.body.filters.targetType)).toEqual(['Thread', 'Forum']);
  });

  it('searches every content type with type filters and highlighted snippets', async () => {
    const fake = createForumsFake(seed());
    const { fetchImpl, calls } = forumFetch(fake);
    await renderMember(<SearchView />, { fetchImpl, path: '/app/search', routePath: '/app/search' });
    expect(screen.getByText('Type something to search.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Threads' }));
    await userEvent.type(screen.getByRole('searchbox', { name: 'Search' }), 'reset{Enter}');
    expect(await screen.findByRole('link', { name: 'How do I reset my profile photo?' })).toHaveAttribute('href', '/app/threads/thr_q');
    expect(screen.getAllByText('reset', { selector: 'mark' }).length).toBeGreaterThan(0);
    expect(calls.find((c) => c.path === '/api/v1/search/enumerate').body).toMatchObject({ search: 'reset', filters: { types: ['Thread'] } });
  });
});

describe('poll card on its own', () => {
  it('hides counts when the server hides results and shows the vote lock', async () => {
    const poll = { ...makePoll('pol_2', 'thr_x', ['A', 'B']), resultsVisible: false, totalVoters: 7, myVote: ['pop_2_1'], allowVoteChange: false, resultsVisibility: 'AfterClose' };
    render(
      <MemoryRouter>
        <AuthProvider restoreOnLoad={false}>
          <ToastProvider>
            <PollCard poll={poll} thread={{ id: 'thr_x', viewer: { canVote: true }, isLocked: false }} onChange={() => {}} />
          </ToastProvider>
        </AuthProvider>
      </MemoryRouter>
    );
    expect(await screen.findByText('Results show when the poll closes.')).toBeInTheDocument();
    expect(screen.getByText('7 voters')).toBeInTheDocument();
    expect(screen.getByText('This poll does not allow changing votes.')).toBeInTheDocument();
    expect(screen.queryByRole('list', { name: 'Poll results' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Change vote' })).toBeNull();
  });
});

describe('admin forum pages', () => {
  function adminForumFetch(fake) {
    return createFetch((call) => {
      if (call.path === '/api/v1/settings/enumerate') return json({ objects: [{ key: 'trending.gravity', value: 1.5 }], endOfResults: true });
      const result = fake.handle({ method: call.method, path: call.path, query: call.query, body: call.body });
      if (!result) return undefined;
      return result.status === 204 ? new Response(null, { status: 204 }) : json(result.body, result.status);
    });
  }

  it('reorders forums with the arrow buttons and saves every active forum once', async () => {
    const fake = createForumsFake(seed({ forums: [general, help, { ...reviews, isArchived: true }] }));
    const { fetchImpl, calls } = adminForumFetch(fake);
    renderAdmin(<ForumsAdminView />, { fetchImpl });
    await screen.findByText('Help');
    await userEvent.click(screen.getByRole('button', { name: 'Move Help up' }));
    expect(screen.getByText('The order changed. Save it to apply.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Save order' }));
    await waitFor(() => expect(calls.find((c) => c.path === '/api/v1/admin/forums/order').body).toEqual({ forumIds: ['frm_h', 'frm_g'] }));
    expect(screen.queryByRole('button', { name: 'Move Reviews up' })).toBeNull();
  });

  it('creates a forum from the drawer and refuses to delete a forum with threads', async () => {
    const fake = createForumsFake(seed());
    const { fetchImpl, calls } = adminForumFetch(fake);
    renderAdmin(<ForumsAdminView />, { fetchImpl });
    await screen.findByText('General');
    await userEvent.click(screen.getByRole('button', { name: 'New forum' }));
    const drawer = await screen.findByRole('dialog', { name: 'New forum' });
    await userEvent.click(within(drawer).getByRole('button', { name: 'Save' }));
    expect(within(drawer).getByText('Names are 2 to 60 characters.')).toBeInTheDocument();
    await userEvent.type(within(drawer).getByLabelText('Name'), 'Show and tell');
    await userEvent.click(within(drawer).getByRole('checkbox', { name: 'Feedback' }));
    await userEvent.click(within(drawer).getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(calls.find((c) => c.method === 'POST' && c.path === '/api/v1/admin/forums').body).toMatchObject({ name: 'Show and tell', allowedThreadTypes: ['Discussion', 'Question', 'Poll'] }));
    expect(await screen.findByText('Show and tell')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Actions for General' }));
    expect(screen.queryByRole('menuitem', { name: 'Delete' })).toBeNull();
  });

  it('lists threads, trending threads with their breakdown, and the forum moderation tabs', async () => {
    const fake = createForumsFake(seed());
    const { fetchImpl, calls } = adminForumFetch(fake);
    const view = renderAdmin(<ThreadsAdminView />, { fetchImpl });
    expect(await screen.findByText('How do I reset my profile photo?')).toBeInTheDocument();
    expect(calls.find((c) => c.path === '/api/v1/admin/threads/enumerate').body.maxResults).toBe(50);
    view.unmount();

    // Two columns are hidden until chosen; this test reads every column by position, so it shows them all.
    window.localStorage.setItem('tk.table.tk.admin.trending.hiddenColumns', '[]');
    const trending = renderAdmin(<TrendingView />, { fetchImpl });
    expect(await screen.findByRole('link', { name: 'How do I reset my profile photo?' })).toBeInTheDocument();
    expect(calls.find((c) => c.path === '/api/v1/admin/trending/enumerate').body.ordering).toBe('ScoreDescending');
    expect(await screen.findByText('1.5')).toBeInTheDocument();
    // The live score from the breakdown is shown; the stored score is labeled with its computed time.
    const trendRow = screen.getByRole('link', { name: 'How do I reset my profile photo?' }).closest('tr');
    const cells = within(trendRow).getAllByRole('cell').map((cell) => cell.textContent);
    // Live score (numerator / denominator) agrees with the breakdown; the stored 2.00 carries its time.
    expect(cells[3]).toBe('0.00');
    expect(cells[8]).toBe('0.00 / 18.52');
    expect(cells[9]).toMatch(/^2\.00 at /);
    trending.unmount();

    renderAdmin(<ModerationView />, { fetchImpl, path: '/admin/test?tab=forumMutes', site: siteWithFeatures({ forums: true }) });
    expect(await screen.findByText('No forum mutes.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('tab', { name: 'Review requests' }));
    expect(await screen.findByText('No review requests.')).toBeInTheDocument();
  });

  it('explains platform-managed trending weights when the settings route is forbidden (MultiProperty)', async () => {
    const fake = createForumsFake(seed());
    let settingsCalls = 0;
    const { fetchImpl } = createFetch((call) => {
      // The first call validates the admin token; later reads are refused as for a MultiProperty property administrator.
      if (call.path === '/api/v1/settings/enumerate' && settingsCalls++ === 0) return json({ objects: [], endOfResults: true });
      if (call.path === '/api/v1/settings/enumerate') return json({ error: 'Forbidden', message: 'In MultiProperty mode deployment settings are managed by platform administrators.' }, 403);
      const result = fake.handle({ method: call.method, path: call.path, query: call.query, body: call.body });
      if (!result) return undefined;
      return result.status === 204 ? new Response(null, { status: 204 }) : json(result.body, result.status);
    });
    renderAdmin(<TrendingView />, { fetchImpl });
    expect(await screen.findByText('Trending weights are deployment settings managed by platform administrators in the platform console.')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Change in Settings' })).toBeNull();
  });
});

describe('forum moderator grant input', () => {
  it('resolves a member id, an exact handle, or an exact email, and rejects anything else', async () => {
    const { resolveMemberId } = await import('../src/views/admin/ForumsAdminView.jsx');
    const queries = [];
    const client = {
      listMembers: async (query) => {
        queries.push(query);
        return { objects: [{ id: 'usr_b', handle: 'mo_mod2', email: 'other@example.test' }, { id: 'usr_a', handle: 'mo_mod', email: 'mo@example.test' }] };
      }
    };
    expect(await resolveMemberId(client, ' usr_x ')).toBe('usr_x');
    expect(queries).toEqual([]);
    expect(await resolveMemberId(client, '@Mo_Mod')).toBe('usr_a');
    expect(queries[0]).toEqual({ search: 'mo_mod', maxResults: 25 });
    expect(await resolveMemberId(client, 'MO@example.test')).toBe('usr_a');
    await expect(resolveMemberId(client, 'mo')).rejects.toThrow(/No member matches mo\./);
  });
});
