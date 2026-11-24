/**
 * Phase 3 API methods (docs/PHASE3_CONTRACT.md section 19), mixed into `ApiClient.prototype`.
 * The mixin only uses `get`, `post`, `put`, `delete`, and `request`, so any client with the same
 * signatures (the mobile app's client) can mix it in too.
 *
 * Every list is the canonical `POST {collection}/enumerate` with an EnumerationQuery
 * (docs/ENUMERATION_AND_METADATA.md); admin collections allow 500 results per page.
 *
 * Comment and reaction calls for threads go through the conversation target (section 0.2): pass
 * the Thread (or ThreadSummary) and the call goes to `/threads/{id}` for an Original thread and
 * to `/feed/{id}` for a LinkedFeedPost thread, so a client can never fork a conversation. A bare
 * id addresses `/threads/{id}`.
 *
 * Global search (resolved decision 11) is `searchContent`: one EnumerationQuery over every
 * content type that exists, with `filters.types`.
 */

import { ENUMERATION_MAX_RESULTS_ADMIN, toEnumerationBody } from './enumeration.js';
import { commentTargetOf } from './forums.js';
import { targetSegment } from './engagementApi.js';

const enc = encodeURIComponent;
const ADMIN = { maxLimit: ENUMERATION_MAX_RESULTS_ADMIN };

function enumBody(query, limits) {
  return toEnumerationBody(query || {}, limits);
}

/** `/threads/{id}` or `/feed/{id}` for a thread's conversation. */
export function conversationBasePath(thread) {
  const target = commentTargetOf(thread, 'Thread');
  if (!target) throw new Error('A thread or thread id is required.');
  return '/' + targetSegment(target.type) + '/' + enc(target.id);
}

function idOf(value) {
  return typeof value === 'string' ? value : value && value.id;
}

/** @this {import('./ApiClient.js').ApiClient} */
export const forumMethods = {
  // ---- Forums (section 3.1) ----

  /** POST /forums/enumerate. Orderings `SortOrderAscending` (default) and others; filters `section`, `followedOnly`, `includeArchived`, `audience`. */
  enumerateForums(query = {}, signal) { return this.post('/forums/enumerate', enumBody(query), { signal }); },
  /** GET /forums/{idOrSlug}. */
  getForum(idOrSlug, signal) { return this.get('/forums/' + enc(idOrSlug), { signal }); },
  /** PUT /forums/{id}/follow with `{ level }` (`NewThreads`, the only Phase 3 level). */
  followForum(id, level = 'NewThreads') { return this.put('/forums/' + enc(id) + '/follow', { level }); },
  /** DELETE /forums/{id}/follow. */
  unfollowForum(id) { return this.delete('/forums/' + enc(id) + '/follow'); },
  /** POST /forums/{id}/read (marks every thread in the forum read; 204). */
  markForumRead(id) { return this.post('/forums/' + enc(id) + '/read'); },

  // ---- Threads (section 4) ----

  /** POST /forums/{id}/threads/enumerate (pinned threads lead the first page). */
  enumerateForumThreads(forumId, query = {}, signal) { return this.post('/forums/' + enc(forumId) + '/threads/enumerate', enumBody(query), { signal }); },
  /** POST /threads/enumerate (across forums, Following, own threads, and forum search with snippets). */
  enumerateThreads(query = {}, signal) { return this.post('/threads/enumerate', enumBody(query), { signal }); },
  /** GET /threads/{id} (also records the caller's read position). */
  getThread(id, signal) { return this.get('/threads/' + enc(id), { signal }); },
  /** POST /forums/{id}/threads. */
  createThread(forumId, body) { return this.post('/forums/' + enc(forumId) + '/threads', body); },
  /** PUT /threads/{id}. */
  updateThread(id, body) { return this.put('/threads/' + enc(id), body); },
  /** DELETE /threads/{id}. */
  deleteThread(id) { return this.delete('/threads/' + enc(id)); },
  /** POST /threads/{id}/revisions/enumerate (newest first). */
  enumerateThreadRevisions(id, query = {}, signal) { return this.post('/threads/' + enc(id) + '/revisions/enumerate', enumBody(query), { signal }); },
  /** POST /threads/{id}/pin. */
  pinThread(id) { return this.post('/threads/' + enc(id) + '/pin'); },
  /** POST /threads/{id}/unpin. */
  unpinThread(id) { return this.post('/threads/' + enc(id) + '/unpin'); },
  /** POST /threads/{id}/lock with `{ reason }` (0 to 500 characters). */
  lockThread(id, reason = '') { return this.post('/threads/' + enc(id) + '/lock', { reason: reason || '' }); },
  /** POST /threads/{id}/unlock. */
  unlockThread(id) { return this.post('/threads/' + enc(id) + '/unlock'); },
  /** POST /threads/{id}/move with `{ forumId, reason?, notifyAuthor? }`. */
  moveThread(id, { forumId, reason, notifyAuthor = true } = {}) { return this.post('/threads/' + enc(id) + '/move', { forumId, reason: reason || undefined, notifyAuthor }); },
  /** POST /threads/{id}/accept-answer with `{ commentId }`. */
  acceptAnswer(id, commentId) { return this.post('/threads/' + enc(id) + '/accept-answer', { commentId }); },
  /** DELETE /threads/{id}/accept-answer. */
  clearAcceptedAnswer(id) { return this.delete('/threads/' + enc(id) + '/accept-answer'); },

  // ---- Reactions and comments on the conversation target (sections 4.9 and 4.10) ----

  /** PUT {conversation}/reactions/{key}. */
  putThreadReaction(thread, key) { return this.request('PUT', conversationBasePath(thread) + '/reactions/' + enc(key)); },
  /** DELETE {conversation}/reactions/{key}. */
  deleteThreadReaction(thread, key) { return this.delete(conversationBasePath(thread) + '/reactions/' + enc(key)); },
  /** POST {conversation}/reactions/enumerate. Filter `key`. */
  enumerateThreadReactions(thread, query = {}, signal) { return this.post(conversationBasePath(thread) + '/reactions/enumerate', enumBody(query), { signal }); },
  /** POST {conversation}/comments/enumerate (tree page: ordering `Top`, `CreatedDescending`, `CreatedAscending`; filters `depth`, `childLimit`). */
  enumerateThreadComments(thread, query = {}, signal) { return this.post(conversationBasePath(thread) + '/comments/enumerate', enumBody(query), { signal }); },
  /** POST {conversation}/comments. */
  createThreadComment(thread, body) { return this.post(conversationBasePath(thread) + '/comments', body); },

  // ---- Polls (section 6) ----

  /** PUT /polls/{id}/votes with `{ optionIds }` (replaces the caller's vote). */
  votePoll(pollId, optionIds) { return this.put('/polls/' + enc(pollId) + '/votes', { optionIds: optionIds || [] }); },
  /** DELETE /polls/{id}/votes. */
  removePollVote(pollId) { return this.delete('/polls/' + enc(pollId) + '/votes'); },
  /** POST /polls/{id}/close (author or moderator in scope). */
  closePoll(pollId) { return this.post('/polls/' + enc(pollId) + '/close'); },
  /**
   * POST /polls/{id}/voters/enumerate: who voted, for polls whose author chose visible voters
   * (resolved decision 4). Filter `optionId`. Items `{ user, optionIds, createdUtc }`.
   */
  enumeratePollVoters(pollId, query = {}, signal) { return this.post('/polls/' + enc(pollId) + '/voters/enumerate', enumBody(query), { signal }); },

  // ---- Feedback (section 7) ----

  /** PUT /threads/{id}/feedback with `{ status, reviewCommentId? }`. */
  setFeedbackStatus(id, status, reviewCommentId) { return this.put('/threads/' + enc(id) + '/feedback', reviewCommentId ? { status, reviewCommentId } : { status }); },

  // ---- Forum to feed portability (section 8) ----

  /** POST /threads/{id}/promote with `{ title?, excerpt?, pin?, audience?, notifyMembers? }`; returns the FeedPost. */
  promoteThread(id, body = {}) { return this.post('/threads/' + enc(id) + '/promote', body); },
  /** POST /threads/{id}/unpromote; returns the Thread. */
  unpromoteThread(id) { return this.post('/threads/' + enc(id) + '/unpromote'); },
  /** POST /feed/{id}/discuss with `{ forumId, title?, labels?, tags? }`; returns the Thread. */
  discussFeedPost(feedPostId, body) { return this.post('/feed/' + enc(feedPostId) + '/discuss', body); },
  /** POST /feed/{id}/undiscuss; returns the FeedPost. */
  undiscussFeedPost(feedPostId) { return this.post('/feed/' + enc(feedPostId) + '/undiscuss'); },

  // ---- Following (section 9.2) ----

  /** PUT /threads/{id}/follow; returns `{ isFollowed: true }`. */
  followThread(id) { return this.put('/threads/' + enc(idOf(id)) + '/follow'); },
  /** DELETE /threads/{id}/follow; returns `{ isFollowed: false }`. */
  unfollowThread(id) { return this.delete('/threads/' + enc(idOf(id)) + '/follow'); },
  /** POST /me/follows/enumerate. Filter `targetType` (`Forum` or `Thread`). */
  enumerateMyFollows(query = {}, signal) { return this.post('/me/follows/enumerate', enumBody(query), { signal }); },

  // ---- Public sample threads (section 10, anonymous) ----

  /** POST /public/threads/enumerate (`maxResults` at most 20). */
  enumeratePublicThreads(query = {}, signal) { return this.post('/public/threads/enumerate', enumBody({ maxResults: 20, ...query }, { maxLimit: 20 }), { signal, retryOnUnauthorized: false }); },
  /** GET /public/threads/{id}. */
  getPublicThread(id, signal) { return this.get('/public/threads/' + enc(id), { signal, retryOnUnauthorized: false }); },

  // ---- Global search (resolved decision 11) ----

  /**
   * POST /search/enumerate: one search box across every content type the server has. `query.search`
   * is required; `filters.types` narrows the types; labels, tags, and the time window apply.
   * Items are `SearchResult` objects `{ type, id, title, snippet, path, author, createdUtc, labels, tags }`.
   */
  searchContent(query = {}, signal) { return this.post('/search/enumerate', enumBody(query), { signal }); },

  // ---- Moderation shared with the member app (Phase 2 section 9.3, Phase 3 section 11.2) ----

  /** POST /admin/moderation/actions with `{ targetType, targetId, action, reason, note?, notifyAuthor? }`. */
  createModerationAction(body) { return this.post('/admin/moderation/actions', body); },

  // ---- Admin: forums (section 3.2) ----

  /** POST /admin/forums/enumerate (admin maximum). Filters `section`, `isArchived`, `audience`, `includeArchived`. */
  adminEnumerateForums(query = {}, signal) { return this.post('/admin/forums/enumerate', enumBody(query, ADMIN), { signal }); },
  /** GET /admin/forums/{id} (AdminForum with a bounded `moderators` preview). */
  adminGetForum(id, signal) { return this.get('/admin/forums/' + enc(id), { signal }); },
  /** POST /admin/forums. */
  adminCreateForum(body) { return this.post('/admin/forums', body); },
  /** PUT /admin/forums/{id}. */
  adminUpdateForum(id, body) { return this.put('/admin/forums/' + enc(id), body); },
  /** PUT /admin/forums/order with `{ forumIds }` (every non-archived forum once). */
  adminOrderForums(forumIds) { return this.put('/admin/forums/order', { forumIds }); },
  /** POST /admin/forums/{id}/archive. */
  adminArchiveForum(id, reason) { return this.post('/admin/forums/' + enc(id) + '/archive', reason ? { reason } : {}); },
  /** POST /admin/forums/{id}/unarchive. */
  adminUnarchiveForum(id, reason) { return this.post('/admin/forums/' + enc(id) + '/unarchive', reason ? { reason } : {}); },
  /** DELETE /admin/forums/{id} (409 ForumNotEmpty while it has threads). */
  adminDeleteForum(id) { return this.delete('/admin/forums/' + enc(id)); },
  /** POST /admin/forums/{id}/moderators/enumerate. */
  adminEnumerateForumModerators(id, query = {}, signal) { return this.post('/admin/forums/' + enc(id) + '/moderators/enumerate', enumBody(query, ADMIN), { signal }); },
  /** POST /admin/moderators with `{ userId, resourceType: 'Forum', resourceId }` (`frm_...` or `*`, section 11.6). */
  grantForumModerator(userId, forumId = '*') { return this.post('/admin/moderators', { userId, resourceType: 'Forum', resourceId: forumId }); },
  /** DELETE /admin/moderators/{userId}?resourceType=Forum&resourceId=. */
  revokeForumModerator(userId, forumId = '*') { return this.delete('/admin/moderators/' + enc(userId), { query: { resourceType: 'Forum', resourceId: forumId } }); },

  // ---- Admin: threads, trending, metrics (sections 5.3 and 12) ----

  /** POST /admin/threads/enumerate (every forum and state in the caller's scope, including Deleted). */
  adminEnumerateThreads(query = {}, signal) { return this.post('/admin/threads/enumerate', enumBody(query, ADMIN), { signal }); },
  /** POST /admin/trending/enumerate (ordering `ScoreDescending`; filters `forumIds`, `type`, `promoted`, `minScore`). */
  adminEnumerateTrending(query = {}, signal) { return this.post('/admin/trending/enumerate', enumBody(query, ADMIN), { signal }); },
  /** GET /admin/metrics/forums?days= (7 to 365). */
  adminForumMetrics(query = {}, signal) { return this.post('/admin/metrics/forums/enumerate', toEnumerationBody(query, { maxLimit: 500 }), { signal }); },

  // ---- Admin: forum mutes (section 11.3) ----

  /** POST /admin/moderation/forum-mutes with `{ userId, forumId, untilUtc, reason, notifyMember }` (`untilUtc` null mutes until lifted, resolved decision 15). */
  createForumMute(body) { return this.post('/admin/moderation/forum-mutes', body); },
  /** POST /admin/moderation/forum-mutes/enumerate. Filters `forumId`, `userId`, `activeOnly` (default true). */
  enumerateForumMutes(query = {}, signal) { return this.post('/admin/moderation/forum-mutes/enumerate', enumBody(query, ADMIN), { signal }); },
  /** DELETE /admin/moderation/forum-mutes/{id} with `{ reason? }`. */
  endForumMute(id, reason) { return this.request('DELETE', '/admin/moderation/forum-mutes/' + enc(id), { body: reason ? { reason } : {} }); }
};
