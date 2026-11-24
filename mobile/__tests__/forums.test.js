import { act, fireEvent, screen, waitFor, within } from '@testing-library/react-native';
import { renderRouter } from 'expo-router/testing-library';
import path from 'path';
import { Dimensions } from 'react-native';
import { createForumsFake, makeForum, makePoll, makeThread, makeThreadComment, moderatorViewer } from '@trelliskit/shared/testing';
import { closeAfterDays } from '../src/components/forums/PollBuilder';
import { threadMarkers } from '../src/components/forums/ThreadCard';
import { describeContentError } from '../src/content/errors';
import { canEditPoll, pollEditDraft, pollEditRequest, pollHasVotes } from '../src/content/pollEdit';
import { pushTarget, resolveNotificationPath } from '../src/notifications/deepLinks';
import { installFakeServer, makeUser, resetEnvironment, signedInAs } from '../test-utils/fakeServer';
import { makePost, userSummary } from '../test-utils/community';
import { propertyWithFeatures } from '../test-utils/property';
import strings from '../src/strings';

const capable = { 'GET /property': propertyWithFeatures({ forums: true, search: true }) };

const APP_DIR = path.resolve(__dirname, '../app');

const general = makeForum('frm_g', { slug: 'general', name: 'General', section: 'Community', sortOrder: 10, unreadThreadCount: 4, allowedThreadTypes: ['Discussion', 'Question', 'Poll'] });
const help = makeForum('frm_h', { slug: 'help', name: 'Help', section: 'Support', sortOrder: 20 });
const reviews = makeForum('frm_r', { slug: 'reviews', name: 'Reviews', sortOrder: 30, allowedThreadTypes: ['Feedback'], viewer: { canCreateThread: true, canFollow: true, canModerate: false, allowedThreadTypes: ['Feedback'] } });

function seed(extra = {}) {
  return {
    forums: [general, help, reviews],
    threads: [
      makeThread('thr_pin', general, { title: 'Welcome', isPinned: true, pinnedUtc: '2026-11-10T00:00:00Z' }),
      makeThread('thr_q', general, { type: 'Question', title: 'How do I reset my photo?', lastActivityUtc: '2026-11-10T20:00:00Z', isUnread: true }),
      makeThread('thr_v', general, { type: 'Poll', title: 'Which night?', poll: makePoll('pol_1', 'thr_v', ['Tuesday', 'Thursday']) })
    ],
    comments: [makeThreadComment('cmt_1', 'thr_q', { body: 'Try the reset option under Account' })],
    ...extra
  };
}

function serve(fake, user = makeUser()) {
  return installFakeServer({ ...capable, ...signedInAs(user) }, { fallback: (call) => fake.handle({ method: call.method, path: call.path, query: call.query, body: call.body }) });
}

describe('forum helpers on mobile', () => {
  test('push taps route to forum and thread paths', () => {
    expect(resolveNotificationPath('/threads/thr_1?comment=cmt_2')).toBe('/threads/thr_1?comment=cmt_2');
    expect(resolveNotificationPath('/forums/frm_1')).toBe('/forums/frm_1');
    expect(resolveNotificationPath('/forums')).toBe('/forums');
    expect(resolveNotificationPath('/threads/thr_1?comment=cmt_2&x=1')).toBeNull();
    expect(pushTarget({ notification: { request: { content: { data: { notificationId: 'ntf_1', path: '/threads/thr_9' } } } } })).toEqual({ notificationId: 'ntf_1', path: '/threads/thr_9' });
  });

  test('markers, poll close dates, and forum error text', () => {
    expect(threadMarkers({ type: 'Question', isPinned: true, isLocked: true, hasAcceptedAnswer: true, state: 'Published' })).toEqual(['Question', 'Pinned', 'Locked', 'Answered']);
    expect(closeAfterDays('', 0)).toBeNull();
    expect(closeAfterDays('2', 0)).toBe(new Date(2 * 86400000).toISOString());
    expect(describeContentError({ code: 'ThreadLocked', status: 409 })).toBe('This thread is locked.');
  });
});

describe('forums tab on a phone', () => {
  beforeEach(() => resetEnvironment());
  afterEach(() => delete global.fetch);

  test('lists forums by section with unread badges and opens a forum with pinned threads first', async () => {
    const fake = createForumsFake(seed());
    serve(fake);
    const router = await renderRouter(APP_DIR, { initialUrl: '/forums' });
    expect(await screen.findByTestId('forum-frm_g')).toBeOnTheScreen();
    expect(screen.getByText('Community')).toBeOnTheScreen();
    expect(screen.getByText('Support')).toBeOnTheScreen();
    expect(screen.getByTestId('forum-frm_g-unread')).toHaveTextContent('4');
    expect(screen.getByLabelText('General, 4 threads with new activity')).toBeOnTheScreen();
    await fireEvent.press(screen.getByTestId('forum-frm_g'));
    await waitFor(() => expect(router.getPathname()).toBe('/forums/frm_g'));
    expect(await screen.findByTestId('thread-thr_pin')).toBeOnTheScreen();
    expect(screen.getByTestId('thread-thr_pin-markers')).toHaveTextContent('Discussion · Pinned');
    expect(screen.getByTestId('thread-thr_q-unread')).toBeOnTheScreen();
    const body = fake.calls.find((c) => c.path === '/forums/frm_g/threads/enumerate').body;
    expect(body).toMatchObject({ maxResults: 20, ordering: 'Active' });

    await fireEvent.press(screen.getByTestId('thread-list-types-Question'));
    await waitFor(() => expect(fake.calls.filter((c) => c.path === '/forums/frm_g/threads/enumerate').at(-1).body.filters).toEqual({ type: ['Question'] }));
    await waitFor(() => expect(screen.queryByTestId('thread-thr_v')).toBeNull());

    await fireEvent.press(screen.getByTestId('thread-list-filters-button'));
    expect(await screen.findByTestId('thread-list-filters-sheet')).toBeOnTheScreen();
  });

  test('opens a thread, votes in its poll, and reads the results', async () => {
    const fake = createForumsFake(seed());
    serve(fake);
    await renderRouter(APP_DIR, { initialUrl: '/threads/thr_v' });
    expect(await screen.findByTestId('thread-header-title')).toHaveTextContent('Which night?');
    expect(screen.getByText('Results show after you vote.')).toBeOnTheScreen();
    await fireEvent.press(screen.getByTestId('poll-vote'));
    expect(await screen.findByTestId('poll-error')).toHaveTextContent('Choose an option.');
    await fireEvent.press(screen.getByTestId('poll-options-pop_1_2'));
    await fireEvent.press(screen.getByTestId('poll-vote'));
    expect(await screen.findByTestId('poll-results')).toBeOnTheScreen();
    expect(screen.getByTestId('poll-result-pop_1_2').props.accessibilityLabel).toBe('Thursday: 1 vote, 100 percent, your vote');
    expect(screen.getByTestId('poll-total')).toHaveTextContent('1 voter');
    expect(fake.calls.find((c) => c.method === 'PUT' && c.path === '/polls/pol_1/votes').body).toEqual({ optionIds: ['pop_1_2'] });
  });

  test('a thread loads its comments from the thread and never asks the feed routes for a thread id', async () => {
    const fake = createForumsFake(seed());
    serve(fake);
    await renderRouter(APP_DIR, { initialUrl: '/threads/thr_q' });
    expect(await screen.findByText('Try the reset option under Account')).toBeOnTheScreen();
    expect(fake.calls.some((c) => c.path.startsWith('/threads/thr_q/comments'))).toBe(true);
    expect(fake.calls.some((c) => c.path.startsWith('/feed/thr_q'))).toBe(false);
  });

  test('comments on a linked thread go to the feed post (one conversation)', async () => {
    const post = makePost('fpo_9', { title: 'News', body: 'Big news' });
    const linked = makeThread('thr_l', general, { kind: 'LinkedFeedPost', title: 'News', body: null, commentTarget: { type: 'FeedPost', id: 'fpo_9' }, source: { type: 'FeedPost', id: 'fpo_9', title: 'News', author: userSummary() } });
    const locked = makeThread('thr_lk', general, { title: 'Closed', isLocked: true, lockReason: 'Resolved' });
    const fake = createForumsFake(seed({ threads: [linked, locked], feedPosts: [post], comments: [] }));
    serve(fake);
    await renderRouter(APP_DIR, { initialUrl: '/threads/thr_l' });
    expect(await screen.findByTestId('thread-header-body')).toHaveTextContent('Big news');
    await fireEvent.changeText(screen.getByTestId('comment-composer-body'), 'Shared');
    await fireEvent.press(screen.getByTestId('comment-composer-submit'));
    await waitFor(() => expect(fake.calls.some((c) => c.method === 'POST' && c.path === '/feed/fpo_9/comments')).toBe(true));
    expect(fake.calls.some((c) => c.path.startsWith('/threads/thr_l/comments'))).toBe(false);
  });

  test('a locked thread is frozen: no reactions and no composer', async () => {
    const locked = makeThread('thr_lk', general, { title: 'Closed', isLocked: true, lockReason: 'Resolved' });
    const fake = createForumsFake(seed({ threads: [locked], comments: [] }));
    serve(fake);
    await renderRouter(APP_DIR, { initialUrl: '/threads/thr_lk' });
    expect(await screen.findByTestId('thread-header-locked')).toHaveTextContent(/This thread is locked: Resolved/);
    expect(screen.queryByTestId('thread-header-reactions-add')).toBeNull();
    expect(screen.getByTestId('comments-closed')).toBeOnTheScreen();
  });

  test('moderators get the actions sheet: pin and lock with a reason', async () => {
    const fake = createForumsFake(seed({ threads: [makeThread('thr_m', general, { title: 'Moderate me', viewer: { ...moderatorViewer, canComment: true, canReact: true, canReport: true } })] }));
    serve(fake, makeUser({ roles: [{ role: 'Moderator', resourceType: 'Forum', resourceId: '*' }] }));
    await renderRouter(APP_DIR, { initialUrl: '/threads/thr_m' });
    await fireEvent.press(await screen.findByTestId('thread-header-moderator'));
    const sheet = await screen.findByTestId('thread-actions-moderator');
    expect(within(sheet).getByTestId('thread-actions-pin')).toBeOnTheScreen();
    expect(within(sheet).getByTestId('thread-actions-move')).toBeOnTheScreen();
    await fireEvent.press(within(sheet).getByTestId('thread-actions-pin'));
    await waitFor(() => expect(screen.getByTestId('thread-header-markers')).toHaveTextContent(/Pinned/));

    await fireEvent.press(screen.getByTestId('thread-header-moderator'));
    await fireEvent.press(await screen.findByTestId('thread-actions-lock'));
    await fireEvent.changeText(await screen.findByTestId('thread-actions-lock-reason'), 'Answered');
    await fireEvent.press(screen.getByTestId('thread-actions-lock-confirm'));
    expect(await screen.findByTestId('thread-header-locked')).toHaveTextContent(/Answered/);
    expect(fake.calls.find((c) => c.path === '/threads/thr_m/lock').body).toEqual({ reason: 'Answered' });
  });

  test('members accept an answer from the comment menu', async () => {
    const fake = createForumsFake(seed({ threads: [makeThread('thr_q', general, { type: 'Question', title: 'Q', author: userSummary({ id: 'usr_1' }), viewer: { canEdit: true, canComment: true, canReact: true, canAcceptAnswer: true } })] }));
    serve(fake);
    await renderRouter(APP_DIR, { initialUrl: '/threads/thr_q' });
    await fireEvent.press(await screen.findByTestId('comment-cmt_1-menu'));
    await fireEvent.press(await screen.findByTestId('comment-cmt_1-actions-accept'));
    expect(await screen.findByTestId('thread-header-accepted')).toBeOnTheScreen();
    expect(await screen.findByTestId('comment-cmt_1-badge')).toHaveTextContent(/Accepted answer/);
  });
});

describe('thread composer', () => {
  beforeEach(() => resetEnvironment());
  afterEach(() => delete global.fetch);

  test('the type picker lists allowed types; a poll needs two options', async () => {
    const fake = createForumsFake(seed());
    serve(fake);
    const router = await renderRouter(APP_DIR, { initialUrl: '/compose-thread?forumId=frm_g' });
    expect(await screen.findByTestId('thread-type-Discussion')).toBeOnTheScreen();
    expect(screen.queryByTestId('thread-type-Feedback')).toBeNull();
    await fireEvent.press(screen.getByTestId('thread-type-Poll'));
    await fireEvent.changeText(screen.getByTestId('thread-title'), 'Best day?');
    await fireEvent.press(screen.getByTestId('thread-submit'));
    expect(await screen.findByTestId('poll-builder-problem')).toHaveTextContent(/Fill in every option/);
    await fireEvent.changeText(screen.getByTestId('poll-builder-option-0'), 'Monday');
    await fireEvent.changeText(screen.getByTestId('poll-builder-option-1'), 'Friday');
    await fireEvent.press(screen.getByTestId('poll-builder-voters-Visible'));
    await fireEvent.press(screen.getByTestId('thread-submit'));
    await waitFor(() => expect(router.getPathname()).toMatch(/^\/threads\/thr_/));
    const create = fake.calls.find((c) => c.method === 'POST' && c.path === '/forums/frm_g/threads');
    expect(create.body).toMatchObject({ type: 'Poll', title: 'Best day?', poll: { options: [{ label: 'Monday' }, { label: 'Friday' }], voterVisibility: 'Visible', resultsVisibility: 'AfterVote' } });
  });

  test('429 too many threads says when to try again (Retry-After)', async () => {
    const fake = createForumsFake(seed());
    installFakeServer(
      { ...capable, ...signedInAs(makeUser()), 'POST /forums/frm_g/threads': { status: 429, body: { error: 'RateLimited', message: 'Too many requests. Try again later.' }, headers: { 'Retry-After': '1500' } } },
      { fallback: (call) => fake.handle({ method: call.method, path: call.path, query: call.query, body: call.body }) }
    );
    await renderRouter(APP_DIR, { initialUrl: '/compose-thread?forumId=frm_g' });
    await fireEvent.changeText(await screen.findByTestId('thread-title'), 'One more thread');
    await fireEvent.changeText(screen.getByTestId('thread-body'), 'Hello there');
    await fireEvent.press(screen.getByTestId('thread-submit'));
    expect(await screen.findByText(strings.errors.rateLimitedFor('25 minutes'))).toBeOnTheScreen();
  });

  test('a Feedback thread requires a video', async () => {
    const fake = createForumsFake(seed());
    serve(fake, makeUser({ flags: { canUploadVideo: true } }));
    await renderRouter(APP_DIR, { initialUrl: '/compose-thread?forumId=frm_r' });
    expect(await screen.findByTestId('thread-focus')).toBeOnTheScreen();
    expect(screen.queryByTestId('thread-type')).toBeNull();
    await fireEvent.changeText(screen.getByTestId('thread-title'), 'Review my round');
    await fireEvent.press(screen.getByTestId('thread-submit'));
    expect(await screen.findByTestId('thread-media-problem')).toHaveTextContent(/Attach a video/);
    expect(fake.calls.some((c) => c.method === 'POST' && c.path.endsWith('/threads'))).toBe(false);
  });
});

describe('forums on a tablet', () => {
  beforeEach(() => resetEnvironment());
  afterEach(() => delete global.fetch);

  async function atWidth(width, run) {
    const original = Dimensions.get('window');
    const screenSize = Dimensions.get('screen');
    Dimensions.set({ window: { ...original, width, height: 820 }, screen: { ...screenSize, width, height: 820 } });
    try {
      await run();
    } finally {
      await act(async () => Dimensions.set({ window: original, screen: screenSize }));
    }
  }

  test('wide windows show forums, threads, and the thread side by side', async () => {
    const fake = createForumsFake(seed());
    serve(fake);
    await atWidth(1180, async () => {
      await renderRouter(APP_DIR, { initialUrl: '/forums' });
      expect(await screen.findByTestId('split-sidebar-pane')).toBeOnTheScreen();
      expect(await screen.findByTestId('thread-thr_q')).toBeOnTheScreen();
      expect(screen.getByTestId('forums-select-thread')).toBeOnTheScreen();
      await fireEvent.press(screen.getByTestId('thread-thr_q'));
      expect(await screen.findByTestId('thread-header-title')).toHaveTextContent('How do I reset my photo?');
      await fireEvent.press(screen.getByTestId('forum-frm_h'));
      await waitFor(() => expect(screen.getByTestId('thread-list-forum-name')).toHaveTextContent('Help'));
    });
  });

  test('below 900 points the forum list collapses into a drawer', async () => {
    const fake = createForumsFake(seed());
    serve(fake);
    await atWidth(820, async () => {
      await renderRouter(APP_DIR, { initialUrl: '/forums' });
      expect(await screen.findByTestId('forums-drawer-open')).toBeOnTheScreen();
      expect(screen.queryByTestId('split-sidebar-pane')).toBeNull();
      await fireEvent.press(screen.getByTestId('forums-drawer-open'));
      const drawer = await screen.findByTestId('split-drawer');
      await fireEvent.press(within(drawer).getByTestId('forum-frm_h'));
      await waitFor(() => expect(screen.queryByTestId('split-drawer')).toBeNull());
      expect(await screen.findByTestId('thread-list-forum-name')).toHaveTextContent('Help');
    });
  });
});

describe('feed portability and search on mobile', () => {
  beforeEach(() => resetEnvironment());
  afterEach(() => delete global.fetch);

  test('a promoted thread card shows its forum and opens the thread', async () => {
    const card = makePost('fpo_p', { kind: 'PromotedThread', title: 'Great thread', commentTarget: { type: 'Thread', id: 'thr_q' }, source: { type: 'Thread', id: 'thr_q', forumId: 'frm_g', forumName: 'General', title: 'Great thread' } });
    const fake = createForumsFake(seed());
    installFakeServer({ ...capable, ...signedInAs(makeUser()), 'POST /feed/enumerate': { success: true, objects: [card], continuationToken: null, endOfResults: true } }, { fallback: (call) => fake.handle(call) });
    const router = await renderRouter(APP_DIR, { initialUrl: '/feed' });
    expect(await screen.findByTestId('post-fpo_p-forum')).toHaveTextContent(/From the General forum/);
    await fireEvent.press(screen.getByTestId('post-fpo_p-forum'));
    await waitFor(() => expect(router.getPathname()).toBe('/threads/thr_q'));
  });

  test('global search sends the type filter and shows snippets', async () => {
    const fake = createForumsFake(seed());
    serve(fake);
    await renderRouter(APP_DIR, { initialUrl: '/forums/search' });
    await fireEvent.press(await screen.findByTestId('search-types-Thread'));
    await fireEvent.changeText(screen.getByTestId('search-input'), 'reset');
    await fireEvent.press(screen.getByTestId('search-submit'));
    expect(await screen.findByTestId('search-hit-thr_q-snippet')).toHaveTextContent(/reset/);
    expect(fake.calls.find((c) => c.path === '/search/enumerate').body).toMatchObject({ search: 'reset', filters: { types: ['Thread'] } });
  });

  test('the Following screen lists followed threads and forums', async () => {
    const fake = createForumsFake(seed({ forums: [{ ...general, isFollowed: true }, help], threads: [makeThread('thr_f', general, { title: 'Followed', isFollowed: true })] }));
    serve(fake);
    await renderRouter(APP_DIR, { initialUrl: '/forums/following' });
    expect(await screen.findByTestId('thread-thr_f')).toBeOnTheScreen();
    await fireEvent.press(screen.getByTestId('following-tabs-Forum'));
    expect(await screen.findByTestId('forum-frm_g')).toBeOnTheScreen();
    expect(fake.calls.filter((c) => c.path === '/me/follows/enumerate').map((c) => c.body.filters.targetType)).toEqual(['Thread', 'Forum']);
  });
});

describe('forums and search before the server ships them', () => {
  beforeEach(() => resetEnvironment());
  afterEach(() => delete global.fetch);

  const forumCalls = (server) => server.calls.filter((c) => /^\/(forums|threads|search)(\/|$)/.test(c.path));

  test('without features.forums navigation hides the shortcut and a direct link shows Coming soon with no forum calls', async () => {
    const server = installFakeServer({ 'GET /property': propertyWithFeatures({ forums: false }), ...signedInAs(makeUser()) });
    await renderRouter(APP_DIR, { initialUrl: '/forums' });
    expect(await screen.findByTestId('screen-forums-coming-soon')).toBeOnTheScreen();
    expect(screen.queryByTestId('tab-forums')).toBeNull();
    expect(screen.getByText('Coming soon')).toBeOnTheScreen();
    expect(screen.queryByTestId('screen-forums')).toBeNull();
    await waitFor(() => expect(server.called('GET /property').length).toBeGreaterThan(0));
    expect(forumCalls(server)).toEqual([]);
  });

  test('a missing capability (older server) also means Coming soon, including thread deep links', async () => {
    const server = installFakeServer({ 'GET /property': propertyWithFeatures({}), ...signedInAs(makeUser()) });
    await renderRouter(APP_DIR, { initialUrl: '/threads/thr_q' });
    expect(await screen.findByTestId('screen-threads-coming-soon')).toBeOnTheScreen();
    await waitFor(() => expect(server.called('GET /property').length).toBeGreaterThan(0));
    expect(forumCalls(server)).toEqual([]);
  });

  test('forums without features.search show the forum list and a Coming soon search screen', async () => {
    const fake = createForumsFake(seed());
    const server = installFakeServer({ 'GET /property': propertyWithFeatures({ forums: true }), ...signedInAs(makeUser()) }, { fallback: (call) => fake.handle({ method: call.method, path: call.path, query: call.query, body: call.body }) });
    await renderRouter(APP_DIR, { initialUrl: '/forums/search' });
    expect(await screen.findByTestId('screen-search-coming-soon')).toBeOnTheScreen();
    expect(server.calls.filter((c) => c.path.startsWith('/search'))).toEqual([]);
  });
});

describe('poll editing (section 6.4)', () => {
  beforeEach(() => resetEnvironment());
  afterEach(() => delete global.fetch);

  const author = makeUser({ id: 'usr_2', displayName: 'Alex Chen' });

  test('helpers: full replacement before the first vote, closing time only after it', () => {
    const poll = makePoll('pol_9', 'thr_9', ['A', 'B'], { closesUtc: '2026-12-01T00:00:00Z' });
    expect(canEditPoll(poll, { author: { id: 'usr_2' } }, { id: 'usr_2' })).toBe(true);
    expect(canEditPoll(poll, { author: { id: 'usr_2' } }, { id: 'usr_1' })).toBe(false);
    expect(canEditPoll({ ...poll, isClosed: true }, { author: { id: 'usr_2' } }, { id: 'usr_2' })).toBe(false);
    const draft = { ...pollEditDraft(poll), options: [{ label: 'A' }, { label: 'B' }, { label: 'C' }], multipleChoice: true, voterVisibility: 'Anonymous' };
    const open = pollEditRequest(poll, draft, { now: 0 });
    expect(open.body.poll).toMatchObject({ options: [{ label: 'A' }, { label: 'B' }, { label: 'C' }], multipleChoice: true, closesUtc: '2026-12-01T00:00:00Z', voterVisibility: 'Anonymous', allowVoteChange: true });
    expect(pollEditRequest(poll, { ...draft, options: [{ label: 'A' }, { label: 'a' }] }, { now: 0 }).problems).toEqual({ options: 'duplicateOption' });
    const voted = { ...poll, totalVoters: 3 };
    expect(pollHasVotes(voted)).toBe(true);
    expect(pollEditRequest(voted, { ...pollEditDraft(voted), closeInDays: '2' }, { now: 0 }).body).toEqual({ poll: { closesUtc: new Date(2 * 86400000).toISOString() } });
    expect(pollEditRequest(voted, pollEditDraft(voted), { now: 0 }).body).toEqual({ poll: { closesUtc: '2026-12-01T00:00:00Z' } });
    expect(describeContentError({ code: 'PollOptionsLocked', status: 409 })).toBe('Poll options cannot change after the first vote.');
  });

  test('the author edits the options before the first vote through PUT /threads/{id}', async () => {
    const fake = createForumsFake(seed());
    let sent = null;
    installFakeServer(
      {
        ...capable,
        ...signedInAs(author),
        'PUT /threads/thr_v': (call) => {
          sent = call.body;
          return makeThread('thr_v', general, { type: 'Poll', title: 'Which night?', poll: makePoll('pol_1', 'thr_v', call.body.poll.options.map((o) => o.label)) });
        }
      },
      { fallback: (call) => fake.handle({ method: call.method, path: call.path, query: call.query, body: call.body }) }
    );
    await renderRouter(APP_DIR, { initialUrl: '/threads/thr_v' });
    await fireEvent.press(await screen.findByTestId('poll-edit'));
    await fireEvent.press(await screen.findByTestId('poll-edit-sheet-builder-add'));
    await fireEvent.changeText(screen.getByTestId('poll-edit-sheet-builder-option-2'), 'Saturday');
    await fireEvent.press(screen.getByTestId('poll-edit-sheet-save'));
    await waitFor(() => expect(sent).not.toBeNull());
    expect(sent.poll.options).toEqual([{ label: 'Tuesday' }, { label: 'Thursday' }, { label: 'Saturday' }]);
    expect(await screen.findByText('Saturday')).toBeOnTheScreen();
  });

  test('a vote arriving meanwhile shows PollOptionsLocked and switches to the closing time form', async () => {
    const fake = createForumsFake(seed());
    let reads = 0;
    installFakeServer(
      {
        ...capable,
        ...signedInAs(author),
        'GET /threads/thr_v': () => {
          reads += 1;
          return makeThread('thr_v', general, { type: 'Poll', title: 'Which night?', poll: makePoll('pol_1', 'thr_v', ['Tuesday', 'Thursday'], { totalVoters: reads > 1 ? 1 : 0 }) });
        },
        'PUT /threads/thr_v': { status: 409, body: { error: 'PollOptionsLocked', message: 'The poll received a vote.' } }
      },
      { fallback: (call) => fake.handle({ method: call.method, path: call.path, query: call.query, body: call.body }) }
    );
    await renderRouter(APP_DIR, { initialUrl: '/threads/thr_v' });
    await fireEvent.press(await screen.findByTestId('poll-edit'));
    await fireEvent.press(await screen.findByTestId('poll-edit-sheet-save'));
    expect(await screen.findByTestId('poll-edit-sheet-error')).toHaveTextContent('Poll options cannot change after the first vote.');
    expect(await screen.findByTestId('poll-edit-sheet-locked')).toBeOnTheScreen();
    expect(screen.getByTestId('poll-edit-sheet-closes')).toBeOnTheScreen();
  });

  test('members who did not write the poll see no edit button', async () => {
    const fake = createForumsFake(seed());
    serve(fake);
    await renderRouter(APP_DIR, { initialUrl: '/threads/thr_v' });
    expect(await screen.findByTestId('poll-vote')).toBeOnTheScreen();
    expect(screen.queryByTestId('poll-edit')).toBeNull();
  });
});
