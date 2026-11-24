/**
 * Client-side chat state for assistant conversations (docs/AI_CONTRACT.md sections 9 and 10), shared
 * by the website and the mobile app: a reducer that applies the section 10.2 stream events to a
 * transcript, helpers for client message ids and dropped-stream polling, and the `features.ai`
 * capability checks.
 */

import { hasServerCapability } from './property.js';
import { parseMarkdown } from './markdown.js';

/** Statuses after which a reply no longer changes. */
export const FINAL_MESSAGE_STATUSES = Object.freeze(['Completed', 'Failed', 'Cancelled', 'Blocked']);

/** Whether the server offers AI assistants (`features.ai === true`; missing means off). */
export function aiAvailable(property) {
  return hasServerCapability(property, 'ai');
}

/** Mobile placement of assistants: `Tab` when the server promotes them, otherwise `Menu`. */
export function aiPlacement(property) {
  const features = (property && property.features) || {};
  return features.aiPlacement === 'Tab' ? 'Tab' : 'Menu';
}

/** Whether a message status is final. */
export function isFinalStatus(status) {
  return FINAL_MESSAGE_STATUSES.includes(status);
}

/** A random UUID for `clientMessageId` (idempotent sends). */
export function newClientMessageId() {
  const cryptoObject = globalThis.crypto;
  if (cryptoObject && typeof cryptoObject.randomUUID === 'function') return cryptoObject.randomUUID();
  const hex = [];
  for (let i = 0; i < 32; i += 1) hex.push(Math.floor(Math.random() * 16).toString(16));
  hex[12] = '4';
  hex[16] = ((parseInt(hex[16], 16) & 0x3) | 0x8).toString(16);
  const s = hex.join('');
  return s.slice(0, 8) + '-' + s.slice(8, 12) + '-' + s.slice(12, 16) + '-' + s.slice(16, 20) + '-' + s.slice(20);
}

/**
 * A new transcript state.
 * @param {object[]} [messages] Messages already loaded, oldest first.
 */
export function createChatState(messages = []) {
  return { messages: messages.slice(), streaming: false, replyId: null, error: null, remainingToday: null, conversation: null, toolActivity: [] };
}

/** Add the member's message optimistically before the stream starts. */
export function addPendingUserMessage(state, content, clientMessageId) {
  const message = { id: 'pending_' + clientMessageId, clientMessageId, role: 'User', content, status: 'Completed', isAiGenerated: false, citations: [], createdUtc: new Date().toISOString(), pending: true };
  return { ...state, messages: state.messages.concat([message]), streaming: true, error: null };
}

function replaceMessage(messages, id, update) {
  let found = false;
  const next = messages.map((m) => {
    if (m.id !== id) return m;
    found = true;
    return typeof update === 'function' ? update(m) : update;
  });
  return found ? next : next.concat([typeof update === 'function' ? update({ id }) : update]);
}

/**
 * Apply one stream event to a transcript state (pure).
 * @param {object} state
 * @param {{ event: string, data: any }} item
 * @returns {object} The next state.
 */
export function applyChatEvent(state, item) {
  const data = (item && item.data) || {};
  switch (item && item.event) {
    case 'message.created': {
      const user = data.userMessage;
      let messages = state.messages.filter((m) => !(m.pending && user && m.clientMessageId && m.clientMessageId === user.clientMessageId));
      messages = messages.filter((m) => !m.pending || !user || m.content !== user.content);
      if (user) messages = replaceMessage(messages, user.id, user);
      if (data.replyMessageId) {
        messages = replaceMessage(messages, data.replyMessageId, (m) => ({ ...m, id: data.replyMessageId, role: 'Assistant', content: m.content || '', status: 'Streaming', isAiGenerated: true, citations: m.citations || [] }));
      }
      return { ...state, messages, streaming: true, replyId: data.replyMessageId || null, conversation: data.conversation || state.conversation, error: null };
    }
    case 'reply.started':
      return { ...state, streaming: true, replyId: data.replyMessageId || state.replyId };
    case 'reply.delta':
      if (!state.replyId) return state;
      return { ...state, messages: replaceMessage(state.messages, state.replyId, (m) => ({ ...m, content: (m.content || '') + (data.text || '') })) };
    case 'reply.reasoning':
      if (!state.replyId) return state;
      return { ...state, messages: replaceMessage(state.messages, state.replyId, (m) => ({ ...m, reasoning: (m.reasoning || '') + (data.text || '') })) };
    case 'reply.citations':
      if (!state.replyId) return state;
      return { ...state, messages: replaceMessage(state.messages, state.replyId, (m) => ({ ...m, citations: data.citations || [] })) };
    case 'tool.started':
    case 'tool.completed': {
      const toolActivity = (state.toolActivity || []).concat([{ event: item.event, ...data }]);
      const replyId = data.replyMessageId || state.replyId;
      if (!replyId || !data.toolCallId) return { ...state, toolActivity };
      const call = item.event === 'tool.started' ? runningToolCall(data) : completedToolCall(data);
      return { ...state, toolActivity, messages: replaceMessage(state.messages, replyId, (m) => upsertToolCall(m, call)) };
    }
    case 'reply.completed':
    case 'reply.blocked': {
      const reply = data.reply;
      const id = (reply && reply.id) || state.replyId;
      // The stored reply is authoritative, its tool calls included; a reply without the field
      // keeps the calls the stream showed.
      const messages = reply ? replaceMessage(state.messages, id, (m) => (reply.toolCalls === undefined && m.toolCalls ? { ...reply, toolCalls: m.toolCalls } : reply)) : state.messages;
      return { ...state, messages, streaming: false, replyId: null, remainingToday: data.remainingToday === undefined ? state.remainingToday : data.remainingToday, blocked: item.event === 'reply.blocked' };
    }
    case 'error': {
      const id = data.replyMessageId || state.replyId;
      const messages = id ? replaceMessage(state.messages, id, (m) => ({ ...m, role: 'Assistant', isAiGenerated: true, status: data.error === 'Cancelled' ? 'Cancelled' : 'Failed', errorCode: data.error || 'ModelError' })) : state.messages;
      return { ...state, messages, streaming: false, replyId: null, error: { code: data.error || 'ModelError', message: data.message || null } };
    }
    default:
      return state;
  }
}

/** Tool call statuses that mean the call did not produce results. */
export const TOOL_FAILURE_STATUSES = Object.freeze(['Failed', 'TimedOut', 'Rejected']);

/** Status of a tool call the stream reported as started and not yet completed. */
export const TOOL_RUNNING_STATUS = 'Running';

/** Most tool calls a reply carries (the per-turn cap). */
export const MAX_TOOL_CALLS_PER_REPLY = 12;

/** Most result links one tool call carries. */
export const MAX_TOOL_RESULTS = 10;

function runningToolCall(data) {
  return { id: data.toolCallId, toolName: data.toolName || null, label: data.label || null, completedLabel: null, status: TOOL_RUNNING_STATUS, resultCount: null, durationMs: null, round: data.round ?? null, results: [] };
}

function completedToolCall(data) {
  const call = { id: data.toolCallId, toolName: data.toolName || null, status: data.status || 'Succeeded', resultCount: data.resultCount ?? null, durationMs: data.durationMs ?? null, results: Array.isArray(data.results) ? data.results.slice(0, MAX_TOOL_RESULTS) : [] };
  if (data.label) call.label = data.label;
  if (data.completedLabel) call.completedLabel = data.completedLabel;
  if (data.round !== undefined && data.round !== null) call.round = data.round;
  return call;
}

/** The tool calls of a message (`message.toolCalls.objects`), or an empty list. */
export function messageToolCalls(message) {
  const calls = message && message.toolCalls;
  return calls && Array.isArray(calls.objects) ? calls.objects : [];
}

function upsertToolCall(message, call) {
  const list = messageToolCalls(message);
  const index = list.findIndex((c) => c.id === call.id);
  let next;
  if (index >= 0) next = list.map((c, i) => (i === index ? { ...c, ...call } : c));
  else if (list.length >= MAX_TOOL_CALLS_PER_REPLY) return message;
  else next = list.concat([{ label: null, completedLabel: null, round: null, ...call }]);
  return { ...message, toolCalls: { objects: next, totalRecords: next.length } };
}

/**
 * Whether a tool result path is an app path a client can open in place: one of the known sections
 * followed by one id segment (`/lessons/les_x`, `/events/evt_x`, `/venues/ven_x`, `/feed/fpo_x`,
 * `/threads/thr_x`). Anything else (absolute URLs, protocol-relative paths, unknown sections)
 * returns null.
 * @param {string} path
 * @returns {string|null}
 */
export function toolResultPath(path) {
  const value = typeof path === 'string' ? path.trim() : '';
  return /^\/(lessons|events|venues|feed|threads)\/[A-Za-z0-9_-]{1,100}$/.test(value) ? value : null;
}

/**
 * Compact display items for the tool calls of one reply (pure): the label while a call runs, the
 * completed label when it succeeded, and a failure label for Failed, TimedOut, and Rejected calls
 * (a call still running on a reply that is no longer streaming counts as failed). Results become
 * links with an app path (`path` null when the client cannot open it).
 * @param {object} message An assistant message (its `toolCalls.objects` and `status`).
 * @param {{ running?: (label: string) => string, failed?: (label: string, status: string) => string, fallbackLabel?: string }} [format]
 * @returns {{ id: string, toolName: string|null, state: 'running'|'done'|'failed', status: string, text: string, resultCount: number|null, durationMs: number|null, round: number|null, results: { key: string, title: string, path: string|null, targetType: string|null, targetId: string|null }[] }[]}
 */
export function toolCallDisplayItems(message, format = {}) {
  const running = format.running || ((label) => label + '...');
  const failed = format.failed || ((label) => label);
  const fallback = format.fallbackLabel || '';
  const streaming = Boolean(message) && message.status === 'Streaming';
  return messageToolCalls(message).slice(0, MAX_TOOL_CALLS_PER_REPLY).map((call, index) => {
    const label = call.label || call.completedLabel || call.toolName || fallback;
    let state = 'done';
    if (call.status === TOOL_RUNNING_STATUS) state = streaming ? 'running' : 'failed';
    else if (TOOL_FAILURE_STATUSES.includes(call.status)) state = 'failed';
    const text = state === 'running' ? running(label) : state === 'failed' ? failed(label, call.status) : (call.completedLabel || label);
    const results = (Array.isArray(call.results) ? call.results : []).slice(0, MAX_TOOL_RESULTS).filter((r) => r && (r.title || r.path)).map((r, i) => ({
      key: (r.targetType || '') + ':' + (r.targetId || i),
      title: r.title || r.path,
      path: toolResultPath(r.path),
      targetType: r.targetType || null,
      targetId: r.targetId || null
    }));
    return { id: call.id || 'tool_' + index, toolName: call.toolName || null, state, status: call.status || null, text, resultCount: call.resultCount ?? null, durationMs: call.durationMs ?? null, round: call.round ?? null, results: state === 'done' ? results : [] };
  });
}

/**
 * Poll a reply until its status is final (after a dropped stream, section 10.1).
 * @param {object} client ApiClient with `getConversationMessage`.
 * @param {string} conversationId
 * @param {string} messageId
 * @param {{ intervalMs?: number, maxAttempts?: number, sleep?: (ms: number) => Promise<void> }} [options]
 */
export async function pollReply(client, conversationId, messageId, { intervalMs = 2000, maxAttempts = 90, sleep } = {}) {
  const wait = sleep || ((ms) => new Promise((resolve) => setTimeout(resolve, ms)));
  for (let attempt = 0; attempt < maxAttempts; attempt += 1) {
    const message = await client.getConversationMessage(conversationId, messageId);
    if (message && isFinalStatus(message.status)) return message;
    await wait(intervalMs);
  }
  return null;
}

/**
 * Assistant-mode Markdown (section 9.2): fenced code blocks and inline code on top of the Phase 2
 * subset. Returns blocks where `{ type: 'code', lang, value }` sits between ordinary parsed blocks,
 * and inline `code` spans become `{ type: 'code', value }` inline nodes. Code is shown as monospaced
 * text, never executed or turned into HTML.
 * @param {string} text
 * @returns {{ type: 'root', children: object[] }}
 */
export function parseAssistantMarkdown(text) {
  // The Phase 2 parser now covers fenced and inline code (plus rules and tables), so assistant
  // replies use it directly.
  return parseMarkdown(String(text || ''), { allowHeadings: true });
}
