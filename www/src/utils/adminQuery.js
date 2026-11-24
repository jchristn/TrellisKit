import { ENUMERATION_MAX_RESULTS_ADMIN, sortToOrdering, toEnumerationParams } from '@trelliskit/shared';

/**
 * Query builders for the admin list endpoints. Every list is an enumeration
 * (docs/ENUMERATION_AND_METADATA.md): the table's page becomes `maxResults` and `skip`, its
 * sort becomes `ordering` (`-createdUtc` is `CreatedDescending`), `search` is the query's search,
 * label and tag filters come from the table's `meta`, and the remaining filters are the
 * collection's own filters. The result is the GET query-string form.
 */

export const ADMIN_PAGE_SIZES = Object.freeze([10, 25, 50, 100, 200]);
export const ADMIN_DEFAULT_PAGE_SIZE = 50;

/** Keep only non-empty values, trimmed. */
export function cleanQuery(values, keys) {
  const result = {};
  for (const key of keys) {
    const raw = values ? values[key] : undefined;
    const value = typeof raw === 'string' ? raw.trim() : raw;
    if (value !== '' && value !== null && value !== undefined && value !== false) result[key] = String(value);
  }
  return result;
}

/**
 * Build the admin enumeration GET params.
 * @param {object} filters table filters
 * @param {readonly string[]} keys accepted filter keys
 * @param {{ pageNumber?: number, pageSize?: number, sort?: string, meta?: object }} [paging]
 */
export function paged(filters, keys, { pageNumber = 1, pageSize = ADMIN_DEFAULT_PAGE_SIZE, sort = '', meta = null } = {}) {
  const clean = cleanQuery(filters, keys);
  const { search, fromUtc, toUtc, ...rest } = clean;
  const size = Math.min(ENUMERATION_MAX_RESULTS_ADMIN, pageSize);
  const query = {
    maxResults: size,
    skip: (Math.max(1, pageNumber) - 1) * size,
    ordering: sortToOrdering(sort),
    search,
    createdAfterUtc: fromUtc,
    createdBeforeUtc: toUtc,
    labels: meta ? meta.labels : [],
    labelMatch: meta ? meta.labelMatch : undefined,
    tags: meta ? meta.tags : {},
    filters: rest
  };
  return toEnumerationParams(query, { maxLimit: ENUMERATION_MAX_RESULTS_ADMIN });
}

/**
 * The same as `paged`, as an EnumerationQuery object for the canonical `POST /enumerate` form
 * (the Phase 3 admin collections).
 */
export function pagedQuery(filters, keys, { pageNumber = 1, pageSize = ADMIN_DEFAULT_PAGE_SIZE, sort = '', meta = null } = {}, transform = (rest) => rest) {
  const clean = cleanQuery(filters, keys);
  const { search, fromUtc, toUtc, ...rest } = clean;
  const size = Math.min(ENUMERATION_MAX_RESULTS_ADMIN, pageSize);
  return {
    maxResults: size,
    skip: (Math.max(1, pageNumber) - 1) * size,
    ordering: sortToOrdering(sort) || undefined,
    search,
    createdAfterUtc: fromUtc,
    createdBeforeUtc: toUtc,
    labels: meta ? meta.labels : [],
    labelMatch: meta ? meta.labelMatch : undefined,
    tags: meta ? meta.tags : {},
    filters: transform(rest)
  };
}

export const ADMIN_FORUM_FILTERS = Object.freeze(['search', 'section', 'isArchived', 'audience']);
/** POST /admin/forums/enumerate query (ordered by `sortOrder`; archived forums included unless filtered). */
export function buildAdminForumsQuery(filters, paging) {
  return pagedQuery(filters, ADMIN_FORUM_FILTERS, { ...paging, sort: '' }, (rest) => ({ ...rest, includeArchived: rest.isArchived ? undefined : true }));
}

export const ADMIN_THREAD_FILTERS = Object.freeze(['search', 'forumId', 'type', 'state', 'authorUserId', 'pinned', 'locked', 'promoted', 'feedbackStatus', 'fromUtc', 'toUtc']);
export const ADMIN_THREAD_DEFAULT_SORT = '-createdUtc';
/** POST /admin/threads/enumerate query; `forumId` becomes `filters.forumIds`. */
export function buildAdminThreadsQuery(filters, paging) {
  return pagedQuery(filters, ADMIN_THREAD_FILTERS, paging, ({ forumId, ...rest }) => ({ ...rest, ...(forumId ? { forumIds: [forumId] } : {}) }));
}

export const TRENDING_FILTERS = Object.freeze(['forumId', 'type', 'promoted', 'minScore']);
/** POST /admin/trending/enumerate query (ordering `ScoreDescending`). */
export function buildTrendingQuery(filters, paging) {
  return { ...pagedQuery(filters, TRENDING_FILTERS, { ...paging, sort: '' }, ({ forumId, minScore, ...rest }) => ({ ...rest, ...(forumId ? { forumIds: [forumId] } : {}), ...(minScore ? { minScore: Number(minScore) } : {}) })), ordering: 'ScoreDescending' };
}

export const FORUM_MUTE_FILTERS = Object.freeze(['forumId', 'userId', 'activeOnly']);

export const REVIEW_FILTERS = Object.freeze(['feedbackStatus', 'forumId']);
/** Review requests: Feedback threads by status, oldest first (PHASE3_CONTRACT.md section 7). */
export function buildReviewQuery(filters, paging) {
  return pagedQuery({ feedbackStatus: 'Open', ...filters }, REVIEW_FILTERS, { ...paging, sort: 'createdUtc' }, ({ forumId, ...rest }) => ({ ...rest, type: ['Feedback'], ...(forumId ? { forumIds: [forumId] } : {}) }));
}

export const MEMBER_FILTERS = Object.freeze(['search', 'membership', 'role', 'banned']);
export const MEMBER_SORTS = Object.freeze(['createdUtc', 'displayName', 'lastSeenUtc']);
export const MEMBER_DEFAULT_SORT = '-createdUtc';

/**
 * POST /admin/members/enumerate query (an EnumerationQuery). `banned` is sent only as `true`;
 * `profile` is the normalized `filters.profile` map (`buildProfileFilters`), sent when not empty.
 * The deprecated `rank` filter is gone: rank filters are profile filters now.
 */
export function buildMembersQuery(filters, paging, profile = null) {
  const f = { ...filters, banned: filters && (filters.banned === true || filters.banned === 'true') ? 'true' : '' };
  return pagedQuery(f, MEMBER_FILTERS, paging, (rest) => (profile && Object.keys(profile).length ? { ...rest, profile } : rest));
}

export const SUBSCRIPTION_FILTERS = Object.freeze(['search', 'store', 'status']);
export const SUBSCRIPTION_DEFAULT_SORT = '-currentPeriodEndUtc';

/** GET /admin/subscriptions query. */
export function buildSubscriptionsQuery(filters, paging) {
  return paged(filters, SUBSCRIPTION_FILTERS, paging);
}

export const CODE_FILTERS = Object.freeze(['search', 'status', 'tag']);
export const CODE_DEFAULT_SORT = '-createdUtc';

/** GET /admin/signup-codes query. */
export function buildCodesQuery(filters, paging) {
  return paged(filters, CODE_FILTERS, paging);
}

export const REDEMPTION_FILTERS = Object.freeze(['status']);
export const REDEMPTION_DEFAULT_SORT = '-redeemedUtc';

/** GET /admin/signup-codes/{id}/redemptions query. */
export function buildRedemptionsQuery(filters, paging) {
  return paged(filters, REDEMPTION_FILTERS, paging);
}

export const AUDIT_FILTERS = Object.freeze(['actorUserId', 'action', 'targetType', 'targetId', 'fromUtc', 'toUtc']);
export const AUDIT_DEFAULT_SORT = '-createdUtc';

/** GET /admin/audit query. */
export function buildAuditQuery(filters, paging) {
  return paged(filters, AUDIT_FILTERS, paging);
}

/** Known audit actions (contract section 7) for the action filter. */
export const AUDIT_ACTIONS = Object.freeze([
  'member.flags', 'member.roles', 'member.comp', 'member.ban', 'member.mute', 'member.sessions.revoke',
  'signupCode.create', 'signupCode.update', 'signupCode.deactivate', 'signupCode.batch', 'signupCode.redeem', 'signupCode.revoke',
  'settings.update', 'server.restart', 'account.delete',
  'feedPost.create', 'feedPost.update', 'feedPost.delete', 'feedPost.pin', 'feedPost.unpin', 'feedPost.publish',
  'media.update', 'media.delete', 'media.reprocess',
  'moderation.hide', 'moderation.unhide', 'moderation.remove', 'moderation.restore', 'moderation.dismiss',
  'moderation.wordFilter.create', 'moderation.wordFilter.update', 'moderation.wordFilter.delete', 'moderation.wordFilter.import',
  'forum.create', 'forum.update', 'forum.order', 'forum.archive', 'forum.unarchive', 'forum.delete',
  'thread.publish', 'thread.delete', 'thread.pin', 'thread.unpin', 'thread.lock', 'thread.unlock', 'thread.move', 'thread.acceptAnswer', 'thread.clearAnswer',
  'thread.promote', 'thread.unpromote', 'feedPost.discuss', 'feedPost.undiscuss'
]);

export const FEED_FILTERS = Object.freeze(['search', 'state', 'authorUserId', 'pinned', 'fromUtc', 'toUtc']);
export const FEED_DEFAULT_SORT = '-publishedUtc';
/** GET /admin/feed query. */
export function buildFeedQuery(filters, paging) {
  return paged(filters, FEED_FILTERS, paging);
}

export const QUEUE_FILTERS = Object.freeze(['status', 'targetType', 'reason', 'overdue', 'scopeType', 'forumIds']);
export const QUEUE_DEFAULT_SORT = 'dueUtc';
/** GET /admin/moderation/queue query. */
export function buildQueueQuery(filters, paging) {
  return paged(filters, QUEUE_FILTERS, paging);
}

export const MOD_ACTION_FILTERS = Object.freeze(['moderatorUserId', 'targetType', 'action', 'fromUtc', 'toUtc']);
export const MOD_ACTION_DEFAULT_SORT = '-createdUtc';
/** GET /admin/moderation/actions query. */
export function buildModerationActionsQuery(filters, paging) {
  return paged(filters, MOD_ACTION_FILTERS, paging);
}

export const WORD_FILTERS = Object.freeze(['search', 'action']);
export const WORD_DEFAULT_SORT = 'term';
/** GET /admin/moderation/word-filter query. */
export function buildWordFilterQuery(filters, paging) {
  return paged(filters, WORD_FILTERS, paging);
}

export const MEDIA_FILTERS = Object.freeze(['kind', 'status', 'ownerUserId', 'attached', 'fromUtc', 'toUtc']);
export const MEDIA_DEFAULT_SORT = '-createdUtc';
/** GET /admin/media query. */
export function buildMediaQuery(filters, paging) {
  return paged(filters, MEDIA_FILTERS, paging);
}
