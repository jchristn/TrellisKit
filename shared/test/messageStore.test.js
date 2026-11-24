import { test } from 'node:test';
import assert from 'node:assert/strict';
import { ApiClient, createConversationListStore, createMessageStore, messageSummaryOf, RealtimeClient, SendState, tombstoneOf } from '../src/index.js';
import { createFakeClock, createFakeWebSocket, createMessagingFake, flushAsync, welcomeData } from '../src/testing/index.js';

const ME = { id: 'usr_1', displayName: 'Sam Rivera' };
const OTHER = { id: 'usr_2', displayName: 'Alex Chen' };

function msg(seq, extra = {}) {
  return { id: 'msg_' + seq, conversationId: 'cnv_1', seq, clientMessageId: 'cm-' + seq, author: OTHER, body: 'Body ' + seq, state: 'Visible', attachments: [], createdUtc: '2026-11-12T12:00:0' + (seq % 10) + 'Z', updatedUtc: '2026-11-12T12:00:00Z', ...extra };
}

function seqs(store) {
  return store.getSnapshot().messages.map((m) => (m.seq === null ? 'p:' + m.clientMessageId : m.seq));
}

test('merges REST pages, socket events, and gap fills by id and seq in seq order', () => {
  const store = createMessageStore({ conversationId: 'cnv_1', currentUser: ME });
  store.mergePage({ objects: [msg(5), msg(4), msg(3)], continuationToken: 'tok', endOfResults: false }, { ordering: 'SeqDescending' });
  assert.deepEqual(seqs(store), [3, 4, 5]);
  assert.equal(store.getSnapshot().hasOlder, true);
  store.applyEvent('message.created', { conversationId: 'cnv_1', message: msg(7) });
  store.applyEvent('message.created', { conversationId: 'cnv_1', message: msg(6), gapFill: true });
  store.applyEvent('message.created', { conversationId: 'cnv_1', message: msg(7) });
  store.mergePage([msg(6), msg(1), msg(2)]);
  assert.deepEqual(seqs(store), [1, 2, 3, 4, 5, 6, 7]);
  assert.equal(store.getSnapshot().newestSeq, 7);
  assert.equal(store.getSnapshot().oldestSeq, 1);
  assert.equal(store.applyEvent('message.created', { conversationId: 'cnv_other', message: msg(8, { conversationId: 'cnv_other' }) }), false);
});

test('message.updated replaces by id; message.deleted makes a tombstone that later updates do not revive', () => {
  const store = createMessageStore({ conversationId: 'cnv_1' });
  store.mergePage([msg(1), msg(2)]);
  store.applyEvent('message.updated', { conversationId: 'cnv_1', message: msg(2, { body: 'Edited', isEdited: true, originalBody: 'Body 2', updatedUtc: '2026-11-12T12:05:00Z' }) });
  assert.equal(store.getMessage('msg_2').body, 'Edited');
  // An older page copy does not overwrite the newer edit.
  store.mergePage([msg(2)]);
  assert.equal(store.getMessage('msg_2').body, 'Edited');
  store.applyEvent('message.deleted', { conversationId: 'cnv_1', messageId: 'msg_2', seq: 2 });
  const tomb = store.getMessage('msg_2');
  assert.equal(tomb.state, 'Deleted');
  assert.equal(tomb.body, '');
  assert.deepEqual(tomb.attachments, []);
  store.applyEvent('message.updated', { conversationId: 'cnv_1', message: msg(2, { body: 'Back', updatedUtc: '2026-11-12T12:09:00Z' }) });
  assert.equal(store.getMessage('msg_2').state, 'Deleted');
  store.applyEvent('message.deleted', { conversationId: 'cnv_1', messageId: 'msg_9', seq: 9 });
  assert.deepEqual(seqs(store), [1, 2, 9]);
  assert.equal(tombstoneOf({ id: 'x', body: 'y' }).body, '');
});

test('a different id at a known seq replaces the stale entry (de-duplication by seq)', () => {
  const store = createMessageStore({ conversationId: 'cnv_1' });
  store.mergePage([msg(1), msg(2)]);
  store.mergePage([msg(2, { id: 'msg_real2' })]);
  assert.deepEqual(store.getSnapshot().messages.map((m) => m.id), ['msg_1', 'msg_real2']);
});

test('sendMessage inserts an optimistic item, replaces it with the 201, and ignores the echo', async () => {
  const fake = createMessagingFake();
  const api = new ApiClient({ fetchImpl: fake.fetchImpl });
  const c = fake.addConversation({ otherUserId: 'usr_2' });
  fake.receive(c.id, { body: 'Hi' });
  const store = createMessageStore({ conversationId: c.id, api, currentUser: fake.state.me, newId: () => 'cm-fixed' });
  await store.loadLatest();
  const snapshots = [];
  store.subscribe((s) => snapshots.push(s));
  const promise = store.sendMessage({ body: 'Hello back' });
  const optimistic = store.getSnapshot().messages[1];
  assert.equal(optimistic.pending, true);
  assert.equal(optimistic.sendState, SendState.Sending);
  assert.equal(optimistic.clientMessageId, 'cm-fixed');
  assert.equal(store.getSnapshot().pendingCount, 1);
  const result = await promise;
  assert.equal(result.ok, true);
  assert.equal(result.message.seq, 2);
  assert.deepEqual(seqs(store), [1, 2]);
  const version = store.getSnapshot().version;
  assert.equal(store.applyEvent('message.created', { conversationId: c.id, message: result.message }), false);
  assert.equal(store.getSnapshot().version, version);
  assert.ok(snapshots.length >= 2);
  assert.notEqual(snapshots[0], snapshots[snapshots.length - 1]);
});

test('an echo that arrives before the 201 replaces the optimistic item once', async () => {
  let resolveSend;
  const echo = msg(4, { author: ME, clientMessageId: 'cm-e', body: 'Mine' });
  const api = { sendMessage: () => new Promise((resolve) => { resolveSend = resolve; }) };
  const store = createMessageStore({ conversationId: 'cnv_1', api, currentUser: ME, newId: () => 'cm-e' });
  store.mergePage([msg(3)]);
  const promise = store.sendMessage({ body: 'Mine' });
  assert.deepEqual(seqs(store), [3, 'p:cm-e']);
  store.applyEvent('message.created', { conversationId: 'cnv_1', message: echo });
  assert.deepEqual(seqs(store), [3, 4]);
  resolveSend(echo);
  const result = await promise;
  assert.equal(result.ok, true);
  assert.deepEqual(seqs(store), [3, 4]);
});

test('sendMessage retries network and 5xx failures with the same clientMessageId, then fails and can be retried', async () => {
  const fake = createMessagingFake();
  const api = new ApiClient({ fetchImpl: fake.fetchImpl });
  const c = fake.addConversation({ otherUserId: 'usr_2' });
  const clock = createFakeClock(0);
  const store = createMessageStore({ conversationId: c.id, api, currentUser: fake.state.me, timers: clock, newId: () => 'cm-retry' });
  fake.fail({ method: 'POST', path: '/conversations/' + c.id + '/messages', status: 0, times: 1 });
  fake.fail({ method: 'POST', path: '/conversations/' + c.id + '/messages', status: 503, times: 1 });
  const promise = store.sendMessage({ body: 'Persistent' });
  await clock.advance(1000);
  await clock.advance(3000);
  const result = await promise;
  assert.equal(result.ok, true);
  const sends = fake.calls.filter((x) => x.method === 'POST' && x.path.endsWith('/messages'));
  assert.equal(sends.length, 3);
  assert.deepEqual(new Set(sends.map((x) => x.body.clientMessageId)), new Set(['cm-retry']));

  fake.fail({ method: 'POST', path: '/conversations/' + c.id + '/messages', status: 500, times: 3 });
  let n = 0;
  const store2 = createMessageStore({ conversationId: c.id, api, currentUser: fake.state.me, timers: clock, newId: () => 'cm-f' + (++n) });
  const failed = store2.sendMessage({ body: 'Doomed' });
  await clock.advance(1000);
  await clock.advance(3000);
  const outcome = await failed;
  assert.equal(outcome.ok, false);
  const item = store2.getSnapshot().messages.find((m) => m.clientMessageId === 'cm-f1');
  assert.equal(item.sendState, SendState.Failed);
  assert.equal(item.error.status, 500);
  assert.equal(store2.getSnapshot().failedCount, 1);
  const retried = await store2.retrySend('cm-f1');
  assert.equal(retried.ok, true);
  assert.equal(store2.getSnapshot().failedCount, 0);
  assert.equal(store2.getSnapshot().messages.filter((m) => m.body === 'Doomed').length, 1);
});

test('a 4xx send failure is not retried; the failed item can be discarded', async () => {
  const fake = createMessagingFake();
  const api = new ApiClient({ fetchImpl: fake.fetchImpl });
  const c = fake.addConversation({ otherUserId: 'usr_2' });
  fake.block('usr_2');
  const store = createMessageStore({ conversationId: c.id, api, currentUser: fake.state.me, newId: () => 'cm-x' });
  const outcome = await store.sendMessage({ body: 'Blocked' });
  assert.equal(outcome.ok, false);
  assert.equal(outcome.error.code, 'MessagingNotAllowed');
  assert.equal(fake.calls.filter((x) => x.method === 'POST' && x.path.endsWith('/messages')).length, 1);
  assert.equal(store.discardPending('cm-x'), true);
  assert.equal(store.getSnapshot().messages.length, 0);
});

test('loadOlder pages with the continuation token, loadNewer catches up with afterSeq', async () => {
  const fake = createMessagingFake();
  const api = new ApiClient({ fetchImpl: fake.fetchImpl });
  const c = fake.addConversation({ otherUserId: 'usr_2' });
  for (let i = 1; i <= 7; i += 1) fake.receive(c.id, { body: 'm' + i });
  const store = createMessageStore({ conversationId: c.id, api, pageSize: 3 });
  await store.loadLatest();
  assert.deepEqual(seqs(store), [5, 6, 7]);
  await store.loadOlder();
  assert.deepEqual(seqs(store), [2, 3, 4, 5, 6, 7]);
  await store.loadOlder();
  assert.deepEqual(seqs(store), [1, 2, 3, 4, 5, 6, 7]);
  assert.equal(store.getSnapshot().hasOlder, false);
  fake.receive(c.id, { body: 'm8' });
  fake.receive(c.id, { body: 'm9' });
  await store.loadNewer();
  assert.deepEqual(seqs(store).slice(-2), [8, 9]);
});

test('attachRealtime feeds socket events and the seq baseline into the RealtimeClient', async () => {
  const fake = createMessagingFake();
  const api = new ApiClient({ fetchImpl: fake.fetchImpl });
  const c = fake.addConversation({ otherUserId: 'usr_2' });
  fake.receive(c.id, { body: 'one' });
  const ws = createFakeWebSocket();
  const rt = new RealtimeClient({ url: 'wss://api.example.test/api/v1/ws', WebSocketImpl: ws.WebSocket, getAccessToken: () => 't', api, timers: createFakeClock(0), random: () => 0.5 });
  const store = createMessageStore({ conversationId: c.id, api, currentUser: fake.state.me });
  await store.loadLatest();
  const detach = store.attachRealtime(rt);
  assert.equal(rt.lastSeqOf(c.id), 1);
  rt.connect();
  ws.last().accept();
  await flushAsync();
  ws.last().serverSend('welcome', welcomeData({ resume: { accepted: true, syncRequired: false } }));
  fake.receive(c.id, { body: 'two' });
  const three = fake.receive(c.id, { body: 'three' });
  ws.last().serverSend('message.created', { conversationId: c.id, message: three });
  await flushAsync();
  assert.deepEqual(seqs(store), [1, 2, 3]);
  const typing = [];
  rt.on('typing', (d) => typing.push(d));
  await store.sendMessage({ body: 'four' });
  assert.deepEqual(seqs(store), [1, 2, 3, 4]);
  detach();
  ws.last().serverSend('message.deleted', { conversationId: c.id, messageId: three.id, seq: 3 });
  await flushAsync();
  assert.equal(store.getMessage(three.id).state, 'Visible');
});

test('the conversation list store merges pages, sync changes, events, and unread counts', () => {
  const list = createConversationListStore({ currentUserId: 'usr_1' });
  const conv = (id, extra = {}) => ({ id, folder: 'Inbox', lastSeq: 1, myLastReadSeq: 1, unreadCount: 0, lastMessage: { id: 'm', seq: 1, createdUtc: '2026-11-12T10:00:00Z' }, updatedUtc: '2026-11-12T10:00:00Z', ...extra });
  list.mergePage({ objects: [conv('cnv_a'), conv('cnv_b', { lastMessage: { id: 'm', seq: 1, createdUtc: '2026-11-12T11:00:00Z' } }), conv('cnv_r', { folder: 'Requests' })] });
  assert.deepEqual(list.getSnapshot().conversations.map((c) => c.id), ['cnv_b', 'cnv_r', 'cnv_a']);
  assert.deepEqual(list.getConversations('Requests').map((c) => c.id), ['cnv_r']);

  list.applyEvent('message.created', { conversationId: 'cnv_a', message: { id: 'm2', seq: 2, author: { id: 'usr_2' }, body: 'Hey', createdUtc: '2026-11-12T12:00:00Z' } });
  let a = list.getConversation('cnv_a');
  assert.equal(a.unreadCount, 1);
  assert.equal(a.lastSeq, 2);
  assert.equal(a.lastMessage.excerpt, 'Hey');
  assert.equal(list.getSnapshot().conversations[0].id, 'cnv_a');
  list.applyEvent('message.created', { conversationId: 'cnv_a', message: { id: 'm2', seq: 2, author: { id: 'usr_2' }, body: 'Hey', createdUtc: '2026-11-12T12:00:00Z' } });
  assert.equal(list.getConversation('cnv_a').unreadCount, 1);
  list.applyEvent('receipt.read', { conversationId: 'cnv_a', userId: 'usr_1', seq: 2 });
  assert.equal(list.getConversation('cnv_a').unreadCount, 0);
  list.applyEvent('receipt.read', { conversationId: 'cnv_a', userId: 'usr_2', seq: 2 });
  assert.equal(list.getConversation('cnv_a').otherLastReadSeq, 2);

  list.setActiveConversation('cnv_b');
  list.applyEvent('message.created', { conversationId: 'cnv_b', message: { id: 'm3', seq: 2, author: { id: 'usr_2' }, body: 'Open', createdUtc: '2026-11-12T12:01:00Z' } });
  assert.equal(list.getConversation('cnv_b').unreadCount, 0);
  list.applyEvent('message.created', { conversationId: 'cnv_a', message: { id: 'm4', seq: 3, author: { id: 'usr_1' }, body: 'Mine', createdUtc: '2026-11-12T12:02:00Z' } });
  a = list.getConversation('cnv_a');
  assert.equal(a.myLastReadSeq, 3);
  assert.equal(a.unreadCount, 0);
  list.applyEvent('message.deleted', { conversationId: 'cnv_a', messageId: 'm4', seq: 3 });
  assert.equal(list.getConversation('cnv_a').lastMessage.state, 'Deleted');

  list.applyEvent('sync', { changes: [{ conversation: { id: 'cnv_r' }, change: 'Removed' }, { conversation: conv('cnv_new', { updatedUtc: '2026-11-12T13:00:00Z', lastMessage: null }), change: 'Updated' }] });
  assert.equal(list.getConversation('cnv_r'), null);
  assert.ok(list.getConversation('cnv_new'));
  list.applyEvent('unread.counts', { notifications: 2, conversations: 1, messages: 3, requests: 0 });
  assert.deepEqual(list.getSnapshot().unread, { notifications: 2, conversations: 1, messages: 3, requests: 0 });
  list.applyEvent('conversation.updated', { conversation: conv('cnv_a', { folder: 'Archived', updatedUtc: '2026-11-12T09:00:00Z' }) });
  assert.equal(list.getConversation('cnv_a').folder, 'Archived');
  assert.equal(list.markRead('cnv_a', 0), false);
  list.applyEvent('conversation.updated', { conversation: conv('cnv_b', { viewer: { canSend: false, cannotSendReason: 'Blocked' }, updatedUtc: '2026-11-12T14:00:00Z' }) });
  assert.equal(list.getConversation('cnv_b'), null);
  list.applyEvent('conversation.updated', { conversation: conv('cnv_new', { requestState: 'Declined', updatedUtc: '2026-11-12T14:00:00Z' }) });
  assert.equal(list.getConversation('cnv_new'), null);
  list.applyEvent('conversation.updated', { conversation: conv('cnv_b', { viewer: { canSend: true, cannotSendReason: null }, updatedUtc: '2026-11-12T15:00:00Z' }) });
  assert.ok(list.getConversation('cnv_b'));
  assert.equal(list.remove('cnv_a'), true);
  assert.deepEqual(messageSummaryOf({ id: 'x', seq: 1, author: { id: 'u' }, body: 'a'.repeat(200), attachments: [{}], state: 'Visible', createdUtc: 't' }).excerpt.length, 120);
});

test('the list store follows a RealtimeClient including its sync results', async () => {
  const fake = createMessagingFake();
  const api = new ApiClient({ fetchImpl: fake.fetchImpl });
  fake.addConversation({ otherUserId: 'usr_2' });
  const ws = createFakeWebSocket();
  const rt = new RealtimeClient({ url: 'wss://api.example.test/api/v1/ws', WebSocketImpl: ws.WebSocket, getAccessToken: () => 't', api, timers: createFakeClock(0) });
  const list = createConversationListStore({ currentUserId: fake.state.me.id });
  const detach = list.attachRealtime(rt);
  let calls = 0;
  const unsubscribe = list.subscribe(() => { calls += 1; });
  rt.connect();
  ws.last().accept();
  await flushAsync();
  ws.last().serverSend('welcome', welcomeData());
  await flushAsync();
  assert.equal(list.getSnapshot().conversations.length, 1);
  assert.ok(calls > 0);
  unsubscribe();
  detach();
});


test('jump-to-message drains every page of the bounded neighborhood before merging', async () => {
  const fake = createMessagingFake();
  const conversation = fake.addConversation({ otherUserId: 'usr_2' });
  for (let i = 1; i <= 120; i++) fake.receive(conversation.id, { body: 'Message ' + i });
  const api = new ApiClient({ fetchImpl: fake.fetchImpl });
  const store = createMessageStore({ conversationId: conversation.id, currentUser: ME, api, pageSize: 1 });
  await store.loadAround(60);
  const messages = store.getSnapshot().messages;
  assert.equal(messages.length, 100);
  assert.equal(messages[0].seq, 11);
  assert.equal(messages.at(-1).seq, 110);
  assert.ok(messages.some(message => message.seq === 60));
  const calls = fake.calls.filter(call => call.path.endsWith('/messages/enumerate'));
  assert.equal(calls.length, 100);
  assert.ok(calls.slice(1).every(call => call.body.continuationToken));
  assert.ok(calls.every(call => call.body.maxResults === 1 && call.body.filters.aroundSeq === 60));
});
