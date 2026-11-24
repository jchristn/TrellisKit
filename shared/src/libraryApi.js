/**
 * Phase 4 library API methods (docs/PHASE4_CONTRACT.md sections 5 and 16), mixed into
 * `ApiClient.prototype`. Only `get`, `post`, `put`, `delete`, and `request` are used, so any client
 * with the same signatures (the mobile app's client) can mix it in too.
 *
 * Every list is the canonical `POST {collection}/enumerate` with an EnumerationQuery
 * (docs/ENUMERATION_AND_METADATA.md); admin collections allow 500 results per page. Library staff
 * (SystemAdmin or LibraryEditor) use the admin series, lesson, metrics, and viewing history
 * methods; library settings and levels stay SystemAdmin only (the server decides).
 */

import { ENUMERATION_MAX_RESULTS_ADMIN, toEnumerationBody } from './enumeration.js';

const enc = encodeURIComponent;
const ADMIN = { maxLimit: ENUMERATION_MAX_RESULTS_ADMIN };
const PUBLIC = { maxLimit: 20 };

function enumBody(query, limits) {
  return toEnumerationBody(query || {}, limits);
}

/** @this {import('./ApiClient.js').ApiClient} */
export const libraryMethods = {
  // ---- Series (section 5.1) ----

  /** POST /series/enumerate. Orderings `SortOrderAscending` (default), `TitleAscending`, `PublishedDescending`, `Relevance`; filters `levels`, `followedOnly`, `progress`, `audience`. */
  enumerateSeries(query = {}, signal) { return this.post('/series/enumerate', enumBody(query), { signal }); },
  /** GET /series/{idOrSlug}. */
  getSeries(idOrSlug, signal) { return this.get('/series/' + enc(idOrSlug), { signal }); },
  /** POST /series/{id}/lessons/enumerate (ordering `PositionAscending`; locked stubs per section 0.4). */
  enumerateSeriesLessons(seriesId, query = {}, signal) { return this.post('/series/' + enc(seriesId) + '/lessons/enumerate', enumBody(query), { signal }); },
  /** PUT /series/{id}/follow; returns `{ isFollowed: true }`. */
  followSeries(id) { return this.put('/series/' + enc(id) + '/follow'); },
  /** DELETE /series/{id}/follow; returns `{ isFollowed: false }`. */
  unfollowSeries(id) { return this.delete('/series/' + enc(id) + '/follow'); },

  // ---- Lessons (sections 5.1 and 5.5) ----

  /** POST /lessons/enumerate. Orderings `PublishedDescending` (default), `TitleAscending`, `DurationAscending`, `Relevance`; filters `seriesIds`, `levels`, `kind`, `hasResources`, `minDurationSeconds`, `maxDurationSeconds`, `progress`, `audience`. */
  enumerateLessons(query = {}, signal) { return this.post('/lessons/enumerate', enumBody(query), { signal }); },
  /** GET /lessons/{idOrSlug}. */
  getLesson(idOrSlug, signal) { return this.get('/lessons/' + enc(idOrSlug), { signal }); },
  /** PUT /lessons/{id}/progress with `{ positionSeconds, playedSeconds, clientUpdatedUtc, sessionId }`; returns LessonProgress. */
  putLessonProgress(id, body, signal) { return this.put('/lessons/' + enc(id) + '/progress', body, { signal }); },
  /** DELETE /lessons/{id}/progress (204). */
  resetLessonProgress(id) { return this.delete('/lessons/' + enc(id) + '/progress'); },
  /** Alias of `resetLessonProgress` (DELETE /lessons/{id}/progress). */
  deleteLessonProgress(id) { return this.delete('/lessons/' + enc(id) + '/progress'); },
  /** POST /lessons/{id}/complete; returns LessonProgress. */
  completeLesson(id) { return this.post('/lessons/' + enc(id) + '/complete'); },
  /** POST /lessons/{id}/uncomplete; returns LessonProgress. */
  uncompleteLesson(id) { return this.post('/lessons/' + enc(id) + '/uncomplete'); },
  /** POST /library/levels/enumerate; items `{ key, label, color }` from the configured source. */
  enumerateLibraryLevels(query = {}, signal) { return this.post('/library/levels/enumerate', enumBody(query), { signal }); },

  // ---- Lesson comments, reactions, promotion (sections 5.1, 5.8, 6) ----

  /** POST /lessons/{id}/comments/enumerate (Phase 2 comment tree page). */
  enumerateLessonComments(id, query = {}, signal) { return this.post('/lessons/' + enc(id) + '/comments/enumerate', enumBody(query), { signal }); },
  /** POST /lessons/{id}/comments. */
  createLessonComment(id, body) { return this.post('/lessons/' + enc(id) + '/comments', body); },
  /** PUT /lessons/{id}/reactions/{key}. */
  putLessonReaction(id, key) { return this.request('PUT', '/lessons/' + enc(id) + '/reactions/' + enc(key)); },
  /** DELETE /lessons/{id}/reactions/{key}. */
  deleteLessonReaction(id, key) { return this.delete('/lessons/' + enc(id) + '/reactions/' + enc(key)); },
  /** POST /lessons/{id}/reactions/enumerate. */
  enumerateLessonReactions(id, query = {}, signal) { return this.post('/lessons/' + enc(id) + '/reactions/enumerate', enumBody(query), { signal }); },
  /** POST /lessons/{id}/promote with `{ title?, excerpt?, pin?, audience?, notifyMembers? }` (default true for lessons); returns the FeedPost. */
  promoteLesson(id, body = {}) { return this.post('/lessons/' + enc(id) + '/promote', body); },
  /** POST /lessons/{id}/unpromote. */
  unpromoteLesson(id) { return this.post('/lessons/' + enc(id) + '/unpromote'); },

  // ---- My library (sections 5.1 and 5.9) ----

  /** POST /me/library/continue/enumerate (`LastWatchedDescending`; default 10); items `{ lesson, progress }`. */
  enumerateContinueWatching(query = {}, signal) { return this.post('/me/library/continue/enumerate', enumBody({ maxResults: 10, ...query }), { signal }); },
  /** POST /me/library/progress/enumerate. Filters `completed`, `seriesId`. */
  enumerateMyLessonProgress(query = {}, signal) { return this.post('/me/library/progress/enumerate', enumBody(query), { signal }); },
  /** POST /lessons/{id}/downloads with `{ deviceId, platform: 'Ios' | 'Android' }`; returns LessonDownload. */
  createLessonDownload(id, body) { return this.post('/lessons/' + enc(id) + '/downloads', body); },
  /** POST /me/library/downloads/enumerate. */
  enumerateMyDownloads(query = {}, signal) { return this.post('/me/library/downloads/enumerate', enumBody(query), { signal }); },
  /** POST /me/library/downloads/{id}/renew; returns LessonDownload. */
  renewLessonDownload(id) { return this.post('/me/library/downloads/' + enc(id) + '/renew'); },
  /** DELETE /me/library/downloads/{id} (204). */
  deleteLessonDownload(id) { return this.delete('/me/library/downloads/' + enc(id)); },

  // ---- Public sample lessons (section 5.12, anonymous) ----

  /** POST /public/lessons/enumerate (`maxResults` at most 20). */
  enumeratePublicLessons(query = {}, signal) { return this.post('/public/lessons/enumerate', enumBody({ maxResults: 20, ...query }, PUBLIC), { signal, retryOnUnauthorized: false }); },
  /** GET /public/lessons/{id}. */
  getPublicLesson(id, signal) { return this.get('/public/lessons/' + enc(id), { signal, retryOnUnauthorized: false }); },

  // ---- Admin: series (section 5.10, library staff) ----

  /** POST /admin/series/enumerate (admin maximum). Filters `status`, `audience`, `levels`. */
  adminEnumerateSeries(query = {}, signal) { return this.post('/admin/series/enumerate', enumBody(query, ADMIN), { signal }); },
  /** POST /admin/series. */
  adminCreateSeries(body) { return this.post('/admin/series', body); },
  /** GET /admin/series/{id}. */
  adminGetSeries(id, signal) { return this.get('/admin/series/' + enc(id), { signal }); },
  /** PUT /admin/series/{id} (`narrowLessons` offers to narrow lessons with the old audience). */
  adminUpdateSeries(id, body) { return this.put('/admin/series/' + enc(id), body); },
  /** DELETE /admin/series/{id} (409 SeriesNotEmpty while it has lessons). */
  adminDeleteSeries(id) { return this.delete('/admin/series/' + enc(id)); },
  /** POST /admin/series/{id}/publish. */
  adminPublishSeries(id) { return this.post('/admin/series/' + enc(id) + '/publish'); },
  /** POST /admin/series/{id}/archive. */
  adminArchiveSeries(id) { return this.post('/admin/series/' + enc(id) + '/archive'); },
  /** POST /admin/series/{id}/unarchive. */
  adminUnarchiveSeries(id) { return this.post('/admin/series/' + enc(id) + '/unarchive'); },
  /** PUT /admin/series/order with `{ seriesIds }`. */
  adminOrderSeries(seriesIds) { return this.put('/admin/series/order', { seriesIds: seriesIds || [] }); },

  // ---- Admin: lessons (section 5.10) ----

  /** POST /admin/lessons/enumerate. Filters `seriesId`, `status`, `hasVideo`, `mediaStatus`. */
  adminEnumerateLessons(query = {}, signal) { return this.post('/admin/lessons/enumerate', enumBody(query, ADMIN), { signal }); },
  /** POST /admin/series/{id}/lessons. */
  adminCreateLesson(seriesId, body) { return this.post('/admin/series/' + enc(seriesId) + '/lessons', body); },
  /** GET /admin/lessons/{id}. */
  adminGetLesson(id, signal) { return this.get('/admin/lessons/' + enc(id), { signal }); },
  /** PUT /admin/lessons/{id} (markers, resources, captions replace the bounded lists; `videoMediaId`). */
  adminUpdateLesson(id, body) { return this.put('/admin/lessons/' + enc(id), body); },
  /** DELETE /admin/lessons/{id} (soft delete). */
  adminDeleteLesson(id) { return this.delete('/admin/lessons/' + enc(id)); },
  /** POST /admin/lessons/{id}/publish. */
  adminPublishLesson(id) { return this.post('/admin/lessons/' + enc(id) + '/publish'); },
  /** POST /admin/lessons/{id}/schedule with `{ publishAtUtc }`. */
  adminScheduleLesson(id, publishAtUtc) { return this.post('/admin/lessons/' + enc(id) + '/schedule', { publishAtUtc }); },
  /** POST /admin/lessons/{id}/archive. */
  adminArchiveLesson(id) { return this.post('/admin/lessons/' + enc(id) + '/archive'); },
  /** POST /admin/lessons/{id}/unarchive. */
  adminUnarchiveLesson(id) { return this.post('/admin/lessons/' + enc(id) + '/unarchive'); },
  /** POST /admin/lessons/{id}/move with `{ seriesId, position }`. */
  adminMoveLesson(id, { seriesId, position } = {}) { return this.post('/admin/lessons/' + enc(id) + '/move', { seriesId, position }); },
  /** PUT /admin/series/{id}/lessons/order with `{ lessonIds }`. */
  adminOrderLessons(seriesId, lessonIds) { return this.put('/admin/series/' + enc(seriesId) + '/lessons/order', { lessonIds: lessonIds || [] }); },

  // ---- Admin: viewing history (resolved decision 17; every read is audited server side) ----

  /** POST /admin/library/members/{userId}/progress/enumerate; items `{ lesson: LessonSummary, progress: LessonProgress }`. */
  adminEnumerateMemberLibraryProgress(userId, query = {}, signal) { return this.post('/admin/library/members/' + enc(userId) + '/progress/enumerate', enumBody(query, ADMIN), { signal }); },
  /** POST /admin/lessons/{id}/viewers/enumerate; items `{ user: UserSummary, progress: LessonProgress }`. */
  adminEnumerateLessonViewers(lessonId, query = {}, signal) { return this.post('/admin/lessons/' + enc(lessonId) + '/viewers/enumerate', enumBody(query, ADMIN), { signal }); },

  // ---- Admin: metrics, settings, levels (SystemAdmin for settings and levels) ----

  /** GET /admin/metrics/library?days= (1 to 365). */
  adminLibraryMetrics(query = {}, signal) { return this.post('/admin/metrics/library/enumerate', toEnumerationBody(query, { maxLimit: 500 }), { signal }); },
  /** GET /admin/library/settings. */
  adminGetLibrarySettings(signal) { return this.get('/admin/library/settings', { signal }); },
  /** PUT /admin/library/settings. */
  adminUpdateLibrarySettings(body) { return this.put('/admin/library/settings', body); },
  /** POST /admin/library/levels/enumerate (Custom levels, including retired ones). */
  adminEnumerateLibraryLevels(query = {}, signal) { return this.post('/admin/library/levels/enumerate', enumBody(query, ADMIN), { signal }); },
  /** POST /admin/library/levels with `{ key, label, color? }`. */
  adminCreateLibraryLevel(body) { return this.post('/admin/library/levels', body); },
  /** PUT /admin/library/levels/{id}. */
  adminUpdateLibraryLevel(id, body) { return this.put('/admin/library/levels/' + enc(id), body); },
  /** DELETE /admin/library/levels/{id} (a level in use is retired). */
  adminDeleteLibraryLevel(id) { return this.delete('/admin/library/levels/' + enc(id)); },
  /** PUT /admin/library/levels/order with `{ levelIds }`. */
  adminOrderLibraryLevels(levelIds) { return this.put('/admin/library/levels/order', { levelIds: levelIds || [] }); },
  /** POST /admin/moderators with `{ userId, resourceType: 'Library', resourceId: '*' }` (grants LibraryEditor). */
  grantLibraryEditor(userId) { return this.post('/admin/moderators', { userId, resourceType: 'Library', resourceId: '*' }); },
  /** DELETE /admin/moderators/{userId}?resourceType=Library&resourceId=*. */
  revokeLibraryEditor(userId) { return this.delete('/admin/moderators/' + enc(userId), { query: { resourceType: 'Library', resourceId: '*' } }); }
};
