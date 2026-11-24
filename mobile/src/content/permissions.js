import { StaffRoles } from '@trelliskit/shared';
import { isSystemAdmin } from '../auth/routing';

/**
 * Client-side posting capabilities from Me (docs/PHASE2_CONTRACT.md section 1.1). These only
 * decide what the app offers; the server enforces everything. Per-item actions always come from
 * the item's `viewer` object instead of being re-derived here.
 */

function flagsOf(user) {
  return (user && user.flags) || {};
}

/** Whether the member is muted right now (`isMuted`, with `mutedUntilUtc` still in the future when set). */
export function isMuted(user, now = Date.now()) {
  const flags = flagsOf(user);
  if (!flags.isMuted) return false;
  if (!flags.mutedUntilUtc) return true;
  const until = Date.parse(flags.mutedUntilUtc);
  return !Number.isFinite(until) || until > now;
}

/**
 * Staff roles (docs/ENUMERATION_AND_METADATA.md section 1; Phase 4 section 1.1 and Phase 5 as-built
 * note D): every global staff role counts, the same list the server uses for `isStaff` and block
 * protection.
 */
export const STAFF_ROLES = StaffRoles;

/** Whether the member holds any staff role (SystemAdmin, Moderator, FeedModerator, DirectoryModerator, EventManager, LibraryEditor, SupportAgent, or Communicator). */
export function isStaffUser(user) {
  return Boolean(user && Array.isArray(user.roles) && user.roles.some((r) => r && STAFF_ROLES.includes(typeof r === 'string' ? r : r.role)));
}

/** Whether `canPost` allows writing at all (default true). */
export function canWrite(user) {
  return isSystemAdmin(user) || flagsOf(user).canPost !== false;
}

/** Whether the member may create feed posts: SystemAdmin, or canPostToFeed and canPost, and not muted. */
export function canCreateFeedPost(user, now) {
  if (isSystemAdmin(user)) return true;
  const flags = flagsOf(user);
  return Boolean(flags.canPostToFeed) && flags.canPost !== false && !isMuted(user, now);
}

/** Whether the member may attach images: canPost, canPostMedia, not muted (or SystemAdmin). */
export function canAttachMedia(user, now) {
  if (isSystemAdmin(user)) return true;
  const flags = flagsOf(user);
  return flags.canPost !== false && flags.canPostMedia !== false && !isMuted(user, now);
}

/** Whether the member may attach video: the image rule plus canUploadVideo (or SystemAdmin). */
export function canAttachVideo(user, now) {
  if (isSystemAdmin(user)) return true;
  return canAttachMedia(user, now) && flagsOf(user).canUploadVideo !== false;
}

/**
 * Attach permissions for a composer. The server's `viewer` flags win when present (they reflect
 * the caller's current flags); Me is the fallback for a new post.
 */
export function attachPermissions(user, viewer) {
  const media = viewer && typeof viewer.canAttachMedia === 'boolean' ? viewer.canAttachMedia : canAttachMedia(user);
  const video = viewer && typeof viewer.canAttachVideo === 'boolean' ? viewer.canAttachVideo : canAttachVideo(user);
  return { media, video: media && video };
}

/** Composer limits from the contract defaults (the server enforces the configured values). */
export const ComposerLimits = Object.freeze({
  postTitle: 140,
  postBody: 10000,
  commentBody: 5000,
  postAttachments: 10,
  postVideos: 1,
  commentAttachments: 1,
  altText: 1000,
  reportDetails: 1000,
  reportOtherMinimum: 10
});
