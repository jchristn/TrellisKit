/**
 * Training hub API methods (docs/TRELLISKIT.md section 15; REST_API.md, Training hub, Library search,
 * and Lesson techniques), mixed into `ApiClient.prototype`. Only `get`, `post`, and `put` are used, so
 * any client with the same signatures (the mobile app's client) can mix them in too. Lists are the
 * canonical `POST {collection}/enumerate` with an EnumerationQuery (member maximum 100 per page).
 */

import { ENUMERATION_MAX_RESULTS_MEMBER, toEnumerationBody } from './enumeration.js';

const enc = encodeURIComponent;
const MEMBER = { maxLimit: ENUMERATION_MAX_RESULTS_MEMBER };

/** @this {import('./ApiClient.js').ApiClient} */
export const trainingMethods = {
  /**
   * GET /me/training: the signed-in member's Training overview. Returns
   * `{ sections: [{ key, available, reason?, segments? }], upNext, continue, recentSaves, newForYou,
   * rank, coach, firstRun, labels, tags }`, where each list is an EnumerationResult. `rank` is
   * `{ rank: EffectiveRankView, next: RankNextView | null, curriculum: CurriculumView | null,
   * showVerificationLabels }` for the first Rank profile field, or null while ranks are off or the
   * property has no Rank field. `coach` is `{ available, placement }`. Signed-out clients never call
   * it; they show the public Library only.
   */
  getTrainingOverview(signal) { return this.get('/me/training', { signal }); },
  /**
   * POST /library/search with `{ query (2 to 100 characters), kinds?: ['Series', 'Lesson', 'CatalogEntry'],
   * maxResultsPerKind? (1 to 25, default 10), labels?, tags? }`. Returns `{ series, lessons, techniques }`,
   * each an EnumerationResult continuable through that kind's own enumeration route.
   */
  searchLibrary(body, signal) { return this.post('/library/search', body || {}, { signal }); },
  /** POST /lessons/{id}/catalog-entries/enumerate: techniques taught in a lesson (CatalogEntryView items, each with `linkId`, `startSeconds`, `position`, `source`, and, for staff, `warning`). */
  listLessonCatalogEntries(lessonId, query = {}, signal) {
    return this.post('/lessons/' + enc(lessonId) + '/catalog-entries/enumerate', toEnumerationBody(query, MEMBER), { signal });
  },
  /** POST /catalog/entries/{id}/lessons/enumerate: lessons that teach a technique (LessonSummaryView items, locked stubs where gated). */
  listCatalogEntryLessons(entryId, query = {}, signal) {
    return this.post('/catalog/entries/' + enc(entryId) + '/lessons/enumerate', toEnumerationBody(query, MEMBER), { signal });
  },
  /** GET /public/catalog/entries/{id}/lessons: public lessons that teach a technique (signed out). Returns an EnumerationResult of LessonSummaryView (at most 50 per page). */
  listPublicCatalogEntryLessons(entryId, signal) {
    return this.get('/public/catalog/entries/' + enc(entryId) + '/lessons', { signal });
  },
  /** PUT /admin/lessons/{id}/catalog-entries with `{ items: [{ catalogEntryId, startSeconds?, position }] }` (at most 50). Staff only. Returns the stored links as one EnumerationResult of CatalogEntryView items (each with `linkId`, `position`, `startSeconds`, `source`, and `warning`). */
  adminSetLessonCatalogEntries(lessonId, items) {
    return this.put('/admin/lessons/' + enc(lessonId) + '/catalog-entries', { items: items || [] });
  }
};
