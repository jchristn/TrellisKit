/**
 * Phase 5 direct message API methods (docs/PHASE5_CONTRACT.md sections 3 and 5, as-built note B),
 * mixed into `ApiClient.prototype`. The mixin only uses `get`, `post`, `put`, `delete`, and
 * `request`, so any client with the same signatures can mix it in too.
 *
 * Every list is the canonical `POST {collection}/enumerate` with an EnumerationQuery
 * (docs/ENUMERATION_AND_METADATA.md). Sending is REST only: the socket is the delivery channel
 * (contract section 4.4). `clientMessageId` makes sends idempotent; generate it once per composed
 * message with `newClientMessageId` and reuse it on every retry.
 *
 * Naming: the AI assistant methods already own `enumerateConversations`, `startConversation`,
 * `getConversation`, and `deleteConversation` on `ApiClient` (assistant chats,
 * docs/AI_CONTRACT.md). So on `ApiClient` the four direct message twins are
 * `enumerateDirectConversations`, `startDirectConversation`, `getDirectConversation`, and
 * `deleteDirectConversation`. `client.messaging` (or `directMessagesApi(client)`) is a facade
 * with every contract section 19 name, including those four, bound to the client.
 */

import { toEnumerationBody } from './enumeration.js';
import { newClientMessageId } from './assistantChat.js';

export { newClientMessageId };

const enc = encodeURIComponent;

/** Page size limits of the messaging collections. */
export const MESSAGES_MAX_RESULTS = 100;
export const MESSAGE_SEARCH_MAX_RESULTS = 50;
export const SYNC_MAX_RESULTS = 100;

/** Conversation folders (contract section 2.1). */
export const ConversationFolder = Object.freeze({ Inbox: 'Inbox', Requests: 'Requests', Archived: 'Archived' });

/** Request states (contract section 2.1). */
export const RequestState = Object.freeze({ Pending: 'Pending', AwaitingAcceptance: 'AwaitingAcceptance' });

/** Message states (contract section 2.2). */
export const MessageState = Object.freeze({ Visible: 'Visible', Deleted: 'Deleted', Removed: 'Removed' });

/** History orderings (contract section 3.4). */
export const MessageOrdering = Object.freeze({ SeqDescending: 'SeqDescending', SeqAscending: 'SeqAscending' });

/** Conversation list orderings (contract section 3.1). */
export const ConversationOrdering = Object.freeze({ LastMessageDescending: 'LastMessageDescending', CreatedDescending: 'CreatedDescending' });

/** Sync change kinds (contract section 5.2). */
export const ConversationChangeKind = Object.freeze({ Updated: 'Updated', Removed: 'Removed' });

/** Direct message policies and push previews (contract section 1.2). */
export const DmPolicy = Object.freeze({ Everyone: 'Everyone', Known: 'Known', Nobody: 'Nobody' });
export const PushPreview = Object.freeze({ SenderOnly: 'SenderOnly', Full: 'Full', Hidden: 'Hidden' });

/** Messaging error codes (contract section 0.4). */
export const MessagingError = Object.freeze({
  CannotMessageSelf: 'CannotMessageSelf',
  MessageEmpty: 'MessageEmpty',
  MessagingNotAllowed: 'MessagingNotAllowed',
  RecipientUnavailable: 'RecipientUnavailable',
  RequestLimitReached: 'RequestLimitReached',
  MessagingRestricted: 'MessagingRestricted',
  EditWindowClosed: 'EditWindowClosed',
  InvalidContinuationToken: 'InvalidContinuationToken'
});

function enumBody(query, limits) {
  return toEnumerationBody(query || {}, limits);
}

function idOf(value) {
  return typeof value === 'string' ? value : value && value.id;
}

/**
 * The JSON body of a send (contract section 3.1). A missing `clientMessageId` is generated, but
 * callers that retry must pass the same id every time.
 * @param {{ clientMessageId?: string, body?: string, mediaIds?: string[], replyToMessageId?: string|null, showLinkPreview?: boolean }} message
 */
export function sendMessageBody(message = {}) {
  const out = {
    clientMessageId: message.clientMessageId || newClientMessageId(),
    body: typeof message.body === 'string' ? message.body : '',
    mediaIds: Array.isArray(message.mediaIds) ? message.mediaIds.slice() : [],
    showLinkPreview: message.showLinkPreview !== false
  };
  if (message.replyToMessageId) out.replyToMessageId = message.replyToMessageId;
  return out;
}

function report(client, targetType, targetId, { reason, details } = {}) {
  const body = { targetType, targetId, reason };
  if (details !== undefined && details !== null) body.details = details;
  return client.post('/reports', body);
}

/** @this {import('./ApiClient.js').ApiClient} */
export const messagingMethods = {
  // ---- Conversations (section 3.1) ----

  /** POST /conversations/enumerate. Orderings `LastMessageDescending` (default), `CreatedDescending`; filters `folder` (default `Inbox`), `unreadOnly`, `withUserId`; `search` over the other member's name and handle. */
  enumerateDirectConversations(query = {}, signal) { return this.post('/conversations/enumerate', enumBody(query), { signal }); },
  /**
   * POST /conversations with `{ recipientUserId, message? }`. Resolves `{ conversation, message? }`
   * (201 when new, 200 with the existing conversation).
   * @param {string} recipientUserId
   * @param {{ clientMessageId?: string, body?: string, mediaIds?: string[] }} [message]
   */
  startDirectConversation(recipientUserId, message) {
    const body = { recipientUserId };
    if (message) {
      const m = sendMessageBody(message);
      body.message = { clientMessageId: m.clientMessageId, body: m.body, mediaIds: m.mediaIds };
    }
    return this.post('/conversations', body);
  },
  /** GET /conversations/{id}. */
  getDirectConversation(id, signal) { return this.get('/conversations/' + enc(idOf(id)), { signal }); },
  /** POST /conversations/{id}/accept. */
  acceptRequest(id) { return this.post('/conversations/' + enc(idOf(id)) + '/accept'); },
  /** POST /conversations/{id}/decline. */
  declineRequest(id) { return this.post('/conversations/' + enc(idOf(id)) + '/decline'); },
  /** PUT /conversations/{id}/folder with `{ folder }` (`Inbox` or `Archived`). */
  setConversationFolder(id, folder) { return this.put('/conversations/' + enc(idOf(id)) + '/folder', { folder }); },
  /** PUT /conversations/{id}/mute with `{ untilUtc }` (null mutes until lifted). */
  muteConversation(id, untilUtc = null) { return this.put('/conversations/' + enc(idOf(id)) + '/mute', { untilUtc: untilUtc || null }); },
  /** DELETE /conversations/{id}/mute. */
  unmuteConversation(id) { return this.delete('/conversations/' + enc(idOf(id)) + '/mute'); },
  /** DELETE /conversations/{id} (delete for me; 204). */
  deleteDirectConversation(id) { return this.delete('/conversations/' + enc(idOf(id))); },
  /** POST /conversations/{id}/read with `{ seq }`; resolves `{ myLastReadSeq, unreadCount }`. */
  markConversationRead(id, seq) { return this.post('/conversations/' + enc(idOf(id)) + '/read', { seq: Number(seq) }); },
  /** POST /conversations/{id}/delivered with `{ seq }` (204; the REST twin of the socket `ack`). */
  markConversationDelivered(id, seq) { return this.post('/conversations/' + enc(idOf(id)) + '/delivered', { seq: Number(seq) }); },

  // ---- Messages (sections 3.3 to 3.5) ----

  /**
   * POST /conversations/{id}/messages/enumerate. Orderings `SeqDescending` (default) and
   * `SeqAscending`; filters `beforeSeq`, `afterSeq`, `aroundSeq`; `search` (as-built note B).
   * At most 100 per page.
   */
  enumerateMessages(conversationId, query = {}, signal) {
    return this.post('/conversations/' + enc(idOf(conversationId)) + '/messages/enumerate', enumBody(query, { maxLimit: MESSAGES_MAX_RESULTS }), { signal });
  },
  /**
   * POST /conversations/{id}/messages. Resolves the Message (201, or 200 with the original for a
   * repeated `clientMessageId`).
   * @param {string} conversationId
   * @param {{ clientMessageId?: string, body?: string, mediaIds?: string[], replyToMessageId?: string|null, showLinkPreview?: boolean }} message
   */
  sendMessage(conversationId, message) { return this.post('/conversations/' + enc(idOf(conversationId)) + '/messages', sendMessageBody(message)); },
  /** PUT /messages/{id} with `{ body }` (one edit within the edit window, as-built note B). */
  editMessage(messageId, body) { return this.put('/messages/' + enc(idOf(messageId)), { body: typeof body === 'string' ? body : (body && body.body) || '' }); },
  /** DELETE /messages/{id}; resolves the tombstone. */
  deleteMessage(messageId) { return this.delete('/messages/' + enc(idOf(messageId))); },
  /**
   * POST /messages/search/enumerate (as-built note B): `search` 2 to 200 characters, orderings
   * `Relevance` (default), `CreatedDescending`, `CreatedAscending`; filters `conversationId`,
   * `withUserId`; at most 50 per page. Items `{ message, conversationId, otherMember, headline }`.
   */
  searchMessages(query = {}, signal) { return this.post('/messages/search/enumerate', enumBody(query, { maxLimit: MESSAGE_SEARCH_MAX_RESULTS }), { signal }); },

  // ---- Reactions (section 3.5) ----

  /** PUT /messages/{id}/reactions/{key}. */
  putMessageReaction(messageId, key) { return this.request('PUT', '/messages/' + enc(idOf(messageId)) + '/reactions/' + enc(key)); },
  /** DELETE /messages/{id}/reactions/{key}. */
  deleteMessageReaction(messageId, key) { return this.delete('/messages/' + enc(idOf(messageId)) + '/reactions/' + enc(key)); },
  /** POST /messages/{id}/reactions/enumerate. Filter `key`. */
  enumerateMessageReactions(messageId, query = {}, signal) { return this.post('/messages/' + enc(idOf(messageId)) + '/reactions/enumerate', enumBody(query), { signal }); },

  // ---- Sync (section 5.2) ----

  /** POST /conversations/changes/enumerate. `syncToken` resumes a completed sync;
   * `continuationToken` pages this retrieval. Persist only the terminal response's syncToken. */
  syncConversations({ syncToken = null, ...query } = {}, signal) {
    return this.post('/conversations/changes/enumerate', { ...enumBody({ maxResults: SYNC_MAX_RESULTS, ...query }), syncToken }, { signal });
  },

  // ---- Counts and settings ----

  /** GET /me/messaging/unread-count; resolves `{ conversations, messages, requests }`. */
  getMessagingUnreadCount(signal) { return this.get('/me/messaging/unread-count', { signal }); },
  /** GET /me/messaging/settings. */
  getMessagingSettings(signal) { return this.get('/me/messaging/settings', { signal }); },
  /** PUT /me/messaging/settings with any of `{ dmPolicy, readReceipts, typingIndicators, pushPreview }`. */
  updateMessagingSettings(body) { return this.put('/me/messaging/settings', body || {}); },

  // ---- Reports (section 3.9) ----

  /** POST /reports with `targetType: "Message"`. */
  reportMessage(messageId, { reason, details } = {}) { return report(this, 'Message', idOf(messageId), { reason, details }); },
  /** POST /reports with `targetType: "Conversation"`. */
  reportConversation(conversationId, { reason, details } = {}) { return report(this, 'Conversation', idOf(conversationId), { reason, details }); }
};

/** Contract names (section 19) that differ from the `ApiClient` method names. */
export const MESSAGING_CONTRACT_ALIASES = Object.freeze({
  enumerateConversations: 'enumerateDirectConversations',
  startConversation: 'startDirectConversation',
  getConversation: 'getDirectConversation',
  deleteConversation: 'deleteDirectConversation'
});

/**
 * A facade over a client with every messaging method under its contract name (section 19), bound
 * to `client`: `enumerateConversations`, `startConversation`, `getConversation`,
 * `deleteConversation` (the direct message routes, not assistant chats), plus every other
 * `messagingMethods` name. Works with any client that has `get`, `post`, `put`, `delete`, `request`.
 * @param {object} client
 */
export function directMessagesApi(client) {
  const facade = {};
  for (const [name, fn] of Object.entries(messagingMethods)) facade[name] = fn.bind(client);
  for (const [alias, name] of Object.entries(MESSAGING_CONTRACT_ALIASES)) facade[alias] = messagingMethods[name].bind(client);
  return Object.freeze(facade);
}

/**
 * Split a message search `headline` (matches wrapped in `**`, REST_API.md "Search") into parts so clients can
 * highlight matches without rendering Markdown. Unbalanced markers are kept as text.
 * @param {string} headline The server headline.
 * @returns {{ text: string, match: boolean }[]} Parts in order; empty parts are dropped.
 */
export function headlineParts(headline) {
  const source = String(headline || '');
  const pieces = source.split('**');
  if (pieces.length % 2 === 0) return source ? [{ text: source, match: false }] : [];
  return pieces.map((text, i) => ({ text, match: i % 2 === 1 })).filter((p) => p.text.length > 0);
}
