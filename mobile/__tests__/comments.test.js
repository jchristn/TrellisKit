import { act, fireEvent, screen, waitFor, within } from '@testing-library/react-native';
import * as Haptics from 'expo-haptics';
import { renderRouter } from 'expo-router/testing-library';
import path from 'path';
import { appendChildren, flattenTree, hideAuthor, insertComment, isPlaceholder, removeComment } from '../src/content/comments';
import { applyReaction, reactionSet, visibleReactions } from '../src/content/reactions';
import { CollapseDelayMs } from '../src/components/feed/CommentComposer';
import strings from '../src/strings';
import { installFakeServer, makeUser, resetEnvironment, signedInAs } from '../test-utils/fakeServer';
import { enumerationResult, likeKey, makeComment, makePost, secondKey, userSummary } from '../test-utils/community';
import { property } from '../test-utils/property';

const APP_DIR = path.resolve(__dirname, '../app');
const me = { id: 'usr_1', displayName: 'Test Member', handle: 'tester', isStaff: false, isDeleted: false };

function tree() {
  return [
    makeComment('cmt_a', {
      childCount: 2,
      descendantCount: 3,
      children: [makeComment('cmt_b', { parentCommentId: 'cmt_a', depth: 1, children: [makeComment('cmt_c', { parentCommentId: 'cmt_b', depth: 2 })], childCount: 1, descendantCount: 1 })],
      moreChildren: { remainingCount: 1, cursor: 'more-a' }
    }),
    makeComment('cmt_d')
  ];
}

describe('comment tree helpers', () => {
  test('flattens depth first, with collapse and load-more rows', () => {
    const rows = flattenTree(tree());
    expect(rows.map((r) => (r.type === 'more' ? 'more:' + r.parentId + ':' + r.cursor : r.comment.id + '@' + r.depth))).toEqual(['cmt_a@0', 'cmt_b@1', 'cmt_c@2', 'more:cmt_a:more-a', 'cmt_d@0']);
    expect(flattenTree(tree(), new Set(['cmt_a'])).map((r) => r.key)).toEqual(['cmt_a', 'cmt_d']);
  });

  test('children pages merge de-duplicated and update the remaining count', () => {
    const next = appendChildren(tree(), 'cmt_a', { items: [makeComment('cmt_b'), makeComment('cmt_e', { parentCommentId: 'cmt_a', depth: 1 })], nextCursor: null });
    expect(next[0].children.map((c) => c.id)).toEqual(['cmt_b', 'cmt_e']);
    expect(next[0].moreChildren).toBeNull();
  });

  test('replies insert under their parent; deletes become placeholders only when they have replies; blocked authors are hidden', () => {
    const withReply = insertComment(tree(), makeComment('cmt_r', { parentCommentId: 'cmt_d', depth: 1 }));
    expect(withReply[1].children[0].id).toBe('cmt_r');
    expect(withReply[1].childCount).toBe(1);
    expect(insertComment(tree(), makeComment('cmt_top'))[0].id).toBe('cmt_top');

    const deleted = removeComment(tree(), 'cmt_a');
    expect(deleted[0].state).toBe('Deleted');
    expect(isPlaceholder(deleted[0])).toBe(true);
    expect(removeComment(tree(), 'cmt_d').map((c) => c.id)).toEqual(['cmt_a']);

    const hidden = hideAuthor([makeComment('cmt_x', { author: userSummary({ id: 'usr_bad' }) })], 'usr_bad');
    expect(hidden[0].state).toBe('BlockedAuthor');
    expect(hidden[0].body).toBeNull();
  });
});

describe('post detail and comments', () => {
  beforeEach(() => resetEnvironment());
  afterEach(() => delete global.fetch);

  const placeholders = [
    makeComment('cmt_del', { state: 'Deleted', author: null, body: null, childCount: 1, children: [makeComment('cmt_kid', { parentCommentId: 'cmt_del', depth: 1 })] }),
    makeComment('cmt_rem', { state: 'Removed', author: null, body: null }),
    makeComment('cmt_hid', { state: 'Hidden', author: null, body: null }),
    makeComment('cmt_blk', { state: 'BlockedAuthor', author: null, body: null })
  ];

  function server(extra = {}) {
    return installFakeServer({
      ...signedInAs(makeUser()),
      'GET /feed/fpo_1': makePost('fpo_1', { commentCount: 5 }),
      'POST /feed/fpo_1/comments/enumerate': enumerationResult(tree()),
      ...extra
    });
  }

  test('the composer area sizes to its content instead of growing over the comment list', async () => {
    server();
    await renderRouter(APP_DIR, { initialUrl: '/feed/fpo_1' });
    const composer = await screen.findByTestId('post-detail-composer');
    const style = [].concat(composer.props.style).reduce((all, part) => ({ ...all, ...(part || {}) }), {});
    // A vertical ScrollView defaults to flexGrow 1, which would stretch it to maxHeight and squeeze the list.
    expect(style.flexGrow).toBe(0);
    expect(style.maxHeight).toBe(360);
  });

  test('renders the tree with placeholders, collapse, and load more replies with the same sort', async () => {
    const s = server({
      'POST /feed/fpo_1/comments/enumerate': (call) => enumerationResult(call.body.ordering === 'CreatedDescending' ? [makeComment('cmt_new')] : tree().concat(placeholders)),
      'POST /comments/cmt_a/children/enumerate': enumerationResult([makeComment('cmt_e', { parentCommentId: 'cmt_a', depth: 1, body: 'Fifth' })])
    });
    await renderRouter(APP_DIR, { initialUrl: '/feed/fpo_1' });
    expect(await screen.findByTestId('comment-cmt_c')).toBeOnTheScreen();
    expect(screen.getByTestId('comment-cmt_del-placeholder')).toHaveTextContent('[deleted]');
    expect(screen.getByTestId('comment-cmt_rem-placeholder')).toHaveTextContent('[removed by a moderator]');
    expect(screen.getByTestId('comment-cmt_hid-placeholder')).toHaveTextContent('[under review]');
    expect(screen.getByTestId('comment-cmt_blk-placeholder')).toHaveTextContent(strings.comments.placeholders.BlockedAuthor);
    expect(screen.getByTestId('comment-cmt_kid')).toBeOnTheScreen();

    await fireEvent.press(screen.getByTestId('comment-cmt_a-collapse'));
    expect(screen.queryByTestId('comment-cmt_b')).toBeNull();
    await fireEvent.press(screen.getByTestId('comment-cmt_a-collapse'));
    expect(await screen.findByTestId('comment-cmt_b')).toBeOnTheScreen();

    await fireEvent.press(screen.getByTestId('more-cmt_a'));
    expect(await screen.findByTestId('comment-cmt_e')).toBeOnTheScreen();
    expect(s.called('POST /comments/cmt_a/children/enumerate')[0].body).toEqual({ maxResults: 20, continuationToken: 'more-a', ordering: 'Top', filters: { depth: 2, childLimit: 5 } });

    await fireEvent.press(screen.getByTestId('comments-sort-new'));
    expect(await screen.findByTestId('comment-cmt_new')).toBeOnTheScreen();
    expect(s.called('POST /feed/fpo_1/comments/enumerate').pop().body.ordering).toBe('CreatedDescending');
  });

  test('the new-comment composer starts as one line and expands on focus or text', async () => {
    jest.useFakeTimers();
    try {
      server();
      await renderRouter(APP_DIR, { initialUrl: '/feed/fpo_1' });
      const input = await screen.findByTestId('comment-composer-body');
      expect(screen.queryByTestId('comment-composer-expanded')).toBeNull();
      expect(screen.queryByTestId('comment-composer-submit')).toBeNull();
      expect(input.props.placeholder).toBe(strings.comments.write);
      await fireEvent(input, 'focus');
      expect(screen.getByTestId('comment-composer-submit')).toBeOnTheScreen();
      await fireEvent(input, 'blur');
      await act(async () => jest.advanceTimersByTime(CollapseDelayMs + 10));
      expect(screen.queryByTestId('comment-composer-submit')).toBeNull();
      await fireEvent.changeText(input, 'Draft');
      await fireEvent(input, 'blur');
      await act(async () => jest.advanceTimersByTime(CollapseDelayMs + 10));
      expect(screen.getByTestId('comment-composer-submit')).toBeOnTheScreen();
    } finally {
      jest.useRealTimers();
    }
  });

  test('reply posts parentCommentId and shows the reply under its parent', async () => {
    const s = server({ 'POST /feed/fpo_1/comments': (call) => ({ status: 201, body: makeComment('cmt_reply', { parentCommentId: call.body.parentCommentId, depth: 1, author: me, body: call.body.body }) }) });
    await renderRouter(APP_DIR, { initialUrl: '/feed/fpo_1' });
    await fireEvent.press(await screen.findByTestId('comment-cmt_d-reply'));
    expect(screen.getByTestId('comment-composer-context')).toHaveTextContent(strings.comments.replyingTo('alexchen'));
    await fireEvent.changeText(screen.getByTestId('comment-composer-body'), 'Yes it does');
    await fireEvent.press(screen.getByTestId('comment-composer-submit'));
    expect(await screen.findByTestId('comment-cmt_reply')).toBeOnTheScreen();
    expect(s.called('POST /feed/fpo_1/comments')[0].body).toEqual({ body: 'Yes it does', parentCommentId: 'cmt_d', mediaIds: [], labels: [], tags: {} });
    expect(screen.queryByTestId('comment-composer-context')).toBeNull();
  });

  test('edit with history, and delete becomes a placeholder', async () => {
    const mine = { canEdit: true, canDelete: true, canReply: true, canReact: true, canReport: false, canModerate: false };
    const own = makeComment('cmt_own', { author: me, viewer: mine, body: 'First', childCount: 1, children: [makeComment('cmt_child', { parentCommentId: 'cmt_own', depth: 1 })] });
    const s = server({
      'POST /feed/fpo_1/comments/enumerate': enumerationResult([own]),
      'PUT /comments/cmt_own': (call) => ({ ...own, body: call.body.body, editedUtc: '2026-11-11T00:00:00Z', revisionCount: 1 }),
      'DELETE /comments/cmt_own': { status: 204, body: null },
      'POST /comments/cmt_own/revisions/enumerate': enumerationResult([{ id: 'cmr_1', revision: 1, body: 'First', attachmentSummary: null, createdUtc: '2026-11-10T00:00:00Z' }])
    });
    await renderRouter(APP_DIR, { initialUrl: '/feed/fpo_1' });
    await fireEvent.press(await screen.findByTestId('comment-cmt_own-menu'));
    await fireEvent.press(await screen.findByTestId('comment-cmt_own-actions-edit'));
    expect(screen.getByTestId('comment-composer-body').props.value).toBe('First');
    await fireEvent.changeText(screen.getByTestId('comment-composer-body'), 'Second');
    await fireEvent.press(screen.getByTestId('comment-composer-submit'));
    await waitFor(() => expect(screen.getByTestId('comment-cmt_own-body')).toHaveTextContent('Second'));
    expect(s.called('PUT /comments/cmt_own')[0].body).toEqual({ body: 'Second', mediaIds: [] });

    await fireEvent.press(screen.getByTestId('comment-cmt_own-byline-edited'));
    expect(await screen.findByTestId('history-cmr_1')).toHaveTextContent(/First/);
    await fireEvent.press(screen.getByTestId('edit-history-close'));

    await fireEvent.press(screen.getByTestId('comment-cmt_own-menu'));
    await fireEvent.press(await screen.findByTestId('comment-cmt_own-actions-delete'));
    await fireEvent.press(await screen.findByTestId('comment-cmt_own-delete-confirm'));
    expect(await screen.findByTestId('comment-cmt_own-placeholder')).toHaveTextContent('[deleted]');
    expect(screen.getByTestId('comment-cmt_child')).toBeOnTheScreen();
  });

  test('a permalink shows the comment in context, then View all comments loads the tree', async () => {
    const s = server({
      'GET /comments/cmt_c': { target: { type: 'FeedPost', id: 'fpo_1' }, ancestors: [makeComment('cmt_a'), makeComment('cmt_b', { depth: 1 })], comment: makeComment('cmt_c', { depth: 2, body: 'The one' }) }
    });
    await renderRouter(APP_DIR, { initialUrl: '/feed/fpo_1?comment=cmt_c' });
    expect(await screen.findByTestId('comment-cmt_c')).toBeOnTheScreen();
    expect(screen.getByText(strings.comments.inContext)).toBeOnTheScreen();
    expect(s.called('POST /feed/fpo_1/comments/enumerate')).toHaveLength(0);
    await fireEvent.press(screen.getByTestId('comments-view-all'));
    expect(await screen.findByTestId('comment-cmt_d')).toBeOnTheScreen();
  });

  test('a deleted post says the content is no longer available', async () => {
    installFakeServer({ ...signedInAs(makeUser()), 'GET /feed/fpo_gone': { status: 404, body: { error: 'NotFound', message: 'Not found.' } }, 'POST /feed/fpo_gone/comments/enumerate': { status: 404, body: { error: 'NotFound' } } });
    await renderRouter(APP_DIR, { initialUrl: '/feed/fpo_gone' });
    expect(await screen.findByTestId('post-detail-gone')).toHaveTextContent(strings.feed.unavailableTitle, { exact: false });
  });

  test('closed comments say so instead of offering the composer', async () => {
    server({ 'GET /feed/fpo_1': makePost('fpo_1', { commentsEnabled: false }), 'POST /feed/fpo_1/comments/enumerate': enumerationResult([]) });
    await renderRouter(APP_DIR, { initialUrl: '/feed/fpo_1' });
    expect(await screen.findByTestId('comments-closed')).toHaveTextContent(strings.comments.disabled);
  });
});

describe('reactions', () => {
  beforeEach(() => {
    resetEnvironment();
    jest.clearAllMocks();
  });
  afterEach(() => delete global.fetch);

  test('applyReaction adds and removes one key; chips follow the property order', () => {
    const start = { reactionCounts: { [likeKey]: 2 }, reactionTotal: 2, myReactions: [] };
    const on = applyReaction(start, likeKey, true);
    expect(on).toEqual({ reactionCounts: { [likeKey]: 3 }, reactionTotal: 3, myReactions: [likeKey] });
    expect(applyReaction(on, likeKey, true)).toEqual(on);
    expect(applyReaction({ reactionCounts: { [likeKey]: 1 }, reactionTotal: 1, myReactions: [likeKey] }, likeKey, false)).toEqual({ reactionCounts: {}, reactionTotal: 0, myReactions: [] });
    const set = reactionSet(property);
    expect(set.likeKey).toBe(property.reactions[0].key);
    expect(visibleReactions({ reactionCounts: { [secondKey]: 1, [likeKey]: 2, retired: 9 }, myReactions: [likeKey] }, set).map((r) => r.key)).toEqual(secondKey === likeKey ? [likeKey] : [likeKey, secondKey]);
  });

  test('a reaction shows at once with a haptic, then takes the server counts', async () => {
    let release;
    const s = installFakeServer({
      ...signedInAs(makeUser()),
      'POST /feed/enumerate': enumerationResult([makePost('fpo_1', { reactionCounts: { [likeKey]: 2 }, reactionTotal: 2 })]),
      ['PUT /feed/fpo_1/reactions/' + likeKey]: () => new Promise((resolve) => (release = () => resolve({ reactionCounts: { [likeKey]: 5 }, reactionTotal: 5, myReactions: [likeKey] })))
    });
    await renderRouter(APP_DIR, { initialUrl: '/feed' });
    const chip = await screen.findByTestId('post-fpo_1-reactions-chip-' + likeKey);
    const pressed = fireEvent.press(chip);
    await waitFor(() => expect(release).toBeDefined());
    expect(screen.getByTestId('post-fpo_1-reactions-chip-' + likeKey)).toHaveTextContent(/3/);
    expect(screen.getByTestId('post-fpo_1-reactions-chip-' + likeKey).props.accessibilityState).toMatchObject({ selected: true });
    expect(Haptics.selectionAsync).toHaveBeenCalled();
    await act(async () => release());
    await pressed;
    await waitFor(() => expect(screen.getByTestId('post-fpo_1-reactions-chip-' + likeKey)).toHaveTextContent(/5/));
    expect(s.called('PUT /feed/fpo_1/reactions/' + likeKey)).toHaveLength(1);
  });

  test('a failed reaction rolls back and says why', async () => {
    installFakeServer({
      ...signedInAs(makeUser()),
      'POST /feed/enumerate': enumerationResult([makePost('fpo_1', { reactionCounts: { [likeKey]: 1 }, reactionTotal: 1, myReactions: [likeKey] })]),
      ['DELETE /feed/fpo_1/reactions/' + likeKey]: { status: 403, body: { error: 'Muted', message: 'Muted.' } }
    });
    await renderRouter(APP_DIR, { initialUrl: '/feed' });
    await fireEvent.press(await screen.findByTestId('post-fpo_1-reactions-chip-' + likeKey));
    expect(await screen.findByTestId('post-fpo_1-reactions-error')).toHaveTextContent(strings.errors.muted);
    expect(screen.getByTestId('post-fpo_1-reactions-chip-' + likeKey)).toHaveTextContent(/1/);
    expect(screen.getByTestId('post-fpo_1-reactions-chip-' + likeKey).props.accessibilityState).toMatchObject({ selected: true });
  });

  test('the picker lists the property reactions with labels and reacts against commentTarget; who reacted pages by reaction', async () => {
    const s = installFakeServer({
      ...signedInAs(makeUser()),
      'POST /feed/enumerate': enumerationResult([makePost('fpo_1', { commentTarget: { type: 'FeedPost', id: 'fpo_1' }, reactionCounts: { [likeKey]: 1 }, reactionTotal: 1 })]),
      ['PUT /feed/fpo_1/reactions/' + secondKey]: { reactionCounts: { [likeKey]: 1, [secondKey]: 1 }, reactionTotal: 2, myReactions: [secondKey] },
      'POST /feed/fpo_1/reactions/enumerate': (call) => enumerationResult([{ user: userSummary(), key: call.body.filters ? call.body.filters.key : likeKey, createdUtc: '2026-11-10T18:10:00Z' }])
    });
    await renderRouter(APP_DIR, { initialUrl: '/feed' });
    await fireEvent.press(await screen.findByTestId('post-fpo_1-reactions-add'));
    const picker = await screen.findByTestId('post-fpo_1-reactions-picker');
    for (const r of property.reactions) expect(within(picker).getByLabelText(strings.reactions.toggleOn(r.label))).toBeOnTheScreen();
    await fireEvent.press(within(picker).getByTestId('post-fpo_1-reactions-pick-' + secondKey));
    await waitFor(() => expect(s.called('PUT /feed/fpo_1/reactions/' + secondKey)).toHaveLength(1));

    await fireEvent(screen.getByTestId('post-fpo_1-reactions-chip-' + likeKey), 'longPress');
    const preview = await screen.findByTestId('reaction-people-preview');
    expect(await within(preview).findByText('Alex Chen')).toBeOnTheScreen();
    expect(s.called('POST /feed/fpo_1/reactions/enumerate')[0].body).toEqual({ maxResults: 5, filters: { key: likeKey } });
    expect(s.called('PUT /feed/fpo_1/reactions/' + likeKey)).toHaveLength(0);
    await fireEvent.press(within(preview).getByTestId('reaction-preview-all'));
    const who = await screen.findByTestId('who-reacted');
    expect(await within(who).findByText('Alex Chen')).toBeOnTheScreen();
    expect(s.called('POST /feed/fpo_1/reactions/enumerate')[1].body).toEqual({ maxResults: 50, filters: { key: likeKey } });

    await fireEvent.press(within(who).getByTestId('who-reacted-filters-button'));
    const labelInput = within(who).getByTestId('who-reacted-filters-panel-label-input');
    await fireEvent.changeText(labelInput, 'regular');
    await fireEvent(labelInput, 'submitEditing');
    await fireEvent.press(within(who).getByTestId('who-reacted-filters-panel-apply'));
    await waitFor(() => expect(s.called('POST /feed/fpo_1/reactions/enumerate').pop().body).toEqual({ maxResults: 50, labels: ['regular'], labelMatch: 'All', filters: { key: likeKey } }));
    expect(within(who).getByTestId('who-reacted-filters-chip-label-regular')).toBeOnTheScreen();
  });

  test('pointer hover previews a read-only reaction, retries errors, and opens a paginated full list', async () => {
    let attempts = 0;
    const row = (id, displayName) => ({ id: 'rx_' + id, user: userSummary({ id, displayName }), key: likeKey });
    const s = installFakeServer({
      ...signedInAs(makeUser()),
      'POST /feed/enumerate': enumerationResult([makePost('fpo_1', { reactionCounts: { [likeKey]: 2 }, reactionTotal: 2, viewer: { canReact: false } })]),
      'POST /feed/fpo_1/reactions/enumerate': (call) => {
        attempts += 1;
        if (attempts === 1) return { status: 503, body: { error: 'Unavailable' } };
        if (call.body.continuationToken) return enumerationResult([row('usr_8', 'Second Person')]);
        return enumerationResult([row('usr_7', 'First Person')], 'next-people');
      }
    });
    await renderRouter(APP_DIR, { initialUrl: '/feed' });
    await fireEvent(await screen.findByTestId('post-fpo_1-reactions-chip-' + likeKey), 'hoverIn');
    const preview = await screen.findByTestId('reaction-people-preview');
    await fireEvent.press(await within(preview).findByLabelText(strings.reactions.previewRetry));
    expect(await within(preview).findByText('First Person')).toBeOnTheScreen();
    expect(s.called('POST /feed/fpo_1/reactions/enumerate')[1].body).toEqual({ maxResults: 5, filters: { key: likeKey } });
    await fireEvent.press(within(preview).getByTestId('reaction-preview-all'));
    const who = await screen.findByTestId('who-reacted');
    await fireEvent.press(await within(who).findByLabelText(strings.feed.loadMore));
    expect(await within(who).findByText('Second Person')).toBeOnTheScreen();
    expect(s.called('POST /feed/fpo_1/reactions/enumerate').at(-1).body).toEqual({ maxResults: 50, continuationToken: 'next-people', filters: { key: likeKey } });
    expect(s.called('PUT /feed/fpo_1/reactions/' + likeKey)).toHaveLength(0);
    expect(s.called('DELETE /feed/fpo_1/reactions/' + likeKey)).toHaveLength(0);
  });

});
