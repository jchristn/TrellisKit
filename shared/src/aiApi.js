/**
 * AI assistants API methods (docs/AI_CONTRACT.md section 20), mixed into `ApiClient.prototype`.
 *
 * Member and visitor chat: assistants, conversations, and messages. Sending returns an async
 * iterator of the section 10.2 events, read from the SSE stream (`fetch` with a body reader; set
 * `client.streamFetchImpl` to `expo/fetch` on mobile) or synthesized from the JSON reply when
 * streaming is off. Visitors (Public assistants signed out) send `X-TK-Visitor-Token`: set
 * `client.visitorToken` (see `createVisitorTokenStore`). Admin methods cover model endpoints,
 * assistants, knowledge, analytics, conversations, and feedback.
 */

import { ApiError, parseRetryAfter } from './ApiError.js';
import { ENUMERATION_MAX_RESULTS_ADMIN, toEnumerationBody } from './enumeration.js';
import { readSseEvents } from './sse.js';

const enc = encodeURIComponent;
const ADMIN = { maxLimit: ENUMERATION_MAX_RESULTS_ADMIN };

/** Header carrying the visitor token of public assistants. */
export const VISITOR_TOKEN_HEADER = 'X-TK-Visitor-Token';

function body(query, limits) {
  return toEnumerationBody(query || {}, limits);
}

function visitorHeaders(client) {
  return client && client.visitorToken ? { [VISITOR_TOKEN_HEADER]: client.visitorToken } : {};
}

function options(client, signal) {
  return { signal, headers: visitorHeaders(client) };
}

async function errorFrom(response) {
  const type = (response.headers && response.headers.get('content-type')) || '';
  const parsed = type.includes('application/json') ? await response.json().catch(() => null) : await response.text().catch(() => null);
  const message = (parsed && typeof parsed === 'object' && parsed.message) || ('Request failed with status ' + response.status + '.');
  const code = (parsed && typeof parsed === 'object' && parsed.error) || null;
  return new ApiError(message, { status: response.status, code, body: parsed, retryAfterSeconds: parseRetryAfter(response.headers && response.headers.get('retry-after')) });
}

/**
 * Events equivalent to a non-streaming `{ userMessage, reply }` result. The server sends no
 * conversation object there, so `message.created` carries `{ id }` from the messages'
 * `conversationId` (callers learn the conversation from it, as from the streamed event).
 */
export function exchangeEvents(exchange) {
  const events = [];
  if (!exchange || !exchange.reply) return events;
  const conversationId = exchange.reply.conversationId || (exchange.userMessage && exchange.userMessage.conversationId) || null;
  events.push({ event: 'message.created', id: null, data: { userMessage: exchange.userMessage, replyMessageId: exchange.reply.id, conversation: conversationId ? { id: conversationId } : null } });
  const reply = exchange.reply;
  if (reply.status === 'Completed' || reply.status === 'Blocked') {
    if (reply.content && reply.status === 'Completed') events.push({ event: 'reply.delta', id: null, data: { text: reply.content } });
    events.push({ event: reply.status === 'Blocked' ? 'reply.blocked' : 'reply.completed', id: null, data: { reply } });
  } else {
    events.push({ event: 'error', id: null, data: { error: reply.errorCode || 'ModelError', message: null, replyMessageId: reply.id } });
  }
  return events;
}

/**
 * POST a chat request and yield its events (SSE, or JSON converted with `exchangeEvents`).
 * @this {import('./ApiClient.js').ApiClient}
 */
async function* streamRequest(client, path, payload, { signal, stream = true } = {}) {
  const fetchImpl = client.streamFetchImpl || client.fetchImpl;
  const send = async () => {
    const headers = {
      Accept: stream ? 'text/event-stream' : 'application/json',
      'Content-Type': 'application/json',
      ...(await client.baseHeaders()),
      ...visitorHeaders(client)
    };
    try {
      return await fetchImpl(client.url(path), { method: 'POST', headers, body: JSON.stringify(payload || {}), signal, credentials: client.credentials });
    } catch (err) {
      const aborted = err && err.name === 'AbortError';
      throw new ApiError(aborted ? 'The request was cancelled.' : 'Unable to reach the server.', { status: 0 });
    }
  };
  let response = await send();
  if (response.status === 401 && client.onUnauthorized) {
    let retry = false;
    try {
      retry = await client.onUnauthorized({ method: 'POST', path, error: new ApiError('Unauthorized', { status: 401 }) });
    } catch {
      retry = false;
    }
    if (retry) response = await send();
  }
  if (!response.ok) throw await errorFrom(response);
  const type = (response.headers && response.headers.get('content-type')) || '';
  if (!type.includes('text/event-stream')) {
    const json = await response.json().catch(() => null);
    if (json && json.reply) {
      for (const event of exchangeEvents(json)) yield event;
    } else {
      yield { event: 'conversation.created', id: null, data: json };
    }
    return;
  }
  for await (const event of readSseEvents(response, signal)) yield event;
}

/** @this {import('./ApiClient.js').ApiClient} */
export const aiMethods = {
  // ---- Member and visitor chat (section 6) ----

  /** POST /assistants/enumerate: assistants the caller may use. */
  enumerateAssistants(query = {}, signal) { return this.post('/assistants/enumerate', body(query), options(this, signal)); },
  /** GET /assistants/{id}: AssistantSummary with remaining messages today and the privacy disclosure. */
  getAssistant(id, signal) { return this.get('/assistants/' + enc(id), options(this, signal)); },
  /** POST /assistant-visitors: a visitor session `{ visitorId, visitorToken }` for Public assistants. */
  createVisitorSession() { return this.post('/assistant-visitors', {}, { retryOnUnauthorized: false }); },
  /** POST /assistants/{id}/conversations without a message: 201 Conversation. */
  createConversation(assistantId) { return this.post('/assistants/' + enc(assistantId) + '/conversations', {}, options(this)); },
  /** POST /assistants/{id}/conversations with a first message: an async iterator of chat events. */
  startConversation(assistantId, { message, clientMessageId } = {}, { signal, stream = true } = {}) {
    return streamRequest(this, '/assistants/' + enc(assistantId) + '/conversations', { message, clientMessageId }, { signal, stream });
  },
  /** POST /assistants/{id}/conversations/enumerate: the caller's conversations with one assistant. */
  enumerateAssistantConversations(assistantId, query = {}, signal) { return this.post('/assistants/' + enc(assistantId) + '/conversations/enumerate', body(query), options(this, signal)); },
  /** POST /assistant-conversations/enumerate: the caller's conversations (filters.assistantId, filters.archived). */
  enumerateConversations(query = {}, signal) { return this.post('/assistant-conversations/enumerate', body(query), options(this, signal)); },
  /** GET /assistant-conversations/{id}: with recentMessages. */
  getConversation(id, signal) { return this.get('/assistant-conversations/' + enc(id), options(this, signal)); },
  /** PUT /assistant-conversations/{id}: `{ title?, isArchived?, labels?, tags? }`. */
  updateConversation(id, changes) { return this.put('/assistant-conversations/' + enc(id), changes, options(this)); },
  /** DELETE /assistant-conversations/{id}. */
  deleteConversation(id) { return this.delete('/assistant-conversations/' + enc(id), options(this)); },
  /** POST /assistant-conversations/{id}/messages: an async iterator of chat events (JSON when `stream` is false). */
  sendAssistantMessage(conversationId, { content, clientMessageId } = {}, { signal, stream = true } = {}) {
    return streamRequest(this, '/assistant-conversations/' + enc(conversationId) + '/messages', { content, clientMessageId }, { signal, stream });
  },
  /** POST /assistant-conversations/{id}/messages/enumerate: oldest first by default. */
  enumerateConversationMessages(conversationId, query = {}, signal) { return this.post('/assistant-conversations/' + enc(conversationId) + '/messages/enumerate', body(query), options(this, signal)); },
  /** GET /assistant-conversations/{id}/messages/{messageId}: poll after a dropped stream. */
  getConversationMessage(conversationId, messageId, signal) { return this.get('/assistant-conversations/' + enc(conversationId) + '/messages/' + enc(messageId), options(this, signal)); },
  /** POST .../messages/{messageId}/cancel. */
  cancelAssistantMessage(conversationId, messageId) { return this.post('/assistant-conversations/' + enc(conversationId) + '/messages/' + enc(messageId) + '/cancel', {}, options(this)); },
  /** POST .../messages/{messageId}/regenerate: an async iterator of chat events. */
  regenerateAssistantMessage(conversationId, messageId, { signal, stream = true } = {}) {
    return streamRequest(this, '/assistant-conversations/' + enc(conversationId) + '/messages/' + enc(messageId) + '/regenerate', {}, { signal, stream });
  },
  /** PUT .../messages/{messageId}/feedback with `{ rating, note?, reasons? }`. */
  setMessageFeedback(conversationId, messageId, feedback) { return this.put('/assistant-conversations/' + enc(conversationId) + '/messages/' + enc(messageId) + '/feedback', feedback, options(this)); },
  /** DELETE .../messages/{messageId}/feedback. */
  clearMessageFeedback(conversationId, messageId) { return this.delete('/assistant-conversations/' + enc(conversationId) + '/messages/' + enc(messageId) + '/feedback', options(this)); },

  // ---- Admin: model endpoints (section 1.3) ----

  /** POST /admin/ai/roles/enumerate: task role page with executor hierarchies. */
  adminListRoles(query = {}, signal) { return this.post('/admin/ai/roles/enumerate', body(query, ADMIN), { signal }); },
  /** GET /admin/ai/roles/{role}. */
  adminGetRole(role, signal) { return this.get('/admin/ai/roles/' + enc(role), { signal }); },
  /** PUT /admin/ai/roles/{role} with `{ reasoningEffort, availabilityPolicy, exhaustionPolicy, executors }`. */
  adminSaveRole(role, config) { return this.put('/admin/ai/roles/' + enc(role), config || {}); },
  /** POST /admin/ai/roles/{role}/test with `{ prompt }`. */
  adminTestRole(role, prompt) { return this.post('/admin/ai/roles/' + enc(role) + '/test', { prompt }); },
  /** POST /admin/ai/calls/enumerate: the per-call ledger with each call's telemetry (filters role, purpose, outcome, endpointId, jobKey, jobRunId, assistantId). */
  adminEnumerateCalls(query = {}, signal) { return this.post('/admin/ai/calls/enumerate', body(query, ADMIN), { signal }); },
  /** POST /admin/ai/harnesses/enumerate: harness profile page; credentials are never returned. */
  adminListHarnesses(query = {}, signal) { return this.post('/admin/ai/harnesses/enumerate', body(query, ADMIN), { signal }); },
  /** GET /admin/ai/harnesses/{runtime}. */
  adminGetHarness(runtime, signal) { return this.get('/admin/ai/harnesses/' + enc(runtime), { signal }); },
  /**
   * PUT /admin/ai/harnesses/{runtime} with `{ model, permissionMode, timeoutSeconds, maxToolCalls, allowWebResearch, baseUrl, credentialType, credential? }`.
   * The profile is replaced; `credential` is write-only (omit it to keep the stored value).
   */
  adminSaveHarness(runtime, profile) { return this.put('/admin/ai/harnesses/' + enc(runtime), profile || {}); },
  /** POST /admin/ai/harnesses/{runtime}/reveal with `{ reason }` (recent sign-in required). */
  revealHarnessCredential(runtime, { reason }) { return this.post('/admin/ai/harnesses/' + enc(runtime) + '/reveal', { reason }, { retryOnUnauthorized: false }); },
  /** POST /admin/ai/prompts/enumerate: administrator-editable prompt page. */
  adminListPrompts(query = {}, signal) { return this.post('/admin/ai/prompts/enumerate', body(query, ADMIN), { signal }); },
  /** GET /admin/ai/prompts/{key}. */
  adminGetPrompt(key, signal) { return this.get('/admin/ai/prompts/' + enc(key), { signal }); },
  /** PUT /admin/ai/prompts/{key} with `{ content }` (saves a draft; the live text changes only when the draft is published). */
  adminSavePrompt(key, content) { return this.put('/admin/ai/prompts/' + enc(key), { content }); },
  /** POST /admin/ai/prompts/{key}/benchmark with `{ dryRun?, assistantId?, replyRole? }`: queue the scope benchmark of the pending draft; returns the JobRun. */
  adminBenchmarkPrompt(key, options = {}) { return this.post('/admin/ai/prompts/' + enc(key) + '/benchmark', options); },
  /** POST /admin/ai/prompts/{key}/publish with `{ versionId? }`: make the draft live through the scope benchmark gate. */
  adminPublishPrompt(key, versionId) { return this.post('/admin/ai/prompts/' + enc(key) + '/publish', versionId ? { versionId } : {}); },
  /** POST /admin/ai/prompts/{key}/reset (back to the property or engine default). */
  adminResetPrompt(key) { return this.post('/admin/ai/prompts/' + enc(key) + '/reset', {}); },
  /** POST /admin/ai/prompts/{key}/versions/enumerate (newest first). */
  adminEnumeratePromptVersions(key, query = {}, signal) { return this.post('/admin/ai/prompts/' + enc(key) + '/versions/enumerate', body(query, ADMIN), { signal }); },
  /** POST /admin/ai/endpoints/enumerate. */
  adminEnumerateEndpoints(query = {}, signal) { return this.post('/admin/ai/endpoints/enumerate', body(query, ADMIN), { signal }); },
  /** GET /admin/ai/endpoints/{id}. */
  adminGetEndpoint(id, signal) { return this.get('/admin/ai/endpoints/' + enc(id), { signal }); },
  /** POST /admin/ai/endpoints. */
  adminCreateEndpoint(endpoint) { return this.post('/admin/ai/endpoints', endpoint); },
  /** PUT /admin/ai/endpoints/{id} (partial; secrets are write-only). */
  adminUpdateEndpoint(id, changes) { return this.put('/admin/ai/endpoints/' + enc(id), changes); },
  /** DELETE /admin/ai/endpoints/{id}. */
  adminDeleteEndpoint(id) { return this.delete('/admin/ai/endpoints/' + enc(id)); },
  /** POST /admin/ai/endpoints/validate: test an unsaved body. */
  validateEndpoint(endpoint) { return this.post('/admin/ai/endpoints/validate', endpoint); },
  /** POST /admin/ai/endpoints/{id}/test with `{ prompt?, maxTokens?, input?, query?, documents? }`. */
  testEndpoint(id, input = {}) { return this.post('/admin/ai/endpoints/' + enc(id) + '/test', input); },
  /** POST /admin/ai/endpoints/{id}/models/enumerate: provider models. */
  enumerateEndpointModels(id, query = {}, signal) { return this.post('/admin/ai/endpoints/' + enc(id) + '/models/enumerate', body(query, ADMIN), { signal }); },
  /** GET /admin/ai/endpoints/{id}/health. */
  getEndpointHealth(id, signal) { return this.get('/admin/ai/endpoints/' + enc(id) + '/health', { signal }); },
  /** POST /admin/ai/endpoints/{id}/health/enumerate. */
  enumerateEndpointHealth(id, query = {}, signal) { return this.post('/admin/ai/endpoints/' + enc(id) + '/health/enumerate', body(query, ADMIN), { signal }); },
  /** POST /admin/ai/endpoints/{id}/health/check. */
  checkEndpointHealth(id) { return this.post('/admin/ai/endpoints/' + enc(id) + '/health/check', {}); },
  /** POST /admin/ai/endpoints/{id}/duplicate with `{ name?, includeSecrets? }`. */
  duplicateEndpoint(id, { name, includeSecrets = false } = {}) { return this.post('/admin/ai/endpoints/' + enc(id) + '/duplicate', { name, includeSecrets }); },
  /** POST /admin/ai/endpoints/{id}/reveal with `{ field, headerName?, reason }` (recent sign-in required). */
  revealEndpointSecret(id, { field, headerName, reason }) { return this.post('/admin/ai/endpoints/' + enc(id) + '/reveal', { field, headerName, reason }, { retryOnUnauthorized: false }); },
  /** POST /admin/ai/endpoints/{id}/usage/enumerate. */
  enumerateEndpointUsage(id, query = {}, signal) { return this.post('/admin/ai/endpoints/' + enc(id) + '/usage/enumerate', body(query, ADMIN), { signal }); },
  /** POST /admin/ai/endpoints/reencrypt. */
  reencryptEndpointSecrets() { return this.post('/admin/ai/endpoints/reencrypt', {}); },

  // ---- Admin: assistants and knowledge (sections 5 and 7) ----

  /** POST /admin/ai/assistants/enumerate. */
  adminEnumerateAssistants(query = {}, signal) { return this.post('/admin/ai/assistants/enumerate', body(query, ADMIN), { signal }); },
  /** GET /admin/ai/assistants/{id}. */
  adminGetAssistant(id, signal) { return this.get('/admin/ai/assistants/' + enc(id), { signal }); },
  /** POST /admin/ai/assistants. */
  adminCreateAssistant(assistant) { return this.post('/admin/ai/assistants', assistant); },
  /** PUT /admin/ai/assistants/{id}. */
  adminUpdateAssistant(id, changes) { return this.put('/admin/ai/assistants/' + enc(id), changes); },
  /** DELETE /admin/ai/assistants/{id}. */
  adminDeleteAssistant(id) { return this.delete('/admin/ai/assistants/' + enc(id)); },
  /** POST /admin/ai/assistants/{id}/duplicate. */
  duplicateAssistant(id, name) { return this.post('/admin/ai/assistants/' + enc(id) + '/duplicate', { name }); },
  /** POST /admin/ai/assistants/{id}/test: an async iterator of chat and debug events. */
  testAssistant(id, { messages, useDraft } = {}, { signal, stream = true } = {}) {
    return streamRequest(this, '/admin/ai/assistants/' + enc(id) + '/test', { messages, useDraft }, { signal, stream });
  },
  /** POST /admin/ai/assistants/{id}/knowledge/preview/enumerate. Filters: query, optional asUserId. */
  previewAssistantKnowledge(id, query = {}, signal) { return this.post('/admin/ai/assistants/' + enc(id) + '/knowledge/preview/enumerate', toEnumerationBody(query), { signal }); },
  /** GET /admin/ai/assistants/{id}/knowledge/status. */
  getAssistantKnowledgeStatus(id, signal) { return this.get('/admin/ai/assistants/' + enc(id) + '/knowledge/status', { signal }); },
  /** POST /admin/ai/assistants/{id}/knowledge/reindex. */
  reindexAssistantKnowledge(id) { return this.post('/admin/ai/assistants/' + enc(id) + '/knowledge/reindex', {}); },
  /** POST /admin/ai/assistants/{id}/knowledge/items/enumerate (filters status, targetType). */
  enumerateAssistantKnowledgeItems(id, query = {}, signal) { return this.post('/admin/ai/assistants/' + enc(id) + '/knowledge/items/enumerate', body(query, ADMIN), { signal }); },
  /** POST /admin/ai/tools/enumerate: the assistant tool registry (filter `disabled` true or false). */
  enumerateAssistantTools(query = {}, signal) { return this.post('/admin/ai/tools/enumerate', body(query, ADMIN), { signal }); },
  /** PUT /admin/ai/tools/{name} with `{ disabled, reason? }`: the property-wide kill switch of one tool (audited). */
  updateAssistantTool(name, changes) { return this.put('/admin/ai/tools/' + enc(name), changes); },

  // ---- Admin: analytics, cost, conversations, feedback (section 14) ----

  /** GET /admin/ai/analytics/overview. */
  aiOverview({ days = 30, assistantId, endpointId } = {}, signal) { return this.get('/admin/ai/analytics/overview', { query: { days, assistantId, endpointId }, signal }); },
  /** GET /admin/ai/analytics/timeseries. */
  aiTimeseries(query = {}, signal) {
    return this.post('/admin/ai/analytics/timeseries/enumerate', body(query, ADMIN), { signal });
  },
  /** POST /admin/ai/analytics/endpoints/enumerate (filters.windowDays). */
  enumerateAiEndpointAnalytics(query = {}, signal) { return this.post('/admin/ai/analytics/endpoints/enumerate', body(query, ADMIN), { signal }); },
  /** POST /admin/ai/analytics/assistants/enumerate (filters.windowDays). */
  enumerateAiAssistantAnalytics(query = {}, signal) { return this.post('/admin/ai/analytics/assistants/enumerate', body(query, ADMIN), { signal }); },
  /** POST /admin/ai/analytics/members/enumerate: cost by member (filters.windowDays, assistantId, subjectType). */
  enumerateAiMemberAnalytics(query = {}, signal) { return this.post('/admin/ai/analytics/members/enumerate', body(query, ADMIN), { signal }); },
  /** POST /admin/ai/conversations/enumerate: every conversation (filters assistantId, userId, visitorId, ownerType, status, archived, surface). */
  adminEnumerateConversations(query = {}, signal) { return this.post('/admin/ai/conversations/enumerate', body(query, ADMIN), { signal }); },
  /** GET /admin/ai/conversations/{id}: audited. */
  adminGetConversation(id, signal) { return this.get('/admin/ai/conversations/' + enc(id), { signal }); },
  /** POST /admin/ai/conversations/{id}/messages/enumerate: messages with telemetry. */
  adminEnumerateConversationMessages(id, query = {}, signal) { return this.post('/admin/ai/conversations/' + enc(id) + '/messages/enumerate', body(query, ADMIN), { signal }); },
  /** POST /admin/ai/feedback/enumerate. */
  enumerateAiFeedback(query = {}, signal) { return this.post('/admin/ai/feedback/enumerate', body(query, ADMIN), { signal }); },
  /** POST /admin/ai/feedback/{id}/review. */
  reviewAiFeedback(id, note) { return this.post('/admin/ai/feedback/' + enc(id) + '/review', { note }); }
};

/**
 * A visitor token store for the public chat page and the widget: localStorage when available, else
 * memory.
 * @param {{ getItem: Function, setItem: Function, removeItem: Function }|null} [storage]
 * @param {string} [key]
 */
export function createVisitorTokenStore(storage, key = 'tk_visitor_token') {
  let memory = null;
  return {
    get() {
      try {
        return (storage && storage.getItem(key)) || memory;
      } catch {
        return memory;
      }
    },
    set(token) {
      memory = token || null;
      try {
        if (storage) {
          if (token) storage.setItem(key, token);
          else storage.removeItem(key);
        }
      } catch {
        // Storage can be unavailable (private mode); memory keeps the token for this page.
      }
    }
  };
}
