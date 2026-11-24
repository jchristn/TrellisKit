/**
 * An in-memory fake of the Phase 5 direct message routes (docs/PHASE5_CONTRACT.md sections 3
 * and 5, as-built note B), seen from one signed-in member (`me`). It keeps conversations,
 * contiguous per-conversation `seq`, idempotent sends by `clientMessageId`, one edit with the
 * original text, tombstones, reactions, read and delivered positions, folders, mutes, delete for
 * me, a change log for sync tokens, search, settings, unread counts, and reports.
 *
 * Test helpers: `receive(conversationId, { body, author? })` appends a message from the other
 * member (returns it, so a test can push the matching socket frame), `addConversation(seed)`,
 * `expireSyncTokens()`, `fail(rule)` (injected failures, see fakeKit), `block(userId)`.
 */

import { clone, createFakeServer, error, matchesMeta, noContent, ok, page, summaryUser, truthy } from './fakeKit.js';

const VIEWER_MESSAGE_MINE = Object.freeze({ canEdit: true, canDelete: true, canReact: true, canReport: false });
const VIEWER_MESSAGE_OTHER = Object.freeze({ canEdit: false, canDelete: false, canReact: true, canReport: true });

/**
 * Create a fake.
 * @param {{ me?: object, users?: object[], now?: () => string, notificationUnread?: number }} [seed]
 */
export function createMessagingFake(seed = {}) {
  let n = 0;
  const nextId = (prefix) => prefix + 'fake' + (++n);
  const now = seed.now || (() => '2026-11-12T12:00:00Z');
  const state = {
    me: seed.me || summaryUser('usr_1', 'Sam Rivera'),
    users: (seed.users || [summaryUser('usr_2', 'Alex Chen'), summaryUser('usr_3', 'Jo Park')]).map(clone),
    conversations: [],
    messages: [],
    reactions: [],
    reports: [],
    blocked: new Set(),
    counter: 0,
    minValidToken: 0,
    notificationUnread: seed.notificationUnread || 0,
    settings: { dmPolicy: 'Everyone', readReceipts: true, typingIndicators: true, pushPreview: 'SenderOnly' }
  };

  const user = (id) => (id === state.me.id ? state.me : state.users.find((u) => u.id === id));
  const conversation = (id) => state.conversations.find((c) => c.id === id && !c.deleted);
  const messagesOf = (c) => state.messages.filter((m) => m.conversationId === c.id && m.seq > c.hiddenBeforeSeq).sort((a, b) => a.seq - b.seq);

  function touch(c) {
    state.counter += 1;
    c.changeCounter = state.counter;
    c.updatedUtc = now();
  }

  function presentMessage(m) {
    const mine = m.author && m.author.id === state.me.id;
    const deleted = m.state === 'Deleted';
    return {
      id: m.id, conversationId: m.conversationId, seq: m.seq, clientMessageId: m.clientMessageId, author: clone(m.author),
      body: deleted ? '' : m.body, originalBody: deleted ? null : m.originalBody, isEdited: m.isEdited, attachments: deleted ? [] : clone(m.attachments),
      linkPreview: deleted ? null : clone(m.linkPreview || null), replyTo: m.replyTo, state: m.state, editedUtc: m.editedUtc, reactionCounts: { ...m.reactionCounts }, reactionTotal: m.reactionTotal,
      myReactions: state.reactions.filter((r) => r.messageId === m.id && r.userId === state.me.id).map((r) => r.key), labels: clone(m.labels), tags: clone(m.tags),
      viewer: mine ? { ...VIEWER_MESSAGE_MINE, canEdit: !m.isEdited && !deleted, canDelete: !deleted } : { ...VIEWER_MESSAGE_OTHER },
      createdUtc: m.createdUtc, updatedUtc: m.updatedUtc
    };
  }

  function summaryOf(m) {
    if (!m) return null;
    return { id: m.id, seq: m.seq, authorUserId: m.author ? m.author.id : null, excerpt: m.state === 'Visible' ? m.body.slice(0, 120) : '', hasAttachments: m.attachments.length > 0, state: m.state, createdUtc: m.createdUtc };
  }

  function unreadOf(c) {
    return Math.min(999, messagesOf(c).filter((m) => m.seq > c.myLastReadSeq && m.author && m.author.id !== state.me.id).length);
  }

  function presentConversation(c) {
    const other = user(c.otherUserId);
    const visible = messagesOf(c);
    return {
      id: c.id, kind: 'Direct',
      members: [{ user: clone(state.me), role: 'Member', joinedUtc: c.createdUtc }, { user: clone(other), role: 'Member', joinedUtc: c.createdUtc }],
      otherMember: clone(other), folder: c.folder, requestState: c.requestState, lastMessage: summaryOf(visible[visible.length - 1]), lastSeq: c.lastSeq,
      myLastReadSeq: c.myLastReadSeq, myLastDeliveredSeq: c.myLastDeliveredSeq, otherLastReadSeq: state.settings.readReceipts ? c.otherLastReadSeq : null,
      unreadCount: unreadOf(c), isMuted: c.isMuted, mutedUntilUtc: c.mutedUntilUtc, labels: clone(c.labels), tags: clone(c.tags),
      viewer: { canSend: c.folder !== 'Declined', canAttachMedia: true, canAttachVideo: true, canReport: true, canBlock: true, canAccept: c.requestState === 'Pending', canDecline: c.requestState === 'Pending', cannotSendReason: null },
      createdUtc: c.createdUtc, updatedUtc: c.updatedUtc
    };
  }

  /** Add a conversation with another member. */
  function addConversation({ otherUserId = state.users[0].id, folder = 'Inbox', requestState = null, labels = [], tags = {} } = {}) {
    const c = {
      id: nextId('cnv_'), otherUserId, folder, requestState, lastSeq: 0, myLastReadSeq: 0, myLastDeliveredSeq: 0, otherLastReadSeq: 0, hiddenBeforeSeq: 0,
      isMuted: false, mutedUntilUtc: null, deleted: false, labels, tags, createdUtc: now(), updatedUtc: now(), changeCounter: 0
    };
    state.conversations.push(c);
    touch(c);
    return c;
  }

  // The server resolves the first link of a message into a preview (fetched by the service, never the
  // member's device) unless the sender turned it off.
  function previewOf(body, showLinkPreview) {
    if (showLinkPreview === false) return null;
    const match = /https?:\/\/[^\s<>()]+/i.exec(String(body || ''));
    if (!match) return null;
    let host = '';
    try {
      host = new URL(match[0]).host;
    } catch {
      return null;
    }
    return { url: match[0], title: 'Preview of ' + host, description: 'Fetched by the service.', siteName: host, imageUrl: null, status: 'Ready' };
  }

  function append(c, author, { clientMessageId, body = '', mediaIds = [], replyToMessageId = null, showLinkPreview = true, linkPreview } = {}) {
    c.lastSeq += 1;
    const reply = replyToMessageId ? state.messages.find((m) => m.id === replyToMessageId && m.conversationId === c.id) : null;
    const m = {
      id: nextId('msg_'), conversationId: c.id, seq: c.lastSeq, clientMessageId: clientMessageId || nextId('cm-'), author: clone(author), body, originalBody: null, isEdited: false,
      attachments: (mediaIds || []).map((id) => ({ id, kind: 'Image' })), replyTo: reply ? { id: reply.id, seq: reply.seq, excerpt: reply.body.slice(0, 120), authorDisplayName: reply.author.displayName, isDeleted: reply.state !== 'Visible' } : null,
      state: 'Visible', editedUtc: null, reactionCounts: {}, reactionTotal: 0, labels: [], tags: {}, createdUtc: now(), updatedUtc: now(),
      linkPreview: linkPreview !== undefined ? clone(linkPreview) : previewOf(body, showLinkPreview)
    };
    state.messages.push(m);
    if (author.id === state.me.id) {
      c.myLastReadSeq = c.lastSeq;
      c.myLastDeliveredSeq = c.lastSeq;
    }
    if (c.deleted && author.id !== state.me.id) c.deleted = false;
    touch(c);
    return m;
  }

  /** The other member sends a message; resolves the presented Message. */
  function receive(conversationId, { body = 'Hello', author, linkPreview } = {}) {
    const c = state.conversations.find((x) => x.id === conversationId);
    if (!c) throw new Error('Unknown conversation ' + conversationId);
    return presentMessage(append(c, author || user(c.otherUserId), { body, linkPreview }));
  }

  function filterConversations(query) {
    const f = query.filters || {};
    const folder = f.folder || 'Inbox';
    const term = String(query.search || '').trim().toLowerCase();
    return state.conversations
      .filter((c) => !c.deleted && !state.blocked.has(c.otherUserId) && c.folder === folder)
      .filter((c) => !truthy(f.unreadOnly) || unreadOf(c) > 0)
      .filter((c) => !f.withUserId || c.otherUserId === f.withUserId)
      .filter((c) => {
        if (!term) return true;
        const u = user(c.otherUserId);
        return u.displayName.toLowerCase().includes(term) || u.handle.toLowerCase().includes(term);
      })
      .filter((c) => matchesMeta(c, query))
      .map(presentConversation)
      .sort((a, b) => ((b.lastMessage && b.lastMessage.createdUtc) || b.updatedUtc).localeCompare((a.lastMessage && a.lastMessage.createdUtc) || a.updatedUtc) || (b.id < a.id ? -1 : 1));
  }

  function enumerateMessages(c, body) {
    const f = body.filters || {};
    let list = messagesOf(c);
    const term = String(body.search || '').trim().toLowerCase();
    if (term) list = list.filter((m) => m.state === 'Visible' && m.body.toLowerCase().includes(term));
    if (f.beforeSeq !== undefined) list = list.filter((m) => m.seq < Number(f.beforeSeq));
    if (f.afterSeq !== undefined) list = list.filter((m) => m.seq > Number(f.afterSeq));
    list = list.filter((m) => matchesMeta(m, body));
    if (f.aroundSeq !== undefined) {
      const around = Number(f.aroundSeq);
      const before = list.filter((m) => m.seq <= around).slice(-50);
      const after = list.filter((m) => m.seq > around).slice(0, 50);
      list = [...before, ...after];
    }
    if (body.ordering !== 'SeqAscending') list = list.slice().reverse();
    // Fetching history marks delivery (contract 4.6).
    if (list.length) c.myLastDeliveredSeq = Math.max(c.myLastDeliveredSeq, c.lastSeq);
    return ok(page(list.map(presentMessage), body, { max: 100 }));
  }

  function sync(body) {
    const size = Math.max(1, Math.min(100, Math.floor(Number(body.maxResults) || 100)));
    const token = body.continuationToken || body.syncToken || null;
    let after;
    let offset = 0;
    if (token === null) after = null;
    else {
      const m = /^sync:(\d+)$/.exec(token) || /^syncp:(\d+):(\d+)$/.exec(token);
      if (!m || Number(m[1]) < state.minValidToken) return error(400, 'InvalidContinuationToken', 'Start a fresh sync.');
      after = Number(m[1]);
      offset = m[2] ? Number(m[2]) : 0;
    }
    let rows;
    if (after === null) {
      rows = state.conversations.filter((c) => !c.deleted).sort((a, b) => b.changeCounter - a.changeCounter).map((c) => ({ conversation: presentConversation(c), change: 'Updated' }));
      after = 0;
    } else {
      rows = state.conversations.filter((c) => c.changeCounter > after).sort((a, b) => a.changeCounter - b.changeCounter)
        .map((c) => ({ conversation: c.deleted || state.blocked.has(c.otherUserId) ? { id: c.id } : presentConversation(c), change: c.deleted || state.blocked.has(c.otherUserId) ? 'Removed' : 'Updated' }));
    }
    const slice = rows.slice(offset, offset + size);
    const end = offset + size >= rows.length;
    return ok({
      success: true, maxResults: size, skip: 0, continuationToken: end ? null : 'syncp:' + after + ':' + (offset + size), syncToken: end ? 'sync:' + state.counter : null, endOfResults: end,
      totalRecords: rows.length, recordsRemaining: Math.max(0, rows.length - offset - slice.length), timestampUtc: now(), objects: slice
    });
  }

  function handleConversation(method, parts, body) {
    if (parts[1] === 'enumerate') return ok(page(filterConversations(body), body));
    if (parts[1] === 'changes' && parts[2] === 'enumerate') return sync(body);
    if (!parts[1] && method === 'POST') {
      if (body.recipientUserId === state.me.id) return error(400, 'CannotMessageSelf', 'You cannot message yourself.');
      const other = user(body.recipientUserId);
      if (!other) return error(404, 'NotFound', 'Not found.');
      if (state.blocked.has(other.id)) return error(403, 'MessagingNotAllowed', 'You cannot message this member.');
      let c = state.conversations.find((x) => x.otherUserId === other.id);
      const created = !c;
      if (!c) c = addConversation({ otherUserId: other.id });
      c.deleted = false;
      let message;
      if (body.message) {
        const existing = state.messages.find((m) => m.conversationId === c.id && m.clientMessageId === body.message.clientMessageId && m.author.id === state.me.id);
        message = existing || append(c, state.me, body.message);
      }
      return ok({ conversation: presentConversation(c), ...(message ? { message: presentMessage(message) } : {}) }, created ? 201 : 200);
    }
    const c = conversation(parts[1]);
    if (!c) return error(404, 'NotFound', 'Not found.');
    const action = parts[2];
    if (!action && method === 'GET') return ok(presentConversation(c));
    if (!action && method === 'DELETE') {
      c.deleted = true;
      c.hiddenBeforeSeq = c.lastSeq;
      touch(c);
      return noContent();
    }
    if (action === 'messages' && parts[3] === 'enumerate') return enumerateMessages(c, body);
    if (action === 'messages' && method === 'POST') {
      if (state.blocked.has(c.otherUserId)) return error(403, 'MessagingNotAllowed', 'You cannot message this member.');
      if (!String(body.body || '').trim() && !(body.mediaIds || []).length) return error(400, 'MessageEmpty', 'Write a message or attach something.');
      if (!body.clientMessageId) return error(400, 'ValidationFailed', 'clientMessageId is required.', { clientMessageId: 'Required.' });
      const existing = state.messages.find((m) => m.conversationId === c.id && m.clientMessageId === body.clientMessageId && m.author.id === state.me.id);
      if (existing) return ok(presentMessage(existing), 200);
      if (c.requestState === 'Pending') { c.requestState = null; c.folder = 'Inbox'; }
      return ok(presentMessage(append(c, state.me, body)), 201);
    }
    if (action === 'read' && method === 'POST') {
      const seq = Math.min(c.lastSeq, Number(body.seq) || 0);
      if (seq > c.myLastReadSeq) { c.myLastReadSeq = seq; touch(c); }
      return ok({ myLastReadSeq: c.myLastReadSeq, unreadCount: unreadOf(c) });
    }
    if (action === 'delivered' && method === 'POST') {
      c.myLastDeliveredSeq = Math.max(c.myLastDeliveredSeq, Math.min(c.lastSeq, Number(body.seq) || 0));
      return noContent();
    }
    if (action === 'accept' || action === 'decline') {
      if (c.requestState !== 'Pending') return error(409, 'Conflict', 'Not a pending request.');
      c.requestState = null;
      c.folder = action === 'accept' ? 'Inbox' : 'Declined';
      touch(c);
      return ok(presentConversation(c));
    }
    if (action === 'folder' && method === 'PUT') {
      if (!['Inbox', 'Archived'].includes(body.folder)) return error(400, 'ValidationFailed', 'Unknown folder.', { folder: 'Inbox or Archived.' });
      c.folder = body.folder;
      touch(c);
      return ok(presentConversation(c));
    }
    if (action === 'mute') {
      c.isMuted = method === 'PUT';
      c.mutedUntilUtc = method === 'PUT' ? body.untilUtc || null : null;
      touch(c);
      return ok(presentConversation(c));
    }
    return undefined;
  }

  // Like the server, the headline wraps the first match in `**`.
  function markMatch(text, term) {
    const at = text.toLowerCase().indexOf(term);
    return at < 0 ? text : text.slice(0, at) + '**' + text.slice(at, at + term.length) + '**' + text.slice(at + term.length);
  }

  function handleMessage(method, parts, body) {
    if (parts[1] === 'search' && parts[2] === 'enumerate') {
      const term = String(body.search || '').trim().toLowerCase();
      if (term.length < 2 || term.length > 200) return error(400, 'ValidationFailed', 'Search needs 2 to 200 characters.', { search: 'Length.' });
      const f = body.filters || {};
      const hits = state.conversations.filter((c) => !c.deleted && (!f.conversationId || c.id === f.conversationId) && (!f.withUserId || c.otherUserId === f.withUserId))
        .flatMap((c) => messagesOf(c).filter((m) => m.state === 'Visible' && m.body.toLowerCase().includes(term) && matchesMeta(m, body))
          .map((m) => ({ message: presentMessage(m), conversationId: c.id, otherMember: clone(user(c.otherUserId)), headline: markMatch(m.body.slice(0, 200), term) })));
      return ok(page(hits, body, { max: 50 }));
    }
    const m = state.messages.find((x) => x.id === parts[1]);
    if (!m) return error(404, 'NotFound', 'Not found.');
    const c = state.conversations.find((x) => x.id === m.conversationId);
    if (parts[2] === 'reactions') {
      if (parts[3] === 'enumerate') return ok(page(state.reactions.filter((r) => r.messageId === m.id && (!(body.filters || {}).key || r.key === body.filters.key)).map((r) => ({ user: clone(user(r.userId)), key: r.key, createdUtc: r.createdUtc })), body));
      const key = parts[3];
      const has = state.reactions.find((r) => r.messageId === m.id && r.userId === state.me.id && r.key === key);
      if (method === 'PUT' && !has) state.reactions.push({ messageId: m.id, userId: state.me.id, key, createdUtc: now() });
      if (method === 'DELETE' && has) state.reactions = state.reactions.filter((r) => r !== has);
      const counts = {};
      for (const r of state.reactions.filter((x) => x.messageId === m.id)) counts[r.key] = (counts[r.key] || 0) + 1;
      m.reactionCounts = counts;
      m.reactionTotal = Object.values(counts).reduce((a, b) => a + b, 0);
      m.updatedUtc = now();
      return ok({ reactionCounts: m.reactionCounts, reactionTotal: m.reactionTotal, myReactions: presentMessage(m).myReactions });
    }
    if (m.author.id !== state.me.id) return error(403, 'Forbidden', 'Only the author can do that.');
    if (method === 'PUT') {
      if (m.isEdited || m.state !== 'Visible') return error(409, 'EditWindowClosed', 'This message can no longer be edited.');
      m.originalBody = m.body;
      m.body = String(body.body || '');
      m.isEdited = true;
      m.editedUtc = now();
      m.updatedUtc = now();
      touch(c);
      return ok(presentMessage(m));
    }
    if (method === 'DELETE') {
      m.state = 'Deleted';
      m.updatedUtc = now();
      touch(c);
      return ok(presentMessage(m));
    }
    return undefined;
  }

  function route({ method, parts, body }) {
    switch (parts[0]) {
      case 'conversations': return handleConversation(method, parts, body);
      case 'messages': return handleMessage(method, parts, body);
      case 'me':
        if (parts[1] !== 'messaging') return undefined;
        if (parts[2] === 'unread-count') {
          const live = state.conversations.filter((c) => !c.deleted && !state.blocked.has(c.otherUserId));
          const counts = live.filter((c) => c.folder !== 'Requests' && c.folder !== 'Declined').map(unreadOf);
          return ok({ conversations: counts.filter((x) => x > 0).length, messages: counts.reduce((a, b) => a + b, 0), requests: live.filter((c) => c.folder === 'Requests').length });
        }
        if (parts[2] === 'settings') {
          if (method === 'PUT') {
            for (const key of ['dmPolicy', 'readReceipts', 'typingIndicators', 'pushPreview']) if (body[key] !== undefined) state.settings[key] = body[key];
          }
          return ok(state.settings);
        }
        return undefined;
      case 'notifications':
        return parts[1] === 'unread-count' ? ok({ unreadCount: state.notificationUnread, latestUpdatedUtc: now() }) : undefined;
      case 'reports':
        if (method !== 'POST' || !['Message', 'Conversation'].includes(body.targetType)) return undefined;
        if (!body.reason) return error(400, 'ValidationFailed', 'Choose a reason.', { reason: 'Required.' });
        state.reports.push({ id: nextId('rpt_'), ...clone(body), createdUtc: now() });
        return ok(state.reports[state.reports.length - 1], 201);
      default: return undefined;
    }
  }

  const server = createFakeServer(route);

  return {
    state,
    calls: server.calls,
    handle: server.handle,
    fetchImpl: server.fetchImpl,
    fail: server.fail,
    addConversation,
    receive,
    presentConversation,
    presentMessage,
    conversation: (id) => state.conversations.find((c) => c.id === id) || null,
    /** Invalidate every sync token issued so far (`400 InvalidContinuationToken`). */
    expireSyncTokens() { state.minValidToken = state.counter + 1; },
    /** Block a member (their conversation is removed from lists and sync). */
    block(userId) {
      state.blocked.add(userId);
      for (const c of state.conversations.filter((x) => x.otherUserId === userId)) touch(c);
    }
  };
}
