/**
 * Content review admin methods (docs/INTEGRATION.md owner requirement R3): the content changelog queue with its
 * Terms of Service review state, one row, the queue summary, and the queue actions (Dismiss, Hide, Remove, Warn,
 * Mute, Ban), which the server routes through the existing moderation and member actions. Mixed into
 * ApiClient.prototype.
 * (c) 2026 Lattice AI, LLC. Licensed under the MIT License; see LICENSE.md.
 */

import { ENUMERATION_MAX_RESULTS_ADMIN, toEnumerationBody } from './enumeration.js';

const enc = encodeURIComponent;
const ADMIN = { maxLimit: ENUMERATION_MAX_RESULTS_ADMIN };

/** Review states of a content changelog row. */
export const ContentReviewStatuses = Object.freeze(['Pending', 'Clean', 'Flagged', 'Dismissed', 'Actioned']);

/** Terms of Service categories the reviewer reports. */
export const ContentReviewCategories = Object.freeze(['SexualContent', 'HarassmentAndHate', 'Violence', 'IllegalActivity', 'SelfHarm', 'Spam', 'Scam', 'PersonalData']);

/** Severities, lowest first. */
export const ContentReviewSeverities = Object.freeze(['None', 'Low', 'Medium', 'High', 'Critical']);

/** Clients a change can come from. */
export const ContentChangeClients = Object.freeze(['Web', 'Mobile', 'Api', 'System']);

/** Changelog actions. */
export const ContentChangeActions = Object.freeze(['Create', 'Update', 'Delete']);

/** Administrator actions on a row; Hide and Remove need a moderatable target, Warn, Mute, and Ban an author. */
export const ContentReviewActions = Object.freeze(['Dismiss', 'Hide', 'Remove', 'Warn', 'Mute', 'Ban']);

/** Whether an action is available on a row (`moderationTargetType` for Hide and Remove, an author for the rest). */
export function contentReviewActionAvailable(row, action) {
  if (!row || !ContentReviewActions.includes(action)) return false;
  if (action === 'Dismiss') return true;
  if (action === 'Hide' || action === 'Remove') return Boolean(row.moderationTargetType);
  return Boolean((row.author && row.author.id) || (row.actor && row.actor.id));
}

export const contentReviewMethods = {
  /** GET /admin/content-review/summary: counts and whether the reviewer can run (reviewerProblem says why not). */
  adminContentReviewSummary(signal) { return this.get('/admin/content-review/summary', { signal }); },
  /** POST /admin/content-review/enumerate (filters status, category, severity, minimumSeverity, targetType, targetId, client, action, actorUserId, authorUserId). */
  adminEnumerateContentReview(query = {}, signal) { return this.post('/admin/content-review/enumerate', toEnumerationBody(query || {}, ADMIN), { signal }); },
  /** GET /admin/content-review/{id}. */
  adminGetContentChange(id, signal) { return this.get('/admin/content-review/' + enc(id), { signal }); },
  /** POST /admin/content-review/{id}/action with `{ action, reason, note?, untilUtc?, notifyAuthor? }`. */
  adminActOnContentChange(id, body) { return this.post('/admin/content-review/' + enc(id) + '/action', body || {}); }
};
