import { MessageState, RequestState } from '@trelliskit/shared';
import strings from '../strings.js';

const LOCAL_HOSTS = ['localhost', '127.0.0.1', '::1', '[::1]'];

/** Whether a host name is a local development host (no direct API connection). */
export function isLocalHost(hostname) {
  const host = String(hostname || '').toLowerCase();
  return LOCAL_HOSTS.includes(host) || host.endsWith('.localhost');
}

/**
 * The realtime socket URL for the website (contract section 4.1, resolved decision 11, as-built
 * note A): the website connects directly to the property's API domain
 * (`wss://<domains.api>/api/v1/ws`) when the property names one and the page is not on a local
 * host; otherwise it uses the page's own origin (`/api/v1/ws`, proxied by the dev server and the
 * Docker nginx).
 * @param {object} property the active property (`domains.api`)
 * @param {{ protocol: string, host: string, hostname: string }} [location]
 * @returns {string}
 */
export function realtimeUrl(property, location = typeof window !== 'undefined' ? window.location : null) {
  const api = property && property.domains && typeof property.domains.api === 'string' ? property.domains.api.trim() : '';
  const loc = location || { protocol: 'http:', host: 'localhost', hostname: 'localhost' };
  if (api && !isLocalHost(loc.hostname)) {
    const host = api.replace(/^[a-z]+:\/\//i, '').replace(/\/.*$/, '');
    return 'wss://' + host + '/api/v1/ws';
  }
  return (loc.protocol === 'https:' ? 'wss://' : 'ws://') + loc.host + '/api/v1/ws';
}

/** Route of a conversation (optionally opened around a message `seq`). */
export function conversationPath(conversationId, aroundSeq = null) {
  return '/app/messages/' + encodeURIComponent(conversationId) + (aroundSeq !== null && aroundSeq !== undefined ? '?around=' + encodeURIComponent(aroundSeq) : '');
}

/** The other member of a direct conversation. */
export function otherMemberOf(conversation, myId) {
  if (!conversation) return null;
  if (conversation.otherMember) return conversation.otherMember;
  const member = (conversation.members || []).find((m) => m && m.user && m.user.id !== myId);
  return member ? member.user : null;
}

/** Local calendar day of a timestamp (`YYYY-MM-DD`), for date separators. */
export function dayKey(value) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  return date.getFullYear() + '-' + String(date.getMonth() + 1).padStart(2, '0') + '-' + String(date.getDate()).padStart(2, '0');
}

/** "Today", "Yesterday", or the date, for a date separator. */
export function dayLabel(value, now = new Date()) {
  const key = dayKey(value);
  if (!key) return '';
  if (key === dayKey(now)) return strings.messaging.today;
  const yesterday = new Date(now.getTime());
  yesterday.setDate(yesterday.getDate() - 1);
  if (key === dayKey(yesterday)) return strings.messaging.yesterday;
  return new Date(value).toLocaleDateString('en-US', { weekday: 'long', month: 'long', day: 'numeric', year: 'numeric' });
}

/**
 * Messages with date separators: `[{ kind: 'day', key, label }, { kind: 'message', message }, ...]`.
 * @param {object[]} messages in display order
 */
export function withDaySeparators(messages, now = new Date()) {
  const out = [];
  let last = null;
  for (const message of messages || []) {
    const key = dayKey(message.createdUtc);
    if (key && key !== last) {
      out.push({ kind: 'day', key: 'day:' + key, label: dayLabel(message.createdUtc, now) });
      last = key;
    }
    out.push({ kind: 'message', key: message.id || message.localId || message.clientMessageId, message });
  }
  return out;
}

/**
 * The receipt shown under the caller's own message: `sending`, `failed`, `read` (the other
 * member read up to it), `delivered`, or `sent`.
 */
export function receiptOf(message, conversation) {
  if (!message) return null;
  if (message.pending) return message.sendState === 'Failed' ? 'failed' : 'sending';
  const seq = Number(message.seq) || 0;
  if (conversation && typeof conversation.otherLastReadSeq === 'number' && conversation.otherLastReadSeq >= seq) return 'read';
  if (conversation && typeof conversation.otherLastDeliveredSeq === 'number' && conversation.otherLastDeliveredSeq >= seq) return 'delivered';
  return 'sent';
}

/** The conversation list preview text of a `MessageSummary`. */
export function previewText(summary, myId) {
  const t = strings.messaging;
  if (!summary) return t.noMessages;
  if (summary.state === MessageState.Deleted) return t.deletedPreview;
  if (summary.state === MessageState.Removed) return t.removedPreview;
  const text = summary.excerpt || (summary.hasAttachments ? t.attachmentPreview : '');
  return summary.authorUserId && summary.authorUserId === myId ? t.you + ': ' + text : text;
}

/** Whether a conversation is a pending request to the caller. */
export function isPendingRequest(conversation) {
  return Boolean(conversation && conversation.requestState === RequestState.Pending);
}

/** The text for a conversation that cannot be sent to (`viewer.cannotSendReason`). */
export function cannotSendText(reason) {
  const map = strings.messaging.cannotSend;
  return map[reason] || map.default;
}

/** Whether the text of a draft contains a URL (link previews are offered for it). */
export function hasUrl(text) {
  return /https?:\/\/\S+/i.test(String(text || ''));
}

/**
 * The submission mode of the ideas board (`ideas.memberSubmissions`, default `StaffOnly`), read
 * from the property (`ideas.memberSubmissions`).
 */
export function ideaSubmissionsOf(property) {
  const ideas = property && property.ideas && typeof property.ideas === 'object' ? property.ideas : {};
  return ideas.memberSubmissions === 'Direct' ? 'Direct' : 'StaffOnly';
}

/** The property's support email (`contact.supportEmail`), or ''. */
export function supportEmailOf(property) {
  return (property && property.contact && property.contact.supportEmail) || '';
}
