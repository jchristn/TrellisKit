import { test } from 'node:test';
import assert from 'node:assert/strict';
import { ApiClient, ApiError, backoffDelay, CloseCode, createMemorySyncTokenStore, RealtimeClient, REALTIME_PROTOCOL, RealtimeStatus } from '../src/index.js';
import { createFakeClock, createFakeWebSocket, createMessagingFake, flushAsync, sequenceRandom, welcomeData } from '../src/testing/index.js';

const URL_WS = 'wss://api.example.test/api/v1/ws';

function setup({ client = {}, syncToken = null, refresh, fake: givenFake, random = () => 0.5 } = {}) {
  const clock = createFakeClock(0);
  const ws = createFakeWebSocket();
  const fake = givenFake || createMessagingFake();
  const api = new ApiClient({ fetchImpl: fake.fetchImpl });
  const tokenStore = createMemorySyncTokenStore(syncToken);
  const auth = { token: 'tok-1', refreshes: 0 };
  const refreshAccessToken = refresh || (async () => { auth.refreshes += 1; auth.token = 'tok-' + (auth.refreshes + 1); return auth.token; });
  const rt = new RealtimeClient({
    url: URL_WS, WebSocketImpl: ws.WebSocket, getAccessToken: () => auth.token, refreshAccessToken, propertyId: 'app_1', client: 'Mobile', clientVersion: '1.4.0',
    installId: 'ins_1', api, syncTokenStore: tokenStore, timers: clock, random, ...client
  });
  const events = [];
  rt.on('*', (type, data) => events.push({ type, data }));
  const of = (type) => events.filter((e) => e.type === type).map((e) => e.data);
  return { clock, ws, fake, api, tokenStore, auth, rt, events, of };
}

async function open(env, welcome = {}) {
  if (!env.rt.isConnected && !env.ws.last()) env.rt.connect();
  await flushAsync();
  const socket = env.ws.last();
  socket.accept();
  await flushAsync();
  socket.serverSend('welcome', welcomeData(welcome));
  await flushAsync();
  return socket;
}

async function acceptAndWelcome(socket, welcome = {}) {
  socket.accept();
  await flushAsync();
  socket.serverSend('welcome', welcomeData(welcome));
  await flushAsync();
}

test('backoffDelay is full jitter from 1 s doubling to a 30 s cap', () => {
  assert.equal(backoffDelay(0, () => 0.5), 500);
  assert.equal(backoffDelay(1, () => 0.5), 1000);
  assert.equal(backoffDelay(4, () => 0.5), 8000);
  assert.equal(backoffDelay(5, () => 0.5), 15000);
  assert.equal(backoffDelay(6, () => 0.5), 15000);
  assert.equal(backoffDelay(40, () => 0.999), 29970);
  assert.equal(backoffDelay(3, () => 0), 0);
  for (let i = 0; i < 50; i += 1) {
    const d = backoffDelay(10);
    assert.ok(d >= 0 && d < 30000);
  }
});

test('connects without a subprotocol and sends hello first with every field', async () => {
  const env = setup({ syncToken: 'sync:7' });
  env.rt.connect();
  const socket = env.ws.last();
  assert.equal(socket.url, URL_WS);
  assert.equal(socket.constructorArgs, 1);
  assert.equal(socket.protocols, undefined);
  socket.accept();
  await flushAsync();
  const frames = socket.sentFrames;
  assert.equal(frames.length, 1);
  assert.equal(frames[0].type, 'hello');
  assert.deepEqual(frames[0].data, {
    accessToken: 'tok-1', propertyId: 'app_1', client: 'Mobile', clientVersion: '1.4.0', installId: 'ins_1', appState: 'Foreground', resume: { syncToken: 'sync:7' },
    capabilities: ['messaging', 'notifications', 'tickets'], protocol: REALTIME_PROTOCOL
  });
  assert.equal(REALTIME_PROTOCOL, 'tk.v1');
  assert.equal(env.rt.status, RealtimeStatus.Connecting);
  socket.serverSend('welcome', welcomeData({ heartbeatSeconds: 20, capabilities: ['notifications'] }));
  await flushAsync();
  assert.equal(env.rt.status, RealtimeStatus.Open);
  assert.equal(env.rt.heartbeatSeconds, 20);
  assert.deepEqual(env.rt.capabilities, ['notifications']);
  assert.equal(env.of('connected').length, 1);
});

test('React Native upgrade headers go in the third constructor argument, still with no subprotocol', () => {
  const env = setup({ client: { headers: { 'X-TK-Property': 'app_1', 'X-TK-App': 'example' } } });
  env.rt.connect();
  const socket = env.ws.last();
  assert.equal(socket.protocols, undefined);
  assert.deepEqual(socket.options, { headers: { 'X-TK-Property': 'app_1', 'X-TK-App': 'example' } });
});

test('pings every heartbeat regardless of traffic and drops a dead connection after two intervals', async () => {
  const env = setup();
  const socket = await open(env);
  await env.clock.advance(10000);
  socket.serverSend('typing', { conversationId: 'cnv_1', userId: 'usr_2', state: 'Start', expiresInMs: 6000 });
  await env.clock.advance(15000);
  assert.equal(socket.framesOf('ping').length, 1);
  socket.serverSend('pong', {});
  await env.clock.advance(25000);
  assert.equal(socket.framesOf('ping').length, 2);
  assert.equal(env.ws.instances.length, 1);
  // No pong or other frame for two intervals since the last inbound frame (t=25 s): dead at t=75 s.
  await env.clock.advance(25000);
  assert.equal(socket.closeCalls.length, 1);
  assert.equal(socket.closeCalls[0].code, CloseCode.HeartbeatTimeout);
  const disconnected = env.of('disconnected');
  assert.equal(disconnected[0].code, CloseCode.HeartbeatTimeout);
  assert.equal(disconnected[0].willReconnect, true);
  assert.equal(disconnected[0].delayMs, 500);
  await env.clock.advance(499);
  assert.equal(env.ws.instances.length, 1);
  await env.clock.advance(1);
  assert.equal(env.ws.instances.length, 2);
});

test('reconnects with exponential backoff and full jitter, then falls back to polling after three failed connects', async () => {
  const env = setup();
  env.rt.connect();
  const delays = [];
  for (let i = 0; i < 4; i += 1) {
    env.ws.last().fail();
    await flushAsync();
    const last = env.of('disconnected').pop();
    delays.push(last.delayMs);
    await env.clock.advance(last.delayMs);
  }
  assert.deepEqual(delays, [500, 1000, 2000, 4000]);
  const fallback = env.of('fallback');
  assert.equal(fallback.length, 1);
  assert.deepEqual(fallback[0], { polling: true, reason: 'connectFailed' });
  assert.equal(env.rt.isPolling, true);
  await acceptAndWelcome(env.ws.last());
  assert.equal(env.rt.isPolling, false);
  assert.deepEqual(env.of('fallback')[1], { polling: false, reason: 'connected' });
});

test('the backoff resets after a connection survives 60 seconds', async () => {
  const env = setup();
  env.rt.connect();
  for (let i = 0; i < 3; i += 1) {
    env.ws.last().fail();
    await flushAsync();
    await env.clock.advance(env.of('disconnected').pop().delayMs);
  }
  await acceptAndWelcome(env.ws.last());
  env.ws.last().serverClose(CloseCode.ServerError);
  await flushAsync();
  assert.equal(env.of('disconnected').pop().delayMs, 4000);
  await env.clock.advance(4000);
  await acceptAndWelcome(env.ws.last(), { heartbeatSeconds: 55 });
  await env.clock.advance(60000);
  env.ws.last().serverSend('pong', {});
  env.ws.last().serverClose(CloseCode.TryAgainLater);
  await flushAsync();
  assert.equal(env.of('disconnected').pop().delayMs, 500);
});

test('4401 refreshes the token once and reconnects; a second 4401 signs out', async () => {
  const env = setup();
  env.rt.connect();
  const first = env.ws.last();
  first.accept();
  await flushAsync();
  first.serverSend('error', { code: 'Unauthorized', message: 'Bad token', frameId: 'c1' });
  first.serverClose(CloseCode.Unauthorized);
  await flushAsync();
  assert.equal(env.auth.refreshes, 1);
  const second = env.ws.last();
  assert.notEqual(second, first);
  second.accept();
  await flushAsync();
  assert.equal(second.framesOf('hello')[0].data.accessToken, 'tok-2');
  second.serverClose(CloseCode.Unauthorized);
  await flushAsync();
  assert.equal(env.auth.refreshes, 1);
  assert.equal(env.of('signedOut').length, 1);
  assert.equal(env.rt.status, RealtimeStatus.Stopped);
  await env.clock.advance(600000);
  assert.equal(env.ws.instances.length, 2);
});

test('4401 after a successful session refreshes again; a failed refresh signs out', async () => {
  const env = setup({ refresh: async () => null });
  await open(env);
  env.ws.last().serverClose(CloseCode.Unauthorized);
  await flushAsync();
  assert.equal(env.of('signedOut').length, 1);
  assert.equal(env.ws.instances.length, 1);

  const env2 = setup();
  await open(env2);
  env2.ws.last().serverClose(CloseCode.Unauthorized);
  await flushAsync();
  await acceptAndWelcome(env2.ws.last());
  env2.ws.last().serverClose(CloseCode.Unauthorized);
  await flushAsync();
  assert.equal(env2.auth.refreshes, 2);
  assert.equal(env2.of('signedOut').length, 0);
  assert.equal(env2.ws.instances.length, 3);
});

for (const code of [CloseCode.MembershipRequired, CloseCode.PropertyNotFound]) {
  test('close ' + code + ' does not reconnect until connect is called again', async () => {
    const env = setup();
    await open(env);
    env.ws.last().serverClose(code);
    await flushAsync();
    assert.equal(env.of('disconnected')[0].willReconnect, false);
    await env.clock.advance(600000);
    env.rt.networkChanged();
    env.rt.setAppState('Foreground');
    assert.equal(env.ws.instances.length, 1);
    env.rt.connect();
    assert.equal(env.ws.instances.length, 2);
  });
}

test('4409 reconnects only in the foreground', async () => {
  const env = setup();
  await open(env);
  env.ws.last().serverClose(CloseCode.Replaced);
  await flushAsync();
  assert.equal(env.of('disconnected')[0].willReconnect, true);
  await env.clock.advance(500);
  assert.equal(env.ws.instances.length, 2);

  await acceptAndWelcome(env.ws.last());
  env.rt.setAppState('Background');
  env.ws.last().serverClose(CloseCode.Replaced);
  await flushAsync();
  assert.equal(env.of('disconnected')[1].willReconnect, false);
  await env.clock.advance(600000);
  assert.equal(env.ws.instances.length, 2);
  env.rt.setAppState('Foreground');
  assert.equal(env.ws.instances.length, 3);
});

test('4429 waits retryAfterMs from the preceding error frame', async () => {
  const env = setup();
  await open(env);
  env.ws.last().serverSend('error', { code: 'RateLimited', message: 'Slow down', retryAfterMs: 7000 });
  env.ws.last().serverClose(CloseCode.RateLimited);
  await flushAsync();
  assert.equal(env.of('disconnected')[0].delayMs, 7000);
  await env.clock.advance(6999);
  assert.equal(env.ws.instances.length, 1);
  await env.clock.advance(1);
  assert.equal(env.ws.instances.length, 2);
  assert.equal(env.of('error')[0].retryAfterMs, 7000);
});

test('1012 after shutdown waits reconnectAfterMs', async () => {
  const env = setup();
  await open(env);
  env.ws.last().serverSend('shutdown', { reconnectAfterMs: 1234 });
  env.ws.last().serverClose(CloseCode.ServiceRestart);
  await flushAsync();
  assert.equal(env.of('shutdown').length, 1);
  assert.equal(env.of('disconnected')[0].delayMs, 1234);
  await env.clock.advance(1233);
  assert.equal(env.ws.instances.length, 1);
  await env.clock.advance(1);
  assert.equal(env.ws.instances.length, 2);
});

test('4503 falls back to polling and retries the socket after 5 minutes', async () => {
  const env = setup();
  await open(env);
  const before = env.fake.calls.length;
  env.ws.last().serverClose(CloseCode.FeatureDisabled);
  await flushAsync();
  assert.deepEqual(env.of('fallback')[0], { polling: true, reason: 'featureDisabled' });
  assert.equal(env.of('disconnected')[0].delayMs, 300000);
  const polled = env.fake.calls.slice(before).map((c) => c.method + ' ' + c.path);
  assert.ok(polled.includes('GET /me/messaging/unread-count'));
  assert.ok(polled.includes('GET /notifications/unread-count'));
  assert.ok(polled.includes('POST /conversations/changes/enumerate'));
  const counts = env.of('unread.counts').pop();
  assert.deepEqual(counts, { polled: true, notifications: 0, conversations: 0, messages: 0, requests: 0 });
  env.rt.networkChanged();
  assert.equal(env.ws.instances.length, 1);
  await env.clock.advance(299999);
  assert.equal(env.ws.instances.length, 1);
  await env.clock.advance(1);
  assert.equal(env.ws.instances.length, 2);
  await acceptAndWelcome(env.ws.last());
  assert.equal(env.rt.isPolling, false);
});

test('other close codes reconnect with backoff; network change and foreground reconnect immediately', async () => {
  const env = setup();
  await open(env);
  env.ws.last().serverClose(CloseCode.ServerError);
  await flushAsync();
  assert.equal(env.of('disconnected')[0].delayMs, 500);
  env.rt.networkChanged();
  assert.equal(env.ws.instances.length, 2);
  await acceptAndWelcome(env.ws.last());
  env.ws.last().serverClose(CloseCode.Normal);
  await flushAsync();
  assert.equal(env.of('disconnected')[1].willReconnect, true);
  env.rt.setAppState('Foreground');
  assert.equal(env.ws.instances.length, 3);
  await env.clock.advance(60000);
  assert.equal(env.ws.instances.length, 3);
});

test('disconnect closes with 1000 and stops reconnecting', async () => {
  const env = setup();
  await open(env);
  const socket = env.ws.last();
  env.rt.disconnect();
  assert.deepEqual(socket.closeCalls[0], { code: 1000, reason: 'client' });
  assert.equal(env.of('disconnected')[0].byClient, true);
  await env.clock.advance(600000);
  assert.equal(env.ws.instances.length, 1);
  assert.equal(env.rt.status, RealtimeStatus.Idle);
});

test('auth.expiring refreshes the token and sends auth.refresh', async () => {
  const env = setup();
  const socket = await open(env);
  socket.serverSend('auth.expiring', { expiresUtc: '2026-11-12T13:00:00Z' });
  await flushAsync();
  assert.deepEqual(socket.framesOf('auth.refresh').map((f) => f.data), [{ accessToken: 'tok-2' }]);
  assert.equal(env.of('auth.expiring').length, 1);
});

test('typing is throttled per conversation and stops on send', async () => {
  const env = setup();
  const socket = await open(env);
  assert.equal(env.rt.typing('cnv_1'), true);
  assert.equal(env.rt.typing('cnv_1'), false);
  assert.equal(env.rt.typing('cnv_2', 'Start'), true);
  await env.clock.advance(2999);
  assert.equal(env.rt.typing('cnv_1'), false);
  await env.clock.advance(1);
  assert.equal(env.rt.typing('cnv_1'), true);
  assert.equal(env.rt.messageSent('cnv_1'), true);
  assert.equal(env.rt.messageSent('cnv_1'), false);
  assert.equal(env.rt.typing('cnv_3', 'Stop'), false);
  assert.deepEqual(socket.framesOf('typing').map((f) => f.data.conversationId + ':' + f.data.state), ['cnv_1:Start', 'cnv_2:Start', 'cnv_1:Start', 'cnv_1:Stop']);
});

test('acks are monotonic and batched at most once per second per conversation', async () => {
  const env = setup();
  const socket = await open(env);
  env.rt.ack('cnv_1', 10);
  env.rt.ack('cnv_1', 11);
  env.rt.ack('cnv_1', 12);
  env.rt.ack('cnv_1', 9);
  env.rt.ack('cnv_2', 4);
  const acks = () => socket.framesOf('ack').map((f) => f.data.conversationId + ':' + f.data.seq);
  assert.deepEqual(acks(), ['cnv_1:10', 'cnv_2:4']);
  await env.clock.advance(999);
  assert.deepEqual(acks(), ['cnv_1:10', 'cnv_2:4']);
  await env.clock.advance(1);
  assert.deepEqual(acks(), ['cnv_1:10', 'cnv_2:4', 'cnv_1:12']);
  await env.clock.advance(5000);
  assert.equal(acks().length, 3);
  env.rt.ack('cnv_1', 13);
  assert.deepEqual(acks().pop(), 'cnv_1:13');
});

test('read frames are monotonic and fall back to REST while disconnected', async () => {
  const env = setup();
  const c = env.fake.addConversation({ otherUserId: 'usr_2' });
  env.fake.receive(c.id, { body: 'a' });
  env.fake.receive(c.id, { body: 'b' });
  const socket = await open(env);
  assert.equal(env.rt.read(c.id, 1), true);
  assert.equal(env.rt.read(c.id, 1), false);
  assert.deepEqual(socket.framesOf('read').map((f) => f.data), [{ conversationId: c.id, seq: 1 }]);
  env.rt.disconnect();
  assert.equal(env.rt.read(c.id, 2), true);
  await flushAsync();
  assert.ok(env.fake.calls.some((x) => x.path === '/conversations/' + c.id + '/read' && x.body.seq === 2));
});

test('state frames carry the app state and the active conversation', async () => {
  const env = setup();
  const socket = await open(env);
  env.rt.setAppState('Background', null);
  env.rt.setActiveConversation('cnv_9');
  env.rt.setAppState('Foreground');
  assert.deepEqual(socket.framesOf('state').map((f) => f.data), [
    { appState: 'Background' }, { appState: 'Background', activeConversationId: 'cnv_9' }, { appState: 'Foreground', activeConversationId: 'cnv_9' }
  ]);
});

test('the emitter delivers every server frame type, ignores unknown types and bad JSON, and supports off', async () => {
  const env = setup();
  const socket = await open(env);
  const seen = [];
  const listener = (data) => seen.push(data);
  env.rt.on('notification.created', listener);
  socket.serverSend('notification.created', { notification: { id: 'ntf_1' }, unreadCount: 3 });
  socket.serverSend('unknown.future', { x: 1 });
  socket.serverSendRaw('not json');
  socket.serverSend('ticket.updated', { ticketId: 'tkt_1', status: 'WaitingOnMember' });
  socket.serverSend('access.changed', { capabilities: ['notifications'], accessTier: 'SignedIn' });
  socket.serverSend('feature.disabled', { feature: 'messaging', message: 'Off' });
  socket.serverSend('receipt.read', { conversationId: 'cnv_1', userId: 'usr_2', seq: 4 });
  socket.serverSend('conversation.updated', { conversation: { id: 'cnv_1' } });
  await flushAsync();
  env.rt.off('notification.created', listener);
  socket.serverSend('notification.created', { notification: { id: 'ntf_2' }, unreadCount: 4 });
  await flushAsync();
  assert.deepEqual(seen.map((d) => d.notification.id), ['ntf_1']);
  assert.equal(env.events.some((e) => e.type === 'unknown.future'), false);
  assert.equal(env.of('ticket.updated').length, 1);
  assert.deepEqual(env.rt.capabilities, ['notifications']);
  assert.equal(env.of('feature.disabled').length, 1);
  assert.equal(env.of('receipt.read').length, 1);
  assert.equal(env.of('conversation.updated').length, 1);
});

test('a sequence gap is filled with afterSeq and SeqAscending before the live event is delivered', async () => {
  const env = setup();
  const c = env.fake.addConversation({ otherUserId: 'usr_2' });
  env.fake.receive(c.id, { body: 'one' });
  const socket = await open(env, { resume: { accepted: true, syncRequired: false } });
  env.rt.trackConversation(c.id, 1);
  env.fake.receive(c.id, { body: 'two' });
  const three = env.fake.receive(c.id, { body: 'three' });
  env.events.length = 0;
  socket.serverSend('message.created', { conversationId: c.id, message: three });
  await flushAsync();
  const created = env.of('message.created');
  assert.deepEqual(created.map((d) => d.message.seq + (d.gapFill ? 'g' : '')), ['2g', '3g', '3']);
  const fill = env.fake.calls.filter((x) => x.path.endsWith('/messages/enumerate')).pop();
  assert.equal(fill.body.ordering, 'SeqAscending');
  assert.deepEqual(fill.body.filters, { afterSeq: 1 });
  assert.equal(env.rt.lastSeqOf(c.id), 3);
  const before = env.fake.calls.length;
  const four = env.fake.receive(c.id, { body: 'four' });
  socket.serverSend('message.created', { conversationId: c.id, message: four });
  await flushAsync();
  assert.equal(env.fake.calls.length, before);
  assert.equal(env.rt.lastSeqOf(c.id), 4);
});

test('a supplied fillGap is used and events of one conversation stay in order', async () => {
  const fills = [];
  let release;
  const gate = new Promise((resolve) => { release = resolve; });
  const env = setup({ client: { fillGap: async (conversationId, afterSeq) => { fills.push([conversationId, afterSeq]); await gate; return [{ id: 'msg_6', conversationId, seq: 6 }]; } } });
  const socket = await open(env, { resume: { accepted: true, syncRequired: false } });
  env.rt.trackConversation('cnv_1', 5);
  socket.serverSend('message.created', { conversationId: 'cnv_1', message: { id: 'msg_7', conversationId: 'cnv_1', seq: 7 } });
  socket.serverSend('message.deleted', { conversationId: 'cnv_1', messageId: 'msg_7', seq: 7 });
  await flushAsync();
  assert.deepEqual(fills, [['cnv_1', 5]]);
  assert.equal(env.of('message.deleted').length, 0);
  release();
  await flushAsync();
  assert.deepEqual(env.events.filter((e) => e.type.startsWith('message.')).map((e) => e.type + ':' + (e.data.message ? e.data.message.seq : e.data.seq)), ['message.created:6', 'message.created:7', 'message.deleted:7']);
});

test('welcome runs the sync from the stored token, stores the final token, and gap-fills changed conversations', async () => {
  const env = setup();
  const c = env.fake.addConversation({ otherUserId: 'usr_2' });
  env.fake.receive(c.id, { body: 'one' });
  await open(env);
  const firstSync = env.of('sync')[0];
  assert.equal(firstSync.changes.length, 1);
  const token = await env.tokenStore.get();
  assert.match(token, /^sync:\d+$/);
  assert.equal(env.rt.lastSeqOf(c.id), 1);

  env.ws.last().serverClose(CloseCode.ServerError);
  await flushAsync();
  env.fake.receive(c.id, { body: 'two' });
  env.fake.receive(c.id, { body: 'three' });
  await env.clock.advance(500);
  env.ws.last().accept();
  await flushAsync();
  assert.equal(env.ws.last().framesOf('hello')[0].data.resume.syncToken, token);
  env.events.length = 0;
  env.ws.last().serverSend('welcome', welcomeData());
  await flushAsync();
  const sync = env.of('sync')[0];
  assert.equal(sync.changes.length, 1);
  assert.equal(sync.reset, false);
  assert.deepEqual(env.of('message.created').map((d) => d.message.seq), [2, 3]);
  assert.equal(env.rt.lastSeqOf(c.id), 3);
  assert.notEqual(await env.tokenStore.get(), token);
});

test('resync.required (BusReconnected) syncs; an invalid token restarts from null; syncRequired false skips', async () => {
  const env = setup();
  const socket = await open(env, { resume: { accepted: true, syncRequired: false } });
  assert.equal(env.of('sync').length, 0);
  await env.tokenStore.set('sync:0');
  env.fake.addConversation({ otherUserId: 'usr_3' });
  env.fake.expireSyncTokens();
  socket.serverSend('resync.required', { reason: 'BusReconnected' });
  await flushAsync();
  const sync = env.of('sync')[0];
  assert.equal(sync.reset, true);
  assert.equal(sync.changes.length, 1);
  const syncCalls = env.fake.calls.filter((x) => x.path === '/conversations/changes/enumerate');
  assert.deepEqual(syncCalls.map((x) => x.body.syncToken), ['sync:0', null]);
  assert.equal(await env.tokenStore.get(), sync.syncToken);
});

test('sync pages to the end and removes Removed conversations from tracking', async () => {
  const pages = [
    { objects: [{ conversation: { id: 'cnv_a', lastSeq: 3 }, change: 'Updated' }], continuationToken: 'p2', endOfResults: false },
    { objects: [{ conversation: { id: 'cnv_b' }, change: 'Removed' }], continuationToken: null, syncToken: 'sync:final', endOfResults: true }
  ];
  const seen = [];
  const api = {
    syncConversations: async (q) => { seen.push({ syncToken: q.syncToken, continuationToken: q.continuationToken }); return pages[seen.length - 1]; },
    enumerateMessages: async () => ({ objects: [{ id: 'm3', conversationId: 'cnv_a', seq: 2 }, { id: 'm4', conversationId: 'cnv_a', seq: 3 }], continuationToken: null, endOfResults: true })
  };
  const clock = createFakeClock(0);
  const store = createMemorySyncTokenStore('sync:start');
  const rt = new RealtimeClient({ url: URL_WS, WebSocketImpl: createFakeWebSocket().WebSocket, api, syncTokenStore: store, timers: clock });
  rt.trackConversation('cnv_a', 1);
  rt.trackConversation('cnv_b', 9);
  const result = await rt.resync();
  assert.deepEqual(seen, [{ syncToken: 'sync:start', continuationToken: null }, { syncToken: 'sync:start', continuationToken: 'p2' }]);
  assert.equal(result.syncToken, 'sync:final');
  assert.equal(store.get(), 'sync:final');
  assert.equal(rt.lastSeqOf('cnv_a'), 3);
  assert.equal(rt.lastSeqOf('cnv_b'), null);
});

test('without WebSocket the client polls: unread every 60 s, the list every 30 s, the open conversation every 5 s backing off to 30 s', async () => {
  const env = setup({ client: { WebSocketImpl: null } });
  const c = env.fake.addConversation({ otherUserId: 'usr_2' });
  env.fake.receive(c.id, { body: 'one' });
  env.rt.connect();
  await flushAsync();
  assert.deepEqual(env.of('fallback')[0], { polling: true, reason: 'unavailable' });
  env.rt.trackConversation(c.id, 1);
  env.rt.setActiveConversation(c.id);
  const count = (path, method = 'POST') => env.fake.calls.filter((x) => x.method === method && x.path === path).length;
  const historyPath = '/conversations/' + c.id + '/messages/enumerate';
  await env.clock.advance(5000);
  assert.equal(count(historyPath), 1);
  env.fake.receive(c.id, { body: 'two' });
  await env.clock.advance(5000);
  assert.equal(count(historyPath), 2);
  assert.deepEqual(env.of('message.created').map((d) => d.message.seq), [2]);
  assert.equal(env.rt.lastSeqOf(c.id), 2);
  assert.equal(count('/me/messaging/unread-count', 'GET'), 1);
  assert.equal(count('/conversations/changes/enumerate'), 1);
  await env.clock.advance(50000);
  assert.equal(count('/me/messaging/unread-count', 'GET'), 2);
  assert.equal(count('/conversations/changes/enumerate'), 3);
  // Two minutes after the last new message (t=10 s) polling slows to every 30 s.
  await env.clock.advance(70000);
  const atSlow = count(historyPath);
  await env.clock.advance(30000);
  assert.equal(count(historyPath) - atSlow, 1);
  env.fake.receive(c.id, { body: 'three' });
  await env.clock.advance(30000);
  const afterNew = count(historyPath);
  await env.clock.advance(10000);
  assert.equal(count(historyPath) - afterNew, 2);
  env.rt.setAppState('Background');
  const bg = env.fake.calls.length;
  await env.clock.advance(120000);
  assert.equal(env.fake.calls.length, bg);
  env.rt.disconnect();
  assert.equal(env.rt.isPolling, false);
});

test('sync errors other than an invalid token are reported, not thrown', async () => {
  const api = { syncConversations: async () => { throw new ApiError('Down', { status: 503, code: 'FeatureDisabled' }); } };
  const rt = new RealtimeClient({ url: URL_WS, WebSocketImpl: null, api, timers: createFakeClock(0), random: sequenceRandom([0.1]) });
  const errors = [];
  rt.on('clientError', (e) => errors.push(e));
  assert.equal(await rt.resync(), null);
  assert.equal(errors[0].during, 'sync');
});


test('incomplete sync never advances the durable watermark or emits partial results', async () => {
  const store = createMemorySyncTokenStore('sync:start');
  const clock = createFakeClock(0);
  const api = { syncConversations: async () => ({ objects: [], endOfResults: false, continuationToken: 'repeated' }) };
  const rt = new RealtimeClient({ url: URL_WS, WebSocketImpl: createFakeWebSocket().WebSocket, api, syncTokenStore: store, timers: clock });
  assert.equal(await rt.resync(), null);
  assert.equal(store.get(), 'sync:start');
});
