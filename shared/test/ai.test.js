import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  ApiClient, aiAvailable, aiMethods, aiPlacement, addPendingUserMessage, applyChatEvent, createChatState, createSseParser, createVisitorTokenStore,
  exchangeEvents, isFinalStatus, messageToolCalls, newClientMessageId, parseAssistantMarkdown, pollReply, readSseEvents, SSE_TERMINAL_EVENTS, toolCallDisplayItems,
  toolResultPath, VISITOR_TOKEN_HEADER
} from '../src/index.js';

function streamResponse(text, chunkSize = 7) {
  const encoder = new TextEncoder();
  const bytes = encoder.encode(text);
  let offset = 0;
  const body = new ReadableStream({
    pull(controller) {
      if (offset >= bytes.length) {
        controller.close();
        return;
      }
      controller.enqueue(bytes.slice(offset, offset + chunkSize));
      offset += chunkSize;
    }
  });
  return new Response(body, { status: 200, headers: { 'content-type': 'text/event-stream; charset=utf-8' } });
}

const STREAM = [
  ': stream', '',
  'event: message.created', 'id: 1', 'data: {"userMessage":{"id":"acm_u","role":"User","content":"Hi","clientMessageId":"c1"},"replyMessageId":"acm_r","conversation":{"id":"acv_1"}}', '',
  'event: reply.started', 'id: 2', 'data: {"replyMessageId":"acm_r","assistantTitle":"Helper"}', '',
  ': ping', '',
  'event: reply.delta', 'id: 3', 'data: {"text":"Hello "}', '',
  'event: reply.delta', 'id: 4', 'data: {"text":"there [1]"}', '',
  'event: reply.citations', 'id: 5', 'data: {"citations":[{"index":1,"targetType":"FeedPost","targetId":"fpo_1","title":"Guide","path":"/feed/fpo_1"}]}', '',
  'event: reply.completed', 'id: 6', 'data: {"reply":{"id":"acm_r","role":"Assistant","content":"Hello there [1]","status":"Completed","isAiGenerated":true,"citations":[{"index":1}]},"remainingToday":49}', '', ''
].join('\n');

test('the SSE parser handles split chunks, comments, ids, and JSON data', () => {
  const events = [];
  const parser = createSseParser((e) => events.push(e));
  for (let i = 0; i < STREAM.length; i += 5) parser.push(STREAM.slice(i, i + 5));
  parser.end();
  assert.deepEqual(events.map((e) => e.event), ['message.created', 'reply.started', 'reply.delta', 'reply.delta', 'reply.citations', 'reply.completed']);
  assert.equal(events[2].id, '3');
  assert.equal(events[3].data.text, 'there [1]');
  assert.ok(SSE_TERMINAL_EVENTS.includes(events[5].event));
});

test('readSseEvents yields events from a streaming body', async () => {
  const names = [];
  for await (const e of readSseEvents(streamResponse(STREAM, 11))) names.push(e.event);
  assert.equal(names.length, 6);
  assert.equal(names[5], 'reply.completed');
});

test('sendAssistantMessage streams with Accept text/event-stream and the visitor header', async () => {
  const calls = [];
  const client = new ApiClient({ fetchImpl: async (url, init) => { calls.push({ url, init }); return streamResponse(STREAM); }, getAccessToken: () => 'tok' });
  client.visitorToken = 'vis-1';
  let state = addPendingUserMessage(createChatState(), 'Hi', 'c1');
  for await (const e of client.sendAssistantMessage('acv_1', { content: 'Hi', clientMessageId: 'c1' })) state = applyChatEvent(state, e);
  assert.equal(calls[0].url, '/api/v1/assistant-conversations/acv_1/messages');
  assert.equal(calls[0].init.headers.Accept, 'text/event-stream');
  assert.equal(calls[0].init.headers[VISITOR_TOKEN_HEADER], 'vis-1');
  assert.equal(calls[0].init.headers.Authorization, 'Bearer tok');
  assert.deepEqual(JSON.parse(calls[0].init.body), { content: 'Hi', clientMessageId: 'c1' });
  assert.equal(state.messages.length, 2, 'pending message replaced by the stored one');
  assert.equal(state.messages[0].id, 'acm_u');
  assert.equal(state.messages[1].content, 'Hello there [1]');
  assert.equal(state.streaming, false);
  assert.equal(state.remainingToday, 49);
});

test('a JSON reply (streaming off) becomes the same events', async () => {
  const exchange = { userMessage: { id: 'acm_u', content: 'Hi' }, reply: { id: 'acm_r', status: 'Completed', content: 'Done' } };
  const client = new ApiClient({ fetchImpl: async () => new Response(JSON.stringify(exchange), { status: 201, headers: { 'content-type': 'application/json' } }) });
  const events = [];
  for await (const e of client.sendAssistantMessage('acv_1', { content: 'Hi' }, { stream: false })) events.push(e.event);
  assert.deepEqual(events, ['message.created', 'reply.delta', 'reply.completed']);
  assert.deepEqual(exchangeEvents({ reply: { id: 'x', status: 'Failed', errorCode: 'ModelTimeout' } }).map((e) => e.event), ['message.created', 'error']);
});

test('errors before the stream become ApiError with the code and Retry-After', async () => {
  const client = new ApiClient({ fetchImpl: async () => new Response(JSON.stringify({ error: 'AssistantLimitReached', message: 'Used up' }), { status: 429, headers: { 'content-type': 'application/json', 'retry-after': '120' } }) });
  await assert.rejects(async () => {
    for await (const e of client.startConversation('ast_1', { message: 'Hi' })) assert.fail(String(e));
  }, (err) => err.status === 429 && err.code === 'AssistantLimitReached' && err.retryAfterSeconds === 120);
});

test('error and blocked events update the transcript', () => {
  let state = applyChatEvent(createChatState(), { event: 'message.created', data: { userMessage: { id: 'u', content: 'q' }, replyMessageId: 'r' } });
  state = applyChatEvent(state, { event: 'reply.delta', data: { text: 'partial' } });
  const failed = applyChatEvent(state, { event: 'error', data: { error: 'ModelTimeout', message: 'Too slow', replyMessageId: 'r' } });
  assert.equal(failed.messages[1].status, 'Failed');
  assert.equal(failed.error.code, 'ModelTimeout');
  const blocked = applyChatEvent(state, { event: 'reply.blocked', data: { reply: { id: 'r', status: 'Blocked', content: 'This reply was withheld.' } } });
  assert.equal(blocked.blocked, true);
  assert.equal(blocked.messages[1].content, 'This reply was withheld.');
});

test('every AI client method exists and maps to its route', async () => {
  const calls = [];
  const client = new ApiClient({ fetchImpl: async (url, init) => { calls.push(init.method + ' ' + url.replace('/api/v1', '')); return new Response('{}', { status: 200, headers: { 'content-type': 'application/json' } }); } });
  for (const name of Object.keys(aiMethods)) assert.equal(typeof ApiClient.prototype[name], 'function', name);
  await client.enumerateAssistants({ search: 'help' });
  await client.getAssistant('ast_1');
  await client.createVisitorSession();
  await client.enumerateConversations({ filters: { assistantId: 'ast_1' } });
  await client.updateConversation('acv_1', { title: 'T' });
  await client.cancelAssistantMessage('acv_1', 'acm_1');
  await client.setMessageFeedback('acv_1', 'acm_1', { rating: 'Up' });
  await client.adminEnumerateEndpoints({ maxResults: 900 });
  await client.adminCreateEndpoint({ name: 'x' });
  await client.testEndpoint('mep_1');
  await client.revealEndpointSecret('mep_1', { field: 'authValue', reason: 'Rotating the key' });
  await client.enumerateEndpointModels('mep_1');
  await client.checkEndpointHealth('mep_1');
  await client.adminUpdateAssistant('ast_1', { enabled: false });
  await client.previewAssistantKnowledge('ast_1', { filters: { query: 'photo' } });
  await client.getAssistantKnowledgeStatus('ast_1');
  await client.aiOverview({ days: 7 });
  await client.aiTimeseries({ filters: { metric: 'cost' } });
  await client.enumerateAiMemberAnalytics();
  await client.adminGetConversation('acv_1');
  await client.reviewAiFeedback('afb_1');
  assert.deepEqual(calls, [
    'POST /assistants/enumerate', 'GET /assistants/ast_1', 'POST /assistant-visitors', 'POST /assistant-conversations/enumerate', 'PUT /assistant-conversations/acv_1',
    'POST /assistant-conversations/acv_1/messages/acm_1/cancel', 'PUT /assistant-conversations/acv_1/messages/acm_1/feedback', 'POST /admin/ai/endpoints/enumerate',
    'POST /admin/ai/endpoints', 'POST /admin/ai/endpoints/mep_1/test', 'POST /admin/ai/endpoints/mep_1/reveal', 'POST /admin/ai/endpoints/mep_1/models/enumerate',
    'POST /admin/ai/endpoints/mep_1/health/check', 'PUT /admin/ai/assistants/ast_1', 'POST /admin/ai/assistants/ast_1/knowledge/preview/enumerate', 'GET /admin/ai/assistants/ast_1/knowledge/status',
    'GET /admin/ai/analytics/overview?days=7', 'POST /admin/ai/analytics/timeseries/enumerate', 'POST /admin/ai/analytics/members/enumerate',
    'GET /admin/ai/conversations/acv_1', 'POST /admin/ai/feedback/afb_1/review'
  ]);
});

test('capability, placement, ids, statuses, and polling', async () => {
  assert.equal(aiAvailable({ features: { ai: true } }), true);
  assert.equal(aiAvailable({ features: {} }), false);
  assert.equal(aiAvailable(null), false);
  assert.equal(aiPlacement({ features: { ai: true, aiPlacement: 'Tab' } }), 'Tab');
  assert.equal(aiPlacement({ features: { ai: true } }), 'Menu');
  assert.match(newClientMessageId(), /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
  assert.equal(isFinalStatus('Streaming'), false);
  assert.equal(isFinalStatus('Blocked'), true);
  let n = 0;
  const fake = { getConversationMessage: async () => ({ status: ++n < 3 ? 'Streaming' : 'Completed' }) };
  const done = await pollReply(fake, 'acv', 'acm', { sleep: async () => {} });
  assert.equal(done.status, 'Completed');
  assert.equal(n, 3);
});

test('visitor tokens survive in storage or fall back to memory', () => {
  const map = new Map();
  const storage = { getItem: (k) => map.get(k) || null, setItem: (k, v) => map.set(k, v), removeItem: (k) => map.delete(k) };
  const store = createVisitorTokenStore(storage);
  store.set('abc');
  assert.equal(map.get('tk_visitor_token'), 'abc');
  assert.equal(store.get(), 'abc');
  const broken = createVisitorTokenStore({ getItem: () => { throw new Error('denied'); }, setItem: () => { throw new Error('denied'); }, removeItem: () => {} });
  broken.set('mem');
  assert.equal(broken.get(), 'mem');
});

test('assistant Markdown adds fenced and inline code without HTML', () => {
  const blocks = parseAssistantMarkdown('Run `npm test` now.\n\n```bash\nnpm install\n<b>x</b>\n```\n\nDone **ok**').children;
  assert.equal(blocks[0].type, 'paragraph');
  assert.ok(blocks[0].children.some((n) => n.type === 'code' && n.value === 'npm test'));
  assert.deepEqual(blocks[1], { type: 'code', lang: 'bash', value: 'npm install\n<b>x</b>' });
  assert.equal(blocks[2].type, 'paragraph');
  const unbalanced = parseAssistantMarkdown('a `b').children;
  assert.ok(unbalanced[0].children.every((n) => n.type !== 'code'));
});

test('exchangeEvents carries the conversation id of a non-streamed reply', () => {
  const events = exchangeEvents({ userMessage: { id: 'm1', conversationId: 'cnv_1' }, reply: { id: 'm2', conversationId: 'cnv_1', status: 'Completed', content: 'Hi' } });
  assert.deepEqual(events[0].data.conversation, { id: 'cnv_1' });
  assert.equal(exchangeEvents({ reply: { id: 'm2', status: 'Completed', content: '' } })[0].data.conversation, null);
});

test('tool events attach tool calls to the streaming reply and keep toolActivity', () => {
  let state = applyChatEvent(createChatState(), { event: 'message.created', data: { userMessage: { id: 'u', content: 'q' }, replyMessageId: 'r' } });
  state = applyChatEvent(state, { event: 'tool.started', data: { toolCallId: 'atc_1', toolName: 'searchLibrary', label: 'Searching the library', round: 1 } });
  let calls = messageToolCalls(state.messages[1]);
  assert.equal(calls.length, 1);
  assert.equal(calls[0].status, 'Running');
  assert.equal(calls[0].label, 'Searching the library');
  assert.deepEqual(toolCallDisplayItems(state.messages[1]).map((i) => [i.state, i.text]), [['running', 'Searching the library...']]);
  state = applyChatEvent(state, { event: 'tool.completed', data: { toolCallId: 'atc_1', toolName: 'searchLibrary', completedLabel: 'Searched the library', status: 'Succeeded', resultCount: 2, durationMs: 40, round: 1,
    results: [{ targetType: 'Lesson', targetId: 'les_1', title: 'Basics', path: '/lessons/les_1' }, { targetType: 'Lesson', targetId: 'les_2', title: 'Odd', path: 'https://elsewhere.example/x' }] } });
  state = applyChatEvent(state, { event: 'tool.started', data: { toolCallId: 'atc_2', toolName: 'findEvents', label: 'Finding events', round: 2 } });
  state = applyChatEvent(state, { event: 'tool.completed', data: { toolCallId: 'atc_2', toolName: 'findEvents', label: 'Finding events', completedLabel: 'Found events', status: 'TimedOut', resultCount: 0, durationMs: 15000, round: 2, results: [] } });
  state = applyChatEvent(state, { event: 'reply.delta', data: { text: 'Here you go' } });
  calls = messageToolCalls(state.messages[1]);
  assert.equal(calls.length, 2);
  assert.equal(calls[0].label, 'Searching the library', 'the started label survives completion');
  assert.equal(state.messages[1].content, 'Here you go');
  assert.equal(state.toolActivity.length, 4);
  const items = toolCallDisplayItems(state.messages[1], { failed: (label) => label + ' (failed)' });
  assert.deepEqual(items.map((i) => [i.state, i.text]), [['done', 'Searched the library'], ['failed', 'Finding events (failed)']]);
  assert.deepEqual(items[0].results.map((r) => r.path), ['/lessons/les_1', null]);
  assert.deepEqual(items[1].results, []);
});

test('the completed reply replaces streamed tool calls, or keeps them when it has none', () => {
  let state = applyChatEvent(createChatState(), { event: 'message.created', data: { userMessage: { id: 'u', content: 'q' }, replyMessageId: 'r' } });
  state = applyChatEvent(state, { event: 'tool.started', data: { toolCallId: 'atc_1', toolName: 'searchFeed', label: 'Searching posts' } });
  const authoritative = applyChatEvent(state, { event: 'reply.completed', data: { reply: { id: 'r', status: 'Completed', content: 'x', toolCalls: { objects: [{ id: 'atc_1', toolName: 'searchFeed', label: 'Searching posts', completedLabel: 'Searched posts', status: 'Succeeded', resultCount: 0, results: [] }], totalRecords: 1 } } } });
  assert.equal(messageToolCalls(authoritative.messages[1])[0].status, 'Succeeded');
  const kept = applyChatEvent(state, { event: 'reply.completed', data: { reply: { id: 'r', status: 'Completed', content: 'x' } } });
  assert.equal(messageToolCalls(kept.messages[1]).length, 1);
  // A call left running on a finished reply shows as failed.
  assert.equal(toolCallDisplayItems(kept.messages[1])[0].state, 'failed');
  // Without a reply in progress tool events only reach toolActivity.
  const idle = applyChatEvent(createChatState([{ id: 'm', role: 'Assistant', content: 'a' }]), { event: 'tool.started', data: { toolCallId: 'atc_9', label: 'L' } });
  assert.equal(idle.toolActivity.length, 1);
  assert.equal(idle.messages[0].toolCalls, undefined);
});

test('tool display items for persisted messages and result paths', () => {
  const message = { id: 'm', role: 'Assistant', status: 'Completed', toolCalls: { objects: [
    { id: 'atc_1', toolName: 'findVenues', label: 'Finding places', completedLabel: 'Found places', status: 'Succeeded', resultCount: 1, results: [{ targetType: 'Venue', targetId: 'ven_1', title: 'North', path: '/venues/ven_1' }] },
    { id: 'atc_2', toolName: 'searchForums', label: 'Searching forums', completedLabel: 'Searched forums', status: 'Rejected', resultCount: 0, results: [{ targetType: 'Thread', targetId: 'thr_1', title: 'T', path: '/threads/thr_1' }] }
  ], totalRecords: 2 } };
  const items = toolCallDisplayItems(message);
  assert.equal(items[0].text, 'Found places');
  assert.equal(items[0].results[0].path, '/venues/ven_1');
  assert.equal(items[1].state, 'failed');
  assert.equal(items[1].results.length, 0, 'failed calls show no links');
  assert.deepEqual(toolCallDisplayItems({ toolCalls: { objects: [] } }), []);
  assert.deepEqual(toolCallDisplayItems(null), []);
  for (const ok of ['/lessons/les_1', '/events/evt_1', '/venues/ven_1', '/feed/fpo_1', '/threads/thr_1']) assert.equal(toolResultPath(ok), ok);
  for (const bad of ['//evil.example/x', 'https://x.example/lessons/1', '/admin/users/1', '/lessons/', '/lessons/a/b', null, 42]) assert.equal(toolResultPath(bad), null);
});

test('tool registry client methods map to their routes', async () => {
  const calls = [];
  const client = new ApiClient({ fetchImpl: async (url, init) => { calls.push({ route: init.method + ' ' + url.replace('/api/v1', ''), body: init.body ? JSON.parse(init.body) : null }); return new Response('{}', { status: 200, headers: { 'content-type': 'application/json' } }); } });
  await client.enumerateAssistantTools({ maxResults: 50, search: 'lib', labels: ['a'], filters: { disabled: 'false' } });
  await client.updateAssistantTool('searchLibrary', { disabled: true, reason: 'Index rebuild' });
  assert.deepEqual(calls.map((c) => c.route), ['POST /admin/ai/tools/enumerate', 'PUT /admin/ai/tools/searchLibrary']);
  assert.equal(calls[0].body.search, 'lib');
  assert.deepEqual(calls[1].body, { disabled: true, reason: 'Index rebuild' });
});
