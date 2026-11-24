import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import * as KeepAwake from 'expo-keep-awake';
import * as SecureStore from 'expo-secure-store';
import { renderRouter } from 'expo-router/testing-library';
import path from 'path';
import { chatErrorCode } from '../src/ai/useAssistantChat';
import { MobileApiClient } from '../src/api/MobileApiClient';
import { createVisitorClient, VisitorTokenKey } from '../src/ai/visitor';
import AssistantMessage, { splitCodeFences } from '../src/components/ai/AssistantMessage';
import { parseMarkdown } from '../src/content/markdown';
import { PropertyProvider } from '../src/property';
import strings from '../src/strings';
import { enumerationResult } from '../test-utils/community';
import { installFakeServer, makeUser, resetEnvironment, signedInAs } from '../test-utils/fakeServer';
import { property, propertyWithFeatures } from '../test-utils/property';

const APP_DIR = path.resolve(__dirname, '../app');
const s = strings.assistants;

// Streamed replies go through expo/fetch; the mock serves the SSE body as text and forwards every
// other call to the fake server.
const mockStreams = new Map();
jest.mock('expo/fetch', () => ({
  fetch: jest.fn(async (url, init) => {
    const key = (init.method || 'GET') + ' ' + new URL(url).pathname.replace(/^\/api\/v1/, '');
    if (mockStreams.has(key)) {
      const text = mockStreams.get(key)(init);
      return { ok: true, status: 200, headers: { get: (name) => (name.toLowerCase() === 'content-type' ? 'text/event-stream' : null) }, text: async () => text };
    }
    return global.fetch(url, init);
  })
}));

function sse(events) {
  return events.map(([event, data]) => 'event: ' + event + '\ndata: ' + JSON.stringify(data) + '\n\n').join('');
}

function summary(extra = {}) {
  return {
    id: 'ast_1', name: 'Helper', title: 'Community helper', description: 'Answers questions', avatar: { emoji: null }, starterPrompts: ['What is on this week?'], disclaimer: null,
    streaming: true, showReasoning: false, audience: 'Members', toolsEnabled: false, knowledgeEnabled: false,
    limits: { maxMessageCharacters: 4000, maxMessagesPerConversation: 100, messagesPerDay: 50, remainingToday: 5 }, privacy: { adminsCanRead: true }, labels: [], ...extra
  };
}

describe('assistant helpers on mobile', () => {
  test('splits fenced code from prose', () => {
    expect(splitCodeFences('Try this:\n```js\nconst a = 1;\n```\nDone.')).toEqual([
      { type: 'prose', value: 'Try this:' },
      { type: 'code', value: 'const a = 1;' },
      { type: 'prose', value: 'Done.' }
    ]);
    expect(splitCodeFences('Unclosed\n```\ncode')).toEqual([{ type: 'prose', value: 'Unclosed' }, { type: 'code', value: 'code' }]);
  });

  test('maps errors to codes', () => {
    expect(chatErrorCode({ code: 'AssistantLimitReached', status: 429 })).toBe('AssistantLimitReached');
    expect(chatErrorCode({ status: 429 })).toBe('RateLimited');
    expect(chatErrorCode({ status: 500 })).toBe('generic');
  });

  test('stream headers carry the bearer token and client identification', async () => {
    const api = new MobileApiClient({ baseUrl: 'https://api.example', appVersion: '1.0.0', tokenStore: { getRefreshToken: async () => null, setRefreshToken: async () => {}, clear: async () => {} } });
    api.accessToken = 'access-1';
    const headers = await api.baseHeaders();
    expect(headers).toMatchObject({ Authorization: 'Bearer access-1', 'X-Client': 'mobile', 'X-Client-Version': '1.0.0' });
    expect(typeof api.startConversation).toBe('function');
    expect(await api.onUnauthorized()).toBe(false);
  });
});

describe('assistants screens', () => {
  beforeEach(() => {
    resetEnvironment();
    mockStreams.clear();
  });
  afterEach(() => delete global.fetch);

  test('without features.ai the screen makes no assistant calls', async () => {
    const server = installFakeServer({ 'GET /property': propertyWithFeatures({ ai: false }), ...signedInAs(makeUser()) });
    await renderRouter(APP_DIR, { initialUrl: '/assistants' });
    expect(await screen.findByText(s.errors.NotAvailable)).toBeOnTheScreen();
    expect(screen.queryByText(s.errors.NotFound)).toBeNull();
    expect(server.calls.some((c) => c.path.startsWith('/assistant'))).toBe(false);
  });

  test('without features.ai a chat deep link says assistants are unavailable, not that the chat is missing', async () => {
    const server = installFakeServer({ 'GET /property': propertyWithFeatures({ ai: false }), ...signedInAs(makeUser()) });
    await renderRouter(APP_DIR, { initialUrl: '/assistants/ast_1' });
    expect(await screen.findByText(s.errors.NotAvailable)).toBeOnTheScreen();
    expect(screen.queryByText(s.errors.NotFound)).toBeNull();
    expect(server.calls.some((c) => c.path.startsWith('/assistant'))).toBe(false);
  });

  test('lists assistants and conversations, then streams a reply in a new conversation', async () => {
    const reply = { id: 'acm_2', conversationId: 'acv_1', role: 'Assistant', content: 'The meetup is **Saturday**.', status: 'Completed', isAiGenerated: true, citations: [], createdUtc: '2026-11-11T00:00:01Z' };
    const server = installFakeServer({
      'GET /property': propertyWithFeatures({ ai: true, aiPlacement: 'Menu' }),
      ...signedInAs(makeUser()),
      'POST /assistants/enumerate': enumerationResult([summary()]),
      'POST /assistant-conversations/enumerate': enumerationResult([{ id: 'acv_0', title: 'Earlier chat', assistant: { id: 'ast_1', title: 'Community helper' }, messageCount: 2, lastMessagePreview: 'Hi', isArchived: false }]),
      'GET /assistants/ast_1': summary(),
      'POST /assistant-conversations/acv_1/messages/enumerate': enumerationResult([reply])
    });
    mockStreams.set('POST /assistants/ast_1/conversations', (init) => {
      const body = JSON.parse(init.body);
      return sse([
        ['message.created', { userMessage: { id: 'acm_1', role: 'User', content: body.message, clientMessageId: body.clientMessageId, status: 'Completed' }, replyMessageId: 'acm_2', conversation: { id: 'acv_1', title: null } }],
        ['reply.started', { replyMessageId: 'acm_2' }],
        ['reply.delta', { text: 'The meetup is ' }],
        ['reply.delta', { text: '**Saturday**.' }],
        ['reply.completed', { reply, remainingToday: 4 }]
      ]);
    });
    const router = await renderRouter(APP_DIR, { initialUrl: '/assistants' });
    expect(await screen.findByTestId('assistant-ast_1')).toBeOnTheScreen();
    expect(await screen.findByTestId('conversation-acv_0')).toBeOnTheScreen();
    expect(screen.getByText(s.remaining(5))).toBeOnTheScreen();
    await fireEvent.press(screen.getByTestId('assistant-ast_1'));
    await waitFor(() => expect(router.getPathname()).toBe('/assistants/ast_1'));
    expect(await screen.findByTestId('assistant-privacy')).toHaveTextContent(/administrators can read assistant conversations/);
    expect(screen.getByTestId('assistant-first-use')).toBeOnTheScreen();
    await fireEvent.changeText(screen.getByTestId('assistant-composer'), 'When is the meetup?');
    await fireEvent.press(screen.getByTestId('assistant-send'));
    await waitFor(() => expect(require('expo/fetch').fetch).toHaveBeenCalled());
    expect(await screen.findByText('Saturday')).toBeOnTheScreen();
    expect(await screen.findByText(s.remaining(4))).toBeOnTheScreen();
    // The screen stayed awake while the reply streamed, and is released after.
    expect(KeepAwake.activateKeepAwakeAsync).toHaveBeenCalledWith('tk.assistantReply');
    await waitFor(() => expect(KeepAwake.deactivateKeepAwake).toHaveBeenCalledWith('tk.assistantReply'));
    const conversations = server.calls.filter((c) => c.path === '/assistant-conversations/enumerate');
    expect(conversations[0].body).toMatchObject({ filters: { archived: false } });
  });
});

describe('reporting an assistant reply', () => {
  beforeEach(() => {
    resetEnvironment();
    mockStreams.clear();
  });
  afterEach(() => delete global.fetch);

  test('a member reports a reply with the report sheet', async () => {
    const reply = { id: 'acm_2', conversationId: 'acv_1', role: 'Assistant', content: 'A reply.', status: 'Completed', citations: [], createdUtc: '2026-11-11T00:00:01Z' };
    const server = installFakeServer({
      'GET /property': propertyWithFeatures({ ai: true, aiPlacement: 'Menu' }),
      ...signedInAs(makeUser()),
      'GET /assistants/ast_1': summary(),
      'POST /assistant-conversations/acv_1/messages/enumerate': enumerationResult([{ id: 'acm_1', role: 'User', content: 'Hi', status: 'Completed' }, reply]),
      'POST /reports': { status: 201, body: { id: 'rpt_1' } }
    });
    await renderRouter(APP_DIR, { initialUrl: '/assistants/ast_1?conversation=acv_1' });
    await fireEvent.press(await screen.findByTestId('report-reply-acm_2'));
    expect(screen.queryByTestId('report-reply-acm_1')).toBeNull();
    await fireEvent.press(await screen.findByTestId('report-reason-Spam'));
    await fireEvent.press(screen.getByTestId('report-submit'));
    expect(await screen.findByTestId('report-thanks')).toBeOnTheScreen();
    expect(server.called('POST /reports')[0].body).toEqual({ targetType: 'AssistantMessage', targetId: 'acm_2', reason: 'Spam' });
  });
});

describe('inline code in assistant replies', () => {
  test('the assistant mode parses single-backtick spans; posts keep them literal', () => {
    expect(parseMarkdown('Run `npm test` now', { code: true })[0].children).toEqual([
      { type: 'text', value: 'Run ' },
      { type: 'code', value: 'npm test' },
      { type: 'text', value: ' now' }
    ]);
    expect(parseMarkdown('Run `npm test` now')[0].children).toEqual([{ type: 'text', value: 'Run `npm test` now' }]);
    // No markup inside a code span, and an unclosed backtick stays literal.
    expect(parseMarkdown('`**x**` and `open', { code: true })[0].children).toEqual([
      { type: 'code', value: '**x**' },
      { type: 'text', value: ' and `open' }
    ]);
  });

  test('renders inline code as monospaced text in a reply', async () => {
    const message = { id: 'acm_9', role: 'Assistant', content: 'Use `tk.config` here.', status: 'Completed', citations: [] };
    await render(<PropertyProvider><AssistantMessage message={message} /></PropertyProvider>);
    expect(screen.getByTestId('markdown-inline-code')).toHaveTextContent('tk.config');
  });
});

describe('visitor sessions for guests', () => {
  beforeEach(() => {
    resetEnvironment();
    mockStreams.clear();
  });
  afterEach(() => delete global.fetch);

  function visitorClient(extra = {}) {
    return createVisitorClient({ baseUrl: 'https://api.example.test', appVersion: '1.0.0', streamFetchImpl: null, ...extra });
  }

  test('starts a session before listing, stores the token securely, and never sends a bearer token', async () => {
    const server = installFakeServer({
      'POST /assistant-visitors': { status: 201, body: { visitorId: 'avs_1', visitorToken: 'vt-1' } },
      'POST /assistants/enumerate': enumerationResult([summary({ audience: 'Public' })])
    });
    const client = visitorClient();
    const page = await client.enumerateAssistants({ maxResults: 50 });
    expect(page.objects[0].id).toBe('ast_1');
    const list = server.called('POST /assistants/enumerate')[0];
    expect(list.headers['X-TK-Visitor-Token']).toBe('vt-1');
    expect(list.headers.Authorization).toBeUndefined();
    expect(SecureStore.__store.get(VisitorTokenKey)).toBe('vt-1');
    expect(server.called('POST /assistant-visitors')).toHaveLength(1);
  });

  test('reuses the stored token, and on a 401 starts a new session once and retries', async () => {
    SecureStore.__store.set(VisitorTokenKey, 'vt-old');
    const server = installFakeServer({
      'POST /assistant-visitors': { status: 201, body: { visitorId: 'avs_2', visitorToken: 'vt-new' } },
      'POST /assistant-conversations/enumerate': (call) => (call.headers['X-TK-Visitor-Token'] === 'vt-new' ? enumerationResult([]) : { status: 401, body: { error: 'Unauthorized', message: 'Sign in.' } })
    });
    const client = visitorClient();
    await client.enumerateConversations({ maxResults: 50 });
    expect(server.called('POST /assistant-conversations/enumerate').map((c) => c.headers['X-TK-Visitor-Token'])).toEqual(['vt-old', 'vt-new']);
    expect(server.called('POST /assistant-visitors')).toHaveLength(1);
    expect(SecureStore.__store.get(VisitorTokenKey)).toBe('vt-new');
  });

  test('a 401 that a new session does not fix surfaces once', async () => {
    SecureStore.__store.set(VisitorTokenKey, 'vt-old');
    const server = installFakeServer({
      'POST /assistant-visitors': { status: 201, body: { visitorId: 'avs_3', visitorToken: 'vt-3' } },
      'GET /assistants/ast_1': { status: 401, body: { error: 'Unauthorized', message: 'Sign in.' } }
    });
    await expect(visitorClient().getAssistant('ast_1')).rejects.toMatchObject({ status: 401 });
    expect(server.called('GET /assistants/ast_1')).toHaveLength(2);
    expect(server.called('POST /assistant-visitors')).toHaveLength(1);
  });

  test('takes the token from the conversation start response', async () => {
    const reply = { id: 'acm_2', conversationId: 'acv_1', role: 'Assistant', content: 'Hi.', status: 'Completed', citations: [] };
    const server = installFakeServer({
      'POST /assistants/ast_1/conversations': () => ({ status: 201, body: { userMessage: { id: 'acm_1', role: 'User', content: 'Hello', conversationId: 'acv_1' }, reply }, headers: { 'X-TK-Visitor-Token': 'vt-start' } }),
      'POST /assistant-conversations/acv_1/messages/enumerate': enumerationResult([reply])
    });
    const client = visitorClient();
    const events = [];
    for await (const event of client.startConversation('ast_1', { message: 'Hello' }, { stream: false })) events.push(event.event);
    expect(events).toContain('reply.completed');
    expect(server.called('POST /assistants/ast_1/conversations')[0].headers['X-TK-Visitor-Token']).toBeUndefined();
    expect(server.called('POST /assistant-visitors')).toHaveLength(0);
    await waitFor(() => expect(SecureStore.__store.get(VisitorTokenKey)).toBe('vt-start'));
    await client.enumerateConversationMessages('acv_1');
    expect(server.called('POST /assistant-conversations/acv_1/messages/enumerate')[0].headers['X-TK-Visitor-Token']).toBe('vt-start');
  });

  test('a streamed send that answers 401 renews the session and retries', async () => {
    SecureStore.__store.set(VisitorTokenKey, 'vt-old');
    installFakeServer({ 'POST /assistant-visitors': { status: 201, body: { visitorId: 'avs_4', visitorToken: 'vt-4' } } });
    const seen = [];
    const streamFetchImpl = jest.fn(async (url, init) => {
      const key = init.method + ' ' + new URL(url).pathname.replace(/^\/api\/v1/, '');
      if (key !== 'POST /assistant-conversations/acv_1/messages') return global.fetch(url, init);
      seen.push(init.headers['X-TK-Visitor-Token']);
      if (init.headers['X-TK-Visitor-Token'] !== 'vt-4') return { ok: false, status: 401, headers: { get: (n) => (n.toLowerCase() === 'content-type' ? 'application/json' : null) }, json: async () => ({ error: 'Unauthorized' }), text: async () => '' };
      return { ok: true, status: 200, headers: { get: (n) => (n.toLowerCase() === 'content-type' ? 'text/event-stream' : null) }, text: async () => sse([['reply.completed', { reply: { id: 'acm_5', status: 'Completed', content: 'ok' } }]]) };
    });
    const client = visitorClient({ streamFetchImpl });
    const events = [];
    for await (const event of client.sendAssistantMessage('acv_1', { content: 'Hi' })) events.push(event.event);
    expect(seen).toEqual(['vt-old', 'vt-4']);
    expect(events).toEqual(['reply.completed']);
  });

  test('guests open the Public assistants with a visitor session when anonymous browsing is on', async () => {
    const guestProperty = {
      ...property,
      access: { model: 'PartiallyGated', anonymousBrowsing: true, gatedFeatures: [], defaultAudience: 'SignedIn', purchasesEnabled: true, signupCodesEnabled: true, revision: 3 },
      features: { ...(property.features || {}), ai: true, aiPlacement: 'Menu' }
    };
    const server = installFakeServer({
      'GET /property': guestProperty,
      'POST /assistant-visitors': { status: 201, body: { visitorId: 'avs_5', visitorToken: 'vt-5' } },
      'POST /assistants/enumerate': enumerationResult([summary({ audience: 'Public' })]),
      'POST /assistant-conversations/enumerate': enumerationResult([])
    });
    await renderRouter(APP_DIR, { initialUrl: '/assistants' });
    expect(await screen.findByTestId('assistant-ast_1')).toBeOnTheScreen();
    expect(screen.getByTestId('assistants-guest')).toBeOnTheScreen();
    const list = server.called('POST /assistants/enumerate')[0];
    expect(list.headers['X-TK-Visitor-Token']).toBe('vt-5');
    expect(list.headers.Authorization).toBeUndefined();
  });

  test('guests get the sign-in prompt when assistants are gated', async () => {
    const gated = {
      ...property,
      access: { model: 'PartiallyGated', anonymousBrowsing: true, gatedFeatures: ['assistants'], defaultAudience: 'SignedIn', purchasesEnabled: true, signupCodesEnabled: true, revision: 3 },
      features: { ...(property.features || {}), ai: true, aiPlacement: 'Menu' }
    };
    const server = installFakeServer({ 'GET /property': gated });
    await renderRouter(APP_DIR, { initialUrl: '/assistants' });
    expect(await screen.findByTestId('feature-locked-assistants-prompt')).toBeOnTheScreen();
    expect(server.calls.some((c) => c.path.startsWith('/assistant'))).toBe(false);
  });
});

describe('tool activity in the assistant chat', () => {
  beforeEach(() => {
    resetEnvironment();
    mockStreams.clear();
  });
  afterEach(() => delete global.fetch);

  test('streamed tool calls show as chips whose results open in the app', async () => {
    const results = [{ targetType: 'Lesson', targetId: 'les_1', title: 'First steps', path: '/lessons/les_1' }, { targetType: 'Other', targetId: 'x_1', title: 'Elsewhere', path: '/unknown/x_1' }];
    const reply = { id: 'acm_2', conversationId: 'acv_1', role: 'Assistant', content: 'Start with First steps.', status: 'Completed', citations: [], createdUtc: '2026-11-11T00:00:01Z' };
    installFakeServer({
      'GET /property': propertyWithFeatures({ ai: true, aiPlacement: 'Menu' }),
      ...signedInAs(makeUser()),
      'GET /assistants/ast_1': summary({ toolsEnabled: true }),
      'POST /assistant-conversations/acv_1/messages/enumerate': enumerationResult([reply])
    });
    mockStreams.set('POST /assistants/ast_1/conversations', (init) => {
      const body = JSON.parse(init.body);
      return sse([
        ['message.created', { userMessage: { id: 'acm_1', role: 'User', content: body.message, clientMessageId: body.clientMessageId, status: 'Completed' }, replyMessageId: 'acm_2', conversation: { id: 'acv_1', title: null } }],
        ['reply.started', { replyMessageId: 'acm_2' }],
        ['tool.started', { toolCallId: 'atc_1', toolName: 'searchLibrary', label: 'Searching the library', round: 1 }],
        ['tool.completed', { toolCallId: 'atc_1', toolName: 'searchLibrary', label: 'Searching the library', completedLabel: 'Searched the library', status: 'Succeeded', resultCount: 2, durationMs: 20, round: 1, results }],
        ['tool.started', { toolCallId: 'atc_2', toolName: 'findEvents', label: 'Finding events', round: 2 }],
        ['tool.completed', { toolCallId: 'atc_2', toolName: 'findEvents', label: 'Finding events', completedLabel: 'Found events', status: 'Failed', resultCount: 0, durationMs: 5, round: 2, results: [] }],
        ['reply.delta', { text: 'Start with First steps.' }],
        ['reply.completed', { reply, remainingToday: 4 }]
      ]);
    });
    const router = await renderRouter(APP_DIR, { initialUrl: '/assistants/ast_1' });
    await fireEvent.changeText(await screen.findByTestId('assistant-composer'), 'Where do I start?');
    await fireEvent.press(screen.getByTestId('assistant-send'));
    expect(await screen.findByText('Searched the library')).toBeOnTheScreen();
    expect(screen.getByText(s.toolFailed('Finding events', 'Failed'))).toBeOnTheScreen();
    expect(screen.queryByTestId('tool-results-atc_1')).toBeNull();
    await fireEvent.press(screen.getByTestId('tool-chip-atc_1'));
    expect(screen.getByText('Elsewhere')).toBeOnTheScreen();
    await fireEvent.press(screen.getByTestId('tool-result-Lesson:les_1'));
    await waitFor(() => expect(router.getPathname()).toBe('/lessons/les_1'));
  });

  test('persisted tool calls render on a loaded conversation', async () => {
    const reply = { id: 'acm_2', conversationId: 'acv_1', role: 'Assistant', content: 'Here.', status: 'Completed', citations: [], createdUtc: '2026-11-11T00:00:01Z',
      toolCalls: { objects: [{ id: 'atc_9', toolName: 'findVenues', label: 'Finding places', completedLabel: 'Found places', status: 'TimedOut', resultCount: 0, durationMs: 15000, round: 1, results: [] }], totalRecords: 1 } };
    installFakeServer({
      'GET /property': propertyWithFeatures({ ai: true, aiPlacement: 'Menu' }),
      ...signedInAs(makeUser()),
      'GET /assistants/ast_1': summary(),
      'POST /assistant-conversations/acv_1/messages/enumerate': enumerationResult([{ id: 'acm_1', role: 'User', content: 'Hi', status: 'Completed' }, reply])
    });
    await renderRouter(APP_DIR, { initialUrl: '/assistants/ast_1?conversation=acv_1' });
    expect(await screen.findByText(s.toolFailed('Finding places', 'TimedOut'))).toBeOnTheScreen();
    expect(screen.getByTestId('tool-activity-acm_2')).toBeOnTheScreen();
  });
});
