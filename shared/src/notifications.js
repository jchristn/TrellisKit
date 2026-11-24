/**
 * Notification helpers (docs/PHASE2_CONTRACT.md section 8).
 */

/** Categories in display order (section 8.5). */
export const NotificationCategory = Object.freeze({
  Replies: 'Replies',
  Mentions: 'Mentions',
  Reactions: 'Reactions',
  FeedPosts: 'FeedPosts',
  Following: 'Following',
  Forums: 'Forums',
  Moderation: 'Moderation',
  Account: 'Account'
});

/** Categories in display order; `Following` and `Forums` arrive in Phase 3 (PHASE3_CONTRACT.md section 9.3). */
export const NotificationCategoryOrder = Object.freeze(['Replies', 'Mentions', 'Reactions', 'FeedPosts', 'Following', 'Forums', 'Moderation', 'Account']);

/**
 * Categories whose email is an opt-in digest rather than one email per event (PHASE3_CONTRACT.md
 * section 9.3: `Following` and `Forums`, weekly by default). Clients label the email toggle so.
 */
export const DigestEmailCategories = Object.freeze(['Following', 'Forums']);

/** Whether a category's email arrives as a digest. */
export function isDigestEmailCategory(category) {
  return DigestEmailCategories.includes(category);
}

/** Notification types the Phase 2 clients deep link (others render a generic row). */
export const NotificationType = Object.freeze({
  PostComment: 'PostComment',
  CommentReply: 'CommentReply',
  Mention: 'Mention',
  Reaction: 'Reaction',
  NewFeedPost: 'NewFeedPost',
  ModerationAction: 'ModerationAction',
  ModerationOverdue: 'ModerationOverdue',
  MembershipExpiring: 'MembershipExpiring',
  ThreadReply: 'ThreadReply',
  NewThreadInForum: 'NewThreadInForum',
  AnswerAccepted: 'AnswerAccepted',
  FeedbackReviewed: 'FeedbackReviewed',
  PollClosed: 'PollClosed',
  ThreadPromoted: 'ThreadPromoted',
  ThreadMoved: 'ThreadMoved'
});

/** Default notification hours for new accounts (section 8.6). */
export const DEFAULT_NOTIFICATION_HOURS = Object.freeze({ enabled: true, start: '07:00', end: '22:00' });

/** Polling interval for the unread count (section 8.4). */
export const UNREAD_POLL_MS = 60000;

/**
 * Badge text for an unread count: '' for zero, '99+' above 99.
 * @param {number} count
 * @returns {string}
 */
export function formatUnreadBadge(count) {
  const n = Number(count) || 0;
  if (n <= 0) return '';
  return n > 99 ? '99+' : String(n);
}

/**
 * Whether a value is a 24-hour `HH:mm` time.
 * @param {string} value
 */
export function isTimeOfDay(value) {
  return typeof value === 'string' && /^([01]\d|2[0-3]):[0-5]\d$/.test(value);
}

/**
 * Minutes after midnight for `HH:mm`, or null.
 * @param {string} value
 */
export function timeToMinutes(value) {
  if (!isTimeOfDay(value)) return null;
  const [h, m] = value.split(':').map(Number);
  return h * 60 + m;
}

/**
 * Whether pushes are allowed at a local minute of the day for a notification hours setting.
 * `start` equal to `end`, or `enabled: false`, means all day; the window may cross midnight.
 * @param {{ enabled: boolean, start: string, end: string }} hours
 * @param {number} minute 0 to 1439
 */
export function isWithinNotificationHours(hours, minute) {
  if (!hours || hours.enabled === false) return true;
  const start = timeToMinutes(hours.start);
  const end = timeToMinutes(hours.end);
  if (start === null || end === null || start === end) return true;
  if (start < end) return minute >= start && minute < end;
  return minute >= start || minute < end;
}

/**
 * Apply a preferences change locally (for optimistic toggles). `change` has the PUT shape.
 * Turning `inApp` off also turns `push` off (push requires an in-app notification).
 * @param {object} doc
 * @param {object} change
 */
export function applyPreferenceChange(doc, change) {
  const next = { ...doc };
  if (change.categories) {
    next.categories = (doc.categories || []).map((row) => {
      const edit = change.categories.find((c) => c.category === row.category);
      if (!edit) return row;
      const merged = { ...row, ...edit };
      if (edit.inApp === false) merged.push = false;
      return merged;
    });
  }
  if (change.notificationHours) next.notificationHours = { ...(doc.notificationHours || {}), ...change.notificationHours };
  if (typeof change.pushPaused === 'boolean') next.pushPaused = change.pushPaused;
  // Phase 3 resolved decision 8: a member setting turns auto-follow on comment off.
  if (typeof change.autoFollowOnComment === 'boolean') next.autoFollowOnComment = change.autoFollowOnComment;
  return next;
}
