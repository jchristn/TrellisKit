/**
 * Framework-neutral stores for direct messages (docs/PHASE5_CONTRACT.md sections 3.3, 4.6, 5.2).
 *
 * `createMessageStore` holds one conversation: it merges REST pages, socket events, gap fills,
 * and optimistic sends by `id` and `seq`, renders in `seq` order (never arrival order), keys
 * optimistic messages by `clientMessageId`, replaces them with the `201` response, ignores the
 * echoed `message.created`, replaces by `id` on `message.updated`, and turns `message.deleted`
 * into a tombstone. `sendMessage` retries with the same `clientMessageId` and leaves a `Failed`
 * item the UI can retry or discard.
 *
 * `createConversationListStore` holds the conversation list: it merges enumeration pages, sync
 * changes (`Removed` drops), socket events, and unread counts.
 *
 * Both expose `subscribe(listener)` (returns an unsubscribe function) and `getSnapshot()` (a
 * stable object that changes only when the state changes), which fits React's
 * `useSyncExternalStore` and any other UI framework. No UI strings live here.
 */

import { collectEnumeration, normalizeEnumerationResult } from './enumeration.js';
import { newClientMessageId, sendMessageBody } from './messagingApi.js';

/** Send states of optimistic items. */
export const SendState = Object.freeze({ Sending: 'Sending', Failed: 'Failed' });

const DEFAULT_RETRY_DELAYS_MS = Object.freeze([1000, 3000]);

function createEmitter() {
  const listeners = new Set();
  return {
    subscribe(listener) {
      if (typeof listener !== 'function') return () => {};
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    emit(snapshot) {
      for (const fn of Array.from(listeners)) {
        try { fn(snapshot); } catch { /* listener errors never break the store */ }
      }
    }
  };
}

function isRetryable(error) {
  if (!error) return false;
  const status = Number(error.status) || 0;
  return status === 0 || status === 408 || status === 429 || status >= 500;
}

function errorInfo(error) {
  return { status: (error && error.status) || 0, code: (error && error.code) || null, message: (error && error.message) || null };
}

/** A tombstone of a message (contract 2.2: empty body, no attachments). */
export function tombstoneOf(message, extra = {}) {
  return { ...(message || {}), ...extra, state: 'Deleted', body: '', originalBody: null, attachments: [], linkPreview: null };
}

/** The conversation list preview of a message (`MessageSummary`, contract 2.2). */
export function messageSummaryOf(message) {
  if (!message) return null;
  const visible = message.state === 'Visible' || !message.state;
  return {
    id: message.id,
    seq: message.seq,
    authorUserId: (message.author && message.author.id) || message.authorUserId || null,
    excerpt: visible ? String(message.body || '').slice(0, 120) : '',
    hasAttachments: Array.isArray(message.attachments) && message.attachments.length > 0,
    state: message.state || 'Visible',
    createdUtc: message.createdUtc || null
  };
}

/**
 * A message store for one conversation.
 * @param {object} options
 * @param {string} options.conversationId
 * @param {object} [options.api] ApiClient (`enumerateMessages`, `sendMessage`)
 * @param {object|null} [options.currentUser] UserSummary of the caller (author of optimistic items)
 * @param {{ setTimeout?: Function, now?: () => number }} [options.timers]
 * @param {number[]} [options.retryDelaysMs] delays between send attempts (default 1000, 3000)
 * @param {number} [options.maxSendAttempts] attempts before `Failed` (default 3)
 * @param {number} [options.pageSize] history page size (default 50)
 * @param {() => string} [options.newId] clientMessageId generator
 */
export function createMessageStore({ conversationId, api = null, currentUser = null, timers = {}, retryDelaysMs = DEFAULT_RETRY_DELAYS_MS, maxSendAttempts = 3, pageSize = 50, newId = newClientMessageId } = {}) {
  const setTimer = timers.setTimeout || ((fn, ms) => globalThis.setTimeout(fn, ms));
  const now = timers.now || (() => Date.now());
  const emitter = createEmitter();
  const byId = new Map();
  const bySeq = new Map();
  const pending = new Map();
  let order = 0;
  let olderToken = null;
  let hasOlder = null;
  let version = 0;
  let snapshot = null;
  let realtime = null;
  const currentUserId = currentUser && currentUser.id;

  function sleep(ms) {
    return new Promise((resolve) => setTimer(resolve, Math.max(0, ms)));
  }

  function compute() {
    const server = Array.from(byId.values()).sort((a, b) => (a.seq ?? Number.MAX_SAFE_INTEGER) - (b.seq ?? Number.MAX_SAFE_INTEGER));
    const local = Array.from(pending.values()).sort((a, b) => a.order - b.order);
    const seqs = server.map((m) => m.seq).filter((s) => typeof s === 'number');
    return Object.freeze({
      conversationId,
      messages: Object.freeze([...server, ...local]),
      newestSeq: seqs.length ? seqs[seqs.length - 1] : null,
      oldestSeq: seqs.length ? seqs[0] : null,
      hasOlder,
      pendingCount: local.filter((m) => m.sendState === SendState.Sending).length,
      failedCount: local.filter((m) => m.sendState === SendState.Failed).length,
      version
    });
  }

  function changed() {
    version += 1;
    snapshot = compute();
    if (realtime && snapshot.newestSeq !== null) realtime.trackConversation(conversationId, snapshot.newestSeq);
    emitter.emit(snapshot);
  }

  function belongs(message) {
    return message && (!message.conversationId || !conversationId || message.conversationId === conversationId);
  }

  /** Merge one server message. `mode`: `created` (echoes of known ids ignored), `updated` (replace by id), `page` (replace unless older). */
  function upsert(message, mode) {
    if (!message || !message.id || !belongs(message)) return false;
    const cmid = message.clientMessageId;
    let touched = false;
    if (cmid && pending.has(cmid)) {
      const local = pending.get(cmid);
      const authorId = message.author && message.author.id;
      if (!currentUserId || !authorId || authorId === currentUserId || (local.author && local.author.id === authorId)) {
        pending.delete(cmid);
        touched = true;
      }
    }
    const existing = byId.get(message.id);
    if (existing) {
      if (mode === 'created') return touched;
      if (existing.state === 'Deleted' && message.state !== 'Deleted') return touched;
      if (mode === 'page' && existing.updatedUtc && message.updatedUtc && message.updatedUtc < existing.updatedUtc) return touched;
    }
    if (typeof message.seq === 'number') {
      const other = bySeq.get(message.seq);
      if (other && other !== message.id) byId.delete(other);
      bySeq.set(message.seq, message.id);
    }
    byId.set(message.id, message);
    return true;
  }

  function tombstone(messageId, seq) {
    const existing = byId.get(messageId);
    if (existing) {
      if (existing.state === 'Deleted') return false;
      byId.set(messageId, tombstoneOf(existing));
      return true;
    }
    if (typeof seq !== 'number') return false;
    const other = bySeq.get(seq);
    if (other && other !== messageId) byId.delete(other);
    bySeq.set(seq, messageId);
    byId.set(messageId, tombstoneOf({ id: messageId, conversationId, seq }));
    return true;
  }

  /**
   * Merge messages (an array or an EnumerationResult). With `ordering: SeqDescending` the result's
   * continuation token becomes the "load older" cursor.
   * @param {object[]|object} page
   * @param {{ ordering?: string }} [options]
   * @returns {number} merged count
   */
  function mergePage(page, { ordering } = {}) {
    const result = Array.isArray(page) ? { objects: page, continuationToken: null, endOfResults: true } : normalizeEnumerationResult(page);
    let count = 0;
    for (const message of result.objects) if (upsert(message, 'page')) count += 1;
    if (ordering === 'SeqDescending') {
      olderToken = result.continuationToken || null;
      hasOlder = !result.endOfResults && Boolean(result.continuationToken);
    }
    changed();
    return count;
  }

  /**
   * Apply a realtime event (`message.created`, `message.updated`, `message.deleted`) for this
   * conversation. Returns whether the state changed.
   */
  function applyEvent(type, data) {
    if (!data || (data.conversationId && data.conversationId !== conversationId)) return false;
    let did = false;
    if (type === 'message.created') did = upsert(data.message, 'created');
    else if (type === 'message.updated') did = upsert(data.message, 'updated');
    else if (type === 'message.deleted') did = tombstone(data.messageId, data.seq);
    if (did) changed();
    return did;
  }

  async function loadLatest(signal) {
    if (!api) throw new Error('An api is required to load messages.');
    const result = await api.enumerateMessages(conversationId, { ordering: 'SeqDescending', maxResults: pageSize }, signal);
    mergePage(result, { ordering: 'SeqDescending' });
    return snapshot;
  }

  async function loadOlder(signal) {
    if (!api) throw new Error('An api is required to load messages.');
    if (hasOlder === false) return snapshot;
    const current = snapshot || compute();
    const query = { ordering: 'SeqDescending', maxResults: pageSize };
    if (olderToken) query.continuationToken = olderToken;
    else if (current.oldestSeq !== null) query.filters = { beforeSeq: current.oldestSeq };
    const result = await api.enumerateMessages(conversationId, query, signal);
    mergePage(result, { ordering: 'SeqDescending' });
    return snapshot;
  }

  async function loadNewer(signal) {
    if (!api) throw new Error('An api is required to load messages.');
    const current = snapshot || compute();
    let token = null;
    for (let page = 0; page < 50; page += 1) {
      const query = { ordering: 'SeqAscending', maxResults: 100, filters: { afterSeq: current.newestSeq || 0 } };
      if (token) query.continuationToken = token;
      const result = normalizeEnumerationResult(await api.enumerateMessages(conversationId, query, signal));
      for (const message of result.objects) upsert(message, 'page');
      if (result.endOfResults || !result.continuationToken || result.continuationToken === token) break;
      token = result.continuationToken;
    }
    changed();
    return snapshot;
  }

  async function loadAround(seq, signal) {
    if (!api) throw new Error('An api is required to load messages.');
    const result = await collectEnumeration((query, pageSignal) => api.enumerateMessages(conversationId, query, pageSignal), { maxResults: pageSize, filters: { aroundSeq: seq } }, { signal, maxPages: 100 });
    mergePage(result);
    return snapshot;
  }

  function setPending(cmid, patch) {
    const item = pending.get(cmid);
    if (!item) return null;
    const next = { ...item, ...patch };
    pending.set(cmid, next);
    changed();
    return next;
  }

  async function attemptSend(cmid) {
    if (!api) throw new Error('An api is required to send messages.');
    for (;;) {
      const item = pending.get(cmid);
      if (!item) {
        const sent = Array.from(byId.values()).find((m) => m.clientMessageId === cmid);
        return sent ? { ok: true, message: sent } : { ok: false, error: null };
      }
      const attempts = item.attempts + 1;
      setPending(cmid, { sendState: SendState.Sending, attempts, error: null });
      try {
        const message = await api.sendMessage(conversationId, sendMessageBody({ clientMessageId: cmid, body: item.body, mediaIds: item.mediaIds, replyToMessageId: item.replyToMessageId, showLinkPreview: item.showLinkPreview }));
        pending.delete(cmid);
        if (message && message.id) upsert(message, 'page');
        changed();
        return { ok: true, message: byId.get(message && message.id) || message };
      } catch (error) {
        if (!pending.has(cmid)) {
          const sent = Array.from(byId.values()).find((m) => m.clientMessageId === cmid);
          if (sent) return { ok: true, message: sent };
        }
        if (isRetryable(error) && attempts < maxSendAttempts) {
          const index = Math.min(attempts - 1, retryDelaysMs.length - 1);
          const fallback = retryDelaysMs.length ? retryDelaysMs[Math.max(0, index)] : 0;
          const delay = error && Number(error.retryAfterSeconds) > 0 ? Number(error.retryAfterSeconds) * 1000 : fallback;
          await sleep(delay);
          continue;
        }
        setPending(cmid, { sendState: SendState.Failed, error: errorInfo(error) });
        return { ok: false, error };
      }
    }
  }

  /**
   * Send a message: insert an optimistic item (`sendState: Sending`), POST with a new
   * `clientMessageId`, retry network, 5xx, and 429 failures with the same id, and leave a
   * `Failed` item on a final failure. Resolves `{ ok, message?, error? }`.
   * @param {{ body?: string, mediaIds?: string[], replyToMessageId?: string|null, showLinkPreview?: boolean, attachments?: object[], clientMessageId?: string }} draft
   */
  function sendMessage(draft = {}) {
    const cmid = draft.clientMessageId || newId();
    order += 1;
    pending.set(cmid, {
      id: null,
      localId: 'local:' + cmid,
      conversationId,
      seq: null,
      clientMessageId: cmid,
      author: currentUser,
      body: typeof draft.body === 'string' ? draft.body : '',
      mediaIds: Array.isArray(draft.mediaIds) ? draft.mediaIds.slice() : [],
      attachments: Array.isArray(draft.attachments) ? draft.attachments.slice() : [],
      replyToMessageId: draft.replyToMessageId || null,
      showLinkPreview: draft.showLinkPreview !== false,
      state: 'Visible',
      isEdited: false,
      reactionCounts: {},
      reactionTotal: 0,
      myReactions: [],
      labels: [],
      tags: {},
      createdUtc: new Date(now()).toISOString(),
      pending: true,
      sendState: SendState.Sending,
      attempts: 0,
      error: null,
      order
    });
    changed();
    if (realtime) realtime.messageSent(conversationId);
    return attemptSend(cmid);
  }

  /** Retry a `Failed` item with the same `clientMessageId`. */
  function retrySend(clientMessageId) {
    const item = pending.get(clientMessageId);
    if (!item) return Promise.resolve({ ok: false, error: null });
    pending.set(clientMessageId, { ...item, attempts: 0 });
    return attemptSend(clientMessageId);
  }

  /** Drop a `Failed` (or any pending) item. */
  function discardPending(clientMessageId) {
    if (!pending.delete(clientMessageId)) return false;
    changed();
    return true;
  }

  /**
   * Feed this store from a RealtimeClient: subscribes to the message events of this
   * conversation and keeps the client's contiguous `seq` baseline. Returns a detach function.
   */
  function attachRealtime(client) {
    realtime = client;
    const current = snapshot || compute();
    if (current.newestSeq !== null) client.trackConversation(conversationId, current.newestSeq);
    const offs = ['message.created', 'message.updated', 'message.deleted'].map((type) => client.on(type, (data) => applyEvent(type, data)));
    return () => {
      for (const off of offs) off();
      if (realtime === client) realtime = null;
    };
  }

  snapshot = compute();

  return {
    conversationId,
    subscribe: emitter.subscribe,
    getSnapshot: () => snapshot,
    getMessages: () => snapshot.messages,
    getMessage: (id) => byId.get(id) || null,
    mergePage,
    applyEvent,
    upsertMessage: (message) => { const did = upsert(message, 'updated'); if (did) changed(); return did; },
    loadLatest,
    loadOlder,
    loadNewer,
    loadAround,
    sendMessage,
    retrySend,
    discardPending,
    attachRealtime
  };
}

function sortKey(conversation) {
  return (conversation.lastMessage && conversation.lastMessage.createdUtc) || conversation.updatedUtc || conversation.createdUtc || '';
}

/** True when a conversation update means it leaves the caller's lists: blocked by me, or a request I declined. */
function leavesLists(conversation) {
  const viewer = conversation.viewer || {};
  return viewer.cannotSendReason === 'Blocked' || conversation.requestState === 'Declined';
}

/**
 * A conversation list store.
 * @param {{ currentUserId?: string|null, activeConversationId?: string|null }} [options]
 */
export function createConversationListStore({ currentUserId = null, activeConversationId = null } = {}) {
  const emitter = createEmitter();
  const byId = new Map();
  let unread = Object.freeze({ notifications: null, conversations: null, messages: null, requests: null });
  let active = activeConversationId;
  let version = 0;
  let snapshot = null;

  function compute() {
    const conversations = Array.from(byId.values()).sort((a, b) => {
      const ka = sortKey(a);
      const kb = sortKey(b);
      if (ka !== kb) return ka < kb ? 1 : -1;
      return a.id < b.id ? 1 : a.id > b.id ? -1 : 0;
    });
    return Object.freeze({ conversations: Object.freeze(conversations), unread, activeConversationId: active, version });
  }

  function changed() {
    version += 1;
    snapshot = compute();
    emitter.emit(snapshot);
  }

  function upsert(conversation, force) {
    if (!conversation || !conversation.id) return false;
    const existing = byId.get(conversation.id);
    if (existing && !force && existing.updatedUtc && conversation.updatedUtc && conversation.updatedUtc < existing.updatedUtc) return false;
    byId.set(conversation.id, conversation);
    return true;
  }

  function patch(id, fn) {
    const existing = byId.get(id);
    if (!existing) return false;
    const next = fn(existing);
    if (!next || next === existing) return false;
    byId.set(id, next);
    return true;
  }

  /** Merge an enumeration page (array or EnumerationResult). */
  function mergePage(page) {
    const objects = Array.isArray(page) ? page : normalizeEnumerationResult(page).objects;
    let count = 0;
    for (const c of objects) if (upsert(c, false)) count += 1;
    changed();
    return count;
  }

  /** Apply sync changes (`{ conversation, change }`): `Updated` replaces, `Removed` drops. */
  function applySync(changes) {
    let did = false;
    for (const item of Array.isArray(changes) ? changes : []) {
      const c = item && item.conversation;
      if (!c || !c.id) continue;
      if (item.change === 'Removed') did = byId.delete(c.id) || did;
      else did = upsert(c, true) || did;
    }
    if (did) changed();
    return did;
  }

  /** Set unread counts (any of `notifications`, `conversations`, `messages`, `requests`). */
  function setUnreadCounts(counts) {
    if (!counts || typeof counts !== 'object') return;
    const next = { ...unread };
    for (const key of ['notifications', 'conversations', 'messages', 'requests']) if (typeof counts[key] === 'number') next[key] = counts[key];
    unread = Object.freeze(next);
    changed();
  }

  /** Local read: `myLastReadSeq` rises and `unreadCount` becomes 0 when caught up. */
  function markRead(conversationId, seq) {
    const did = patch(conversationId, (c) => {
      const value = Number(seq);
      if (!(value > (c.myLastReadSeq || 0))) return c;
      return { ...c, myLastReadSeq: value, unreadCount: value >= (c.lastSeq || 0) ? 0 : c.unreadCount };
    });
    if (did) changed();
    return did;
  }

  /** Apply a realtime event. Returns whether the state changed. */
  function applyEvent(type, data) {
    if (!data) return false;
    let did = false;
    switch (type) {
      case 'conversation.created':
      case 'conversation.updated':
        // A conversation whose other member I blocked, or a request I declined, leaves my lists (the sync reports it
        // `Removed`); the server still publishes the update so other devices learn about it.
        if (data.conversation && leavesLists(data.conversation)) did = byId.delete(data.conversation.id);
        else did = upsert(data.conversation, true);
        break;
      case 'message.created': {
        const m = data.message;
        if (!m) break;
        did = patch(data.conversationId || m.conversationId, (c) => {
          if (typeof m.seq === 'number' && m.seq <= (c.lastSeq || 0) && c.lastMessage && c.lastMessage.seq >= m.seq) return c;
          const mine = currentUserId && m.author && m.author.id === currentUserId;
          const next = { ...c, lastSeq: Math.max(c.lastSeq || 0, m.seq || 0), lastMessage: messageSummaryOf(m), updatedUtc: m.createdUtc || c.updatedUtc };
          if (mine) {
            next.myLastReadSeq = Math.max(c.myLastReadSeq || 0, m.seq || 0);
            next.unreadCount = 0;
          } else if (c.id !== active && (m.seq || 0) > (c.myLastReadSeq || 0)) {
            next.unreadCount = Math.min(999, (c.unreadCount || 0) + 1);
          }
          return next;
        });
        break;
      }
      case 'message.updated':
        did = patch(data.conversationId, (c) => (c.lastMessage && data.message && c.lastMessage.id === data.message.id ? { ...c, lastMessage: messageSummaryOf(data.message) } : c));
        break;
      case 'message.deleted':
        did = patch(data.conversationId, (c) => (c.lastMessage && c.lastMessage.id === data.messageId ? { ...c, lastMessage: { ...c.lastMessage, state: 'Deleted', excerpt: '', hasAttachments: false } } : c));
        break;
      case 'receipt.read':
        did = patch(data.conversationId, (c) => {
          const seq = Number(data.seq) || 0;
          if (currentUserId && data.userId === currentUserId) {
            if (seq <= (c.myLastReadSeq || 0)) return c;
            return { ...c, myLastReadSeq: seq, unreadCount: seq >= (c.lastSeq || 0) ? 0 : c.unreadCount };
          }
          if (seq <= (c.otherLastReadSeq || 0)) return c;
          return { ...c, otherLastReadSeq: seq };
        });
        break;
      case 'receipt.delivered':
        did = patch(data.conversationId, (c) => {
          const seq = Number(data.seq) || 0;
          if (currentUserId && data.userId === currentUserId) return seq > (c.myLastDeliveredSeq || 0) ? { ...c, myLastDeliveredSeq: seq } : c;
          return seq > (c.otherLastDeliveredSeq || 0) ? { ...c, otherLastDeliveredSeq: seq } : c;
        });
        break;
      case 'unread.counts':
        setUnreadCounts(data);
        return true;
      case 'sync':
        return applySync(data.changes);
      default:
        return false;
    }
    if (did) changed();
    return did;
  }

  /** The open conversation (its incoming messages do not raise `unreadCount`). */
  function setActiveConversation(id) {
    active = id || null;
    changed();
  }

  function remove(id) {
    if (!byId.delete(id)) return false;
    changed();
    return true;
  }

  /** Subscribe to a RealtimeClient's list events (including `sync`). Returns a detach function. */
  function attachRealtime(client) {
    const types = ['conversation.created', 'conversation.updated', 'message.created', 'message.updated', 'message.deleted', 'receipt.read', 'receipt.delivered', 'unread.counts', 'sync'];
    const offs = types.map((type) => client.on(type, (data) => applyEvent(type, data)));
    return () => { for (const off of offs) off(); };
  }

  snapshot = compute();

  return {
    subscribe: emitter.subscribe,
    getSnapshot: () => snapshot,
    getConversation: (id) => byId.get(id) || null,
    /** Conversations of one folder (all when omitted), newest activity first. */
    getConversations: (folder) => (folder ? snapshot.conversations.filter((c) => (c.folder || 'Inbox') === folder) : snapshot.conversations),
    upsertConversation: (c) => { const did = upsert(c, true); if (did) changed(); return did; },
    mergePage,
    applySync,
    applyEvent,
    setUnreadCounts,
    markRead,
    setActiveConversation,
    remove,
    attachRealtime
  };
}
