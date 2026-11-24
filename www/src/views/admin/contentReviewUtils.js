/**
 * Helpers of the content review queue (docs/INTEGRATION.md owner requirement R3): links to the changed content,
 * badge tones, the enumeration filters, and the action body.
 * (c) 2026 Lattice AI, LLC. Licensed under the MIT License; see LICENSE.md.
 */

import { postPath } from '../../utils/engagement.js';
import { appThreadPath } from '../../utils/forums.js';

/** Where "open the content" goes for a row, or null when the type has no page. */
export function contentLink(row) {
  if (!row || !row.targetId) return null;
  const ctxType = row.contextType;
  const ctxId = row.contextId;
  switch (row.targetType) {
    case 'FeedPost':
      return postPath(row.targetId);
    case 'Comment':
      if (ctxType === 'FeedPost' && ctxId) return postPath(ctxId, row.targetId);
      if (ctxType === 'Thread' && ctxId) return appThreadPath(ctxId, row.targetId);
      return null;
    case 'Thread':
      return appThreadPath(row.targetId);
    case 'User':
      return '/admin/members?member=' + encodeURIComponent(row.targetId);
    case 'Message':
      return '/admin/moderation';
    case 'Idea':
      return '/app/ideas/' + encodeURIComponent(row.targetId);
    case 'Event':
      return '/app/events/' + encodeURIComponent(row.targetId);
    case 'Ticket':
      return '/admin/support?ticket=' + encodeURIComponent(row.targetId);
    case 'TicketMessage':
      return ctxId ? '/admin/support?ticket=' + encodeURIComponent(ctxId) : null;
    default:
      return null;
  }
}

/** Where "open the member" goes for a row's author (or actor), or null. */
export function memberLink(row) {
  const who = (row && row.author) || (row && row.actor);
  return who && who.id ? '/admin/members?member=' + encodeURIComponent(who.id) : null;
}

/** Badge tone of a review status. */
export function reviewTone(status) {
  switch (status) {
    case 'Flagged':
      return 'danger';
    case 'Pending':
      return 'warning';
    case 'Clean':
      return 'success';
    case 'Actioned':
      return 'info';
    default:
      return 'neutral';
  }
}

/** Badge tone of a severity. */
export function severityTone(severity) {
  switch (severity) {
    case 'Critical':
    case 'High':
      return 'danger';
    case 'Medium':
      return 'warning';
    case 'Low':
      return 'info';
    default:
      return 'neutral';
  }
}

/** Enumeration filters from the filter form (empty values dropped). */
export function queueFilters(form) {
  const f = form || {};
  const filters = {};
  if (f.status) filters.status = [f.status];
  if (f.category) filters.category = [f.category];
  if (f.severity) filters.minimumSeverity = f.severity;
  if (f.targetType) filters.targetType = [f.targetType];
  if (f.client) filters.client = [f.client];
  if (f.actor && f.actor.trim()) filters.actorUserId = f.actor.trim();
  return filters;
}

/**
 * The action request body, or `{ error }` naming the invalid field: reason 3 to 500 characters (optional for
 * Dismiss), note at most 2000, and for Mute an optional future `until` (a datetime-local value).
 */
export function actionBody(action, form, now = new Date()) {
  const reason = ((form && form.reason) || '').trim();
  const note = ((form && form.note) || '').trim();
  if (action !== 'Dismiss' && (reason.length < 3 || reason.length > 500)) return { error: 'reason' };
  if (reason.length > 500) return { error: 'reason' };
  if (note.length > 2000) return { error: 'note' };
  const body = { action };
  if (reason) body.reason = reason;
  if (note) body.note = note;
  if (action === 'Mute' && form && form.until) {
    const until = new Date(form.until);
    if (Number.isNaN(until.getTime()) || until <= now) return { error: 'until' };
    body.untilUtc = until.toISOString();
  }
  return { body };
}

/** The first line of a snapshot without its "column: " prefix, cut for the table. */
export function snapshotExcerpt(snapshot, max = 120) {
  const text = String(snapshot || '').split('\n').map((line) => line.replace(/^[a-z]+: /, '')).join(' ').trim();
  return text.length > max ? text.slice(0, max - 1) + '…' : text;
}
