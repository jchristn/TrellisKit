/**
 * Forum helpers (docs/PHASE3_CONTRACT.md): enums, limits, the "one conversation" comment target
 * rule (section 0.2), paths (section 19), forum grouping, draft validation, and the moderator and
 * feedback menus driven by the server's `viewer` object (section 1.4). No React, browser, or
 * Node APIs, so the website and the mobile app share it. Forum names, sections, and reactions
 * always come from the server.
 */

import { ThreadType } from './enums.js';

/** Thread types in display order (section 2.2); `ThreadType` itself lives in enums.js. */
export const ThreadTypes = Object.freeze(['Discussion', 'Question', 'Poll', 'Feedback']);

/** Thread kinds; clients skip kinds they do not know (section 2.2). */
export const ThreadKind = Object.freeze({ Original: 'Original', LinkedFeedPost: 'LinkedFeedPost' });
export const KNOWN_THREAD_KINDS = Object.freeze(['Original', 'LinkedFeedPost']);

/** Thread states (section 2.2). */
export const ThreadStates = Object.freeze(['Processing', 'Published', 'Hidden', 'Removed']);

/** Thread orderings (section 4.2). `Relevance` is valid only with `search`. */
export const ThreadOrdering = Object.freeze({ Active: 'Active', Hot: 'Hot', CreatedDescending: 'CreatedDescending', CreatedAscending: 'CreatedAscending', Top: 'Top', Relevance: 'Relevance' });

/** The orderings the forum screens offer, in display order (section 19: Active, Hot, New, Top). */
export const MemberThreadOrderings = Object.freeze(['Active', 'Hot', 'CreatedDescending', 'Top']);

/** Forum orderings (section 3.1). */
export const ForumOrderings = Object.freeze(['SortOrderAscending', 'NameAscending', 'ActivityDescending', 'CreatedDescending', 'CreatedAscending']);

/** Feedback review statuses (section 7). */
export const FeedbackStatuses = Object.freeze(['Open', 'InReview', 'Reviewed', 'Closed']);

/** Who may create threads in a forum (section 2.1). */
export const ThreadCreationModes = Object.freeze(['Members', 'Moderators', 'Admins']);

/** Audiences (TRELLISKIT.md section 7). */
export const Audiences = Object.freeze(['Public', 'SignedIn', 'Members']);

/** Fixed limits from the contract defaults (the server enforces the configured values). */
export const FORUM_LIMITS = Object.freeze({
  titleMin: 3,
  titleMax: 140,
  bodyMax: 10000,
  attachments: 10,
  videosPerThread: 1,
  focusMax: 500,
  lockReasonMax: 500,
  promoteTitleMax: 140,
  promoteExcerptMax: 300,
  forumNameMin: 2,
  forumNameMax: 60,
  forumDescriptionMax: 500,
  forumGuidelinesMax: 5000,
  forumSectionMax: 60,
  maxForums: 200,
  forumIdsFilterMax: 50,
  pinnedPerForum: 5
});

/** Forum slugs: 2 to 60 of `a-z 0-9 -` (section 2.1). */
export const FORUM_SLUG_PATTERN = /^[a-z0-9-]{2,60}$/;

/** Whether a client renders this thread (known kind and an id). */
export function isKnownThread(thread) {
  return Boolean(thread && thread.id && KNOWN_THREAD_KINDS.includes(thread.kind || ThreadKind.Original));
}

/** Whether a thread is a discussion opened from a feed post (section 8.2). */
export function isLinkedThread(thread) {
  return Boolean(thread && thread.kind === ThreadKind.LinkedFeedPost);
}

const PREFIX_TYPES = [['thr_', 'Thread'], ['fpo_', 'FeedPost'], ['cmt_', 'Comment']];

/**
 * The conversation target of a card (section 0.2): its `commentTarget` when present, else the
 * object itself (the type comes from `fallbackType` or the id prefix). Clients load and post
 * comments, react, and report the conversation against this target, never the card's own id.
 * @param {object|string} item A Thread, ThreadSummary, FeedPost, or a bare id.
 * @param {string} [fallbackType]
 * @returns {{ type: string, id: string }|null}
 */
export function commentTargetOf(item, fallbackType) {
  if (!item) return null;
  if (typeof item === 'string') {
    const match = PREFIX_TYPES.find(([prefix]) => item.startsWith(prefix));
    const type = fallbackType || (match ? match[1] : null);
    return type ? { type, id: item } : null;
  }
  const t = item.commentTarget;
  if (t && t.type && t.id) return { type: t.type, id: t.id };
  if (!item.id) return null;
  return commentTargetOf(item.id, fallbackType);
}

/** App-relative path of a forum (`/forums/{idOrSlug}`); the website prefixes `/app`. */
export function forumPath(idOrSlug) {
  return '/forums/' + encodeURIComponent(idOrSlug);
}

/** App-relative path of a thread, optionally focused on a comment (`?comment=`). */
export function threadPath(id, commentId) {
  return '/threads/' + encodeURIComponent(id) + (commentId ? '?comment=' + encodeURIComponent(commentId) : '');
}

/**
 * App-relative path of the original object of a conversation (section 8.4: notifications and
 * permalinks always point at the comment owner): `/threads/{id}` or `/feed/{id}`.
 * @param {{ type: string, id: string }} target
 * @param {string} [commentId]
 */
export function conversationPath(target, commentId) {
  if (!target || !target.id) return null;
  const q = commentId ? '?comment=' + encodeURIComponent(commentId) : '';
  if (target.type === 'Thread') return '/threads/' + encodeURIComponent(target.id) + q;
  if (target.type === 'FeedPost') return '/feed/' + encodeURIComponent(target.id) + q;
  if (target.type === 'Idea') return '/ideas/' + encodeURIComponent(target.id) + q;
  return null;
}

/**
 * Where a card opens: a promoted feed card opens its thread (section 8.4), a feed post opens
 * itself, a thread opens its own page.
 * @param {object} card FeedPost or Thread.
 */
export function cardPath(card) {
  if (!card || !card.id) return null;
  if (card.kind === 'PromotedThread' && card.source && card.source.id) return threadPath(card.source.id);
  if (card.id.startsWith('thr_')) return threadPath(card.id);
  return '/feed/' + encodeURIComponent(card.id);
}

/**
 * Group forums for the index (section 2.1): by `section`, sections in the order of their first
 * forum's `sortOrder`, forums by `sortOrder` then name. Forums without a section form the group
 * with `section: ''`.
 * @param {object[]} forums
 * @returns {{ section: string, forums: object[] }[]}
 */
/** The ways the forum index can be shown, in display order. */
export const ForumIndexViews = Object.freeze(['list', 'cards', 'gallery']);

/** A stored forum index view, or `list` when the value is not one of ForumIndexViews. */
export function forumIndexView(value) {
  return ForumIndexViews.includes(value) ? value : 'list';
}

export function groupForumsBySection(forums) {
  const sorted = (forums || []).filter((f) => f && f.id).slice().sort((a, b) => (Number(a.sortOrder) || 0) - (Number(b.sortOrder) || 0) || String(a.name || '').localeCompare(String(b.name || '')));
  const groups = [];
  const index = new Map();
  for (const forum of sorted) {
    const section = String(forum.section || '').trim();
    if (!index.has(section)) {
      index.set(section, groups.length);
      groups.push({ section, forums: [] });
    }
    groups[index.get(section)].forums.push(forum);
  }
  return groups;
}

/** Split a forum thread page into pinned and other threads (section 4.2 `includePinned`). */
export function splitPinned(threads) {
  const pinned = [];
  const rest = [];
  for (const t of threads || []) (t && t.isPinned ? pinned : rest).push(t);
  return { pinned, rest };
}

/** Unread badge text for a forum (capped at 99 by the server): '' when none or anonymous. */
export function unreadBadge(count) {
  const n = Number(count) || 0;
  if (n <= 0) return '';
  return n >= 99 ? '99+' : String(n);
}

function scalarCount(text) {
  return Array.from(String(text || '')).length;
}

/**
 * Validate a thread draft before sending (sections 2.2 and 4.4). Returns `{ field: code }`:
 * `typeRequired`, `typeNotAllowed`, `titleTooShort`, `titleTooLong`, `bodyRequired`, `bodyTooLong`,
 * `videoRequired`, `tooManyAttachments`, `tooManyVideos`, `focusTooLong`, and `poll` with the
 * first poll problem code (see `validatePollDraft` in polls.js).
 * @param {{ type: string, title: string, body: string, mediaCount?: number, videoCount?: number, focus?: string, allowedTypes?: string[], pollProblems?: object }} draft
 * @param {{ bodyMax?: number, attachments?: number, videosPerThread?: number }} [limits]
 */
export function validateThreadDraft(draft, limits = {}) {
  const { type, title, body, mediaCount = 0, videoCount = 0, focus = '', allowedTypes, pollProblems } = draft || {};
  const bodyMax = limits.bodyMax || FORUM_LIMITS.bodyMax;
  const maxAttachments = limits.attachments === undefined ? FORUM_LIMITS.attachments : limits.attachments;
  const maxVideos = limits.videosPerThread === undefined ? FORUM_LIMITS.videosPerThread : limits.videosPerThread;
  const problems = {};
  if (!ThreadTypes.includes(type)) problems.type = 'typeRequired';
  else if (Array.isArray(allowedTypes) && !allowedTypes.includes(type)) problems.type = 'typeNotAllowed';
  const titleLength = scalarCount(String(title || '').trim());
  if (titleLength < FORUM_LIMITS.titleMin) problems.title = 'titleTooShort';
  else if (titleLength > FORUM_LIMITS.titleMax) problems.title = 'titleTooLong';
  const text = String(body || '').trim();
  const needsBody = type === ThreadType.Discussion || type === ThreadType.Question;
  if (needsBody && !text) problems.body = 'bodyRequired';
  else if (scalarCount(text) > bodyMax) problems.body = 'bodyTooLong';
  if (type === ThreadType.Feedback && videoCount < 1) problems.media = 'videoRequired';
  if (mediaCount > maxAttachments) problems.media = 'tooManyAttachments';
  if (videoCount > maxVideos) problems.media = 'tooManyVideos';
  if (type === ThreadType.Feedback && scalarCount(focus) > FORUM_LIMITS.focusMax) problems.focus = 'focusTooLong';
  if (type === ThreadType.Poll && pollProblems && Object.keys(pollProblems).length) problems.poll = Object.values(pollProblems)[0];
  return problems;
}

/**
 * The `POST /forums/{id}/threads` body for a draft (section 4.4). `poll` is sent only for Poll
 * threads and `feedback` only for Feedback threads.
 * @param {{ type: string, title: string, body?: string, mediaIds?: string[], showLinkPreview?: boolean, audience?: string|null, poll?: object|null, focus?: string, labels?: string[], tags?: object }} draft
 */
export function threadCreateBody(draft) {
  const out = {
    type: draft.type,
    title: String(draft.title || '').trim(),
    body: String(draft.body || '').trim(),
    mediaIds: draft.mediaIds || [],
    showLinkPreview: draft.showLinkPreview !== false,
    audience: draft.audience || null,
    poll: draft.type === ThreadType.Poll ? draft.poll || null : null,
    feedback: draft.type === ThreadType.Feedback ? { focus: String(draft.focus || '').trim() } : null,
    labels: draft.labels || [],
    tags: draft.tags || {}
  };
  return out;
}

/** Whether reactions are allowed on a thread: `viewer.canReact`, and never on a locked thread (resolved decision 14). */
export function threadCanReact(thread) {
  if (!thread) return false;
  const viewer = thread.viewer || {};
  return viewer.canReact !== false && !thread.isLocked && thread.state !== 'Hidden' && thread.state !== 'Removed';
}

/** Whether the composer shows: `viewer.canComment`, Published, and not locked. */
export function threadCanComment(thread) {
  if (!thread) return false;
  const viewer = thread.viewer || {};
  return Boolean(viewer.canComment) && !thread.isLocked && (thread.state || 'Published') === 'Published';
}

/**
 * Moderator and owner actions for a thread, from its `viewer` (section 1.4) and state. Keys:
 * `edit`, `history`, `pin`, `unpin`, `lock`, `unlock`, `move`, `editMetadata`, `hide`, `unhide`,
 * `remove`, `restore`, `promote`, `unpromote`, `closePoll`, `feedbackStatus`, `report`, `delete`.
 * @param {object} thread
 * @param {{ isAuthor?: boolean }} [options]
 * @returns {string[]}
 */
export function threadActions(thread, { isAuthor = false } = {}) {
  if (!thread) return [];
  const v = thread.viewer || {};
  const state = thread.state || 'Published';
  const out = [];
  if (v.canEdit) out.push('edit');
  if (thread.editedUtc || thread.revisionCount > 0) out.push('history');
  if (v.canPin && state === 'Published') out.push(thread.isPinned ? 'unpin' : 'pin');
  if (v.canLock) out.push(thread.isLocked ? 'unlock' : 'lock');
  if (v.canMove) out.push('move');
  if (v.canModerate && !isAuthor) out.push('editMetadata');
  if (v.canModerate) {
    if (state === 'Hidden') out.push('unhide', 'remove');
    else if (state === 'Removed') out.push('restore');
    else out.push('hide', 'remove');
  }
  if (v.canPromote && !isLinkedThread(thread)) out.push(thread.promotion ? 'unpromote' : 'promote');
  if (v.canClosePoll && thread.poll && !thread.poll.isClosed) out.push('closePoll');
  if (thread.type === ThreadType.Feedback && feedbackTransitions(thread, { isAuthor }).length) out.push('feedbackStatus');
  if (v.canReport && !isAuthor) out.push('report');
  if (v.canDelete) out.push('delete');
  return out;
}

/** Whether any moderator action is available (the mobile moderator actions sheet). */
export function hasModeratorActions(thread) {
  return Boolean(thread && thread.viewer && thread.viewer.canModerate);
}

/**
 * Feedback status targets the caller may set (section 7): reviewers (`viewer.canSetFeedbackStatus`)
 * may set any other status (`Reviewed` needs a review comment, chosen from the comment menu);
 * the author may only withdraw (`Closed`) an Open or InReview request.
 * @param {object} thread
 * @param {{ isAuthor?: boolean }} [options]
 */
export function feedbackTransitions(thread, { isAuthor = false } = {}) {
  if (!thread || thread.type !== ThreadType.Feedback) return [];
  const status = (thread.feedback && thread.feedback.status) || 'Open';
  const v = thread.viewer || {};
  if (v.canSetFeedbackStatus) return FeedbackStatuses.filter((s) => s !== status && s !== 'Reviewed');
  if (isAuthor && (status === 'Open' || status === 'InReview')) return ['Closed'];
  return [];
}

/**
 * Typed forum thread filters for an EnumerationQuery (section 4.2) from a UI state. Booleans
 * that mean "no filter" are dropped; `answered: false` is kept (as `'false'`, which the enumeration helper sends as the JSON boolean `false`).
 * @param {{ types?: string[], answered?: boolean|null, unanswered?: boolean, feedbackStatus?: string[], pollOpen?: boolean|null, followedOnly?: boolean, unreadOnly?: boolean, hasMedia?: boolean, hasVideo?: boolean, forumIds?: string[], authorUserId?: string, includeComments?: boolean|null }} ui
 */
export function threadFilters(ui = {}) {
  const f = {};
  if (ui.types && ui.types.length) f.type = ui.types.slice();
  if (ui.answered === true) f.answered = true;
  if (ui.answered === false) f.answered = 'false';
  if (ui.unanswered) f.unanswered = true;
  if (ui.feedbackStatus && ui.feedbackStatus.length) f.feedbackStatus = ui.feedbackStatus.slice();
  if (ui.pollOpen === true) f.pollOpen = true;
  if (ui.pollOpen === false) f.pollOpen = 'false';
  if (ui.followedOnly) f.followedOnly = true;
  if (ui.unreadOnly) f.unreadOnly = true;
  if (ui.hasMedia) f.hasMedia = true;
  if (ui.hasVideo) f.hasVideo = true;
  if (ui.forumIds && ui.forumIds.length) f.forumIds = ui.forumIds.slice(0, FORUM_LIMITS.forumIdsFilterMax);
  if (ui.authorUserId) f.authorUserId = ui.authorUserId;
  if (ui.includeComments === false) f.includeComments = 'false';
  return f;
}

/**
 * Split a search snippet into plain and highlighted runs (section 9.1: `text` is plain text and
 * `highlights` are `{ start, length }` offsets into it). Overlapping or out-of-range highlights
 * are clamped; the result never contains HTML.
 * @param {string} text
 * @param {{ start: number, length: number }[]} [highlights]
 * @returns {{ text: string, highlight: boolean }[]}
 */
export function snippetSegments(text, highlights = []) {
  const source = String(text || '');
  const ranges = (Array.isArray(highlights) ? highlights : [])
    .map((h) => ({ start: Math.max(0, Math.floor(Number(h && h.start) || 0)), end: Math.max(0, Math.floor(Number(h && h.start) || 0) + Math.floor(Number(h && h.length) || 0)) }))
    .filter((r) => r.end > r.start && r.start < source.length)
    .sort((a, b) => a.start - b.start);
  const out = [];
  let at = 0;
  for (const r of ranges) {
    const start = Math.max(r.start, at);
    const end = Math.min(r.end, source.length);
    if (end <= start) continue;
    if (start > at) out.push({ text: source.slice(at, start), highlight: false });
    out.push({ text: source.slice(start, end), highlight: true });
    at = end;
  }
  if (at < source.length || !out.length) out.push({ text: source.slice(at), highlight: false });
  return out.filter((s) => s.text.length > 0 || out.length === 1);
}

/**
 * Audience choices an access model allows (TRELLISKIT.md section 7): `Open` has no memberships
 * (Public and SignedIn), `FullyGated` gates everything except Public samples (Members and
 * Public), and `PartiallyGated` uses all three. A missing `access` means FullyGated.
 * @param {{ access?: { model?: string } }} property
 * @returns {string[]}
 */
export function audienceOptions(property) {
  const model = (property && property.access && property.access.model) || 'FullyGated';
  if (model === 'Open') return ['SignedIn', 'Public'];
  if (model === 'PartiallyGated') return ['Members', 'SignedIn', 'Public'];
  return ['Members', 'Public'];
}

/**
 * Validate an admin forum form (section 2.1). Returns `{ field: code }`: `nameLength`,
 * `slugInvalid`, `descriptionLength`, `guidelinesLength`, `sectionLength`, `typesRequired`,
 * `colorInvalid`.
 * @param {object} form
 */
export function validateForumForm(form) {
  const problems = {};
  const len = (v) => Array.from(String(v || '').trim()).length;
  if (len(form.name) < FORUM_LIMITS.forumNameMin || len(form.name) > FORUM_LIMITS.forumNameMax) problems.name = 'nameLength';
  if (form.slug && !FORUM_SLUG_PATTERN.test(form.slug)) problems.slug = 'slugInvalid';
  if (len(form.description) > FORUM_LIMITS.forumDescriptionMax) problems.description = 'descriptionLength';
  if (len(form.guidelines) > FORUM_LIMITS.forumGuidelinesMax) problems.guidelines = 'guidelinesLength';
  if (len(form.section) > FORUM_LIMITS.forumSectionMax) problems.section = 'sectionLength';
  if (!Array.isArray(form.allowedThreadTypes) || !form.allowedThreadTypes.length) problems.allowedThreadTypes = 'typesRequired';
  if (form.color && !/^#[0-9A-Fa-f]{6}$/.test(form.color)) problems.color = 'colorInvalid';
  return problems;
}

/**
 * Move one item of an ordered list (drag to reorder or the arrow buttons).
 * @template T
 * @param {T[]} list
 * @param {number} from
 * @param {number} to
 * @returns {T[]}
 */
export function moveItem(list, from, to) {
  const items = Array.isArray(list) ? list.slice() : [];
  if (from < 0 || from >= items.length || to < 0 || to >= items.length || from === to) return items;
  const [item] = items.splice(from, 1);
  items.splice(to, 0, item);
  return items;
}
