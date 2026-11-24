/**
 * Phase 2 API methods (docs/PHASE2_CONTRACT.md section 18), mixed into `ApiClient.prototype`.
 * Every method maps to exactly one route. List methods take an `EnumerationQuery`
 * (docs/ENUMERATION_AND_METADATA.md: `maxResults`, `continuationToken`, `ordering`, `search`,
 * `labels`, `labelMatch`, `tags`, and collection `filters`) and send its GET form; the response is
 * an `EnumerationResult` (use `normalizeEnumerationResult`). `enumerate(path, query)` sends the
 * canonical `POST {path}/enumerate` form.
 *
 * Comments and reactions are always addressed through a target (`{ type, id }`), because a feed
 * card's conversation lives on its `commentTarget` (contract section 5.1). Phase 2 only has
 * `FeedPost` and `Comment` targets; the URL segment comes from `targetSegment`.
 */

import { ENUMERATION_MAX_RESULTS_ADMIN, toEnumerationBody, toEnumerationParams } from './enumeration.js';

const enc = encodeURIComponent;
const ADMIN = { maxLimit: ENUMERATION_MAX_RESULTS_ADMIN };

/** GET a collection with an EnumerationQuery in the query-string form. */
function list(client, path, query, signal, limits) {
  return client.get(path, { query: toEnumerationParams(query || {}, limits), signal });
}

/** URL segment for each target type (contract section 0.3). */
export const TARGET_SEGMENTS = Object.freeze({
  FeedPost: 'feed',
  Comment: 'comments',
  User: 'users',
  Thread: 'threads',
  Lesson: 'lessons',
  Event: 'events',
  Series: 'series',
  Venue: 'venues',
  Instructor: 'instructors',
  Message: 'messages',
  Idea: 'ideas'
});

/**
 * URL segment for a target type.
 * @param {string} type
 * @returns {string}
 * @throws {Error} for an unknown type
 */
export function targetSegment(type) {
  const segment = TARGET_SEGMENTS[type];
  if (!segment) throw new Error('Unknown target type: ' + type);
  return segment;
}

function targetPath(target) {
  if (!target || !target.type || !target.id) throw new Error('A target needs a type and an id.');
  return '/' + targetSegment(target.type) + '/' + enc(target.id);
}

/** @this {import('./ApiClient.js').ApiClient} */
export const engagementMethods = {
  // ---- Enumeration and metadata (docs/ENUMERATION_AND_METADATA.md) ----

  /** POST {collectionPath}/enumerate with an EnumerationQuery body (admin paths allow 500 results). */
  enumerate(collectionPath, query = {}, signal) {
    const path = collectionPath.replace(/\/+$/, '');
    return this.post(path + '/enumerate', toEnumerationBody(query, path.startsWith('/admin') ? ADMIN : undefined), { signal });
  },
  /** POST /labels/enumerate. `query` adds `targetType` and `prefix`; returns `{ label, count }` objects. */
  enumerateLabels({ targetType, prefix, ...query } = {}, signal) {
    return this.post('/labels/enumerate', { ...toEnumerationBody(query), ...(targetType ? { targetType } : {}), ...(prefix ? { prefix } : {}) }, { signal });
  },
  /** POST /tags/keys/enumerate; returns `{ key, count }` objects. */
  enumerateTagKeys({ targetType, prefix, ...query } = {}, signal) {
    return this.post('/tags/keys/enumerate', { ...toEnumerationBody(query), ...(targetType ? { targetType } : {}), ...(prefix ? { prefix } : {}) }, { signal });
  },
  /** POST /tags/values/enumerate (requires `key`); returns `{ value, count }` objects. */
  enumerateTagValues({ targetType, key, prefix, ...query } = {}, signal) {
    return this.post('/tags/values/enumerate', { ...toEnumerationBody(query), key, ...(targetType ? { targetType } : {}), ...(prefix ? { prefix } : {}) }, { signal });
  },
  /** GET /metadata/{targetType}/{targetId}. */
  getMetadata(targetType, targetId, signal) { return this.get('/metadata/' + enc(targetType) + '/' + enc(targetId), { signal }); },
  /** PUT /metadata/{targetType}/{targetId} with `{ labels?, tags? }` (replace semantics). */
  putMetadata(targetType, targetId, body) { return this.put('/metadata/' + enc(targetType) + '/' + enc(targetId), body); },
  /** PATCH /metadata/{targetType}/{targetId} with `{ addLabels?, removeLabels?, setTags?, removeTagKeys? }`. */
  patchMetadata(targetType, targetId, body) { return this.request('PATCH', '/metadata/' + enc(targetType) + '/' + enc(targetId), { body }); },
  /** POST /admin/metadata/bulk with `{ targetType, targetIds (max 500), addLabels?, removeLabels?, setTags?, removeTagKeys? }`. */
  bulkMetadata(body) { return this.post('/admin/metadata/bulk', body); },

  // ---- Feed (sections 5.2 to 5.8) ----

  /** GET /feed (EnumerationQuery; pinned posts lead the first page). */
  listFeed(query = {}, signal) { return list(this, '/feed', query, signal); },
  /** GET /feed/{id}. */
  getFeedPost(id, signal) { return this.get('/feed/' + enc(id), { signal }); },
  /** POST /feed. */
  createFeedPost(body) { return this.post('/feed', body); },
  /** PUT /feed/{id}. */
  updateFeedPost(id, body) { return this.put('/feed/' + enc(id), body); },
  /** DELETE /feed/{id}. */
  deleteFeedPost(id) { return this.delete('/feed/' + enc(id)); },
  /** POST /feed/{id}/pin. */
  pinFeedPost(id) { return this.post('/feed/' + enc(id) + '/pin'); },
  /** POST /feed/{id}/unpin. */
  unpinFeedPost(id) { return this.post('/feed/' + enc(id) + '/unpin'); },
  /** GET /me/feed-posts. Filter: `state`. */
  listMyFeedPosts(query = {}, signal) { return list(this, '/me/feed-posts', query, signal); },
  /** GET /feed/{id}/revisions. */
  listFeedPostRevisions(id, query = {}, signal) { return list(this, '/feed/' + enc(id) + '/revisions', query, signal); },

  // ---- Public feed (section 5.10, anonymous) ----

  /** GET /public/feed. */
  listPublicFeed(query = {}, signal) { return this.get('/public/feed', { query: toEnumerationParams(query), signal, retryOnUnauthorized: false }); },
  /** GET /public/feed/{id}. */
  getPublicFeedPost(id, signal) { return this.get('/public/feed/' + enc(id), { signal, retryOnUnauthorized: false }); },

  // ---- Link previews (section 5.9) ----

  /** POST /link-previews. */
  getLinkPreview(url, signal) { return this.post('/link-previews', { url }, { signal }); },

  // ---- Reactions (section 6) ----

  /** POST /reactions/set/enumerate: configured reaction rows plus likeKey. */
  getReactionSet(query = {}, signal) { return this.post('/reactions/set/enumerate', toEnumerationBody(query), { signal }); },
  /** PUT /{segment}/{id}/reactions/{key}. */
  putReaction(target, key) { return this.request('PUT', targetPath(target) + '/reactions/' + enc(key)); },
  /** DELETE /{segment}/{id}/reactions/{key}. */
  deleteReaction(target, key) { return this.delete(targetPath(target) + '/reactions/' + enc(key)); },
  /** GET /{segment}/{id}/reactions. Filter: `key`. */
  listReactions(target, query = {}, signal) { return list(this, targetPath(target) + '/reactions', query, signal); },

  // ---- Comments (section 7) ----

  /** GET /{segment}/{id}/comments. Ordering `Top`, `CreatedDescending`, `CreatedAscending`; filters `depth`, `childLimit`. */
  listComments(target, query = {}, signal) { return list(this, targetPath(target) + '/comments', query, signal); },
  /** GET /comments/{id}/children (`continuationToken` from a comment's `moreChildren`). */
  listCommentChildren(id, query = {}, signal) { return list(this, '/comments/' + enc(id) + '/children', query, signal); },
  /** GET /comments/{id} (permalink with ancestors). `query`: `{ ordering, depth, childLimit }`. */
  getComment(id, query = {}, signal) { return this.get('/comments/' + enc(id), { query, signal }); },
  /** POST /{segment}/{id}/comments. */
  createComment(target, body) { return this.post(targetPath(target) + '/comments', body); },
  /** PUT /comments/{id}. */
  updateComment(id, body) { return this.put('/comments/' + enc(id), body); },
  /** DELETE /comments/{id}. */
  deleteComment(id) { return this.delete('/comments/' + enc(id)); },
  /** GET /comments/{id}/revisions. */
  listCommentRevisions(id, query = {}, signal) { return list(this, '/comments/' + enc(id) + '/revisions', query, signal); },

  // ---- Media (section 4) ----

  /** POST /media/uploads. */
  createUpload(body) { return this.post('/media/uploads', body); },
  /** POST /media/{id}/upload-urls. */
  refreshUploadUrls(id) { return this.post('/media/' + enc(id) + '/upload-urls'); },
  /** POST /media/{id}/complete. `parts` is omitted for single uploads. */
  completeUpload(id, parts) { return this.post('/media/' + enc(id) + '/complete', parts && parts.length ? { parts } : {}); },
  /** GET /media/{id}. */
  getMedia(id, signal) { return this.get('/media/' + enc(id), { signal }); },
  /** GET /media?ids=. Up to 20 ids. */
  getMediaBatch(ids, query = {}, signal) { return this.post('/media/enumerate', toEnumerationBody({ ...query, filters: { ...query.filters, ids: ids || [] } }), { signal }); },
  /** PUT /media/{id}. */
  updateMedia(id, body) { return this.put('/media/' + enc(id), body); },
  /** DELETE /media/{id}. */
  deleteMedia(id) { return this.delete('/media/' + enc(id)); },
  /** POST /media/grant. */
  getMediaGrant(signal) { return this.post('/media/grant', undefined, { signal }); },

  // ---- Notifications (section 8) ----

  /** GET /notifications. Filter: `unreadOnly`. */
  listNotifications(query = {}, signal) { return list(this, '/notifications', query, signal); },
  /** GET /notifications/unread-count. */
  getUnreadCount(signal) { return this.get('/notifications/unread-count', { signal }); },
  /** POST /notifications/read. */
  markNotificationsRead(ids) { return this.post('/notifications/read', { ids }); },
  /** POST /notifications/unread. */
  markNotificationsUnread(ids) { return this.post('/notifications/unread', { ids }); },
  /** POST /notifications/read-all. */
  markAllNotificationsRead(beforeUtc) { return this.post('/notifications/read-all', beforeUtc ? { beforeUtc } : {}); },
  /** POST /me/notification-preferences/enumerate: category rows plus shared notification settings. */
  getNotificationPreferences(query = {}, signal) { return this.post('/me/notification-preferences/enumerate', toEnumerationBody(query), { signal }); },
  /** PUT /me/notification-preferences with any subset. */
  updateNotificationPreferences(body) { return this.put('/me/notification-preferences', body); },
  /** GET /me/push-tokens. */
  listPushTokens(query = {}, signal) { return this.post('/me/push-tokens/enumerate', toEnumerationBody(query), { signal }); },
  /** POST /me/push-tokens. */
  registerPushToken(body) { return this.post('/me/push-tokens', body); },
  /** DELETE /me/push-tokens. */
  unregisterPushToken(token) { return this.request('DELETE', '/me/push-tokens', { body: { token } }); },

  // ---- Reports and blocks (section 9) ----

  /** POST /reports. */
  createReport(body) { return this.post('/reports', body); },
  /** POST /users/{id}/block. */
  blockUser(id) { return this.post('/users/' + enc(id) + '/block'); },
  /** DELETE /users/{id}/block. */
  unblockUser(id) { return this.delete('/users/' + enc(id) + '/block'); },
  /** GET /me/blocks. */
  listBlocks(query = {}, signal) { return list(this, '/me/blocks', query, signal); },
  /** GET /users/{id} (Phase 1, plus `isBlockedByMe`). */
  getUser(id, signal) { return this.get('/users/' + enc(id), { signal }); },

  // ---- Member search (section 3.4) ----

  /** GET /members/search. `search` is the typed text; filters `contextType`, `contextId`. */
  searchMembers(query = {}, signal) { return list(this, '/members/search', query, signal); },

  // ---- Admin: moderators and word filter (sections 10.4 and 9.4) ----

  /** GET /admin/moderators. */
  listModerators(query = {}, signal) { return list(this, '/admin/moderators', query, signal, ADMIN); },
  /** POST /admin/moderators. */
  grantModerator(userId, resourceType = 'Feed') { return this.post('/admin/moderators', { userId, resourceType }); },
  /** DELETE /admin/moderators/{userId}?resourceType=. */
  revokeModerator(userId, resourceType = 'Feed') { return this.delete('/admin/moderators/' + enc(userId), { query: { resourceType } }); },
  /** GET /admin/moderation/word-filter (page-number paging). */
  listWordFilter(query = {}, signal) { return list(this, '/admin/moderation/word-filter', query, signal, ADMIN); },
  /** POST /admin/moderation/word-filter. */
  createWordFilterTerm(body) { return this.post('/admin/moderation/word-filter', body); },
  /** PUT /admin/moderation/word-filter/{id}. */
  updateWordFilterTerm(id, body) { return this.put('/admin/moderation/word-filter/' + enc(id), body); },
  /** DELETE /admin/moderation/word-filter/{id}. */
  deleteWordFilterTerm(id) { return this.delete('/admin/moderation/word-filter/' + enc(id)); },
  /** POST /admin/moderation/word-filter/import. */
  importWordFilter(body) { return this.post('/admin/moderation/word-filter/import', body); },
  /** POST /admin/moderation/word-filter/test. */
  testWordFilter(text) { return this.post('/admin/moderation/word-filter/test', { text }); }
};
