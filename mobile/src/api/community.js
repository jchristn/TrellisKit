/**
 * Typed wrappers for the Phase 2 member routes (docs/PHASE2_CONTRACT.md): feed, comments,
 * reactions, media, notifications, preferences, push tokens, reports, blocks, member search,
 * link previews, and public profiles. Each takes the MobileApiClient first so screens and tests
 * can pass their own instance. Paths are relative to /api/v1.
 *
 * These mirror the ApiClient method names the contract lists for @trelliskit/shared (section 18);
 * they live here until the shared package ships them.
 */

import { buildEnumerationQuery } from '../content/enumeration';
import { targetSegment as sharedTargetSegment, toEnumerationBody } from '@trelliskit/shared';

const enc = encodeURIComponent;

/**
 * POST {path}/enumerate with an EnumerationQuery (docs/ENUMERATION_AND_METADATA.md section 2).
 * Returns the raw EnumerationResult (read it with readEnumeration).
 * @param {object} api
 * @param {string} path Collection path, for example /feed or /feed/fpo_1/comments.
 * @param {{ continuationToken?: string|null, maxResults?: number, filters?: object, ordering?: string, collectionFilters?: object, signal?: AbortSignal }} [options]
 */
export function enumerate(api, path, { signal, ...options } = {}) {
  return api.post(path + '/enumerate', buildEnumerationQuery(options), signal ? { signal } : undefined);
}

/** Comment orderings: the tree's sort choices mapped to EnumerationQuery orderings. */
export const CommentOrdering = Object.freeze({ top: 'Top', new: 'CreatedDescending', old: 'CreatedAscending' });

/** Most media ids accepted in a filtered status enumeration. */
export const MediaBatchMax = 20;

/** URL segment for a reactable or commentable target (contract section 0.3). */
export function targetSegment(targetType) {
  switch (targetType) {
    case 'FeedPost':
      return 'feed';
    case 'Comment':
      return 'comments';
    case 'Thread':
      return 'threads';
    case 'Lesson':
      return 'lessons';
    case 'Event':
      return 'events';
    default:
      // Phase 5 targets (`Message`, `Idea`) and any later ones from the shared table.
      return sharedTargetSegment(targetType) || null;
  }
}

/** Base path of a target, for example /feed/fpo_1 or /comments/cmt_1. */
export function targetPath(target) {
  const segment = target ? targetSegment(target.type || target.targetType) : null;
  return segment ? '/' + segment + '/' + enc(target.id || target.targetId) : null;
}

export const feed = {
  list: (api, { continuationToken, maxResults = 20, filters } = {}) => enumerate(api, '/feed', { continuationToken, maxResults, filters }),
  get: (api, id) => api.get('/feed/' + enc(id)),
  create: (api, body) => api.post('/feed', body),
  update: (api, id, body) => api.put('/feed/' + enc(id), body),
  remove: (api, id) => api.delete('/feed/' + enc(id)),
  pin: (api, id) => api.post('/feed/' + enc(id) + '/pin'),
  unpin: (api, id) => api.post('/feed/' + enc(id) + '/unpin'),
  revisions: (api, id, { continuationToken, maxResults = 20, filters } = {}) => enumerate(api, '/feed/' + enc(id) + '/revisions', { continuationToken, maxResults, filters }),
  mine: (api, { state, continuationToken, maxResults = 20, filters } = {}) => enumerate(api, '/me/feed-posts', { continuationToken, maxResults, filters, collectionFilters: { state } })
};

export const reactions = {
  set: (api, query = {}) => api.post('/reactions/set/enumerate', toEnumerationBody(query)),
  /** PUT or DELETE {target}/reactions/{key}; returns { reactionCounts, reactionTotal, myReactions }. */
  toggle: (api, target, key, on) => api.request(on ? 'PUT' : 'DELETE', targetPath(target) + '/reactions/' + enc(key)),
  list: (api, target, { key, continuationToken, maxResults = 50, filters } = {}) => enumerate(api, targetPath(target) + '/reactions', { continuationToken, maxResults, filters, collectionFilters: { key } })
};

export const comments = {
  /** Tree page for a target (contract 7.3): top-level comments per page, nested replies bounded by depth and childLimit. */
  list: (api, target, { sort = 'top', limit = 20, depth, childLimit, continuationToken, filters } = {}) =>
    enumerate(api, targetPath(target) + '/comments', { continuationToken, maxResults: limit, filters, ordering: CommentOrdering[sort], collectionFilters: { depth, childLimit } }),
  children: (api, id, { sort = 'top', limit = 20, depth, childLimit, continuationToken } = {}) =>
    enumerate(api, '/comments/' + enc(id) + '/children', { continuationToken, maxResults: limit, ordering: CommentOrdering[sort], collectionFilters: { depth, childLimit } }),
  /** Permalink: { target, ancestors, comment } (contract 7.4). */
  get: (api, id, { depth, childLimit, sort } = {}) => api.get('/comments/' + enc(id), { query: { depth, childLimit, sort } }),
  create: (api, target, body) => api.post(targetPath(target) + '/comments', body),
  update: (api, id, body) => api.put('/comments/' + enc(id), body),
  remove: (api, id) => api.delete('/comments/' + enc(id)),
  revisions: (api, id, { continuationToken, maxResults = 20, filters } = {}) => enumerate(api, '/comments/' + enc(id) + '/revisions', { continuationToken, maxResults, filters })
};

export const media = {
  createUpload: (api, body) => api.post('/media/uploads', body),
  refreshUploadUrls: (api, id) => api.post('/media/' + enc(id) + '/upload-urls'),
  complete: (api, id, parts) => api.post('/media/' + enc(id) + '/complete', parts ? { parts } : {}),
  get: (api, id) => api.get('/media/' + enc(id)),
  getBatch: (api, ids, query = {}) => api.post('/media/enumerate', toEnumerationBody({ ...query, filters: { ...query.filters, ids: ids.slice(0, MediaBatchMax) } })),
  update: (api, id, body) => api.put('/media/' + enc(id), body),
  remove: (api, id) => api.delete('/media/' + enc(id)),
  grant: (api) => api.post('/media/grant')
};

export const notifications = {
  list: (api, { continuationToken, maxResults = 30, unreadOnly, filters } = {}) => enumerate(api, '/notifications', { continuationToken, maxResults, filters, collectionFilters: { unreadOnly: unreadOnly ? true : undefined } }),
  unreadCount: (api) => api.get('/notifications/unread-count'),
  markRead: (api, ids) => api.post('/notifications/read', { ids }),
  markUnread: (api, ids) => api.post('/notifications/unread', { ids }),
  markAllRead: (api, beforeUtc) => api.post('/notifications/read-all', beforeUtc ? { beforeUtc } : {}),
  getPreferences: (api, query = {}) => api.post('/me/notification-preferences/enumerate', toEnumerationBody(query)),
  updatePreferences: (api, body) => api.put('/me/notification-preferences', body)
};

export const pushTokens = {
  list: (api, { continuationToken, maxResults = 10 } = {}) => enumerate(api, '/me/push-tokens', { continuationToken, maxResults }),
  register: (api, body, options) => api.post('/me/push-tokens', body, options),
  unregister: (api, token, options) => api.request('DELETE', '/me/push-tokens', { ...options, body: { token } })
};

export const safety = {
  report: (api, { targetType, targetId, reason, details }) => api.post('/reports', { targetType, targetId, reason, details: details || undefined }),
  block: (api, userId) => api.post('/users/' + enc(userId) + '/block'),
  unblock: (api, userId) => api.delete('/users/' + enc(userId) + '/block'),
  listBlocks: (api, { continuationToken, maxResults = 50, filters } = {}) => enumerate(api, '/me/blocks', { continuationToken, maxResults, filters })
};

export const members = {
  /** Mention autocomplete (contract 3.4) as a bounded member enumeration with `search`. */
  search: (api, q, { limit = 8, contextType, contextId, signal } = {}) =>
    enumerate(api, '/members', { signal, maxResults: limit, filters: { search: String(q || '').replace(/^@/, '') }, collectionFilters: { contextType, contextId } }),
  get: (api, id) => api.get('/users/' + enc(id))
};

export const linkPreviews = {
  get: (api, url, options) => api.post('/link-previews', { url }, options)
};

/**
 * Labels and tags (docs/ENUMERATION_AND_METADATA.md section 1): read and replace an object's
 * metadata, and the bounded autocomplete enumerations for filters and editors.
 */
export const metadata = {
  get: (api, targetType, targetId) => api.get('/metadata/' + enc(targetType) + '/' + enc(targetId)),
  put: (api, targetType, targetId, { labels, tags }) => api.put('/metadata/' + enc(targetType) + '/' + enc(targetId), { labels, tags }),
  labels: (api, { targetType, prefix, maxResults = 10, signal } = {}) => api.post('/labels/enumerate', { ...buildEnumerationQuery({ maxResults }), targetType, prefix: prefix || undefined }, signal ? { signal } : undefined),
  tagKeys: (api, { targetType, prefix, maxResults = 10, signal } = {}) => api.post('/tags/keys/enumerate', { ...buildEnumerationQuery({ maxResults }), targetType, prefix: prefix || undefined }, signal ? { signal } : undefined),
  tagValues: (api, { targetType, key, prefix, maxResults = 10, signal } = {}) => api.post('/tags/values/enumerate', { ...buildEnumerationQuery({ maxResults }), targetType, key, prefix: prefix || undefined }, signal ? { signal } : undefined)
};
