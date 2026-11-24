import { describe, expect, it } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route } from 'react-router-dom';
import strings from '../src/strings.js';
import { groupedAdminRoutes } from '../src/views/admin/adminRoutes.js';
import { memberRoutesFor } from '../src/views/member/memberRoutes.js';
import { API_KINDS, TOOL_CALLING_MODES, assistantBody, assistantForm, authTypesFor, canonicalToolNames, endpointBody, endpointForm, hostChangedWithSecrets, percent, providerFieldsFor, providerSettingsProblems, toolLimitProblems, usd, widgetSnippet } from '../src/views/admin/ai/aiAdminUtils.js';
import AiEndpointsView from '../src/views/admin/ai/AiEndpointsView.jsx';
import AiAssistantsView from '../src/views/admin/ai/AiAssistantsView.jsx';
import AiConversationsView, { ToolCallTable } from '../src/views/admin/ai/AiConversationsView.jsx';
import ToolActivity from '../src/views/ai/ToolActivity.jsx';
import AiAnalyticsView from '../src/views/admin/ai/AiAnalyticsView.jsx';
import AssistantsListView from '../src/views/ai/AssistantsListView.jsx';
import AssistantChatView from '../src/views/ai/AssistantChatView.jsx';
import { createFetch, json, renderAdmin } from './helpers.jsx';
import { enumPage, memberFetch, renderMember } from './engagementHelpers.jsx';

const ep = strings.ai.admin.endpoints;
const as = strings.ai.admin.assistants;
const cv = strings.ai.admin.conversations;
const an = strings.ai.admin.analytics;

function makeEndpoint(id, extra = {}) {
  return {
    id, name: 'Local ' + id, description: null, enabled: true, kind: 'Completion', apiType: 'Ollama', model: 'llama3', endpointUrl: 'http://ollama.internal:11434',
    authType: 'Bearer', authHeaderName: null, authQueryParameter: null, authUsername: null, hasAuthValue: true, extraHeaders: [],
    parameters: { temperature: 0.7, topP: null, maxTokens: null, contextWindow: 8192, streaming: true, timeoutMs: 120000, firstResponseTimeoutMs: null, dimensions: null },
    limits: { maxConcurrentRequests: 2, maxQueueDepth: 20, maxRetries: 2 }, healthCheck: { enabled: true, intervalMs: 60000 },
    pricing: { inputPerMillionTokens: null, outputPerMillionTokens: null },
    health: { status: 'Healthy', uptimePercentage: 99.5, lastLatencyMs: 120 }, labels: [], tags: {}, ...extra
  };
}

function makeAssistant(id, extra = {}) {
  return {
    id, name: 'Helper', description: 'Answers questions', enabled: true, title: 'Community helper', avatar: { emoji: null, mediaId: null, imageUrl: null },
    systemPrompt: 'You help members of {name}.', models: { chat: 'mep_1' }, generation: { temperature: null, topP: null }, audience: 'Members',
    knowledge: { enabled: false, mode: 'Keyword', maxContextItems: 6, maxContextCharacters: 6000, citations: true, sources: [] },
    limits: { messagesPerMemberPerDay: 50, messagesPerVisitorPerDay: 10, maxMessagesPerConversation: 100, maxTokensPerReply: 800, maxMessageCharacters: 4000, conversationRetentionDays: 365 },
    streaming: true, showReasoning: false, starterPrompts: ['What is on this week?'], disclaimer: null,
    embed: { enabled: false, allowedOrigins: [], launcherLabel: null, position: 'BottomRight' }, moderationMode: 'Off',
    budget: { monthlyUsd: 20, hardStop: false, monthToDateUsd: 1.5 }, stats: { conversations: 4, messages: 12, thumbsUpRate: 0.75 },
    labels: [], tags: {}, ...extra
  };
}

function memberSummary(extra = {}) {
  return {
    id: 'ast_1', title: 'Community helper', description: 'Answers questions', avatar: { emoji: null }, starterPrompts: ['What is on this week?'], disclaimer: null,
    streaming: true, showReasoning: false, limits: { maxMessageCharacters: 4000, remainingToday: 5, messagesPerDay: 50 }, privacy: { adminsCanRead: true }, ...extra
  };
}

function sse(events) {
  const text = events.map(([event, data]) => 'event: ' + event + '\ndata: ' + JSON.stringify(data) + '\n\n').join('');
  return new Response(text, { status: 200, headers: { 'content-type': 'text/event-stream' } });
}

describe('AI admin helpers', () => {
  it('builds endpoint bodies that follow the write-only secret rules', () => {
    const stored = makeEndpoint('mep_1');
    const keep = endpointBody(endpointForm(stored), { isCreate: false });
    expect(keep).not.toHaveProperty('authValue');
    expect(keep.parameters).toMatchObject({ temperature: 0.7, contextWindow: 8192, dimensions: null });
    const replace = endpointBody({ ...endpointForm(stored), secretMode: 'replace', authValue: 'new-secret' }, { isCreate: false });
    expect(replace.authValue).toBe('new-secret');
    expect(endpointBody({ ...endpointForm(stored), secretMode: 'clear' }, { isCreate: false }).authValue).toBe('');
    expect(endpointBody({ ...endpointForm(null), name: 'x', model: 'm', authType: 'None' }, { isCreate: true })).not.toHaveProperty('authValue');
    const headers = endpointBody({ ...endpointForm(stored), extraHeaders: [{ name: 'X-Org', value: '', hasValue: true }, { name: ' ', value: 'v' }] }, { isCreate: false }).extraHeaders;
    expect(headers).toEqual([{ name: 'X-Org' }]);
    expect(hostChangedWithSecrets(stored, { ...endpointForm(stored), endpointUrl: 'https://other.example' })).toBe(true);
    expect(hostChangedWithSecrets(stored, endpointForm(stored))).toBe(false);
    expect(hostChangedWithSecrets({ ...stored, hasAuthValue: false }, { ...endpointForm(stored), endpointUrl: 'https://other.example' })).toBe(false);
  });

  it('handles provider settings for Azure OpenAI, Vertex AI, and Bedrock', () => {
    expect(Object.keys(API_KINDS)).toEqual(expect.arrayContaining(['AzureOpenAI', 'VertexAI', 'Bedrock']));
    expect(API_KINDS.Bedrock).toContain('Rerank');
    expect(authTypesFor('AzureOpenAI')).toEqual(['ApiKey', 'Bearer']);
    expect(authTypesFor('VertexAI')).toEqual(['ApiKey', 'None']);
    expect(providerFieldsFor('Bedrock', 'None')).toEqual({ used: ['region'], required: ['region'] });
    expect(providerFieldsFor('Bedrock', 'ApiKey').required).toEqual(['region', 'accessKeyId']);
    expect(providerFieldsFor('Ollama', 'None').used).toEqual([]);
    const azure = { ...endpointForm(null), name: 'Azure', model: 'gpt', apiType: 'AzureOpenAI', authType: 'ApiKey' };
    expect(providerSettingsProblems(azure)).toEqual({ deployment: 'required' });
    expect(providerSettingsProblems({ ...azure, deployment: 'bad value!' })).toEqual({ deployment: 'invalid' });
    expect(endpointBody({ ...azure, deployment: ' chat-prod ', region: 'stale' }, { isCreate: true }).providerSettings).toEqual({ region: null, project: null, apiVersion: null, deployment: 'chat-prod', accessKeyId: null });
    expect(endpointBody(endpointForm(makeEndpoint('mep_1')), { isCreate: false })).not.toHaveProperty('providerSettings');
    const vertex = endpointForm(makeEndpoint('mep_v', { apiType: 'VertexAI', authType: 'None', providerSettings: { region: 'us-central1', project: 'example-project', apiVersion: null, deployment: null, accessKeyId: null } }));
    expect(vertex.project).toBe('example-project');
    expect(endpointBody(vertex, { isCreate: false }).providerSettings).toEqual({ region: 'us-central1', project: 'example-project', apiVersion: null, deployment: null, accessKeyId: null });
  });

  it('round-trips assistant forms and builds the widget snippet', () => {
    const a = makeAssistant('ast_1', { knowledge: { enabled: true, mode: 'Hybrid', maxContextItems: 4, citations: true, sources: [{ targetType: 'FeedPost', labels: ['guides', 'faq'], labelMatch: 'Any', maxItems: 50 }] } });
    const body = assistantBody(assistantForm(a));
    expect(body.models).toMatchObject({ chat: 'mep_1', chatFallback: null, embedding: null });
    expect(body.knowledge.sources).toEqual([{ targetType: 'FeedPost', labels: ['guides', 'faq'], labelMatch: 'Any', maxItems: 50 }]);
    expect(body.limits.maxTokensPerReply).toBe(800);
    expect(body.budget).toEqual({ monthlyUsd: 20, hardStop: false });
    expect(body.starterPrompts).toEqual(['What is on this week?']);
    expect(widgetSnippet('https://web.example', { id: 'ast_1', embed: { launcherLabel: 'Ask "us"', position: 'BottomLeft' } }))
      .toBe('<script src="https://web.example/assistant-widget.js" data-assistant="ast_1" data-position="BottomLeft" data-label="Ask &quot;us&quot;" async></script>');
    expect(usd(0.0123)).toBe('$0.0123');
    expect(usd(12)).toBe('$12.00');
    expect(percent(0.256)).toBe('25.6%');
    expect(percent(null)).toBe('-');
  });

  it('keeps the AI sections out of navigation until the server reports the capability', () => {
    const ids = (property) => groupedAdminRoutes(property).flatMap((g) => g.routes.map((r) => r.id));
    expect(ids({ features: {} })).not.toContain('ai-endpoints');
    expect(ids({ features: { ai: true } })).toEqual(expect.arrayContaining(['ai-endpoints', 'ai-assistants', 'ai-conversations', 'ai-analytics']));
    expect(memberRoutesFor({ features: {} }).map((r) => r.id)).not.toContain('assistants');
    expect(memberRoutesFor({ features: { ai: true } }).map((r) => r.id)).toContain('assistants');
  });
});

describe('AI admin pages', () => {
  it('lists endpoints, tests one, and never shows a stored secret', async () => {
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/admin/ai/endpoints/enumerate') return json(enumPage([makeEndpoint('mep_1')]));
      if (call.path === '/api/v1/admin/ai/endpoints/mep_1/test') return json({ success: true, latencyMs: 210, timeToFirstTokenMs: 90, output: 'Hello there friend, welcome!' });
      return undefined;
    });
    renderAdmin(<AiEndpointsView />, { fetchImpl });
    const row = (await screen.findByText('Local mep_1')).closest('tr');
    expect(within(row).getByText(ep.health.Healthy)).toBeInTheDocument();
    expect(within(row).getByText('99.5%')).toBeInTheDocument();
    // The enabled column states the endpoint's state, not the action that would change it.
    expect(within(row).getByText(ep.enabledBadge)).toBeInTheDocument();
    expect(within(row).queryByText(ep.enable)).not.toBeInTheDocument();
    await userEvent.click(within(row).getByRole('button', { name: strings.admin.common.rowActions('Local mep_1') }));
    await userEvent.click(screen.getByRole('menuitem', { name: ep.test }));
    await userEvent.click(screen.getByRole('button', { name: ep.testRun }));
    expect(await screen.findByText('Hello there friend, welcome!')).toBeInTheDocument();
    expect(calls.find((c) => c.path.endsWith('/test')).body).toMatchObject({ prompt: expect.any(String) });
  });

  it('shows the finish reason, token counts, and a budget hint when a reasoning model returns no text', async () => {
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/admin/ai/endpoints/enumerate') return json(enumPage([makeEndpoint('mep_1')]));
      if (call.path === '/api/v1/admin/ai/endpoints/mep_1/test') return json({ success: true, latencyMs: 900, output: '', finishReason: 'length', maxTokens: 64, truncated: true, hint: 'ReasoningConsumedBudget', usage: { promptTokens: 12, completionTokens: 64, reasoningTokens: 64, totalTokens: 76 } });
      return undefined;
    });
    renderAdmin(<AiEndpointsView />, { fetchImpl });
    const row = (await screen.findByText('Local mep_1')).closest('tr');
    await userEvent.click(within(row).getByRole('button', { name: strings.admin.common.rowActions('Local mep_1') }));
    await userEvent.click(screen.getByRole('menuitem', { name: ep.test }));
    const cap = screen.getByLabelText(ep.testMaxTokens);
    await userEvent.clear(cap);
    await userEvent.type(cap, '64');
    await userEvent.click(screen.getByRole('button', { name: ep.testRun }));
    expect(await screen.findByText(ep.testHints.ReasoningConsumedBudget)).toBeInTheDocument();
    expect(screen.getByText(ep.testEmptyOutput)).toBeInTheDocument();
    expect(screen.getByText(ep.testTokens({ promptTokens: 12, completionTokens: 64, reasoningTokens: 64, totalTokens: 76 }))).toBeInTheDocument();
    expect(screen.getByText(new RegExp(ep.testFinish('length')))).toBeInTheDocument();
    expect(calls.find((c) => c.path.endsWith('/test')).body).toMatchObject({ maxTokens: 64 });
  });

  it('edits an endpoint keeping the stored secret by default', async () => {
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/admin/ai/endpoints/enumerate') return json(enumPage([makeEndpoint('mep_1')]));
      if (call.method === 'PUT' && call.path === '/api/v1/admin/ai/endpoints/mep_1') return json(makeEndpoint('mep_1', { name: 'Renamed' }));
      if (call.path === '/api/v1/admin/ai/endpoints/mep_1/models/enumerate') return json(enumPage([{ name: 'llama3' }]));
      return undefined;
    });
    renderAdmin(<AiEndpointsView />, { fetchImpl });
    await userEvent.click(await screen.findByText('Local mep_1'));
    const name = screen.getByLabelText(ep.name);
    expect(screen.getByText(ep.secretSet)).toBeInTheDocument();
    await userEvent.clear(name);
    await userEvent.type(name, 'Renamed');
    await userEvent.click(screen.getByRole('button', { name: ep.save }));
    await waitFor(() => expect(calls.some((c) => c.method === 'PUT')).toBe(true));
    const put = calls.find((c) => c.method === 'PUT');
    expect(put.body.name).toBe('Renamed');
    expect(put.body).not.toHaveProperty('authValue');
  });

  it('creates a Bedrock endpoint with its provider settings', async () => {
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/admin/ai/endpoints/enumerate') return json(enumPage([]));
      if (call.method === 'POST' && call.path === '/api/v1/admin/ai/endpoints') return json(makeEndpoint('mep_b', { apiType: 'Bedrock' }), 201);
      return undefined;
    });
    renderAdmin(<AiEndpointsView />, { fetchImpl });
    await userEvent.click(await screen.findByRole('button', { name: ep.add }));
    await userEvent.type(screen.getByLabelText(ep.name), 'Bedrock chat');
    await userEvent.selectOptions(screen.getByLabelText(ep.apiType), 'Bedrock');
    await userEvent.type(screen.getByLabelText(ep.model), 'example.model-v1');
    expect(screen.getByTestId('ep-provider-settings')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: ep.save }));
    expect(await screen.findByText(ep.providerInvalid)).toBeInTheDocument();
    expect(calls.some((c) => c.method === 'POST' && c.path === '/api/v1/admin/ai/endpoints')).toBe(false);
    await userEvent.type(screen.getByLabelText(ep.providerFields.region), 'us-east-1');
    // Static keys: API key auth adds the access key id.
    expect(screen.queryByLabelText(ep.providerFields.accessKeyId)).toBeNull();
    await userEvent.selectOptions(screen.getByLabelText(ep.authType), 'ApiKey');
    await userEvent.type(screen.getByLabelText(ep.providerFields.accessKeyId), 'AKIAEXAMPLE');
    await userEvent.type(screen.getByLabelText(ep.authValue), 'secret-key');
    await userEvent.click(screen.getByRole('button', { name: ep.save }));
    await waitFor(() => expect(calls.some((c) => c.method === 'POST' && c.path === '/api/v1/admin/ai/endpoints')).toBe(true));
    const body = calls.find((c) => c.method === 'POST' && c.path === '/api/v1/admin/ai/endpoints').body;
    expect(body).toMatchObject({ apiType: 'Bedrock', authType: 'ApiKey', authValue: 'secret-key', providerSettings: { region: 'us-east-1', accessKeyId: 'AKIAEXAMPLE', project: null, apiVersion: null, deployment: null } });
  });

  it('creates an assistant only with a chat model', async () => {
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/admin/ai/assistants/enumerate') return json(enumPage([makeAssistant('ast_1')]));
      if (call.path === '/api/v1/admin/ai/endpoints/enumerate') return json(enumPage([makeEndpoint('mep_1'), makeEndpoint('mep_e', { kind: 'Embedding', name: 'Embedder' })]));
      if (call.method === 'POST' && call.path === '/api/v1/admin/ai/assistants') return json(makeAssistant('ast_2'), 201);
      return undefined;
    });
    renderAdmin(<AiAssistantsView />, { fetchImpl });
    const row = (await screen.findByText('Helper')).closest('tr');
    expect(within(row).getByText('$1.50 / $20.00')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: as.add }));
    await userEvent.type(screen.getByLabelText(as.name), 'Coach');
    await userEvent.click(screen.getByRole('button', { name: as.save }));
    expect(calls.some((c) => c.method === 'POST' && c.path === '/api/v1/admin/ai/assistants')).toBe(false);
    await userEvent.click(screen.getByRole('tab', { name: as.tabs.models }));
    const chat = screen.getByLabelText(as.purposes.chat);
    expect(within(chat).queryByText(/Embedder/)).toBeNull();
    await userEvent.selectOptions(chat, 'mep_1');
    await userEvent.click(screen.getByRole('button', { name: as.save }));
    await waitFor(() => expect(calls.some((c) => c.method === 'POST' && c.path === '/api/v1/admin/ai/assistants')).toBe(true));
    expect(calls.find((c) => c.method === 'POST' && c.path === '/api/v1/admin/ai/assistants').body).toMatchObject({ name: 'Coach', title: 'Coach', models: { chat: 'mep_1' } });
  });

  it('opens a conversation with reply telemetry', async () => {
    const conversation = { id: 'acv_1', title: 'Meetup times', assistant: { id: 'ast_1', title: 'Community helper' }, messageCount: 2, lastMessageUtc: '2026-11-10T10:00:00Z', lastMessagePreview: 'Saturdays at noon', surface: 'Web', owner: { type: 'User', userId: 'usr_2', member: { displayName: 'Alex Chen' } } };
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/admin/ai/assistants/enumerate') return json(enumPage([makeAssistant('ast_1')]));
      if (call.path === '/api/v1/admin/ai/conversations/enumerate') return json(enumPage([conversation]));
      if (call.path === '/api/v1/admin/ai/conversations/acv_1') return json(conversation);
      if (call.path === '/api/v1/admin/ai/conversations/acv_1/messages/enumerate') {
        return json(enumPage([
          { id: 'acm_1', role: 'User', content: 'When is the meetup?', status: 'Completed', createdUtc: '2026-11-10T10:00:00Z' },
          { id: 'acm_2', role: 'Assistant', content: 'Saturdays at noon', status: 'Completed', createdUtc: '2026-11-10T10:00:01Z', model: { endpointName: 'Local', model: 'llama3', usedFallback: false }, usage: { promptTokens: 120, completionTokens: 30 }, latencyMs: 900, timeToFirstTokenMs: 150, finishReason: 'stop', telemetry: { costUsd: 0.000321, stages: [{ stage: 'chat', model: 'llama3', durationMs: 900, success: true, promptTokens: 120, completionTokens: 30 }] } }
        ]));
      }
      return undefined;
    });
    renderAdmin(<AiConversationsView />, { fetchImpl });
    await userEvent.click(await screen.findByText('Meetup times'));
    expect(await screen.findByText(cv.tokens(120, 30), { exact: false })).toBeInTheDocument();
    expect(screen.getByText(cv.auditedNote)).toBeInTheDocument();
    expect(calls.some((c) => c.path === '/api/v1/admin/ai/conversations/acv_1')).toBe(true);
  });

  it('shows analytics tiles and marks feedback reviewed', async () => {
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/admin/ai/analytics/overview') return json({ days: 30, conversations: 4, messages: 40, activeMembers: 3, visitors: 1, requests: 44, errors: 2, errorRate: 0.045, costUsd: 0.42, latencyP95Ms: 1800, ttftAverageMs: 300, thumbsUpRate: 0.8, fallbackRate: 0, monthToDateCostUsd: 0.42, monthlyBudgetUsd: 25 });
      if (call.path === '/api/v1/admin/ai/analytics/timeseries') return json({ metric: 'requests', granularity: 'Day', points: [{ timestampUtc: '2026-11-10T00:00:00Z', value: 4 }] });
      if (call.path.startsWith('/api/v1/admin/ai/analytics/')) return json(enumPage([]));
      if (call.path === '/api/v1/admin/ai/feedback/enumerate') return json(enumPage([{ id: 'afb_1', rating: 'Down', reasons: ['Incorrect'], note: 'Wrong day', reviewed: false, promptContent: 'When?', replyContent: 'Mondays', member: null, assistant: { id: 'ast_1', name: 'Helper' } }]));
      if (call.path === '/api/v1/admin/ai/feedback/afb_1/review') return json({ id: 'afb_1', reviewed: true });
      return undefined;
    });
    renderAdmin(<AiAnalyticsView />, { fetchImpl });
    expect(await screen.findByText('$0.42 / $25.00')).toBeInTheDocument();
    expect(screen.getByText('4.5%')).toBeInTheDocument();
    expect(calls.find((c) => c.path === '/api/v1/admin/ai/analytics/endpoints/enumerate').body.filters).toMatchObject({ windowDays: '30' });
    await userEvent.click(await screen.findByRole('button', { name: an.review }));
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/admin/ai/feedback/afb_1/review')).toBe(true));
  });
});

describe('member assistants', () => {
  it('lists assistants', async () => {
    const { fetchImpl } = memberFetch((call) => {
      if (call.path === '/api/v1/assistants/enumerate') return json(enumPage([memberSummary()]));
      return undefined;
    });
    await renderMember(<AssistantsListView />, { fetchImpl, features: { ai: true } });
    expect(await screen.findByRole('link', { name: strings.ai.openAssistant('Community helper') })).toHaveAttribute('href', '/app/assistants/ast_1');
  });

  it('streams a reply into a new conversation and shows the disclosure', async () => {
    window.localStorage.clear();
    const reply = { id: 'acm_2', conversationId: 'acv_1', role: 'Assistant', content: 'The meetup is Saturday.', status: 'Completed', isAiGenerated: true, citations: [], createdUtc: '2026-11-11T00:00:01Z' };
    const { fetchImpl, calls } = memberFetch((call) => {
      if (call.path === '/api/v1/assistants/ast_1') return json(memberSummary());
      if (call.path === '/api/v1/assistants/ast_1/conversations/enumerate') return json(enumPage([]));
      if (call.path === '/api/v1/assistant-conversations/acv_1') return json({ id: 'acv_1', title: null, assistant: { id: 'ast_1', title: 'Community helper' }, messageCount: 2, recentMessages: enumPage([reply]) });
      if (call.path === '/api/v1/assistant-conversations/acv_1/messages/enumerate') return json(enumPage([reply]));
      if (call.method === 'POST' && call.path === '/api/v1/assistants/ast_1/conversations') {
        return sse([
          ['message.created', { userMessage: { id: 'acm_1', role: 'User', content: call.body.message, clientMessageId: call.body.clientMessageId, status: 'Completed', createdUtc: '2026-11-11T00:00:00Z' }, replyMessageId: 'acm_2', conversation: { id: 'acv_1', title: null } }],
          ['reply.started', { replyMessageId: 'acm_2' }],
          ['reply.delta', { text: 'The meetup ' }],
          ['reply.delta', { text: 'is Saturday.' }],
          ['reply.completed', { reply, remainingToday: 4 }]
        ]);
      }
      return undefined;
    });
    await renderMember(<AssistantChatView />, { fetchImpl, path: '/app/assistants/ast_1', routePath: '/app/assistants/:id', extraRoutes: <Route path='/app/assistants/:id/c/:conversationId' element={<AssistantChatView />} />, features: { ai: true } });
    expect(await screen.findByText(strings.ai.firstUseTitle)).toBeInTheDocument();
    expect(screen.getByText(/administrators can read assistant conversations/i)).toBeInTheDocument();
    await userEvent.type(screen.getByLabelText(strings.ai.composerLabel), 'When is the meetup?');
    await userEvent.click(screen.getByRole('button', { name: strings.ai.send }));
    expect(await screen.findByText('The meetup is Saturday.')).toBeInTheDocument();
    expect(await screen.findByText(strings.ai.remaining(4))).toBeInTheDocument();
    const post = calls.find((c) => c.method === 'POST' && c.path === '/api/v1/assistants/ast_1/conversations');
    expect(post.body.message).toBe('When is the meetup?');
    expect(post.headers.Accept || post.headers.accept).toBe('text/event-stream');
  });

  it('reports an assistant reply with the report dialog', async () => {
    window.localStorage.setItem('tk_ai_notice_ast_1', '1');
    const reply = { id: 'acm_2', conversationId: 'acv_1', role: 'Assistant', content: 'A reply.', status: 'Completed', isAiGenerated: true, citations: [], createdUtc: '2026-11-11T00:00:01Z' };
    const user = { id: 'acm_1', conversationId: 'acv_1', role: 'User', content: 'Hi', status: 'Completed', createdUtc: '2026-11-11T00:00:00Z' };
    const { fetchImpl, calls } = memberFetch((call) => {
      if (call.path === '/api/v1/assistants/ast_1') return json(memberSummary());
      if (call.path === '/api/v1/assistants/ast_1/conversations/enumerate') return json(enumPage([]));
      if (call.path === '/api/v1/assistant-conversations/acv_1') return json({ id: 'acv_1', title: null, assistant: { id: 'ast_1', title: 'Community helper' }, messageCount: 2, recentMessages: enumPage([user, reply]) });
      if (call.path === '/api/v1/assistant-conversations/acv_1/messages/enumerate') return json(enumPage([user, reply]));
      if (call.method === 'POST' && call.path === '/api/v1/reports') return json({ id: 'rpt_1' }, 201);
      return undefined;
    });
    await renderMember(<AssistantChatView />, { fetchImpl, path: '/app/assistants/ast_1/c/acv_1', routePath: '/app/assistants/:id/c/:conversationId', features: { ai: true } });
    expect(await screen.findByText('A reply.')).toBeInTheDocument();
    await userEvent.click(await screen.findByRole('button', { name: strings.ai.reportReply }));
    const dialog = await screen.findByRole('dialog', { name: strings.safety.reportTitle(strings.safety.reportWhat.AssistantMessage) });
    await userEvent.click(within(dialog).getByRole('radio', { name: new RegExp(strings.safety.reasons.Spam.label) }));
    await userEvent.click(screen.getByRole('button', { name: strings.safety.submitReport }));
    expect(await screen.findByText(strings.safety.thanks)).toBeInTheDocument();
    // An assistant reply has no member author: the confirmation does not talk about "the member".
    expect(screen.getByText(strings.safety.thanksBodyFor.AssistantMessage)).toBeInTheDocument();
    expect(screen.queryByText(/the member/i)).toBeNull();
    expect(calls.find((c) => c.path === '/api/v1/reports').body).toEqual({ targetType: 'AssistantMessage', targetId: 'acm_2', reason: 'Spam' });
  });
});

describe('assistant tool calling', () => {
  const tl = as.tools;
  const registry = [
    { name: 'searchLibrary', description: 'Search lessons', label: 'Searching the library', completedLabel: 'Searched the library', aliases: [], worksSignedOut: true, parametersSchema: {}, disabled: false, labels: [], tags: {} },
    { name: 'findVenues', description: 'Find places', label: 'Finding places', completedLabel: 'Found places', aliases: ['findGyms'], worksSignedOut: false, parametersSchema: {}, disabled: true, disabledReason: 'Maintenance', labels: [], tags: {} }
  ];

  it('builds tool settings, checks their bounds, and resolves aliases', () => {
    const form = assistantForm(makeAssistant('ast_1', { tools: { enabled: true, allowed: ['findGyms'], maxToolIterations: 3, maxToolCallsPerTurn: 6, toolCallTimeoutMs: 15000, maxToolOutputCharacters: 8000, maxTokensPerTurn: 0, showToolActivity: false }, models: { chat: 'mep_1', toolRouting: 'mep_2' } }));
    const body = assistantBody(form);
    expect(body.tools).toEqual({ enabled: true, allowed: ['findGyms'], showToolActivity: false, maxToolIterations: 3, maxToolCallsPerTurn: 6, toolCallTimeoutMs: 15000, maxToolOutputCharacters: 8000, maxTokensPerTurn: 0 });
    expect(body.models.toolRouting).toBe('mep_2');
    expect(assistantBody(assistantForm(null)).tools).toMatchObject({ enabled: false, allowed: [], showToolActivity: true, maxToolIterations: 4, maxTokensPerTurn: 32000 });
    expect(toolLimitProblems(form)).toEqual({});
    expect(toolLimitProblems({ ...form, maxToolIterations: '11', maxTokensPerTurn: '500', toolCallTimeoutMs: '99' })).toEqual({ 'tools.maxToolIterations': 'range', 'tools.maxTokensPerTurn': 'range', 'tools.toolCallTimeoutMs': 'range' });
    expect(canonicalToolNames(registry, ['findGyms', 'findVenues', 'searchLibrary'])).toEqual(['findVenues', 'searchLibrary']);
    expect(TOOL_CALLING_MODES).toEqual(['Native', 'Prompt', 'Off']);
    expect(endpointBody(endpointForm(makeEndpoint('mep_1', { parameters: { ...makeEndpoint('mep_1').parameters, toolCalling: 'Prompt' } })), { isCreate: false }).parameters.toolCalling).toBe('Prompt');
    expect(endpointForm(makeEndpoint('mep_1')).toolCalling).toBe('Native');
  });

  it('shows running, completed, and failed tool chips with in-app result links', () => {
    const message = { id: 'acm_2', role: 'Assistant', status: 'Streaming', toolCalls: { objects: [
      { id: 'atc_1', toolName: 'searchLibrary', label: 'Searching the library', completedLabel: null, status: 'Running', results: [] },
      { id: 'atc_2', toolName: 'findEvents', label: 'Finding events', completedLabel: 'Found events', status: 'Succeeded', resultCount: 1, results: [{ targetType: 'Event', targetId: 'evt_1', title: 'Open day', path: '/events/evt_1' }] },
      { id: 'atc_3', toolName: 'searchForums', label: 'Searching forums', completedLabel: 'Searched forums', status: 'TimedOut', results: [] }
    ], totalRecords: 3 } };
    render(<MemoryRouter><ToolActivity message={message} linkBase="/app" /></MemoryRouter>);
    const list = screen.getByTestId('ai-tool-activity');
    expect(list).toHaveAttribute('aria-live', 'polite');
    expect(screen.getByText(strings.ai.toolRunning('Searching the library'))).toBeInTheDocument();
    expect(screen.getByText('Found events')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open day' })).toHaveAttribute('href', '/app/events/evt_1');
    expect(screen.getByText(strings.ai.toolFailed('Searching forums', 'TimedOut'))).toBeInTheDocument();
  });

  it('streams tool activity into the reply and renders persisted tool calls', async () => {
    window.localStorage.setItem('tk.ai.notice.ast_1', '1');
    const toolCall = { id: 'atc_1', toolName: 'searchLibrary', label: 'Searching the library', completedLabel: 'Searched the library', status: 'Succeeded', resultCount: 1, durationMs: 30, round: 1, results: [{ targetType: 'Lesson', targetId: 'les_1', title: 'First steps', path: '/lessons/les_1' }], createdUtc: '2026-11-11T00:00:01Z' };
    const reply = { id: 'acm_2', conversationId: 'acv_1', role: 'Assistant', content: 'Try First steps.', status: 'Completed', isAiGenerated: true, citations: [], toolCalls: { objects: [toolCall], totalRecords: 1 }, createdUtc: '2026-11-11T00:00:01Z' };
    const { fetchImpl } = memberFetch((call) => {
      if (call.path === '/api/v1/assistants/ast_1') return json(memberSummary());
      if (call.path === '/api/v1/assistants/ast_1/conversations/enumerate') return json(enumPage([]));
      if (call.path === '/api/v1/assistant-conversations/acv_1') return json({ id: 'acv_1', title: null, assistant: { id: 'ast_1', title: 'Community helper' }, messageCount: 2, recentMessages: enumPage([reply]) });
      if (call.path === '/api/v1/assistant-conversations/acv_1/messages/enumerate') return json(enumPage([reply]));
      if (call.method === 'POST' && call.path === '/api/v1/assistants/ast_1/conversations') {
        return sse([
          ['message.created', { userMessage: { id: 'acm_1', role: 'User', content: call.body.message, clientMessageId: call.body.clientMessageId, status: 'Completed' }, replyMessageId: 'acm_2', conversation: { id: 'acv_1', title: null } }],
          ['reply.started', { replyMessageId: 'acm_2' }],
          ['tool.started', { toolCallId: 'atc_1', toolName: 'searchLibrary', label: 'Searching the library', round: 1 }],
          ['tool.completed', { toolCallId: 'atc_1', toolName: 'searchLibrary', label: 'Searching the library', completedLabel: 'Searched the library', status: 'Succeeded', resultCount: 1, durationMs: 30, round: 1, results: toolCall.results }],
          ['reply.delta', { text: 'Try First steps.' }],
          ['reply.completed', { reply, remainingToday: 4 }]
        ]);
      }
      return undefined;
    });
    await renderMember(<AssistantChatView />, { fetchImpl, path: '/app/assistants/ast_1', routePath: '/app/assistants/:id', extraRoutes: <Route path='/app/assistants/:id/c/:conversationId' element={<AssistantChatView />} />, features: { ai: true } });
    await userEvent.type(await screen.findByLabelText(strings.ai.composerLabel), 'Where do I start?');
    await userEvent.click(screen.getByRole('button', { name: strings.ai.send }));
    expect(await screen.findByText('Try First steps.')).toBeInTheDocument();
    expect(await screen.findByText('Searched the library')).toBeInTheDocument();
    await userEvent.click(screen.getByText('Searched the library'));
    expect(screen.getByRole('link', { name: 'First steps' })).toHaveAttribute('href', '/app/lessons/les_1');
  });

  it('edits tools on an assistant, saves them, and flips a property-wide switch', async () => {
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/admin/ai/assistants/enumerate') return json(enumPage([makeAssistant('ast_1', { tools: { enabled: true, allowed: ['findGyms'], maxToolIterations: 4, maxToolCallsPerTurn: 6, toolCallTimeoutMs: 15000, maxToolOutputCharacters: 8000, maxTokensPerTurn: 32000, showToolActivity: true } })]));
      if (call.path === '/api/v1/admin/ai/endpoints/enumerate') return json(enumPage([makeEndpoint('mep_1'), makeEndpoint('mep_2', { name: 'Router' })]));
      if (call.path === '/api/v1/admin/ai/tools/enumerate') return json(enumPage(registry));
      if (call.method === 'PUT' && call.path === '/api/v1/admin/ai/tools/searchLibrary') return json({ ...registry[0], disabled: true, disabledReason: 'Rebuilding' });
      if (call.method === 'PUT' && call.path === '/api/v1/admin/ai/assistants/ast_1') {
        if (call.body.tools.maxToolIterations === 4) return json({ error: 'ValidationFailed', message: 'Validation failed.', fieldErrors: { 'tools.allowed': 'Unknown tool.' } }, 400);
        return json(makeAssistant('ast_1'));
      }
      return undefined;
    });
    renderAdmin(<AiAssistantsView />, { fetchImpl });
    await userEvent.click(await screen.findByText('Helper'));
    await userEvent.click(screen.getByRole('tab', { name: as.tabs.models }));
    await userEvent.selectOptions(screen.getByLabelText(as.purposes.toolRouting), 'mep_2');
    await userEvent.click(screen.getByRole('tab', { name: as.tabs.tools }));
    const box = await screen.findByTestId('ai-tool-registry');
    // The alias is checked through its tool; there is no separate alias checkbox.
    expect(within(box).getAllByRole('checkbox')).toHaveLength(2);
    expect(within(box).getByRole('checkbox', { name: /Finding places/ })).toBeChecked();
    expect(within(box).queryByRole('checkbox', { name: /findGyms/ })).toBeNull();
    expect(within(box).getByText(tl.worksSignedOut)).toBeInTheDocument();
    expect(within(box).getByText(tl.disabledBadge)).toBeInTheDocument();
    await userEvent.click(within(box).getByRole('checkbox', { name: /Searching the library/ }));
    await userEvent.click(screen.getByRole('button', { name: as.save }));
    expect(await screen.findByText('Unknown tool.')).toBeInTheDocument();
    const first = calls.find((c) => c.method === 'PUT' && c.path === '/api/v1/admin/ai/assistants/ast_1').body;
    expect(first.tools.allowed).toEqual(['findVenues', 'searchLibrary']);
    expect(first.models.toolRouting).toBe('mep_2');
    const rounds = screen.getByLabelText(tl.maxToolIterations);
    await userEvent.clear(rounds);
    await userEvent.type(rounds, '20');
    await userEvent.click(screen.getByRole('button', { name: as.save }));
    expect(await screen.findByText(tl.checkLimits)).toBeInTheDocument();
    await userEvent.clear(rounds);
    await userEvent.type(rounds, '5');
    await userEvent.click(screen.getByRole('button', { name: as.save }));
    await waitFor(() => expect(calls.filter((c) => c.method === 'PUT' && c.path === '/api/v1/admin/ai/assistants/ast_1')).toHaveLength(2));
    expect(calls.find((c) => c.path === '/api/v1/admin/ai/tools/enumerate')).toBeTruthy();
  });

  it('turns a tool off property-wide with a reason', async () => {
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/admin/ai/assistants/enumerate') return json(enumPage([makeAssistant('ast_1')]));
      if (call.path === '/api/v1/admin/ai/endpoints/enumerate') return json(enumPage([makeEndpoint('mep_1')]));
      if (call.path === '/api/v1/admin/ai/tools/enumerate') return json(enumPage(registry));
      if (call.method === 'PUT' && call.path === '/api/v1/admin/ai/tools/searchLibrary') return json({ ...registry[0], disabled: true, disabledReason: 'Rebuilding' });
      return undefined;
    });
    renderAdmin(<AiAssistantsView />, { fetchImpl });
    await userEvent.click(await screen.findByText('Helper'));
    await userEvent.click(screen.getByRole('tab', { name: as.tabs.tools }));
    const switches = await screen.findByTestId('ai-tool-kill-switches');
    await userEvent.click(await within(switches).findByRole('button', { name: tl.disable + ' Searching the library' }));
    await userEvent.type(screen.getByLabelText(tl.reason), 'Rebuilding');
    const dialog = screen.getByRole('dialog', { name: tl.disableTitle('Searching the library') });
    await userEvent.click(within(dialog).getByRole('button', { name: tl.disable }));
    await waitFor(() => expect(calls.some((c) => c.method === 'PUT' && c.path === '/api/v1/admin/ai/tools/searchLibrary')).toBe(true));
    expect(calls.find((c) => c.path === '/api/v1/admin/ai/tools/searchLibrary').body).toEqual({ disabled: true, reason: 'Rebuilding' });
    expect(await within(switches).findByRole('button', { name: tl.enable + ' Searching the library' })).toBeInTheDocument();
  });

  it('sets the tool calling mode of an endpoint and shows tool calls in a conversation', async () => {
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/admin/ai/endpoints/enumerate') return json(enumPage([makeEndpoint('mep_1')]));
      if (call.method === 'PUT' && call.path === '/api/v1/admin/ai/endpoints/mep_1') return json(makeEndpoint('mep_1'));
      if (call.path === '/api/v1/admin/ai/endpoints/mep_1/models/enumerate') return json(enumPage([]));
      return undefined;
    });
    const view = renderAdmin(<AiEndpointsView />, { fetchImpl });
    await userEvent.click(await screen.findByText('Local mep_1'));
    await userEvent.selectOptions(screen.getByLabelText(ep.toolCalling), 'Prompt');
    expect(screen.getByText(ep.toolCallingHints.Prompt)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: ep.save }));
    await waitFor(() => expect(calls.some((c) => c.method === 'PUT')).toBe(true));
    expect(calls.find((c) => c.method === 'PUT').body.parameters.toolCalling).toBe('Prompt');
    view.unmount();

    render(<ToolCallTable calls={[{ id: 'atc_1', toolName: 'searchLibrary', round: 1, status: 'Failed', argumentsJson: '{"query":"guard"}', resultCount: 0, durationMs: 12, resultSummary: 'No results', resultCharacters: 10, error: 'Index offline' }]} />);
    const table = screen.getByTestId('ai-tool-calls');
    expect(within(table).getByText(cv.toolStatuses.Failed)).toBeInTheDocument();
    expect(within(table).getByText(/"query": "guard"/)).toBeInTheDocument();
    expect(within(table).getByText('Index offline')).toBeInTheDocument();
    expect(within(table).getByText(cv.toolCharacters(10))).toBeInTheDocument();
  });
});
