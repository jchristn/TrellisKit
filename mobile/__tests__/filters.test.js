import { act, fireEvent, screen, waitFor, within } from '@testing-library/react-native';
import { router as appRouter } from 'expo-router';
import { renderRouter } from 'expo-router/testing-library';
import path from 'path';
import { Dimensions } from 'react-native';
import { activeFilterCount, buildEnumerationQuery, clampMaxResults, emptyFilters, filtersToQuery, MaxResultsMember } from '../src/content/enumeration';
import { metadataChanged, metadataPayload } from '../src/content/metadata';
import strings from '../src/strings';
import { installFakeServer, makeUser, resetEnvironment, signedInAs } from '../test-utils/fakeServer';
import { enumerationResult, makeComment, makePost } from '../test-utils/community';

const APP_DIR = path.resolve(__dirname, '../app');
const poster = () => makeUser({ flags: { canPostToFeed: true } });
const moderator = () => makeUser({ roles: [{ role: 'FeedModerator', resourceType: 'Feed', resourceId: '*' }] });

describe('EnumerationQuery', () => {
  test('maxResults is always set and clamped to the member maximum', () => {
    expect(clampMaxResults(undefined)).toBe(25);
    expect(clampMaxResults(5000)).toBe(MaxResultsMember);
    expect(clampMaxResults(0)).toBe(1);
    expect(buildEnumerationQuery({})).toEqual({ maxResults: 25 });
  });

  test('filters become search, ordering, labels with labelMatch, tags (has key is *), and typed filters', () => {
    const filters = { search: '  welcome ', ordering: 'CreatedAscending', labels: ['topics', 'fundamentals'], labelMatch: 'Any', tags: [{ key: 'level', value: 'beginner' }, { key: 'position', any: true }, { key: ' ', value: 'x' }] };
    expect(activeFilterCount(filters)).toBe(5);
    expect(filtersToQuery(filters)).toEqual({ search: 'welcome', ordering: 'CreatedAscending', labels: ['topics', 'fundamentals'], labelMatch: 'Any', tags: { level: 'beginner', position: '*' } });
    expect(buildEnumerationQuery({ continuationToken: 't', maxResults: 30, filters, collectionFilters: { unreadOnly: true, state: undefined } })).toMatchObject({ maxResults: 30, continuationToken: 't', filters: { unreadOnly: true } });
    expect(filtersToQuery(emptyFilters())).toEqual({});
  });
});

describe('metadata payloads', () => {
  test('members never send staff: entries; staff keep them; labels de-duplicate case-insensitively', () => {
    const value = { labels: ['Topics', 'topics', ' staff:featured ', 'drills'], tags: { level: 'beginner', 'staff:review': 'pending' } };
    expect(metadataPayload(value)).toEqual({ labels: ['Topics', 'drills'], tags: { level: 'beginner' } });
    expect(metadataPayload(value, { staff: true })).toEqual({ labels: ['Topics', 'staff:featured', 'drills'], tags: { level: 'beginner', 'staff:review': 'pending' } });
    expect(metadataChanged({ labels: ['a'], tags: {} }, { labels: ['a'], tags: {} })).toBe(false);
    expect(metadataChanged({ labels: ['a'], tags: {} }, { labels: ['a'], tags: { k: 'v' } })).toBe(true);
  });
});

describe('list filters', () => {
  beforeEach(() => resetEnvironment());
  afterEach(() => delete global.fetch);

  function feedServer(extra = {}) {
    return installFakeServer({
      ...signedInAs(makeUser()),
      'POST /feed/enumerate': (call) => enumerationResult(call.body.labels ? [makePost('fpo_topics')] : [makePost('fpo_1'), makePost('fpo_topics')]),
      'POST /labels/enumerate': (call) => enumerationResult([{ label: 'topics', count: 4 }, { label: 'staff:featured', count: 1 }, { label: 'topical', count: 1 }].filter((l) => !call.body.prefix || l.label.startsWith(call.body.prefix))),
      'POST /tags/keys/enumerate': enumerationResult([{ key: 'level', count: 3 }]),
      'POST /tags/values/enumerate': (call) => enumerationResult(call.body.key === 'level' ? [{ value: 'beginner', count: 2 }] : []),
      ...extra
    });
  }

  test('phone: the filter sheet applies labels (autocomplete, All/Any), a tag, and search; chips show with a count and persist for the session', async () => {
    const server = feedServer();
    await renderRouter(APP_DIR, { initialUrl: '/feed' });
    await screen.findByTestId('post-fpo_1');
    expect(screen.getByTestId('feed-filters-button')).toHaveProp('accessibilityLabel', strings.filters.button);
    await fireEvent.press(screen.getByTestId('feed-filters-button'));
    const sheet = await screen.findByTestId('feed-filters-sheet');

    const labelInput = within(sheet).getByTestId('feed-filters-panel-label-input');
    await fireEvent(labelInput, 'focus');
    await fireEvent.changeText(labelInput, 'top');
    await fireEvent.press(await within(sheet).findByTestId('feed-filters-panel-label-input-suggestion-topics'));
    expect(within(sheet).queryByTestId('feed-filters-panel-label-input-suggestion-staff:featured')).toBeNull();
    expect(server.called('POST /labels/enumerate').pop().body).toMatchObject({ targetType: 'FeedPost', prefix: 'top', maxResults: 10 });
    await fireEvent.press(within(sheet).getByTestId('feed-filters-panel-label-match-Any'));

    await fireEvent.press(within(sheet).getByTestId('feed-filters-panel-add-tag'));
    const keyInput = within(sheet).getByTestId('filter-tag-0-key');
    await fireEvent(keyInput, 'focus');
    await fireEvent.press(await within(sheet).findByTestId('filter-tag-0-key-suggestion-level'));
    const valueInput = within(sheet).getByTestId('filter-tag-0-value');
    await fireEvent(valueInput, 'focus');
    await fireEvent.press(await within(sheet).findByTestId('filter-tag-0-value-suggestion-beginner'));
    expect(server.called('POST /tags/values/enumerate').pop().body).toMatchObject({ targetType: 'FeedPost', key: 'level' });

    await fireEvent.changeText(within(sheet).getByTestId('feed-filters-panel-search'), 'welcome');
    await fireEvent.press(within(sheet).getByTestId('feed-filters-panel-apply'));

    await waitFor(() => expect(server.called('POST /feed/enumerate').pop().body).toEqual({ maxResults: 20, search: 'welcome', labels: ['topics'], labelMatch: 'Any', tags: { level: 'beginner' } }));
    expect(await screen.findByTestId('post-fpo_topics')).toBeOnTheScreen();
    expect(screen.queryByTestId('post-fpo_1')).toBeNull();
    expect(screen.getByTestId('feed-filters-button')).toHaveProp('accessibilityLabel', strings.filters.buttonActive(3));
    expect(screen.getByTestId('feed-filters-chip-label-topics')).toBeOnTheScreen();
    expect(screen.getByTestId('feed-filters-chip-tag-0')).toHaveTextContent('level = beginner', { exact: false });

    // Filters persist for the session: leave the feed and come back.
    await act(async () => appRouter.push('/profile'));
    await act(async () => appRouter.push('/feed'));
    expect(await screen.findByTestId('feed-filters-chip-label-topics')).toBeOnTheScreen();

    // Removing a chip refetches without it; Clear all removes every filter.
    await fireEvent.press(screen.getByTestId('feed-filters-chip-search'));
    await waitFor(() => expect(server.called('POST /feed/enumerate').pop().body.search).toBeUndefined());
    await fireEvent.press(await screen.findByTestId('feed-filters-clear-all'));
    await waitFor(() => expect(server.called('POST /feed/enumerate').pop().body).toEqual({ maxResults: 20 }));
    expect(screen.queryByTestId('feed-filters-active')).toBeNull();
  });

  test('a "has key" tag row sends * for the value', async () => {
    const server = feedServer();
    await renderRouter(APP_DIR, { initialUrl: '/feed' });
    await fireEvent.press(await screen.findByTestId('feed-filters-button'));
    const sheet = await screen.findByTestId('feed-filters-sheet');
    await fireEvent.press(within(sheet).getByTestId('feed-filters-panel-add-tag'));
    await fireEvent.changeText(within(sheet).getByTestId('filter-tag-0-key'), 'level');
    await fireEvent(within(sheet).getByTestId('filter-tag-0-any'), 'valueChange', true);
    expect(within(sheet).queryByTestId('filter-tag-0-value')).toBeNull();
    await fireEvent.press(within(sheet).getByTestId('feed-filters-panel-apply'));
    await waitFor(() => expect(server.called('POST /feed/enumerate').pop().body.tags).toEqual({ level: '*' }));
    expect(screen.getByTestId('feed-filters-chip-tag-0')).toHaveTextContent(strings.filters.hasKeyChip('level'), { exact: false });
  });

  test('tablet: filters sit in a side panel next to the results, with no filter button', async () => {
    const original = Dimensions.get('window');
    const screenSize = Dimensions.get('screen');
    Dimensions.set({ window: { ...original, width: 1180, height: 820 }, screen: { ...screenSize, width: 1180, height: 820 } });
    try {
      const server = feedServer();
      await renderRouter(APP_DIR, { initialUrl: '/feed' });
      const panel = await screen.findByTestId('feed-filters-side-panel');
      expect(screen.queryByTestId('feed-filters-button')).toBeNull();
      await fireEvent.changeText(within(panel).getByTestId('feed-filters-panel-search'), 'topics');
      await fireEvent.press(within(panel).getByTestId('feed-filters-panel-ordering-CreatedAscending'));
      await fireEvent.press(within(panel).getByTestId('feed-filters-panel-apply'));
      await waitFor(() => expect(server.called('POST /feed/enumerate').pop().body).toEqual({ maxResults: 20, search: 'topics', ordering: 'CreatedAscending' }));
      expect(screen.getByTestId('split-list-pane')).toBeOnTheScreen();
    } finally {
      await act(async () => Dimensions.set({ window: original, screen: screenSize }));
    }
  });

  test('portrait tablet: the feed keeps a readable detail pane and puts its filters in the sheet', async () => {
    const original = Dimensions.get('window');
    const screenSize = Dimensions.get('screen');
    Dimensions.set({ window: { ...original, width: 820, height: 1180 }, screen: { ...screenSize, width: 820, height: 1180 } });
    try {
      const server = feedServer();
      await renderRouter(APP_DIR, { initialUrl: '/feed' });
      expect(await screen.findByTestId('split-detail-pane')).toBeOnTheScreen();
      expect(screen.queryByTestId('feed-filters-side-panel')).toBeNull();
      await fireEvent.press(await screen.findByTestId('feed-filters-button'));
      const sheet = await screen.findByTestId('feed-filters-sheet');
      await fireEvent.changeText(within(sheet).getByTestId('feed-filters-panel-search'), 'topics');
      await fireEvent.press(within(sheet).getByTestId('feed-filters-panel-apply'));
      await waitFor(() => expect(server.called('POST /feed/enumerate').pop().body).toEqual({ maxResults: 20, search: 'topics' }));
    } finally {
      await act(async () => Dimensions.set({ window: original, screen: screenSize }));
    }
  });

  test('notifications and blocked members send their filters in the enumeration', async () => {
    const server = installFakeServer({
      ...signedInAs(makeUser()),
      'POST /notifications/enumerate': enumerationResult([]),
      'POST /me/blocks/enumerate': enumerationResult([])
    });
    await renderRouter(APP_DIR, { initialUrl: '/notifications' });
    await waitFor(() => expect(server.called('POST /notifications/enumerate')).toHaveLength(1));
    await fireEvent.press(screen.getByTestId('notifications-filter-unread'));
    await waitFor(() => expect(server.called('POST /notifications/enumerate').pop().body).toEqual({ maxResults: 30, filters: { unreadOnly: true } }));
    await fireEvent.press(screen.getByTestId('notifications-filters-button'));
    await fireEvent.changeText(await screen.findByTestId('notifications-filters-panel-search'), 'reply');
    await fireEvent.press(screen.getByTestId('notifications-filters-panel-apply'));
    await waitFor(() => expect(server.called('POST /notifications/enumerate').pop().body).toEqual({ maxResults: 30, search: 'reply', filters: { unreadOnly: true } }));

    await act(async () => appRouter.push('/account/blocked'));
    await fireEvent.press(await screen.findByTestId('blocked-filters-button'));
    await fireEvent.changeText(await screen.findByTestId('blocked-filters-panel-search'), 'alex');
    await fireEvent.press(screen.getByTestId('blocked-filters-panel-apply'));
    await waitFor(() => expect(server.called('POST /me/blocks/enumerate').pop().body).toEqual({ maxResults: 50, search: 'alex' }));
  });

  test('comments filter by label and page with the continuation token', async () => {
    const server = installFakeServer({
      ...signedInAs(makeUser()),
      'GET /feed/fpo_1': makePost('fpo_1'),
      'POST /feed/fpo_1/comments/enumerate': (call) => (call.body.continuationToken ? enumerationResult([makeComment('cmt_2')]) : enumerationResult([makeComment('cmt_1')], 'next')),
      'POST /labels/enumerate': enumerationResult([{ label: 'question', count: 2 }])
    });
    await renderRouter(APP_DIR, { initialUrl: '/feed/fpo_1' });
    expect(await screen.findByTestId('comment-cmt_1')).toBeOnTheScreen();
    expect(server.called('POST /feed/fpo_1/comments/enumerate')[0].body).toEqual({ maxResults: 20, ordering: 'Top', filters: { depth: 3, childLimit: 5 } });
    await fireEvent.press(screen.getByTestId('comments-load-more'));
    expect(await screen.findByTestId('comment-cmt_2')).toBeOnTheScreen();
    expect(server.called('POST /feed/fpo_1/comments/enumerate')[1].body).toMatchObject({ continuationToken: 'next', ordering: 'Top' });

    await fireEvent.press(screen.getByTestId('comments-filters-button'));
    const input = await screen.findByTestId('comments-filters-panel-label-input');
    await fireEvent.changeText(input, 'question');
    await fireEvent(input, 'submitEditing');
    await fireEvent.press(screen.getByTestId('comments-filters-panel-apply'));
    await waitFor(() => expect(server.called('POST /feed/fpo_1/comments/enumerate').pop().body).toEqual({ maxResults: 20, ordering: 'Top', labels: ['question'], labelMatch: 'All', filters: { depth: 3, childLimit: 5 } }));
  });
});

describe('labels and tags editor', () => {
  beforeEach(() => resetEnvironment());
  afterEach(() => delete global.fetch);

  test('a member adds labels and tags to a new post; staff: names are rejected', async () => {
    const server = installFakeServer({
      ...signedInAs(poster()),
      'POST /labels/enumerate': enumerationResult([{ label: 'announcements', count: 3 }]),
      'POST /feed': (call) => ({ status: 201, body: makePost('fpo_new', { ...call.body }) })
    });
    await renderRouter(APP_DIR, { initialUrl: '/compose' });
    await fireEvent.changeText(await screen.findByTestId('compose-body'), 'Hi');
    await fireEvent.press(screen.getByTestId('compose-metadata-open'));
    const labelInput = screen.getByTestId('compose-metadata-label-input');
    await fireEvent(labelInput, 'focus');
    await fireEvent.press(await screen.findByTestId('compose-metadata-label-input-suggestion-announcements'));
    await fireEvent.changeText(labelInput, 'staff:featured');
    await fireEvent(labelInput, 'submitEditing');
    expect(screen.getByTestId('compose-metadata-error')).toHaveTextContent(strings.metadata.reserved);
    await fireEvent.changeText(labelInput, 'bad<label>');
    await fireEvent(labelInput, 'submitEditing');
    expect(screen.getByTestId('compose-metadata-error')).toHaveTextContent(strings.metadata.invalid);
    await fireEvent.press(screen.getByTestId('compose-metadata-add-tag'));
    await fireEvent.changeText(screen.getByTestId('compose-metadata-tag-0-key'), 'level');
    await fireEvent.changeText(screen.getByTestId('compose-metadata-tag-0-value'), 'beginner');
    await fireEvent.press(screen.getByTestId('compose-submit'));
    await waitFor(() => expect(server.called('POST /feed')).toHaveLength(1));
    expect(server.called('POST /feed')[0].body).toMatchObject({ labels: ['announcements'], tags: { level: 'beginner' } });
  });

  test('editing keeps metadata unless changed; members never see staff: entries, staff do', async () => {
    const post = makePost('fpo_1', { body: 'Old', labels: ['drills', 'staff:featured'], tags: { level: 'beginner', 'staff:review': 'pending' }, viewer: { canEdit: true, canDelete: true, canReact: true, canComment: true, canAttachMedia: true, canAttachVideo: true } });
    const server = installFakeServer({ ...signedInAs(poster()), 'GET /feed/fpo_1': post, 'PUT /feed/fpo_1': (call) => ({ ...post, ...call.body }) });
    await renderRouter(APP_DIR, { initialUrl: '/compose?id=fpo_1' });
    await fireEvent.press(await screen.findByTestId('compose-metadata-open'));
    expect(screen.getByTestId('compose-metadata-label-drills')).toBeOnTheScreen();
    expect(screen.queryByTestId('compose-metadata-label-staff:featured')).toBeNull();
    expect(screen.getByTestId('compose-metadata-tag-0-key').props.value).toBe('level');
    expect(screen.queryByTestId('compose-metadata-tag-1')).toBeNull();
    await fireEvent.press(screen.getByTestId('compose-submit'));
    await waitFor(() => expect(server.called('PUT /feed/fpo_1')).toHaveLength(1));
    expect(server.called('PUT /feed/fpo_1')[0].body.labels).toBeUndefined();
    expect(server.called('PUT /feed/fpo_1')[0].body.tags).toBeUndefined();
  });

  test('staff see staff: metadata in the editor', async () => {
    const post = makePost('fpo_1', { body: 'Old', labels: ['staff:featured'], tags: {}, viewer: { canEdit: true } });
    installFakeServer({ ...signedInAs(makeUser({ ...moderator(), flags: { canPostToFeed: true } })), 'GET /feed/fpo_1': post });
    await renderRouter(APP_DIR, { initialUrl: '/compose?id=fpo_1' });
    await fireEvent.press(await screen.findByTestId('compose-metadata-open'));
    expect(screen.getByTestId('compose-metadata-label-staff:featured')).toBeOnTheScreen();
  });

  test('the profile editor sends labels and tags only when they change', async () => {
    const me = makeUser({ labels: ['mentor'], tags: {} });
    const server = installFakeServer({ ...signedInAs(me), 'GET /auth/me': me, 'PUT /me/profile': (call) => ({ ...me, ...call.body }), 'GET /handles/tester/availability': { available: true } });
    await renderRouter(APP_DIR, { initialUrl: '/account/edit-profile' });
    await fireEvent.press(await screen.findByTestId('profile-metadata-open'));
    await fireEvent.press(screen.getByTestId('profile-metadata-label-mentor'));
    const input = screen.getByTestId('profile-metadata-label-input');
    await fireEvent.changeText(input, 'night owl');
    await fireEvent(input, 'submitEditing');
    await fireEvent.press(screen.getByTestId('profile-submit'));
    await waitFor(() => expect(server.called('PUT /me/profile')).toHaveLength(1));
    expect(server.called('PUT /me/profile')[0].body).toMatchObject({ labels: ['night owl'], tags: {} });
  });
});
