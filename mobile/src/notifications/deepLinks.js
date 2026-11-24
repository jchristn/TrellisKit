/**
 * Deep links from notifications and pushes (docs/PHASE2_CONTRACT.md sections 8.1 and 8.8). The
 * server sends an app-relative `path` that the app passes to Expo Router unchanged. Only the
 * routes the app knows are followed; anything else (a reserved type from a later phase, or a
 * malformed value) opens the notification center instead of a broken screen.
 */

const ID = '[A-Za-z0-9_-]{1,64}';

const KNOWN = [
  new RegExp('^/admin/metrics/[A-Za-z0-9_-]{1,64}$'),
  new RegExp('^/feed$'),
  new RegExp('^/feed/fpo_' + ID + '$'),
  new RegExp('^/feed/fpo_' + ID + '\\?comment=cmt_' + ID + '$'),
  new RegExp('^/notifications$'),
  new RegExp('^/account/membership$'),
  new RegExp('^/account/notifications$'),
  new RegExp('^/members/usr_' + ID + '$'),
  // Phase 3 forum paths (docs/PHASE3_CONTRACT.md section 9.3).
  new RegExp('^/forums$'),
  new RegExp('^/forums/frm_' + ID + '$'),
  new RegExp('^/threads/thr_' + ID + '$'),
  new RegExp('^/threads/thr_' + ID + '\\?comment=cmt_' + ID + '$'),
  // Phase 4 paths (docs/PHASE4_CONTRACT.md sections 8.1 and 16).
  new RegExp('^/directory$'),
  new RegExp('^/venues/ven_' + ID + '$'),
  new RegExp('^/instructors/ins_' + ID + '$'),
  new RegExp('^/venues/ven_' + ID + '\\?comment=cmt_' + ID + '$'),
  // Venue reviews (docs/PHASE4_CONTRACT.md section 3.12): VenueReview and VenueReviewReply.
  new RegExp('^/venues/ven_' + ID + '\\?review=vrv_' + ID + '$'),
  new RegExp('^/instructors/ins_' + ID + '\\?comment=cmt_' + ID + '$'),
  new RegExp('^/me/directory/submissions$'),
  new RegExp('^/directory/submissions$'),
  new RegExp('^/events$'),
  new RegExp('^/events/evt_' + ID + '$'),
  new RegExp('^/events/evt_' + ID + '\\?comment=cmt_' + ID + '$'),
  new RegExp('^/me/events/proposals$'),
  new RegExp('^/events/proposals$'),
  new RegExp('^/library$'),
  new RegExp('^/series/ser_' + ID + '$'),
  new RegExp('^/series/ser_' + ID + '\\?comment=cmt_' + ID + '$'),
  new RegExp('^/lessons/les_' + ID + '$'),
  new RegExp('^/lessons/les_' + ID + '\\?comment=cmt_' + ID + '$'),
  // Phase 5 paths (docs/PHASE5_CONTRACT.md section 9.1).
  new RegExp('^/messages$'),
  new RegExp('^/messages/requests$'),
  new RegExp('^/messages/cnv_' + ID + '$'),
  new RegExp('^/support$'),
  new RegExp('^/support/tickets/tkt_' + ID + '$'),
  new RegExp('^/ideas$'),
  new RegExp('^/ideas/ida_' + ID + '$'),
  new RegExp('^/ideas/ida_' + ID + '\\?comment=cmt_' + ID + '$'),
  new RegExp('^/announcements$'),
  new RegExp('^/announcements/bct_' + ID + '$')
];

/** Server paths that map to a different app route (the server's path, then the app's). */
const ALIASES = [
  ['/directory', '/map?section=places'],
  ['/events', '/map?section=events'],
  ['/me/directory/submissions', '/directory/submissions'],
  ['/me/events/proposals', '/events/proposals']
];

/** Staff paths without a native destination still open the notification's native detail. */
const STAFF = /^\/admin(\/[A-Za-z0-9_\/-]*)?(\?[A-Za-z0-9_=&%-]*)?$/;

/**
 * Recognize a staff path without treating it as permission to launch a browser.
 * Kept as a path classifier for callers migrating from older versions.
 * @param {unknown} path
 * @returns {string|null}
 */
export function staffWebPath(path) {
  if (typeof path !== 'string') return null;
  const trimmed = path.trim();
  return STAFF.test(trimmed) ? trimmed : null;
}

/** The notification center, used when a path cannot be followed. */
export const FallbackPath = '/notifications';

/**
 * The route to open for a notification path, or null when it is not one the app knows.
 * @param {unknown} path
 * @returns {string|null}
 */
export function resolveNotificationPath(path) {
  if (typeof path !== 'string') return null;
  const trimmed = path.trim();
  if (!trimmed.startsWith('/') || trimmed.startsWith('//')) return null;
  if (!KNOWN.some((pattern) => pattern.test(trimmed))) return null;
  const alias = ALIASES.find(([from]) => from === trimmed);
  return alias ? alias[1] : trimmed;
}

/** Bound notification text passed through navigation, rendered as native text only. */
function detailParams(item) {
  const text = (value, max) => typeof value === 'string' ? value.slice(0, max) : '';
  return { notificationId: text(item.id || item.notificationId, 128), notificationTitle: text(item.title, 1000), notificationBody: text(item.body, 8000) };
}

/** One policy for notification rows and pushes: recognized native route, native staff detail, or center. */
export function notificationDestination(item = {}) {
  const path = resolveNotificationPath(item.path);
  if (path) return path.startsWith('/admin/metrics/') ? { pathname: path, params: detailParams(item) } : path;
  if (staffWebPath(item.path)) return { pathname: '/notification-detail', params: detailParams(item) };
  return FallbackPath;
}

/** Data of a tapped push, including its displayed text for native staff details after a cold start. */
export function pushTarget(response) {
  const content = response?.notification?.request?.content;
  const data = content?.data;
  if (!data || typeof data !== 'object') return null;
  const item = { ...data, title: content.title || data.title, body: content.body || data.body };
  return {
    notificationId: typeof data.notificationId === 'string' ? data.notificationId : null,
    path: notificationDestination(item)
  };
}

/** Badge text: counts above 99 show as "99+" (contract 8.3). */
export function badgeText(count) {
  if (!count || count < 1) return null;
  return count > 99 ? '99+' : String(count);
}
