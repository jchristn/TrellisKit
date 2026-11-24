import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  ApiClient, ApiError, applyPreferenceChange, applyReaction, createCursorPager, createReactionSetCache, extractMentionHandles, formatUnreadBadge,
  insertMention, isStaffUser, isTimeOfDay, isWithinNotificationHours, mentionQueryAt, mergeUniqueById, moderationActionsFor, normalizeReactionSet,
  parseRetryAfter, reactionSet, reportProblems, targetSegment, visibleReactions
} from '../src/index.js';

const property = { reactions: [{ key: 'like', emoji: '+', label: 'Like' }, { key: 'fire', emoji: '!', label: 'Fire' }, { key: 'idea', emoji: '?', label: 'Idea' }] };

test('mentions: extraction, autocomplete trigger, and insertion', () => {
  assert.deepEqual(extractMentionHandles('hi @Alex and @alex, @sam. mail a@b.test \\@escaped @ab'), ['Alex', 'sam']);
  assert.equal(mentionQueryAt('hello @', 7), null);
  assert.deepEqual(mentionQueryAt('hello @a', 8), { query: 'a', start: 6, end: 8 });
  assert.deepEqual(mentionQueryAt('x @al.ex', 8), { query: 'al.ex', start: 2, end: 8 });
  assert.equal(mentionQueryAt('mail me@ex', 10), null);
  assert.equal(mentionQueryAt('done @alex ', 11), null);
  const out = insertMention('hi @al there', { start: 3, end: 6 }, 'alexchen');
  assert.deepEqual(out, { text: 'hi @alexchen there', caret: 12 });
  assert.deepEqual(insertMention('hi @al', { start: 3, end: 6 }, 'alexchen'), { text: 'hi @alexchen ', caret: 13 });
});

test('cursor pager pages, de-duplicates, refreshes, and records errors', async () => {
  const pages = {
    '': { items: [{ id: 'a', v: 1 }, { id: 'b' }], nextCursor: 'c1', unreadCount: 4 },
    c1: { items: [{ id: 'b', v: 2 }, { id: 'c' }], nextCursor: null }
  };
  const seen = [];
  const pager = createCursorPager(async ({ cursor }) => {
    seen.push(cursor);
    return pages[cursor || ''];
  });
  const states = [];
  pager.subscribe((s) => states.push(s));
  await pager.loadMore();
  assert.equal(pager.getState().extra.unreadCount, 4);
  await pager.loadMore();
  assert.deepEqual(pager.getState().items.map((x) => x.id), ['a', 'b', 'c']);
  assert.equal(pager.getState().items[1].v, 2);
  assert.equal(pager.getState().done, true);
  await pager.loadMore();
  assert.deepEqual(seen, [null, 'c1']);
  await pager.refresh();
  assert.deepEqual(pager.getState().items.map((x) => x.id), ['a', 'b']);
  assert.ok(states.some((s) => s.loading));
  pager.update((items) => items.filter((x) => x.id !== 'a'));
  assert.deepEqual(pager.getState().items.map((x) => x.id), ['b']);

  const failing = createCursorPager(async () => { throw new Error('down'); });
  await failing.loadMore();
  assert.equal(failing.getState().error.message, 'down');
  assert.equal(failing.getState().loading, false);
  assert.deepEqual(mergeUniqueById([{ id: 1 }], [{ id: 1, x: 1 }, { id: 2 }]), [{ id: 1, x: 1 }, { id: 2 }]);
});

test('reactions: set from the property, visible counts in set order, optimistic toggles', async () => {
  assert.deepEqual(reactionSet(property).likeKey, 'like');
  assert.deepEqual(reactionSet({}).items, []);
  assert.equal(normalizeReactionSet({ items: [] }, property).likeKey, 'like');
  assert.equal(normalizeReactionSet({ items: [{ key: 'x', emoji: 'x', label: 'X' }], likeKey: 'x' }).likeKey, 'x');
  const set = reactionSet(property);
  const shown = visibleReactions(set, { idea: 2, like: 1, removed: 9, fire: 0 }, ['idea']);
  assert.deepEqual(shown.map((r) => [r.key, r.count, r.mine]), [['like', 1, false], ['idea', 2, true]]);

  const start = { reactionCounts: { like: 1 }, reactionTotal: 1, myReactions: [] };
  const added = applyReaction(start, 'like', true);
  assert.deepEqual(added, { reactionCounts: { like: 2 }, reactionTotal: 2, myReactions: ['like'] });
  assert.deepEqual(applyReaction(added, 'like', true), added);
  assert.deepEqual(applyReaction(added, 'like', false), { reactionCounts: { like: 1 }, reactionTotal: 1, myReactions: [] });
  assert.deepEqual(applyReaction({ reactionCounts: { fire: 1 }, reactionTotal: 1, myReactions: ['fire'] }, 'fire', false), { reactionCounts: {}, reactionTotal: 0, myReactions: [] });

  let loads = 0;
  const cache = createReactionSetCache(async () => { loads += 1; if (loads === 1) throw new Error('x'); return { items: [] }; });
  await assert.rejects(cache.get());
  await cache.get();
  await cache.get();
  assert.equal(loads, 2);
});

test('notifications: badge text, times, hours window, and optimistic preference changes', () => {
  assert.equal(formatUnreadBadge(0), '');
  assert.equal(formatUnreadBadge(7), '7');
  assert.equal(formatUnreadBadge(100), '99+');
  assert.ok(isTimeOfDay('07:00'));
  assert.ok(!isTimeOfDay('7:00'));
  assert.ok(!isTimeOfDay('24:00'));
  const day = { enabled: true, start: '07:00', end: '22:00' };
  assert.ok(isWithinNotificationHours(day, 7 * 60));
  assert.ok(!isWithinNotificationHours(day, 22 * 60));
  assert.ok(!isWithinNotificationHours(day, 3 * 60));
  const night = { enabled: true, start: '22:00', end: '06:00' };
  assert.ok(isWithinNotificationHours(night, 23 * 60));
  assert.ok(isWithinNotificationHours(night, 60));
  assert.ok(!isWithinNotificationHours(night, 12 * 60));
  assert.ok(isWithinNotificationHours({ enabled: true, start: '09:00', end: '09:00' }, 0));
  assert.ok(isWithinNotificationHours({ enabled: false, start: '09:00', end: '10:00' }, 0));

  const doc = { pushPaused: false, categories: [{ category: 'Replies', inApp: true, push: true, email: false }], notificationHours: { enabled: true, start: '07:00', end: '22:00', timeZone: 'UTC' } };
  const next = applyPreferenceChange(doc, { categories: [{ category: 'Replies', inApp: false }], notificationHours: { start: '08:00' }, pushPaused: true });
  assert.deepEqual(next.categories[0], { category: 'Replies', inApp: false, push: false, email: false });
  assert.equal(next.notificationHours.start, '08:00');
  assert.equal(next.notificationHours.timeZone, 'UTC');
  assert.equal(next.pushPaused, true);
});

test('moderation helpers', () => {
  assert.deepEqual(reportProblems({ reason: 'Other', details: 'short' }), { details: 'detailsRequired' });
  assert.deepEqual(reportProblems({ reason: 'Spam' }), {});
  assert.deepEqual(reportProblems({ reason: 'Nope' }), { reason: 'reasonRequired' });
  assert.deepEqual(reportProblems({ reason: 'Spam', details: 'x'.repeat(1001) }), { details: 'detailsTooLong' });
  assert.deepEqual(moderationActionsFor('Hidden'), ['Unhide', 'Remove']);
  assert.deepEqual(moderationActionsFor('Removed'), ['Restore']);
  assert.deepEqual(moderationActionsFor('Published'), ['Hide', 'Remove']);
  assert.ok(isStaffUser({ isStaff: true }));
  assert.ok(isStaffUser({ roles: [{ role: 'FeedModerator' }] }));
  assert.ok(isStaffUser({ roles: ['SystemAdmin'] }));
  // Phase 4 (section 1.1): DirectoryModerator, EventManager, and LibraryEditor holders are staff.
  assert.ok(isStaffUser({ roles: [{ role: 'EventManager' }] }));
  assert.ok(isStaffUser({ roles: ['LibraryEditor'] }));
  assert.ok(!isStaffUser({ roles: [{ role: 'User' }] }));
});

test('ApiClient Phase 2 routes, target segments, and Retry-After on 429', async () => {
  const calls = [];
  const fetchImpl = async (url, init) => {
    calls.push({ url, method: init.method, body: init.body });
    if (url.includes('/reports')) return new Response(JSON.stringify({ error: 'RateLimited', message: 'Slow down.' }), { status: 429, headers: { 'content-type': 'application/json', 'retry-after': '42' } });
    return new Response(JSON.stringify({ ok: true }), { status: 200, headers: { 'content-type': 'application/json' } });
  };
  const client = new ApiClient({ fetchImpl });
  await client.listFeed({ continuationToken: 'abc', maxResults: 20, labels: ['a', 'b'], tags: { level: 'x' } });
  await client.putReaction({ type: 'Comment', id: 'cmt_1' }, 'like');
  await client.deleteReaction({ type: 'FeedPost', id: 'fpo_1' }, 'fire');
  await client.listComments({ type: 'FeedPost', id: 'fpo_1' }, { ordering: 'Top', filters: { depth: 3 } });
  await client.enumerate('/admin/media', { maxResults: 900, filters: { kind: 'Video' } });
  await client.enumerateLabels({ targetType: 'FeedPost', prefix: 'gu' });
  await client.completeUpload('med_1');
  await client.completeUpload('med_2', [{ partNumber: 1, etag: '"x"' }]);
  await client.getMediaBatch(['med_a', 'med_b']);
  await client.revokeModerator('usr_1');
  await client.unregisterPushToken('ExponentPushToken[a]');
  assert.deepEqual(calls.map((c) => c.method + ' ' + c.url), [
    'GET /api/v1/feed?maxResults=20&continuationToken=abc&labels=a&labels=b&labelMatch=All&tag.level=x',
    'PUT /api/v1/comments/cmt_1/reactions/like',
    'DELETE /api/v1/feed/fpo_1/reactions/fire',
    'GET /api/v1/feed/fpo_1/comments?maxResults=25&ordering=Top&depth=3',
    'POST /api/v1/admin/media/enumerate',
    'POST /api/v1/labels/enumerate',
    'POST /api/v1/media/med_1/complete',
    'POST /api/v1/media/med_2/complete',
    'POST /api/v1/media/enumerate',
    'DELETE /api/v1/admin/moderators/usr_1?resourceType=Feed',
    'DELETE /api/v1/me/push-tokens'
  ]);
  assert.deepEqual(JSON.parse(calls[4].body), { maxResults: 500, filters: { kind: 'Video' } });
  assert.deepEqual(JSON.parse(calls[5].body), { maxResults: 25, targetType: 'FeedPost', prefix: 'gu' });
  assert.equal(calls[6].body, '{}');
  assert.equal(JSON.parse(calls[7].body).parts[0].etag, '"x"');
  await assert.rejects(client.createReport({ targetType: 'User', targetId: 'usr_2', reason: 'Spam' }), (err) => err instanceof ApiError && err.retryAfterSeconds === 42 && err.code === 'RateLimited');
  assert.throws(() => targetSegment('Nope'));
  assert.equal(parseRetryAfter('5'), 5);
  assert.equal(parseRetryAfter(new Date(Date.now() + 10000).toUTCString()) > 0, true);
  assert.equal(parseRetryAfter(''), null);
  const err = new ApiError('x', { body: { fieldErrors: { body: 'bad' } } });
  assert.deepEqual(err.fieldErrors, { body: 'bad' });
});
