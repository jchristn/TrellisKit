/**
 * Moderation enums (docs/PHASE2_CONTRACT.md section 9).
 */

/** Report reason codes in display order (section 9.1). */
export const ReportReasons = Object.freeze(['Spam', 'Harassment', 'HateSpeech', 'SexualContent', 'Violence', 'SelfHarm', 'Impersonation', 'IntellectualProperty', 'Other']);

/** Report target types (Phase 2, plus `Thread` from Phase 3 section 11.1). */
export const ReportTargetTypes = Object.freeze(['FeedPost', 'Comment', 'User', 'Thread']);

/** Moderation action history values added by forums (PHASE3_CONTRACT.md section 11.2). */
export const ForumModerationActions = Object.freeze(['Pin', 'Unpin', 'Lock', 'Unlock', 'Move', 'EditMetadata', 'ForumMute', 'ForumUnmute']);

/** Moderation actions a moderator can take (section 9.3). */
export const ModerationActions = Object.freeze(['Hide', 'Unhide', 'Remove', 'Restore']);

/** Report details limits (section 3.5). */
export const REPORT_DETAILS_MAX = 1000;
export const REPORT_OTHER_MIN = 10;

/** Moderation reason and note limits (section 3.5). */
export const MODERATION_REASON_MIN = 3;
export const MODERATION_TEXT_MAX = 500;

/**
 * Validate a report before sending. Returns a map of field to problem code
 * (`detailsRequired`, `detailsTooLong`, `reasonRequired`).
 * @param {{ reason: string, details?: string }} report
 */
export function reportProblems({ reason, details = '' }) {
  const problems = {};
  const text = String(details || '').trim();
  if (!ReportReasons.includes(reason)) problems.reason = 'reasonRequired';
  if (reason === 'Other' && Array.from(text).length < REPORT_OTHER_MIN) problems.details = 'detailsRequired';
  if (Array.from(text).length > REPORT_DETAILS_MAX) problems.details = 'detailsTooLong';
  return problems;
}

/**
 * Actions available for a target in a given state: Hidden can be unhidden or removed, Removed
 * can be restored, visible content can be hidden or removed.
 * @param {string} state FeedPost or Comment state (`Published`, `Visible`, `Hidden`, `Removed`, ...)
 * @returns {string[]}
 */
export function moderationActionsFor(state) {
  if (state === 'Removed') return ['Restore'];
  if (state === 'Hidden') return ['Unhide', 'Remove'];
  return ['Hide', 'Remove'];
}

/** Default `notifyAuthor` per action (section 9.3). */
export function defaultNotifyAuthor(action) {
  return action === 'Remove' || action === 'Restore';
}

/** Staff roles that cannot be blocked (section 9.2). */
export const StaffRoles = Object.freeze(['SystemAdmin', 'Moderator', 'FeedModerator', 'DirectoryModerator', 'EventManager', 'LibraryEditor', 'SupportAgent', 'Communicator', 'RankAwarder']);

/**
 * Whether a `Me` or member with `roles` is staff.
 * @param {{ roles?: Array<string|{ role: string }>, isStaff?: boolean }} user
 */
export function isStaffUser(user) {
  if (!user) return false;
  if (user.isStaff === true) return true;
  return (user.roles || []).some((r) => StaffRoles.includes(typeof r === 'string' ? r : r && r.role));
}
