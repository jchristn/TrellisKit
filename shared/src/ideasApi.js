/**
 * Phase 5 ideas board API methods (docs/PHASE5_CONTRACT.md section 7), mixed into
 * `ApiClient.prototype`. Comments and reactions on an idea use the Phase 2 shapes on the idea's
 * `commentTarget` (`{ type: "Idea", id }`), so the generic `listComments`, `createComment`,
 * `putReaction`, and `deleteReaction` helpers also work with an idea target.
 */

import { ENUMERATION_MAX_RESULTS_ADMIN, toEnumerationBody } from './enumeration.js';

const enc = encodeURIComponent;
const ADMIN = { maxLimit: ENUMERATION_MAX_RESULTS_ADMIN };

/** Ideas board orderings (contract section 7.1). */
export const IdeaOrdering = Object.freeze({ Top: 'Top', Trending: 'Trending', CreatedDescending: 'CreatedDescending', UpdatedDescending: 'UpdatedDescending', Relevance: 'Relevance' });

/** Member submission modes (setting `ideas.memberSubmissions`). */
export const IdeaSubmissions = Object.freeze({ Direct: 'Direct', StaffOnly: 'StaffOnly' });

/** Ideas error codes (contract section 0.4). */
export const IdeaError = Object.freeze({ IdeaMerged: 'IdeaMerged' });

function enumBody(query, limits) {
  return toEnumerationBody(query || {}, limits);
}

function idOf(value) {
  return typeof value === 'string' ? value : value && value.id;
}

function ideaPath(id) {
  return '/ideas/' + enc(idOf(id));
}

/** @this {import('./ApiClient.js').ApiClient} */
export const ideasMethods = {
  // ---- Member board (section 7.1) ----

  /**
   * POST /ideas/enumerate. Orderings `Top` (default), `Trending`, `CreatedDescending`,
   * `UpdatedDescending`, `Relevance`; filters `status` (keys), `open`, `votedByMe`, `authorUserId`,
   * `audience`, `includeMerged`.
   */
  enumerateIdeas(query = {}, signal) { return this.post('/ideas/enumerate', enumBody(query), { signal }); },
  /** GET /ideas/{id} (a merged idea returns its own payload with `mergedInto`). */
  getIdea(id, signal) { return this.get(ideaPath(id), { signal }); },
  /** POST /ideas with `{ title, body, labels?, tags? }` (only when `ideas.memberSubmissions` is `Direct`). */
  createIdea(body) { return this.post('/ideas', body); },
  /** PUT /ideas/{id}. */
  updateIdea(id, body) { return this.put(ideaPath(id), body); },
  /** PUT /ideas/{id}/vote (idempotent); resolves the Idea. */
  voteIdea(id) { return this.request('PUT', ideaPath(id) + '/vote'); },
  /** DELETE /ideas/{id}/vote; resolves the Idea. */
  unvoteIdea(id) { return this.delete(ideaPath(id) + '/vote'); },
  /** PUT /ideas/{id}/follow; resolves `{ isFollowed: true }`. */
  followIdea(id) { return this.request('PUT', ideaPath(id) + '/follow'); },
  /** DELETE /ideas/{id}/follow; resolves `{ isFollowed: false }`. */
  unfollowIdea(id) { return this.delete(ideaPath(id) + '/follow'); },
  /**
   * POST /ideas/statuses/enumerate: the configured statuses for the board's filter chips. Not in
   * the contract route table; clients may read `property.json` `ideas.statuses` instead.
   */
  enumerateIdeaStatuses(query = {}, signal) { return this.post('/ideas/statuses/enumerate', enumBody(query), { signal }); },

  // ---- Comments and reactions (section 7.1, Phase 2 shapes) ----

  /** POST /ideas/{id}/comments/enumerate (ordering `Top`, `CreatedDescending`, `CreatedAscending`; filters `depth`, `childLimit`). */
  enumerateIdeaComments(id, query = {}, signal) { return this.post(ideaPath(id) + '/comments/enumerate', enumBody(query), { signal }); },
  /** POST /ideas/{id}/comments (`409 IdeaMerged` for a merged idea). */
  createIdeaComment(id, body) { return this.post(ideaPath(id) + '/comments', body); },
  /** PUT /ideas/{id}/reactions/{key}. */
  putIdeaReaction(id, key) { return this.request('PUT', ideaPath(id) + '/reactions/' + enc(key)); },
  /** DELETE /ideas/{id}/reactions/{key}. */
  deleteIdeaReaction(id, key) { return this.delete(ideaPath(id) + '/reactions/' + enc(key)); },
  /** POST /ideas/{id}/reactions/enumerate. Filter `key`. */
  enumerateIdeaReactions(id, query = {}, signal) { return this.post(ideaPath(id) + '/reactions/enumerate', enumBody(query), { signal }); },
  /** POST /reports with `targetType: "Idea"`. */
  reportIdea(id, { reason, details } = {}) {
    const body = { targetType: 'Idea', targetId: idOf(id), reason };
    if (details !== undefined && details !== null) body.details = details;
    return this.post('/reports', body);
  },

  // ---- Public board (section 7.3, anonymous) ----

  /** POST /public/ideas/enumerate (Public ideas when `ideas.publicBoardEnabled`). */
  enumeratePublicIdeas(query = {}, signal) { return this.post('/public/ideas/enumerate', enumBody(query), { signal, retryOnUnauthorized: false }); },
  /** GET /public/ideas/{id}. */
  getPublicIdea(id, signal) { return this.get('/public/ideas/' + enc(idOf(id)), { signal, retryOnUnauthorized: false }); },

  // ---- Staff (section 7.2) ----

  /** POST /admin/ideas/enumerate (adds `hidden`, `merged` filters). */
  adminEnumerateIdeas(query = {}, signal) { return this.post('/admin/ideas/enumerate', enumBody(query, ADMIN), { signal }); },
  /** POST /admin/ideas (staff-created). */
  adminCreateIdea(body) { return this.post('/admin/ideas', body); },
  /** PUT /admin/ideas/{id}. */
  adminUpdateIdea(id, body) { return this.put('/admin/ideas/' + enc(idOf(id)), body); },
  /** PUT /admin/ideas/{id}/status with `{ status, statusNote, notifyVoters }`. */
  adminSetIdeaStatus(id, { status, statusNote = '', notifyVoters = true } = {}) {
    return this.put('/admin/ideas/' + enc(idOf(id)) + '/status', { status, statusNote: statusNote || '', notifyVoters: notifyVoters !== false });
  },
  /** POST /admin/ideas/{id}/merge with `{ intoIdeaId }`. */
  adminMergeIdea(id, intoIdeaId) { return this.post('/admin/ideas/' + enc(idOf(id)) + '/merge', { intoIdeaId: idOf(intoIdeaId) }); },
  /** POST /admin/ideas/{id}/unmerge. */
  adminUnmergeIdea(id) { return this.post('/admin/ideas/' + enc(idOf(id)) + '/unmerge'); },
  /** DELETE /admin/ideas/{id} (soft delete). */
  adminDeleteIdea(id) { return this.delete('/admin/ideas/' + enc(idOf(id))); },

  // ---- Staff idea statuses (section 7.2, SystemAdmin) ----

  /** POST /admin/ideas/statuses/enumerate (includes retired statuses). */
  adminEnumerateIdeaStatuses(query = {}, signal) { return this.post('/admin/ideas/statuses/enumerate', enumBody(query, ADMIN), { signal }); },
  /** POST /admin/ideas/statuses with `{ key, label, color, isClosed, isInitial }`. */
  adminCreateIdeaStatus(body) { return this.post('/admin/ideas/statuses', body); },
  /** PUT /admin/ideas/statuses/{id}. */
  adminUpdateIdeaStatus(id, body) { return this.put('/admin/ideas/statuses/' + enc(idOf(id)), body); },
  /** DELETE /admin/ideas/statuses/{id} (retires). */
  adminDeleteIdeaStatus(id) { return this.delete('/admin/ideas/statuses/' + enc(idOf(id))); },
  /** PUT /admin/ideas/statuses/order with `{ ideaStatusIds }`. */
  adminOrderIdeaStatuses(ideaStatusIds) { return this.put('/admin/ideas/statuses/order', { ideaStatusIds: (ideaStatusIds || []).map(idOf) }); }
};
