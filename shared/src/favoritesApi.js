/**
 * Saves and private collections API methods (REST_API.md, Saves), mixed into `ApiClient.prototype`.
 * Only `get`, `post`, `put`, and `delete` are used, so any client with the same signatures (the
 * mobile app's client) can mix them in too. Lists are the canonical `POST {collection}/enumerate`
 * with an EnumerationQuery (member maximum 100 per page).
 */

import { ENUMERATION_MAX_RESULTS_MEMBER, toEnumerationBody } from './enumeration.js';

const enc = encodeURIComponent;
const MEMBER = { maxLimit: ENUMERATION_MAX_RESULTS_MEMBER };

function savePath(targetType, targetId) {
  return '/me/saves/' + enc(targetType) + '/' + enc(targetId);
}

/** @this {import('./ApiClient.js').ApiClient} */
export const favoritesMethods = {
  /**
   * PUT /me/saves/{targetType}/{targetId}: save a target (idempotent). Every option is optional:
   * `collectionIds` (replaces the set, at most 20), `note` (null clears; visible to you and
   * moderators), `labels`, `tags`. Returns the FavoriteView.
   */
  saveItem(targetType, targetId, options = {}) {
    const body = {};
    for (const key of ['collectionIds', 'note', 'labels', 'tags']) {
      if (options[key] !== undefined) body[key] = options[key];
    }
    return this.put(savePath(targetType, targetId), body);
  },
  /** DELETE /me/saves/{targetType}/{targetId} (idempotent; 204). */
  unsaveItem(targetType, targetId) { return this.delete(savePath(targetType, targetId)); },
  /** POST /me/saves/enumerate. Orderings `CreatedDescending` (default), `CreatedAscending`, `UpdatedDescending`; filters `targetTypes`, `collectionId`. */
  listSaves(query = {}, signal) { return this.post('/me/saves/enumerate', toEnumerationBody(query, MEMBER), { signal }); },
  /** POST /me/saves/state with 1 to 100 `{ targetType, targetId }`; returns `{ items: [{ targetType, targetId, saved, favoriteId }] }`. */
  saveState(targets, signal) { return this.post('/me/saves/state', { targets: targets || [] }, { signal }); },
  /** POST /me/save-collections/enumerate. Orderings `PositionAscending` (default), `NameAscending`, `CreatedDescending`, `CreatedAscending`. */
  listSaveCollections(query = {}, signal) { return this.post('/me/save-collections/enumerate', toEnumerationBody(query, MEMBER), { signal }); },
  /** POST /me/save-collections with `{ name, labels?, tags? }`; returns the FavoriteCollection (201). */
  createSaveCollection(body) { return this.post('/me/save-collections', body || {}); },
  /** PUT /me/save-collections/{id} with `{ name?, labels?, tags? }`. */
  updateSaveCollection(id, body) { return this.put('/me/save-collections/' + enc(id), body || {}); },
  /** PUT /me/save-collections/order with every collection id exactly once. */
  orderSaveCollections(ids) { return this.put('/me/save-collections/order', { ids: ids || [] }); },
  /** DELETE /me/save-collections/{id}; the saves stay. */
  deleteSaveCollection(id) { return this.delete('/me/save-collections/' + enc(id)); }
};
