import { describeContentError } from '../content/errors';
import strings from '../strings';

/**
 * Pure display helpers for direct messages (docs/PHASE5_CONTRACT.md sections 2.1, 2.2, 4.6, and
 * as-built note B). No React Native imports, so they are easy to test.
 */

const t = strings.messaging;

/** The member on the other side of a direct conversation (UserSummary or a placeholder). */
export function otherMemberOf(conversation, meId) {
  if (!conversation) return null;
  if (conversation.otherMember) return conversation.otherMember;
  const members = Array.isArray(conversation.members) ? conversation.members : [];
  const other = members.map((m) => m && m.user).find((u) => u && u.id !== meId);
  return other || null;
}

/** The name to show for a conversation. */
export function conversationName(conversation, meId) {
  const other = otherMemberOf(conversation, meId);
  if (!other || other.isDeleted) return t.deletedMember;
  return other.displayName || other.handle || t.deletedMember;
}

/** The list preview of a conversation's last message (MessageSummary). */
export function previewText(conversation, meId) {
  const last = conversation && conversation.lastMessage;
  if (!last) return t.noMessagesYet;
  let text;
  if (last.state === 'Deleted') text = t.deletedPreview;
  else if (last.state === 'Removed') text = t.removedPreview;
  else text = last.excerpt || (last.hasAttachments ? t.attachment : '');
  return last.authorUserId && meId && last.authorUserId === meId && last.state === 'Visible' ? t.youPrefix(text) : text;
}

/** Whether a message is a tombstone or removed by a moderator (no body, actions, or reactions). */
export function isGone(message) {
  return Boolean(message) && (message.state === 'Deleted' || message.state === 'Removed');
}

/** The body text to render (tombstones and removals show a placeholder). */
export function displayBody(message) {
  if (!message) return '';
  if (message.state === 'Deleted') return t.deleted;
  if (message.state === 'Removed') return t.removed;
  return message.body || '';
}

/** Whether a message was edited (as-built note B: `isEdited`, or an `editedUtc`). */
export function isEdited(message) {
  return Boolean(message) && !isGone(message) && Boolean(message.isEdited || message.editedUtc);
}

/** Whether the original text of an edited message can be shown (as-built note B: `originalBody`). */
export function hasOriginal(message) {
  return isEdited(message) && typeof message.originalBody === 'string' && message.originalBody.length > 0;
}

/**
 * Delivery state of one of my messages: `Sending`, `Failed`, `Read`, `Delivered`, or `Sent`.
 * Read uses `otherLastReadSeq` (null when receipts are off on either side); delivered uses
 * `otherLastDeliveredSeq` when the socket reported it.
 */
export function receiptState(message, conversation) {
  if (!message) return null;
  if (message.pending) return message.sendState === 'Failed' ? 'Failed' : 'Sending';
  const seq = Number(message.seq) || 0;
  const read = conversation && typeof conversation.otherLastReadSeq === 'number' ? conversation.otherLastReadSeq : null;
  if (read !== null && seq > 0 && read >= seq) return 'Read';
  const delivered = conversation && typeof conversation.otherLastDeliveredSeq === 'number' ? conversation.otherLastDeliveredSeq : null;
  if (delivered !== null && seq > 0 && delivered >= seq) return 'Delivered';
  return 'Sent';
}

/** Text for a receipt state. */
export function receiptText(state) {
  switch (state) {
    case 'Sending':
      return t.sending;
    case 'Failed':
      return t.failed;
    case 'Read':
      return t.read;
    case 'Delivered':
      return t.delivered;
    case 'Sent':
      return t.sent;
    default:
      return '';
  }
}

/** The newest of my messages that is not pending (receipts show only on it). */
export function lastOwnSeq(messages, meId) {
  let seq = null;
  for (const m of messages || []) {
    if (!m.pending && m.author && m.author.id === meId && typeof m.seq === 'number' && !isGone(m)) seq = seq === null ? m.seq : Math.max(seq, m.seq);
  }
  return seq;
}

/** The highest `seq` of a message from the other member (what reading the screen marks read). */
export function newestIncomingSeq(messages, meId) {
  let seq = null;
  for (const m of messages || []) {
    if (typeof m.seq === 'number' && (!m.author || m.author.id !== meId)) seq = seq === null ? m.seq : Math.max(seq, m.seq);
  }
  return seq;
}

function dayKey(value) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  return date.getFullYear() + '-' + (date.getMonth() + 1) + '-' + date.getDate();
}

/** "Today", "Yesterday", or a short date for a day separator. */
export function dayLabel(value, now = new Date()) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  if (dayKey(date) === dayKey(now)) return t.today;
  const yesterday = new Date(now.getTime() - 86400000);
  if (dayKey(date) === dayKey(yesterday)) return t.yesterday;
  try {
    return date.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: date.getFullYear() === now.getFullYear() ? undefined : 'numeric' });
  } catch {
    return date.toDateString();
  }
}

/** A short time of day for a message ("6:02 PM"). */
export function timeLabel(value) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  try {
    return date.toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' });
  } catch {
    return '';
  }
}

/**
 * Rows for the inverted message list, newest first: message rows plus a day separator after the
 * last message of each day (which renders above it in the inverted list).
 * @param {object[]} messages In `seq` order (oldest first), pending items last.
 * @returns {{ key: string, type: 'message'|'day', message?: object, label?: string }[]}
 */
export function messageRows(messages, now = new Date()) {
  const rows = [];
  const list = messages || [];
  for (let i = list.length - 1; i >= 0; i -= 1) {
    const m = list[i];
    rows.push({ key: m.id || m.localId || 'm' + i, type: 'message', message: m });
    const older = list[i - 1];
    const day = dayKey(m.createdUtc);
    if (!older || dayKey(older.createdUtc) !== day) rows.push({ key: 'day-' + day + '-' + (m.id || m.localId || i), type: 'day', label: dayLabel(m.createdUtc, now) });
  }
  return rows;
}

/** Member-facing text for a messaging error. */
export function describeMessagingError(error) {
  const code = error && error.code;
  if (code && t.errors[code]) return t.errors[code];
  return describeContentError(error);
}

/** Why the composer is replaced by a notice, or null when sending is allowed. */
export function cannotSendText(conversation) {
  const viewer = (conversation && conversation.viewer) || {};
  if (viewer.canSend !== false) return null;
  const reason = viewer.cannotSendReason;
  return (reason && t.cannotSend[reason]) || t.cannotSend.Unavailable;
}

/** The badge count for the Messages entry: unread conversations plus pending requests. */
export function messagesBadgeCount(unread) {
  if (!unread) return 0;
  return (Number(unread.conversations) || 0) + (Number(unread.requests) || 0);
}
