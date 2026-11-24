import { test } from 'node:test';
import assert from 'node:assert/strict';
import { ApiClient, broadcastsMethods } from '../src/index.js';
import { createBroadcastsFake } from '../src/testing/index.js';

function recorder() {
  const calls = [];
  const fetchImpl = async (url, init) => {
    calls.push({ method: init.method, url: url.replace('/api/v1', ''), body: init.body ? JSON.parse(init.body) : undefined });
    return new Response(JSON.stringify({ ok: true }), { status: 200, headers: { 'content-type': 'application/json' } });
  };
  return { calls, client: new ApiClient({ fetchImpl }) };
}

test('broadcast methods map to the contract routes', async () => {
  const names = ['adminEnumerateBroadcasts', 'adminCreateBroadcast', 'adminGetBroadcast', 'adminUpdateBroadcast', 'adminDeleteBroadcast', 'adminTestBroadcast', 'adminScheduleBroadcast',
    'adminSendBroadcast', 'adminCancelBroadcast', 'adminEnumerateBroadcastDeliveries', 'adminPreviewSegment', 'adminEnumerateSegments', 'adminCreateSegment', 'adminGetSegment',
    'adminUpdateSegment', 'adminDeleteSegment', 'getAnnouncement', 'enumerateAnnouncements'];
  for (const name of names) assert.equal(typeof ApiClient.prototype[name], 'function', name);
  assert.equal(Object.keys(broadcastsMethods).length, names.length);
  const { calls, client } = recorder();
  await client.adminEnumerateBroadcasts({ filters: { status: 'Sent' } });
  await client.adminCreateBroadcast({ title: 'Hi' });
  await client.adminGetBroadcast('bct_1');
  await client.adminUpdateBroadcast('bct_1', { title: 'Hi' });
  await client.adminDeleteBroadcast('bct_1');
  await client.adminTestBroadcast('bct_1', ['push', 'email']);
  await client.adminScheduleBroadcast('bct_1', '2026-12-20T17:00:00Z');
  await client.adminSendBroadcast('bct_1', '812');
  await client.adminCancelBroadcast('bct_1');
  await client.adminEnumerateBroadcastDeliveries('bct_1', { maxResults: 1000, filters: { channel: 'email' } });
  await client.adminPreviewSegment({ segmentId: 'bsg_1' });
  await client.adminEnumerateSegments();
  await client.adminCreateSegment({ name: 'Active' });
  await client.adminGetSegment('bsg_1');
  await client.adminUpdateSegment('bsg_1', { name: 'A' });
  await client.adminDeleteSegment('bsg_1');
  await client.getAnnouncement('bct_1');
  await client.enumerateAnnouncements();
  assert.deepEqual(calls.map((c) => c.method + ' ' + c.url), [
    'POST /admin/broadcasts/enumerate', 'POST /admin/broadcasts', 'GET /admin/broadcasts/bct_1', 'PUT /admin/broadcasts/bct_1', 'DELETE /admin/broadcasts/bct_1',
    'POST /admin/broadcasts/bct_1/test', 'POST /admin/broadcasts/bct_1/schedule', 'POST /admin/broadcasts/bct_1/send', 'POST /admin/broadcasts/bct_1/cancel',
    'POST /admin/broadcasts/bct_1/deliveries/enumerate', 'POST /admin/broadcasts/segments/preview', 'POST /admin/broadcasts/segments/enumerate', 'POST /admin/broadcasts/segments',
    'GET /admin/broadcasts/segments/bsg_1', 'PUT /admin/broadcasts/segments/bsg_1', 'DELETE /admin/broadcasts/segments/bsg_1', 'GET /announcements/bct_1', 'POST /announcements/enumerate'
  ]);
  assert.deepEqual(calls[5].body, { channels: ['push', 'email'] });
  assert.deepEqual(calls[6].body, { sendAtUtc: '2026-12-20T17:00:00Z' });
  assert.deepEqual(calls[7].body, { confirmCount: 812 });
  assert.equal(calls[9].body.maxResults, 500);
});

test('the broadcasts fake: segments, preview, confirmCount, sending, cancel, and announcements', async () => {
  const fake = createBroadcastsFake({ audienceCount: 100, maxPerDay: 1 });
  const client = new ApiClient({ fetchImpl: fake.fetchImpl });
  await assert.rejects(client.adminCreateSegment({ name: 'Bad', filters: [{ field: 'shoeSize', operator: 'eq', value: '9' }] }), (e) => e.code === 'SegmentInvalid');
  const segment = await client.adminCreateSegment({ name: 'Ranked', filters: [{ field: 'profile.rank', operator: 'in', value: 'a,b' }] });
  const preview = await client.adminPreviewSegment({ segmentId: segment.id });
  assert.equal(preview.count, 100);
  assert.ok(preview.sample.objects.length <= 10);

  const draft = await client.adminCreateBroadcast({ title: 'Holiday', channels: { inApp: true, push: true }, inApp: { title: 'Holiday', body: 'Closed Monday.' }, segmentId: segment.id });
  assert.equal(draft.status, 'Draft');
  assert.equal((await client.adminTestBroadcast(draft.id, ['push'])).accepted, true);
  await assert.rejects(client.adminSendBroadcast(draft.id, 50), (e) => e.status === 400 && e.code === 'ValidationFailed');
  const sending = await client.adminSendBroadcast(draft.id, 97);
  assert.equal(sending.status, 'Sending');
  await assert.rejects(client.adminUpdateBroadcast(draft.id, { title: 'x' }), (e) => e.code === 'BroadcastNotEditable');
  const deliveries = await client.adminEnumerateBroadcastDeliveries(draft.id);
  assert.equal(deliveries.objects.length, 1);

  const second = await client.adminCreateBroadcast({ title: 'Again' });
  await assert.rejects(client.adminScheduleBroadcast(second.id, '2026-12-20T17:00:00Z'), (e) => e.code === 'BroadcastLimitReached');
  assert.equal((await client.adminCancelBroadcast(second.id)).status, 'Canceled');
  await assert.rejects(client.adminCreateBroadcast({ title: 'E', category: 'Essential' }), (e) => Boolean(e.fieldErrors.essentialReason));

  const list = await client.enumerateAnnouncements();
  assert.deepEqual(list.objects.map((a) => a.title), ['Holiday']);
  assert.equal((await client.getAnnouncement(draft.id)).body, 'Closed Monday.');
  await assert.rejects(client.getAnnouncement(second.id), (e) => e.status === 404);
  const drafts = await client.adminCreateBroadcast({ title: 'Draft' });
  assert.equal(await client.adminDeleteBroadcast(drafts.id), null);
  assert.equal((await client.adminEnumerateBroadcasts({ filters: { status: 'Sent' } })).objects.length, 1);
});
