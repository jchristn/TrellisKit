import { Role, isStaffUser } from '@trelliskit/shared';
import strings from '../strings.js';
import { isSystemAdmin } from './onboarding.js';
import { formatDateTime } from './format.js';

/** Roles that open the moderation console (contract section 10.2). */
export const MODERATOR_ROLES = Object.freeze([Role.Moderator, Role.FeedModerator]);

/**
 * Whether a ticket message is the opening message the server stores with a new ticket (a member
 * Reply written in the same transaction, so it shares the ticket's creation time). The ticket views
 * show it once, as the original request, with its attachments.
 * @param {{ createdUtc?: string }|null} ticket
 * @param {{ kind?: string, authorRole?: string, createdUtc?: string }|null} message
 * @returns {boolean}
 */
export function isOpeningTicketMessage(ticket, message) {
  return Boolean(ticket && message && ticket.createdUtc && message.kind !== 'Note' && message.authorRole === 'Member' && message.createdUtc === ticket.createdUtc);
}

/** Whether `Me` holds a Moderator or FeedModerator role (any scope). */
export function isModerator(me) {
  return Boolean(me && Array.isArray(me.roles) && me.roles.some((r) => r && MODERATOR_ROLES.includes(r.role)));
}

/**
 * Phase 4 staff areas and the role that opens each (docs/PHASE4_CONTRACT.md section 1.1 and
 * resolved decision 3): `directory` (DirectoryModerator), `events` (EventManager), `library`
 * (LibraryEditor). SystemAdmin opens every area.
 */
export const STAFF_AREA_ROLES = Object.freeze({ directory: Role.DirectoryModerator, events: Role.EventManager, library: Role.LibraryEditor });

/** The Phase 4 staff areas `Me` holds a role for (`['library', ...]`). */
export function staffAreas(me) {
  const roles = me && Array.isArray(me.roles) ? me.roles.map((r) => (typeof r === 'string' ? r : r && r.role)) : [];
  return Object.keys(STAFF_AREA_ROLES).filter((area) => roles.includes(STAFF_AREA_ROLES[area]));
}

/** Whether `Me` may open the admin console without SystemAdmin (a moderator or a Phase 4 staff role). */
export function isConsoleStaff(me) {
  return isModerator(me) || staffAreas(me).length > 0;
}

/** Whether `Me` holds a forum `Moderator` assignment (any forum or `*`, PHASE3_CONTRACT.md section 1.1). */
export function isForumModerator(me) {
  // A Moderator assignment on the Messaging resource type moderates direct message reports only.
  return Boolean(me && Array.isArray(me.roles) && me.roles.some((r) => r && r.role === Role.Moderator && r.resourceType !== 'Messaging'));
}

/** Whether a member is muted right now (`isMuted`, with `mutedUntilUtc` in the future when set). */
export function isMutedNow(me, now = new Date()) {
  const flags = (me && me.flags) || {};
  if (!flags.isMuted) return false;
  if (!flags.mutedUntilUtc) return true;
  return new Date(flags.mutedUntilUtc).getTime() > now.getTime();
}

/**
 * Posting permissions for the signed-in member (contract section 1.1), used before a post exists.
 * Once a post or comment exists, menus read its `viewer` object instead.
 * @param {object} me
 */
export function postingPermissions(me) {
  const flags = (me && me.flags) || {};
  const admin = isSystemAdmin(me);
  const muted = !admin && isMutedNow(me);
  const canPost = admin || flags.canPost !== false;
  const canCreateFeedPost = admin || (Boolean(flags.canPostToFeed) && canPost);
  const canAttachMedia = admin || (canPost && flags.canPostMedia !== false && !muted);
  const canAttachVideo = admin || (canAttachMedia && flags.canUploadVideo !== false);
  return {
    isAdmin: admin,
    isStaff: admin || isStaffUser(me),
    muted,
    mutedUntilUtc: muted ? flags.mutedUntilUtc || null : null,
    canPost: canPost && !muted,
    postingBlocked: !canPost,
    canCreateFeedPost,
    canAttachMedia,
    canAttachVideo
  };
}

/** Text for a muted banner. */
export function mutedText(perms) {
  return strings.composer.mutedBanner(perms.mutedUntilUtc ? formatDateTime(perms.mutedUntilUtc) : '');
}

/**
 * Website route for a notification `path` (contract section 8.1): app-relative paths are
 * prefixed with `/app`; mobile-only paths map to the web equivalent. Returns null when the path
 * is missing or unsafe.
 * @param {string} path
 */
export function webPathFor(path) {
  if (typeof path !== 'string' || !path.startsWith('/') || path.startsWith('//')) return null;
  if (path.startsWith('/account/membership')) return '/app/membership';
  if (path.startsWith('/account/notifications')) return '/app/settings/notifications';
  // The member's own lists live under /me on the API and in the app shell on the website.
  if (path.startsWith('/me/directory/submissions')) return '/app/directory/submissions';
  if (path.startsWith('/me/events/proposals')) return '/app/events/proposals';
  // Staff notifications (Phase 5 support) point at the admin console.
  if (path === '/admin' || path.startsWith('/admin/')) return path;
  return '/app' + path;
}

/** Route for a feed post and an optional comment permalink. */
export function postPath(postId, commentId) {
  return '/app/feed/' + encodeURIComponent(postId) + (commentId ? '?comment=' + encodeURIComponent(commentId) : '');
}

/** Route for a member profile. */
export function memberPath(userId) {
  return '/app/members/' + encodeURIComponent(userId);
}

/**
 * Message for an API error using a code map (`{ Code: 'text' | (seconds) => text }`), with the
 * `RateLimited` Retry-After value and the server's message as fallbacks.
 * @param {any} err
 * @param {Record<string, string|((seconds: number|null) => string)>} map
 * @param {string} [fallback]
 */
export function errorText(err, map = {}, fallback = strings.common.errorGeneric) {
  const code = err && err.code;
  const entry = (code && map[code]) || (code === 'RateLimited' ? strings.composer.errors.RateLimited : null);
  if (typeof entry === 'function') return entry(err.retryAfterSeconds || null);
  if (entry) return entry;
  if (err && err.status === 0 && map.network) return map.network;
  return (err && err.message) || fallback;
}

/**
 * The composer error to show: the "wait for uploads" notice only while an upload is still in
 * flight, so it clears itself once the uploads finish instead of lingering until the next submit.
 * @param {string} error
 * @param {{ busy: boolean }} uploads
 */
export function composerErrorText(error, uploads) {
  if (error === strings.composer.waitForUploads && !(uploads && uploads.busy)) return '';
  return error;
}

/** The URL of a media file: absolute URLs as is, root-relative ones against the page origin (same-origin API). */
export function mediaUrl(url) {
  if (!url || typeof url !== 'string') return null;
  if (/^https?:\/\//i.test(url) || url.startsWith('/')) return url;
  return null;
}

/** Display name for a UserSummary, honoring deleted accounts. */
export function authorName(user) {
  if (!user || user.isDeleted) return strings.feed.deletedMember;
  return user.displayName || (user.handle ? '@' + user.handle : strings.common.unknown);
}

/** The device time zone (IANA id), or 'UTC' when unavailable. */
export function deviceTimeZone() {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
  } catch {
    return 'UTC';
  }
}

/** IANA time zones the browser knows, always including `current`. */
export function timeZoneOptions(current) {
  let zones = [];
  try {
    if (typeof Intl.supportedValuesOf === 'function') zones = Intl.supportedValuesOf('timeZone');
  } catch {
    zones = [];
  }
  const all = new Set(zones.length ? zones : ['UTC']);
  if (current) all.add(current);
  all.add(deviceTimeZone());
  return Array.from(all).sort();
}
