import { act, fireEvent, renderHook, screen, waitFor, within } from '@testing-library/react-native';
import { renderRouter } from 'expo-router/testing-library';
import path from 'path';
import { Dimensions } from 'react-native';
import { useCursorList } from '../src/hooks/useCursorList';
import { mergePage, readPage } from '../src/content/paging';
import strings from '../src/strings';
import { installFakeServer, makeUser, resetEnvironment, signedInAs } from '../test-utils/fakeServer';
import { enumerationResult, makeMedia, makePost, userSummary } from '../test-utils/community';

const APP_DIR = path.resolve(__dirname, '../app');

describe('cursor paging helpers', () => {
  test('pages merge de-duplicated by id, keeping the first position and the newer copy', () => {
    const merged = mergePage([{ id: 'a', v: 1 }, { id: 'b', v: 1 }], [{ id: 'b', v: 2 }, { id: 'c', v: 1 }]);
    expect(merged).toEqual([{ id: 'a', v: 1 }, { id: 'b', v: 2 }, { id: 'c', v: 1 }]);
    expect(readPage(enumerationResult([1], 'tok'))).toEqual({ items: [1], nextCursor: 'tok' });
    expect(readPage({ objects: [1], continuationToken: 'stale', endOfResults: true })).toEqual({ items: [1], nextCursor: null });
    expect(readPage(null)).toEqual({ items: [], nextCursor: null });
  });

  test('useCursorList loads, appends the next page once, refreshes from the first page, and filters', async () => {
    const pages = {
      '': enumerationResult([{ id: 'p1' }, { id: 'p2', kind: 'PromotedThread' }], 'c2'),
      c2: enumerationResult([{ id: 'p1' }, { id: 'p3' }])
    };
    const fetchPage = jest.fn(async (cursor) => pages[cursor || '']);
    const { result } = await renderHook(() => useCursorList(fetchPage, { filter: (p) => !p.kind }));
    await waitFor(() => expect(result.current.status).toBe('ready'));
    expect(result.current.items.map((p) => p.id)).toEqual(['p1']);
    expect(result.current.hasMore).toBe(true);

    await act(async () => {
      await Promise.all([result.current.loadMore(), result.current.loadMore()]);
    });
    expect(fetchPage.mock.calls.map((c) => c[0])).toEqual([null, 'c2']);
    expect(result.current.items.map((p) => p.id)).toEqual(['p1', 'p3']);
    expect(result.current.hasMore).toBe(false);

    await act(async () => {
      await result.current.loadMore();
    });
    expect(fetchPage).toHaveBeenCalledTimes(2);

    await act(async () => {
      await result.current.refresh();
    });
    expect(fetchPage.mock.calls[2][0]).toBeNull();
    expect(result.current.items.map((p) => p.id)).toEqual(['p1']);
  });
});

describe('feed tab', () => {
  beforeEach(() => resetEnvironment());
  afterEach(() => delete global.fetch);

  const pinned = makePost('fpo_pin', { isPinned: true, pinnedUtc: '2026-11-10T19:00:00Z', title: 'Welcome', body: 'Start **here**.' });

  test('first page shows pinned posts first, then loads the next page by continuation token, skipping unknown kinds', async () => {
    const server = installFakeServer({
      ...signedInAs(makeUser()),
      'POST /feed/enumerate': (call) => {
        if (!call.body.continuationToken) return enumerationResult([pinned, makePost('fpo_1'), { ...makePost('fpo_x'), kind: 'FutureKind' }], 'cur-2');
        return enumerationResult([makePost('fpo_1'), makePost('fpo_2')]);
      }
    });
    await renderRouter(APP_DIR, { initialUrl: '/feed' });
    expect(await screen.findByTestId('post-fpo_pin')).toBeOnTheScreen();
    expect(screen.getByTestId('post-fpo_pin-pinned')).toHaveTextContent('Pinned');
    expect(screen.getByTestId('post-fpo_1')).toBeOnTheScreen();
    expect(screen.queryByTestId('post-fpo_x')).toBeNull();
    expect(server.called('POST /feed/enumerate')[0].body).toEqual({ maxResults: 20 });

    await act(async () => {
      fireEvent(screen.getByTestId('feed-list'), 'onEndReached');
    });
    expect(await screen.findByTestId('post-fpo_2')).toBeOnTheScreen();
    expect(server.called('POST /feed/enumerate')[1].body).toEqual({ maxResults: 20, continuationToken: 'cur-2' });
    expect(screen.getAllByTestId('post-fpo_1')).toHaveLength(1);
  });

  test('pull to refresh refetches the first page', async () => {
    let n = 0;
    const server = installFakeServer({
      ...signedInAs(makeUser()),
      'POST /feed/enumerate': () => {
        n += 1;
        return enumerationResult(n === 1 ? [makePost('fpo_1')] : [makePost('fpo_new'), makePost('fpo_1')]);
      }
    });
    await renderRouter(APP_DIR, { initialUrl: '/feed' });
    await screen.findByTestId('post-fpo_1');
    const list = screen.getByTestId('feed-list');
    await act(async () => {
      list.props.refreshControl.props.onRefresh();
    });
    expect(await screen.findByTestId('post-fpo_new')).toBeOnTheScreen();
    expect(server.called('POST /feed/enumerate').map((c) => c.body.continuationToken)).toEqual([undefined, undefined]);
  });

  test('a failed load shows Retry; an empty feed says so', async () => {
    let fail = true;
    installFakeServer({ ...signedInAs(makeUser()), 'POST /feed/enumerate': () => (fail ? { status: 500, body: { error: 'InternalError', message: 'Boom.' } } : enumerationResult([])) });
    await renderRouter(APP_DIR, { initialUrl: '/feed' });
    expect(await screen.findByTestId('feed-error')).toHaveTextContent(/Could not load the feed/);
    fail = false;
    await fireEvent.press(screen.getByTestId('feed-error-retry'));
    expect(await screen.findByTestId('feed-empty')).toBeOnTheScreen();
  });

  test('post cards render the Markdown subset natively, link mentions to profiles, and show media with grant cookies', async () => {
    const post = makePost('fpo_1', {
      body: '## Heading\n\nHello @alexchen and @nobody, see https://example.test/about.\n\n- one\n- two\n\n> quoted\n\n<b>not html</b>',
      mentions: [{ userId: 'usr_2', handle: 'alexchen', displayName: 'Alex Chen' }],
      attachments: [makeMedia('med_1', { altText: 'A photo' })],
      linkPreview: { url: 'https://example.test/about', canonicalUrl: 'https://example.test/about', siteName: 'Site', title: 'About us', description: 'Who we are', imageUrl: null }
    });
    installFakeServer({
      ...signedInAs(makeUser()),
      'POST /feed/enumerate': enumerationResult([post]),
      'POST /media/grant': { mode: 'Local', expiresUtc: '2099-01-01T00:00:00Z', cookies: [{ name: 'tk_media', value: 'abc', path: '/api/v1/media/files', expiresUtc: '2099-01-01T00:00:00Z' }] },
      'GET /users/usr_2': { id: 'usr_2', displayName: 'Alex Chen', handle: 'alexchen', avatarUrl: null, bio: null, profile: {}, createdUtc: '2026-01-01T00:00:00Z', isBlockedByMe: false }
    });
    const router = await renderRouter(APP_DIR, { initialUrl: '/feed' });
    const body = await screen.findByTestId('post-fpo_1-body');
    expect(within(body).getByRole('header')).toHaveTextContent('Heading');
    expect(body).toHaveTextContent(/@nobody/);
    expect(body).toHaveTextContent(/<b>not html<\/b>/);
    expect(body).toHaveTextContent(/quoted/);
    expect(screen.getByTestId('post-fpo_1-preview')).toHaveTextContent(/About us/);

    await waitFor(() => expect(screen.getByTestId('post-fpo_1-media-image-0').props.source.headers.Cookie).toBe('tk_media=abc'));
    expect(screen.getByTestId('post-fpo_1-media-image-0').props.source.uri).toMatch(/\/api\/v1\/media\/files\/med_1\/medium\.webp$/);

    await fireEvent.press(screen.getByTestId('mention-alexchen'));
    await waitFor(() => expect(router.getPathname()).toBe('/members/usr_2'));
    expect(await screen.findByTestId('member-name')).toHaveTextContent('Alex Chen');
  });

  test('tapping a post on a phone opens /feed/{id}', async () => {
    installFakeServer({ ...signedInAs(makeUser()), 'POST /feed/enumerate': enumerationResult([makePost('fpo_1')]), 'GET /feed/fpo_1': makePost('fpo_1'), 'POST /feed/fpo_1/comments/enumerate': enumerationResult([]) });
    const router = await renderRouter(APP_DIR, { initialUrl: '/feed' });
    await fireEvent.press(await screen.findByTestId('post-fpo_1-open'));
    await waitFor(() => expect(router.getPathname()).toBe('/feed/fpo_1'));
    expect(await screen.findByTestId('post-detail')).toBeOnTheScreen();
  });

  test('on a tablet the feed and the selected post sit side by side', async () => {
    const original = Dimensions.get('window');
    const screenSize = Dimensions.get('screen');
    Dimensions.set({ window: { ...original, width: 1180, height: 820 }, screen: { ...screenSize, width: 1180, height: 820 } });
    try {
      installFakeServer({ ...signedInAs(makeUser()), 'POST /feed/enumerate': enumerationResult([makePost('fpo_1'), makePost('fpo_2')]), 'GET /feed/fpo_2': makePost('fpo_2', { body: 'Full post two' }), 'POST /feed/fpo_2/comments/enumerate': enumerationResult([]) });
      const router = await renderRouter(APP_DIR, { initialUrl: '/feed' });
      expect(await screen.findByTestId('split-list-pane')).toBeOnTheScreen();
      expect(screen.getByTestId('split-detail-pane')).toHaveTextContent(/Select a post/);
      await fireEvent.press(screen.getByTestId('post-fpo_2-open'));
      const detail = screen.getByTestId('split-detail-pane');
      await waitFor(() => expect(within(detail).getByTestId('post-detail')).toBeOnTheScreen());
      await waitFor(() => expect(within(detail).getByTestId('post-fpo_2-body')).toHaveTextContent('Full post two'));
      expect(router.getPathname()).toBe('/feed');
    } finally {
      await act(async () => Dimensions.set({ window: original, screen: screenSize }));
    }
  });

  test('the edited marker opens the edit history', async () => {
    installFakeServer({
      ...signedInAs(makeUser()),
      'POST /feed/enumerate': enumerationResult([makePost('fpo_1', { editedUtc: '2026-11-10T19:00:00Z', revisionCount: 1, body: 'Now' })]),
      'POST /feed/fpo_1/revisions/enumerate': enumerationResult([{ id: 'fpr_1', revision: 1, title: null, body: 'Before', attachmentSummary: 'Removed 1 image', editedBy: userSummary(), createdUtc: '2026-11-10T18:00:00Z' }])
    });
    await renderRouter(APP_DIR, { initialUrl: '/feed' });
    await fireEvent.press(await screen.findByTestId('post-fpo_1-byline-edited'));
    expect(await screen.findByTestId('history-fpr_1')).toHaveTextContent(/Before/);
    expect(screen.getByTestId('history-fpr_1')).toHaveTextContent(/Removed 1 image/);
    expect(screen.getByTestId('history-current')).toHaveTextContent(/Now/);
  });

  test('edit history filters revisions by label and tag', async () => {
    const server = installFakeServer({
      ...signedInAs(makeUser()),
      'POST /feed/enumerate': enumerationResult([makePost('fpo_1', { editedUtc: '2026-11-10T19:00:00Z', revisionCount: 1, body: 'Now' })]),
      'POST /feed/fpo_1/revisions/enumerate': (call) => enumerationResult(call.body.labels ? [] : [{ id: 'fpr_1', revision: 1, title: null, body: 'Before', createdUtc: '2026-11-10T18:00:00Z' }])
    });
    await renderRouter(APP_DIR, { initialUrl: '/feed' });
    await fireEvent.press(await screen.findByTestId('post-fpo_1-byline-edited'));
    expect(await screen.findByTestId('history-fpr_1')).toBeOnTheScreen();
    await fireEvent.press(screen.getByTestId('edit-history-filters-button'));
    const labelInput = screen.getByTestId('edit-history-filters-panel-label-input');
    await fireEvent.changeText(labelInput, 'topics');
    await fireEvent(labelInput, 'submitEditing');
    await fireEvent.press(screen.getByTestId('edit-history-filters-panel-add-tag'));
    await fireEvent.changeText(screen.getByTestId('filter-tag-0-key'), 'level');
    await fireEvent.changeText(screen.getByTestId('filter-tag-0-value'), 'beginner');
    await fireEvent.press(screen.getByTestId('edit-history-filters-panel-apply'));
    await waitFor(() => expect(server.called('POST /feed/fpo_1/revisions/enumerate').pop().body).toEqual({ maxResults: 20, labels: ['topics'], labelMatch: 'All', tags: { level: 'beginner' } }));
    expect(await screen.findByText(strings.history.noMatches)).toBeOnTheScreen();
    expect(screen.getByTestId('edit-history-filters-chip-label-topics')).toBeOnTheScreen();
    expect(screen.queryByTestId('history-current')).toBeNull();
    expect(server.called('POST /labels/enumerate').every((c) => c.body.targetType === 'FeedPostRevision')).toBe(true);
  });
});
