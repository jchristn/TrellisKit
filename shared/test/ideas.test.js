import { test } from 'node:test';
import assert from 'node:assert/strict';
import { ApiClient, ideasMethods } from '../src/index.js';
import { createIdeasFake } from '../src/testing/index.js';

function recorder() {
  const calls = [];
  const fetchImpl = async (url, init) => {
    calls.push({ method: init.method, url: url.replace('/api/v1', ''), body: init.body ? JSON.parse(init.body) : undefined });
    return new Response(JSON.stringify({ ok: true }), { status: 200, headers: { 'content-type': 'application/json' } });
  };
  return { calls, client: new ApiClient({ fetchImpl }) };
}

test('ideas methods map to the contract routes', async () => {
  const names = ['enumerateIdeas', 'getIdea', 'createIdea', 'updateIdea', 'voteIdea', 'unvoteIdea', 'followIdea', 'unfollowIdea', 'enumerateIdeaStatuses', 'enumerateIdeaComments',
    'createIdeaComment', 'putIdeaReaction', 'deleteIdeaReaction', 'enumerateIdeaReactions', 'reportIdea', 'enumeratePublicIdeas', 'getPublicIdea', 'adminEnumerateIdeas', 'adminCreateIdea',
    'adminUpdateIdea', 'adminSetIdeaStatus', 'adminMergeIdea', 'adminUnmergeIdea', 'adminDeleteIdea', 'adminEnumerateIdeaStatuses', 'adminCreateIdeaStatus', 'adminUpdateIdeaStatus',
    'adminDeleteIdeaStatus', 'adminOrderIdeaStatuses'];
  for (const name of names) assert.equal(typeof ApiClient.prototype[name], 'function', name);
  assert.equal(Object.keys(ideasMethods).length, names.length);

  const { calls, client } = recorder();
  await client.enumerateIdeas({ ordering: 'Trending', filters: { status: ['Planned'], open: true }, labels: ['web'] });
  await client.getIdea('ida_1');
  await client.createIdea({ title: 'T', body: 'B' });
  await client.updateIdea('ida_1', { title: 'T2' });
  await client.voteIdea('ida_1');
  await client.unvoteIdea('ida_1');
  await client.followIdea('ida_1');
  await client.unfollowIdea('ida_1');
  await client.enumerateIdeaComments('ida_1', { ordering: 'Top' });
  await client.createIdeaComment('ida_1', { body: 'Yes' });
  await client.putIdeaReaction('ida_1', 'like');
  await client.deleteIdeaReaction('ida_1', 'like');
  await client.putReaction({ type: 'Idea', id: 'ida_1' }, 'like');
  await client.enumeratePublicIdeas();
  await client.getPublicIdea('ida_1');
  await client.adminEnumerateIdeas({ maxResults: 999, filters: { merged: true } });
  await client.adminSetIdeaStatus('ida_1', { status: 'Shipped', statusNote: 'Done' });
  await client.adminMergeIdea('ida_1', { id: 'ida_2' });
  await client.adminUnmergeIdea('ida_1');
  await client.adminDeleteIdea('ida_1');
  await client.adminOrderIdeaStatuses(['ids_b', 'ids_a']);
  await client.reportIdea('ida_1', { reason: 'Spam' });
  assert.deepEqual(calls.map((c) => c.method + ' ' + c.url), [
    'POST /ideas/enumerate', 'GET /ideas/ida_1', 'POST /ideas', 'PUT /ideas/ida_1', 'PUT /ideas/ida_1/vote', 'DELETE /ideas/ida_1/vote', 'PUT /ideas/ida_1/follow', 'DELETE /ideas/ida_1/follow',
    'POST /ideas/ida_1/comments/enumerate', 'POST /ideas/ida_1/comments', 'PUT /ideas/ida_1/reactions/like', 'DELETE /ideas/ida_1/reactions/like', 'PUT /ideas/ida_1/reactions/like',
    'POST /public/ideas/enumerate', 'GET /public/ideas/ida_1', 'POST /admin/ideas/enumerate', 'PUT /admin/ideas/ida_1/status', 'POST /admin/ideas/ida_1/merge',
    'POST /admin/ideas/ida_1/unmerge', 'DELETE /admin/ideas/ida_1', 'PUT /admin/ideas/statuses/order', 'POST /reports'
  ]);
  assert.deepEqual(calls[0].body, { maxResults: 25, ordering: 'Trending', labels: ['web'], labelMatch: 'All', filters: { status: ['Planned'], open: true } });
  assert.equal(calls[15].body.maxResults, 500);
  assert.deepEqual(calls[16].body, { status: 'Shipped', statusNote: 'Done', notifyVoters: true });
  assert.deepEqual(calls[17].body, { intoIdeaId: 'ida_2' });
  assert.deepEqual(calls[20].body, { ideaStatusIds: ['ids_b', 'ids_a'] });
  assert.deepEqual(calls[21].body, { targetType: 'Idea', targetId: 'ida_1', reason: 'Spam' });
});

test('the ideas fake: votes, closed statuses, merges, follows, comments, and the public board', async () => {
  const fake = createIdeasFake();
  const client = new ApiClient({ fetchImpl: fake.fetchImpl });
  const a = await client.createIdea({ title: 'Dark mode', body: 'Please.', labels: ['web'] });
  assert.equal(a.voteCount, 1);
  assert.equal(a.hasVoted, true);
  const b = fake.addIdea({ title: 'Dark theme', author: null, voters: ['usr_5', 'usr_6'], audience: 'Public' });
  assert.deepEqual((await client.enumerateIdeas()).objects.map((i) => i.id), [b.id, a.id]);
  assert.deepEqual((await client.enumerateIdeas({ labels: ['web'] })).objects.map((i) => i.id), [a.id]);
  const voted = await client.voteIdea(b.id);
  assert.equal(voted.voteCount, 3);
  assert.equal((await client.voteIdea(b.id)).voteCount, 3);
  assert.equal((await client.unvoteIdea(b.id)).voteCount, 2);

  await client.adminSetIdeaStatus(a.id, { status: 'Shipped', statusNote: 'Out now' });
  await assert.rejects(client.voteIdea(a.id), (e) => e.status === 409);
  assert.equal((await client.enumerateIdeas({ filters: { open: true } })).objects.length, 1);
  await client.adminSetIdeaStatus(a.id, { status: 'UnderReview' });

  const merged = await client.adminMergeIdea(a.id, b.id);
  assert.equal(merged.mergedInto.id, b.id);
  assert.equal((await client.getIdea(b.id)).voteCount, 3);
  await assert.rejects(client.voteIdea(a.id), (e) => e.code === 'IdeaMerged');
  await assert.rejects(client.createIdeaComment(a.id, { body: 'x' }), (e) => e.code === 'IdeaMerged');
  assert.equal((await client.enumerateIdeas()).objects.length, 1);
  await client.adminUnmergeIdea(a.id);
  assert.equal((await client.getIdea(b.id)).voteCount, 2);

  assert.deepEqual(await client.followIdea(b.id), { isFollowed: true });
  assert.deepEqual(await client.unfollowIdea(b.id), { isFollowed: false });
  await client.createIdeaComment(b.id, { body: 'Agree' });
  assert.equal((await client.enumerateIdeaComments(b.id)).objects.length, 1);
  assert.deepEqual((await client.putIdeaReaction(b.id, 'like')).reactionCounts, { like: 1 });

  const pub = await client.enumeratePublicIdeas();
  assert.deepEqual(pub.objects.map((i) => i.id), [b.id]);
  assert.equal(pub.objects[0].hasVoted, undefined);
  await assert.rejects(client.getPublicIdea(a.id), (e) => e.status === 404);

  const status = await client.adminCreateIdeaStatus({ key: 'Testing', label: 'Testing' });
  await client.adminOrderIdeaStatuses([status.id]);
  assert.equal((await client.adminEnumerateIdeaStatuses()).objects[0].key, 'Testing');
  await client.adminDeleteIdea(a.id);
  await assert.rejects(client.getIdea(a.id), (e) => e.status === 404);
});

test('the ideas fake refuses member submissions when staff only', async () => {
  const fake = createIdeasFake({ memberSubmissions: 'StaffOnly', staff: false });
  const client = new ApiClient({ fetchImpl: fake.fetchImpl });
  await assert.rejects(client.createIdea({ title: 'Idea', body: 'x' }), (e) => e.status === 403);
  await assert.rejects(client.adminEnumerateIdeas(), (e) => e.status === 403);
});
