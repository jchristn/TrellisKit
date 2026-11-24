import { test } from 'node:test';
import assert from 'node:assert/strict';
import { ApiClient, ApiError, directMessagesApi, headlineParts, MESSAGING_CONTRACT_ALIASES, messagingMethods, newClientMessageId, sendMessageBody, TARGET_SEGMENTS } from '../src/index.js';
import { createMessagingFake } from '../src/testing/index.js';

function recorder(responseBody = { ok: true }) {
  const calls = [];
  const fetchImpl = async (url, init) => {
    calls.push({ method: init.method, url: url.replace('/api/v1', ''), body: init.body ? JSON.parse(init.body) : undefined, headers: init.headers });
    return new Response(JSON.stringify(responseBody), { status: 200, headers: { 'content-type': 'application/json' } });
  };
  return { calls, client: new ApiClient({ fetchImpl }) };
}

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;

test('every messaging method exists on ApiClient and under its contract name on client.messaging', async () => {
  const names = ['enumerateConversations', 'startConversation', 'getConversation', 'enumerateMessages', 'sendMessage', 'editMessage', 'deleteMessage', 'markConversationRead',
    'markConversationDelivered', 'acceptRequest', 'declineRequest', 'setConversationFolder', 'muteConversation', 'unmuteConversation', 'deleteConversation', 'syncConversations',
    'getMessagingUnreadCount', 'getMessagingSettings', 'updateMessagingSettings', 'searchMessages', 'putMessageReaction', 'deleteMessageReaction', 'enumerateMessageReactions',
    'reportMessage', 'reportConversation'];
  const { calls, client } = recorder();
  for (const name of names) {
    assert.equal(typeof client.messaging[name], 'function', name);
    const onClient = MESSAGING_CONTRACT_ALIASES[name] || name;
    assert.equal(typeof ApiClient.prototype[onClient], 'function', onClient);
  }
  assert.equal(Object.keys(messagingMethods).length, names.length);
  assert.equal(client.messaging, client.messaging);
  assert.equal(typeof directMessagesApi(client).startConversation, 'function');
  // The assistant chat methods keep their names and routes (docs/AI_CONTRACT.md).
  await client.enumerateConversations();
  await client.enumerateDirectConversations();
  await client.getConversation('acv_1');
  await client.getDirectConversation('cnv_1');
  assert.deepEqual(calls.map((c) => c.method + ' ' + c.url), ['POST /assistant-conversations/enumerate', 'POST /conversations/enumerate', 'GET /assistant-conversations/acv_1', 'GET /conversations/cnv_1']);
});

test('messaging methods map to the contract routes and bodies', async () => {
  const { calls, client } = recorder();
  await client.messaging.enumerateConversations({ filters: { folder: 'Requests', unreadOnly: true }, labels: ['a'], tags: { k: 'v' } });
  await client.messaging.startConversation('usr_2', { clientMessageId: 'cm-1', body: 'Hi' });
  await client.messaging.startConversation('usr_3');
  await client.messaging.getConversation('cnv_1');
  await client.enumerateMessages('cnv_1', { ordering: 'SeqAscending', maxResults: 500, filters: { afterSeq: 0 } });
  await client.sendMessage('cnv_1', { clientMessageId: 'cm-2', body: 'Yo', replyToMessageId: 'msg_1' });
  await client.editMessage('msg_1', 'Edited');
  await client.deleteMessage('msg_1');
  await client.markConversationRead('cnv_1', '418');
  await client.markConversationDelivered('cnv_1', 418);
  await client.acceptRequest('cnv_1');
  await client.declineRequest({ id: 'cnv_2' });
  await client.setConversationFolder('cnv_1', 'Archived');
  await client.muteConversation('cnv_1');
  await client.unmuteConversation('cnv_1');
  await client.messaging.deleteConversation('cnv_1');
  await client.syncConversations({ continuationToken: null, maxResults: 900 });
  await client.syncConversations({ syncToken: 'sync:5' });
  await client.getMessagingUnreadCount();
  await client.getMessagingSettings();
  await client.updateMessagingSettings({ dmPolicy: 'Known' });
  await client.searchMessages({ search: 'thursday', maxResults: 100, filters: { withUserId: 'usr_2' } });
  await client.putMessageReaction('msg_1', 'like');
  await client.deleteMessageReaction('msg_1', 'like');
  await client.enumerateMessageReactions('msg_1', { filters: { key: 'like' } });
  await client.reportMessage('msg_1', { reason: 'Harassment', details: 'x' });
  await client.reportConversation('cnv_1', { reason: 'Spam' });
  assert.deepEqual(calls.map((c) => c.method + ' ' + c.url), [
    'POST /conversations/enumerate', 'POST /conversations', 'POST /conversations', 'GET /conversations/cnv_1', 'POST /conversations/cnv_1/messages/enumerate',
    'POST /conversations/cnv_1/messages', 'PUT /messages/msg_1', 'DELETE /messages/msg_1', 'POST /conversations/cnv_1/read', 'POST /conversations/cnv_1/delivered',
    'POST /conversations/cnv_1/accept', 'POST /conversations/cnv_2/decline', 'PUT /conversations/cnv_1/folder', 'PUT /conversations/cnv_1/mute', 'DELETE /conversations/cnv_1/mute',
    'DELETE /conversations/cnv_1', 'POST /conversations/changes/enumerate', 'POST /conversations/changes/enumerate', 'GET /me/messaging/unread-count', 'GET /me/messaging/settings',
    'PUT /me/messaging/settings', 'POST /messages/search/enumerate', 'PUT /messages/msg_1/reactions/like', 'DELETE /messages/msg_1/reactions/like',
    'POST /messages/msg_1/reactions/enumerate', 'POST /reports', 'POST /reports'
  ]);
  assert.deepEqual(calls[0].body, { maxResults: 25, labels: ['a'], labelMatch: 'All', tags: { k: 'v' }, filters: { folder: 'Requests', unreadOnly: true } });
  assert.deepEqual(calls[1].body, { recipientUserId: 'usr_2', message: { clientMessageId: 'cm-1', body: 'Hi', mediaIds: [] } });
  assert.deepEqual(calls[2].body, { recipientUserId: 'usr_3' });
  assert.deepEqual(calls[4].body, { maxResults: 100, ordering: 'SeqAscending', filters: { afterSeq: 0 } });
  assert.deepEqual(calls[5].body, { clientMessageId: 'cm-2', body: 'Yo', mediaIds: [], showLinkPreview: true, replyToMessageId: 'msg_1' });
  assert.deepEqual(calls[6].body, { body: 'Edited' });
  assert.deepEqual(calls[8].body, { seq: 418 });
  assert.deepEqual(calls[12].body, { folder: 'Archived' });
  assert.deepEqual(calls[13].body, { untilUtc: null });
  assert.deepEqual(calls[16].body, { maxResults: 100, syncToken: null });
  assert.deepEqual(calls[17].body, { maxResults: 100, syncToken: 'sync:5' });
  assert.equal(calls[21].body.maxResults, 50);
  assert.deepEqual(calls[21].body.filters, { withUserId: 'usr_2' });
  assert.deepEqual(calls[25].body, { targetType: 'Message', targetId: 'msg_1', reason: 'Harassment', details: 'x' });
  assert.deepEqual(calls[26].body, { targetType: 'Conversation', targetId: 'cnv_1', reason: 'Spam' });
});

test('newClientMessageId is a UUID v4 with and without crypto.randomUUID', () => {
  const a = newClientMessageId();
  assert.match(a, UUID);
  assert.notEqual(a, newClientMessageId());
  const descriptor = Object.getOwnPropertyDescriptor(globalThis, 'crypto');
  Object.defineProperty(globalThis, 'crypto', { value: {}, configurable: true, writable: true });
  try {
    for (let i = 0; i < 20; i += 1) assert.match(newClientMessageId(), UUID);
  } finally {
    Object.defineProperty(globalThis, 'crypto', descriptor);
  }
  const generated = sendMessageBody({ body: 'x' });
  assert.match(generated.clientMessageId, UUID);
  assert.equal(TARGET_SEGMENTS.Idea, 'ideas');
});

test('the messaging fake: start, send idempotently, edit once, tombstone, read, sync, search', async () => {
  const fake = createMessagingFake();
  const client = new ApiClient({ fetchImpl: fake.fetchImpl });
  await assert.rejects(client.messaging.startConversation('usr_1'), (e) => e instanceof ApiError && e.code === 'CannotMessageSelf');
  const started = await client.messaging.startConversation('usr_2', { clientMessageId: 'cm-a', body: 'Hello' });
  const id = started.conversation.id;
  assert.equal(started.message.seq, 1);
  const again = await client.messaging.startConversation('usr_2');
  assert.equal(again.conversation.id, id);

  const m2 = await client.sendMessage(id, { clientMessageId: 'cm-b', body: 'Second' });
  const dup = await client.sendMessage(id, { clientMessageId: 'cm-b', body: 'Second' });
  assert.equal(dup.id, m2.id);
  assert.equal(m2.seq, 2);
  await assert.rejects(client.sendMessage(id, { clientMessageId: 'cm-c', body: '  ' }), (e) => e.code === 'MessageEmpty');

  const edited = await client.editMessage(m2.id, 'Second, edited');
  assert.equal(edited.isEdited, true);
  assert.equal(edited.originalBody, 'Second');
  await assert.rejects(client.editMessage(m2.id, 'Again'), (e) => e.status === 409 && e.code === 'EditWindowClosed');
  const tomb = await client.deleteMessage(m2.id);
  assert.equal(tomb.state, 'Deleted');
  assert.equal(tomb.body, '');

  fake.receive(id, { body: 'See you Thursday?' });
  const conv = await client.messaging.getConversation(id);
  assert.equal(conv.unreadCount, 1);
  assert.equal((await client.getMessagingUnreadCount()).messages, 1);
  const read = await client.markConversationRead(id, 3);
  assert.deepEqual(read, { myLastReadSeq: 3, unreadCount: 0 });

  const history = await client.enumerateMessages(id, { ordering: 'SeqAscending', filters: { afterSeq: 1 } });
  assert.deepEqual(history.objects.map((m) => m.seq), [2, 3]);
  const newest = await client.enumerateMessages(id, { maxResults: 2 });
  assert.deepEqual(newest.objects.map((m) => m.seq), [3, 2]);

  const first = await client.syncConversations();
  assert.equal(first.endOfResults, true);
  assert.equal(first.continuationToken, null);
  assert.match(first.syncToken, /^sync:/);
  fake.receive(id, { body: 'Another' });
  const next = await client.syncConversations({ syncToken: first.syncToken });
  assert.equal(next.objects.length, 1);
  assert.equal(next.objects[0].change, 'Updated');
  fake.expireSyncTokens();
  await assert.rejects(client.syncConversations({ syncToken: next.syncToken }), (e) => e.status === 400 && e.code === 'InvalidContinuationToken');

  const hits = await client.searchMessages({ search: 'thursday' });
  assert.equal(hits.objects.length, 1);
  assert.equal(hits.objects[0].conversationId, id);
  await assert.rejects(client.searchMessages({ search: 'x' }), (e) => e.code === 'ValidationFailed');

  const reaction = await client.putMessageReaction(hits.objects[0].message.id, 'like');
  assert.deepEqual(reaction.reactionCounts, { like: 1 });
  const report = await client.reportConversation(id, { reason: 'Spam' });
  assert.equal(report.targetType, 'Conversation');
  const settings = await client.updateMessagingSettings({ readReceipts: false });
  assert.equal(settings.readReceipts, false);
});

test('the messaging fake: requests, folders, mute, delete for me, blocks removed from sync', async () => {
  const fake = createMessagingFake();
  const client = new ApiClient({ fetchImpl: fake.fetchImpl });
  const request = fake.addConversation({ otherUserId: 'usr_3', folder: 'Requests', requestState: 'Pending' });
  fake.receive(request.id, { body: 'Hi there' });
  const requests = await client.messaging.enumerateConversations({ filters: { folder: 'Requests' } });
  assert.equal(requests.objects.length, 1);
  assert.equal(requests.objects[0].viewer.canAccept, true);
  const accepted = await client.acceptRequest(request.id);
  assert.equal(accepted.folder, 'Inbox');
  await assert.rejects(client.acceptRequest(request.id), (e) => e.status === 409);
  assert.equal((await client.setConversationFolder(request.id, 'Archived')).folder, 'Archived');
  assert.equal((await client.muteConversation(request.id, '2026-12-01T00:00:00Z')).mutedUntilUtc, '2026-12-01T00:00:00Z');
  assert.equal((await client.unmuteConversation(request.id)).isMuted, false);
  const token = (await client.syncConversations()).syncToken;
  fake.block('usr_3');
  const changes = await client.syncConversations({ syncToken: token });
  assert.deepEqual(changes.objects.map((c) => c.change), ['Removed']);
  await assert.rejects(client.sendMessage(request.id, { clientMessageId: 'z', body: 'x' }), (e) => e.code === 'MessagingNotAllowed');
  assert.equal(await client.messaging.deleteConversation(request.id), null);
  await assert.rejects(client.messaging.getConversation(request.id), (e) => e.status === 404);
});

test('the messaging fake pages sync results and keeps the final token', async () => {
  const fake = createMessagingFake({ users: Array.from({ length: 5 }, (_, i) => ({ id: 'usr_' + (i + 2), displayName: 'Member ' + i, handle: 'm' + i })) });
  const client = new ApiClient({ fetchImpl: fake.fetchImpl });
  const start = (await client.syncConversations()).syncToken;
  for (const u of fake.state.users) fake.addConversation({ otherUserId: u.id });
  const p1 = await client.syncConversations({ maxResults: 2, syncToken: start });
  assert.equal(p1.endOfResults, false);
  const p2 = await client.syncConversations({ maxResults: 2, continuationToken: p1.continuationToken });
  const p3 = await client.syncConversations({ maxResults: 2, continuationToken: p2.continuationToken });
  assert.equal(p3.endOfResults, true);
  assert.equal(p1.objects.length + p2.objects.length + p3.objects.length, 5);
  const after = await client.syncConversations({ syncToken: p3.syncToken });
  assert.equal(after.objects.length, 0);
});

test('headlineParts splits search headline match markers', () => {
  assert.deepEqual(headlineParts('see you **Thursday** at **six**'), [
    { text: 'see you ', match: false }, { text: 'Thursday', match: true }, { text: ' at ', match: false }, { text: 'six', match: true }
  ]);
  assert.deepEqual(headlineParts('**word**'), [{ text: 'word', match: true }]);
  assert.deepEqual(headlineParts('a ** b'), [{ text: 'a ** b', match: false }]);
  assert.deepEqual(headlineParts(''), []);
  assert.deepEqual(headlineParts(null), []);
});
