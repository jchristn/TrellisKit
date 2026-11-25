import { toEnumerationBody } from './enumeration.js';

/**
 * Verified ranks, lineage, curricula, and attendance (docs/TRELLISKIT.md, Ranks, curricula, and attendance).
 * Every route answers 404 NotAvailable while the server's `ranks.enabled` setting is off. Rank staff
 * (SystemAdmin or RankAwarder) use the admin rank, attendance, and verification methods; curriculum
 * editors (SystemAdmin or LibraryEditor) use the admin curriculum methods.
 */
const enc = encodeURIComponent;

function enumBody(query, limits) {
  return toEnumerationBody(query || {}, limits);
}

export const ranksMethods = {
  /** GET /me/rank: effective rank per Rank field, next requirements, recent history, and lineage. */
  getMyRank(signal) { return this.get('/me/rank', { signal }); },
  /** GET /members/{id}/rank: a member's effective rank and verified flag per visible Rank field. */
  getMemberRank(userId, signal) { return this.get('/members/' + enc(userId) + '/rank', { signal }); },
  /** PUT /me/lineage with `{ fieldKey, entries: [{ optionKey, promotedByUserId, promotedByName, promotedUtc }] }`. */
  putMyLineage(body) { return this.put('/me/lineage', body); },
  /** POST /admin/lineage/{id}/verify (the named promoter or a SystemAdmin). */
  verifyLineage(id) { return this.post('/admin/lineage/' + enc(id) + '/verify', {}); },
  /** POST /admin/ranks/awards with `{ userId, fieldKey, optionKey, level, note, eventId, labels, tags }`. */
  awardRank(body) { return this.post('/admin/ranks/awards', body); },
  /** POST /admin/ranks/awards/{id}/revoke with `{ reason }`. */
  revokeRankAward(id, reason) { return this.post('/admin/ranks/awards/' + enc(id) + '/revoke', { reason }); },
  /** POST /admin/ranks/awards/enumerate (filters userId, fieldKey, optionKey, actorId, action, status, fromUtc, toUtc). */
  enumerateRankAwards(query = {}, signal) { return this.post('/admin/ranks/awards/enumerate', enumBody(query, { maxLimit: 500 }), { signal }); },
  /** POST /admin/ranks/eligibility/enumerate (filters fieldKey, optionKey, eligibleOnly). */
  enumerateRankEligibility(query = {}, signal) { return this.post('/admin/ranks/eligibility/enumerate', enumBody(query, { maxLimit: 500 }), { signal }); },
  /** POST /admin/ranks/requirements/enumerate (filters fieldKey, optionKey). */
  enumerateRankRequirements(query = {}, signal) { return this.post('/admin/ranks/requirements/enumerate', enumBody(query, { maxLimit: 500 }), { signal }); },
  /** PUT /admin/ranks/requirements with `{ fieldKey, requirements }` (SystemAdmin; at most 200). */
  putRankRequirements(body) { return this.put('/admin/ranks/requirements', body); },
  /** POST /admin/moderators with `{ userId, resourceType: 'Ranks', resourceId: '*' }` (grants RankAwarder). */
  grantRankAwarder(userId) { return this.post('/admin/moderators', { userId, resourceType: 'Ranks', resourceId: '*' }); },
  /** DELETE /admin/moderators/{userId}?resourceType=Ranks&resourceId=*. */
  revokeRankAwarder(userId) { return this.delete('/admin/moderators/' + enc(userId), { query: { resourceType: 'Ranks', resourceId: '*' } }); },

  /** POST /curricula/enumerate: published curricula with the caller's progress. */
  enumerateCurricula(query = {}, signal) { return this.post('/curricula/enumerate', enumBody(query), { signal }); },
  /** GET /curricula/{id}: items and the caller's state per item. */
  getCurriculum(id, signal) { return this.get('/curricula/' + enc(id), { signal }); },
  /** PUT /me/curricula/{id}/items/{itemId}/complete (CatalogEntry and Custom items). */
  completeCurriculumItem(id, itemId) { return this.put('/me/curricula/' + enc(id) + '/items/' + enc(itemId) + '/complete', {}); },
  /** DELETE /me/curricula/{id}/items/{itemId}/complete. */
  uncompleteCurriculumItem(id, itemId) { return this.delete('/me/curricula/' + enc(id) + '/items/' + enc(itemId) + '/complete'); },
  /** POST /admin/curricula/enumerate (filters fieldKey, status, visibility). */
  adminEnumerateCurricula(query = {}, signal) { return this.post('/admin/curricula/enumerate', enumBody(query, { maxLimit: 500 }), { signal }); },
  /** POST /admin/curricula with `{ name, description, fieldKey, visibility, position, labels, tags }`. */
  adminCreateCurriculum(body) { return this.post('/admin/curricula', body); },
  /** GET /admin/curricula/{id}. */
  adminGetCurriculum(id, signal) { return this.get('/admin/curricula/' + enc(id), { signal }); },
  /** PUT /admin/curricula/{id} (omitted fields stay). */
  adminUpdateCurriculum(id, body) { return this.put('/admin/curricula/' + enc(id), body); },
  /** PUT /admin/curricula/{id}/items with `{ items }` (at most 500; listed ids keep progress). */
  adminReplaceCurriculumItems(id, items) { return this.put('/admin/curricula/' + enc(id) + '/items', { items }); },
  /** POST /admin/curricula/{id}/publish. */
  adminPublishCurriculum(id) { return this.post('/admin/curricula/' + enc(id) + '/publish', {}); },
  /** POST /admin/curricula/{id}/archive. */
  adminArchiveCurriculum(id) { return this.post('/admin/curricula/' + enc(id) + '/archive', {}); },
  /** GET /admin/curricula/{id}/progress/{userId}. */
  adminGetCurriculumProgress(id, userId, signal) { return this.get('/admin/curricula/' + enc(id) + '/progress/' + enc(userId), { signal }); },
  /** POST /admin/curricula/{id}/progress/{userId}/items/{itemId}/verify (rank staff). */
  verifyCurriculumItem(id, userId, itemId) { return this.post('/admin/curricula/' + enc(id) + '/progress/' + enc(userId) + '/items/' + enc(itemId) + '/verify', {}); },

  /** POST /attendance/check-in with `{ targetType, targetId, occurrenceDate, code, userId, method, labels, tags }`. */
  checkIn(body) { return this.post('/attendance/check-in', body); },
  /** GET /attendance/check-in-code?targetType=&targetId=&occurrenceDate= (rank staff). */
  getCheckInCode({ targetType, targetId, occurrenceDate } = {}, signal) {
    const query = { targetType, targetId };
    if (occurrenceDate) query.occurrenceDate = occurrenceDate;
    return this.get('/attendance/check-in-code', { query, signal });
  },
  /** POST /admin/attendance/enumerate (filters targetType, targetId, occurrenceDate, userId). */
  enumerateAttendance(query = {}, signal) { return this.post('/admin/attendance/enumerate', enumBody(query, { maxLimit: 500 }), { signal }); },
  /** DELETE /admin/attendance/{id}. */
  deleteAttendance(id) { return this.delete('/admin/attendance/' + enc(id)); },
  /** POST /me/attendance/enumerate. */
  enumerateMyAttendance(query = {}, signal) { return this.post('/me/attendance/enumerate', enumBody(query), { signal }); }
};
