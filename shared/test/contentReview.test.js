import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  ApiClient, ContentReviewActions, ContentReviewCategories, ContentReviewSeverities, ContentReviewStatuses, contentReviewActionAvailable, contentReviewMethods
} from '../src/index.js';

function recorder() {
  const calls = [];
  const fetchImpl = async (url, init) => {
    calls.push({ method: init.method, url: url.replace('/api/v1', ''), body: init.body ? JSON.parse(init.body) : undefined });
    return new Response(JSON.stringify({ ok: true }), { status: 200, headers: { 'content-type': 'application/json' } });
  };
  return { calls, client: new ApiClient({ fetchImpl }) };
}

test('content review ApiClient methods exist and map to their routes', async () => {
  const names = ['adminContentReviewSummary', 'adminEnumerateContentReview', 'adminGetContentChange', 'adminActOnContentChange'];
  for (const name of names) assert.equal(typeof ApiClient.prototype[name], 'function', name);
  assert.equal(Object.keys(contentReviewMethods).length, names.length);
  assert.deepEqual(ContentReviewStatuses, ['Pending', 'Clean', 'Flagged', 'Dismissed', 'Actioned']);
  assert.equal(ContentReviewCategories.length, 9);
  assert.ok(ContentReviewCategories.includes('Profanity'));
  assert.equal(ContentReviewSeverities[ContentReviewSeverities.length - 1], 'Critical');

  const { calls, client } = recorder();
  await client.adminContentReviewSummary();
  await client.adminEnumerateContentReview({ maxResults: 5000, filters: { status: ['Flagged'], category: ['Spam'] } });
  await client.adminGetContentChange('chg_1');
  await client.adminActOnContentChange('chg_1', { action: 'Hide', reason: 'Spam' });
  assert.deepEqual(calls.map((c) => c.method + ' ' + c.url), [
    'GET /admin/content-review/summary', 'POST /admin/content-review/enumerate', 'GET /admin/content-review/chg_1', 'POST /admin/content-review/chg_1/action'
  ]);
  assert.deepEqual(calls[1].body.filters, { status: ['Flagged'], category: ['Spam'] });
  assert.ok(calls[1].body.maxResults <= 500, 'admin page size is clamped');
  assert.deepEqual(calls[3].body, { action: 'Hide', reason: 'Spam' });
});

test('content review actions follow the row', () => {
  const post = { moderationTargetType: 'FeedPost', author: { id: 'usr_1' } };
  const message = { moderationTargetType: null, author: { id: 'usr_2' } };
  const orphan = { moderationTargetType: null, author: null, actor: null };
  assert.deepEqual(ContentReviewActions.filter((a) => contentReviewActionAvailable(post, a)), ['Dismiss', 'Hide', 'Remove', 'Warn', 'Mute', 'Ban']);
  assert.deepEqual(ContentReviewActions.filter((a) => contentReviewActionAvailable(message, a)), ['Dismiss', 'Warn', 'Mute', 'Ban']);
  assert.deepEqual(ContentReviewActions.filter((a) => contentReviewActionAvailable(orphan, a)), ['Dismiss']);
  assert.equal(contentReviewActionAvailable(null, 'Dismiss'), false);
  assert.equal(contentReviewActionAvailable(post, 'Shout'), false);
});
