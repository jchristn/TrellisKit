/**
 * Phase 4 directory API methods (docs/PHASE4_CONTRACT.md sections 3 and 16), mixed into
 * `ApiClient.prototype`. Only `get`, `post`, `put`, `delete`, and `request` are used.
 *
 * Privacy (section 3.5): every `near` the client sends is rounded to 2 decimal places (about 1 km)
 * here, so no caller can send a precise device position by accident.
 */

import { ENUMERATION_MAX_RESULTS_ADMIN, toEnumerationBody } from './enumeration.js';
import { roundNear } from './directory.js';

const enc = encodeURIComponent;
const ADMIN = { maxLimit: ENUMERATION_MAX_RESULTS_ADMIN };

/** Maximum transport page size; the server independently configures the generated feature budget. */
export const MAP_MAX_FEATURES = 1000;

function enumBody(query, limits) {
  const q = query || {};
  const filters = q.filters && q.filters.near ? { ...q.filters, near: roundNear(q.filters.near) } : q.filters;
  return toEnumerationBody(filters === q.filters ? q : { ...q, filters }, limits);
}

/** The map enumeration body: `bbox` and `zoom` at the top level plus the EnumerationQuery fields (section 3.3). */
export function mapEnumerationBody({ bbox, zoom, ...query } = {}) {
  const base = toEnumerationBody({ ...query, maxResults: query.maxResults || 100 }, { maxLimit: MAP_MAX_FEATURES });
  const out = { ...base };
  if (bbox) out.bbox = { south: Number(bbox.south), west: Number(bbox.west), north: Number(bbox.north), east: Number(bbox.east) };
  if (zoom !== undefined && zoom !== null) out.zoom = Math.max(0, Math.min(22, Math.round(Number(zoom) || 0)));
  return out;
}

/** @this {import('./ApiClient.js').ApiClient} */
export const directoryMethods = {
  // ---- Venues and instructors (section 3.1) ----

  /** POST /venues/enumerate. Orderings `NameAscending`, `DistanceAscending` (with `near`), `CreatedDescending`, `CreatedAscending`, `UpdatedDescending`, `Relevance`; filters `near`, `bbox`, `programs`, `countryCode`, `region`, `locality`, `visitorPolicy`, `verifiedOnly`, `includeClosed`, `instructorId`, `audience`. */
  enumerateVenues(query = {}, signal) { return this.post('/venues/enumerate', enumBody(query), { signal }); },
  /** GET /venues/{idOrSlug}. */
  getVenue(idOrSlug, signal) { return this.get('/venues/' + enc(idOrSlug), { signal }); },
  /** POST /venues/{id}/instructors/enumerate (`NameAscending`). */
  enumerateVenueInstructors(venueId, query = {}, signal) { return this.post('/venues/' + enc(venueId) + '/instructors/enumerate', enumBody(query), { signal }); },
  /** POST /instructors/enumerate. Filters `venueId`, `rank`, `claimed`, `linkedUserId`, `audience`. */
  enumerateInstructors(query = {}, signal) { return this.post('/instructors/enumerate', enumBody(query), { signal }); },
  /** GET /instructors/{idOrSlug}. */
  getInstructor(idOrSlug, signal) { return this.get('/instructors/' + enc(idOrSlug), { signal }); },
  /** POST /instructors/{id}/venues/enumerate. */
  enumerateInstructorVenues(instructorId, query = {}, signal) { return this.post('/instructors/' + enc(instructorId) + '/venues/enumerate', enumBody(query), { signal }); },
  /** POST /directory/map/enumerate with `{ bbox, zoom, maxResults, labels, tags, filters }`; items are `Point` or `Cluster` MapFeatures. */
  enumerateDirectoryMap(query = {}, signal) { return this.post('/directory/map/enumerate', mapEnumerationBody(query), { signal }); },
  /** POST /directory/geocode/enumerate: paged candidates for a place (2 to 200 characters), provider result set at most five. */
  geocodePlace(place, query = {}, signal) { return this.post('/directory/geocode/enumerate', toEnumerationBody({ ...query, filters: { ...query.filters, query: String(place || '').trim().slice(0, 200) } }), { signal }); },
  /** POST /directory/programs/enumerate (`SortOrderAscending`). */
  enumerateDirectoryPrograms(query = {}, signal) { return this.post('/directory/programs/enumerate', enumBody(query), { signal }); },

  // ---- Venue reviews (section 3.12) ----

  /**
   * POST /venues/{id}/reviews/enumerate. Orderings `CreatedDescending` (default, newest), `CreatedAscending`,
   * `RatingDescending` (highest first, newest first within a rating), `RatingAscending` (lowest first, newest first
   * within a rating); filters `rating` (1 to 5, one or a list), `authorUserId`, `hasReply`. Signed in only.
   */
  enumerateVenueReviews(venueId, query = {}, signal) { return this.post('/venues/' + enc(venueId) + '/reviews/enumerate', enumBody(query), { signal }); },
  /** GET /venues/{id}/reviews/{reviewId}. */
  getVenueReview(venueId, reviewId, signal) { return this.get('/venues/' + enc(venueId) + '/reviews/' + enc(reviewId), { signal }); },
  /** POST /venues/{id}/reviews with `{ rating (1 to 5), body, labels?, tags? }`; one per member per venue (`409 AlreadyReviewed`). */
  createVenueReview(venueId, body) { return this.post('/venues/' + enc(venueId) + '/reviews', body); },
  /** PUT /venues/{id}/reviews/{reviewId} with `{ rating?, body?, labels?, tags? }` (the author). */
  updateVenueReview(venueId, reviewId, body) { return this.put('/venues/' + enc(venueId) + '/reviews/' + enc(reviewId), body); },
  /** DELETE /venues/{id}/reviews/{reviewId} (the author). */
  deleteVenueReview(venueId, reviewId) { return this.delete('/venues/' + enc(venueId) + '/reviews/' + enc(reviewId)); },
  /** PUT /venues/{id}/reviews/{reviewId}/reply with `{ body }` (a linked instructor of the venue; one public reply per review). */
  replyToVenueReview(venueId, reviewId, body) { return this.put('/venues/' + enc(venueId) + '/reviews/' + enc(reviewId) + '/reply', { body }); },
  /** DELETE /venues/{id}/reviews/{reviewId}/reply (the reply's author or directory staff). */
  deleteVenueReviewReply(venueId, reviewId) { return this.delete('/venues/' + enc(venueId) + '/reviews/' + enc(reviewId) + '/reply'); },

  // ---- Venue member media (section 3.13) ----

  /** POST /venues/{id}/media/enumerate. Orderings `CreatedDescending` (default), `CreatedAscending`; filters `kind` (`Image`, `Video`), `uploaderUserId`. Signed in only. */
  enumerateVenueMedia(venueId, query = {}, signal) { return this.post('/venues/' + enc(venueId) + '/media/enumerate', enumBody(query), { signal }); },
  /** POST /venues/{id}/media with `{ mediaIds (1 to 10 uploaded media ids), labels?, tags? }`; returns `{ objects: VenueMediaItem[] }`. */
  addVenueMedia(venueId, mediaIds, extra = {}) { return this.post('/venues/' + enc(venueId) + '/media', { ...extra, mediaIds: (mediaIds || []).slice(0, 10) }); },
  /** DELETE /venues/{id}/media/{itemId} with optional `{ reason }`: the uploader deletes; directory staff and the venue's linked instructors remove. */
  removeVenueMedia(venueId, itemId, reason) { return this.delete('/venues/' + enc(venueId) + '/media/' + enc(itemId), reason ? { body: { reason } } : undefined); },

  // ---- Submissions and claims (sections 3.7 and 3.8) ----

  /** POST /directory/submissions. A `200` with `status: 'Draft'` returns possible duplicates; resubmit with `confirmNotDuplicate: true`. */
  createDirectorySubmission(body) { return this.post('/directory/submissions', body); },
  /** POST /me/directory/submissions/enumerate. Filters `status`, `type`. */
  enumerateMySubmissions(query = {}, signal) { return this.post('/me/directory/submissions/enumerate', enumBody(query), { signal }); },
  /** POST /directory/submissions/{id}/withdraw. */
  withdrawSubmission(id) { return this.post('/directory/submissions/' + enc(id) + '/withdraw'); },
  /** POST /me/instructor-links/{instructorId}/unlink ("Unlink me"). */
  unlinkMyInstructor(instructorId) { return this.post('/me/instructor-links/' + enc(instructorId) + '/unlink'); },

  // ---- Public directory (section 3.11, anonymous) ----

  /** POST /public/venues/enumerate. */
  enumeratePublicVenues(query = {}, signal) { return this.post('/public/venues/enumerate', enumBody(query), { signal, retryOnUnauthorized: false }); },
  /** GET /public/venues/{id}. */
  getPublicVenue(id, signal) { return this.get('/public/venues/' + enc(id), { signal, retryOnUnauthorized: false }); },
  /** POST /public/directory/map/enumerate. */
  enumeratePublicDirectoryMap(query = {}, signal) { return this.post('/public/directory/map/enumerate', mapEnumerationBody(query), { signal, retryOnUnauthorized: false }); },

  // ---- Admin: venues (section 3.9, directory staff) ----

  /** POST /admin/venues/enumerate. Member filters plus `status`, `geoSource` (`None` finds "needs a pin"), `verified`. */
  adminEnumerateVenues(query = {}, signal) { return this.post('/admin/venues/enumerate', enumBody(query, ADMIN), { signal }); },
  /** POST /admin/venues (venue fields plus `geo?` for a manual pin). */
  adminCreateVenue(body) { return this.post('/admin/venues', body); },
  /** GET /admin/venues/{id}. */
  adminGetVenue(id, signal) { return this.get('/admin/venues/' + enc(id), { signal }); },
  /** PUT /admin/venues/{id}. */
  adminUpdateVenue(id, body) { return this.put('/admin/venues/' + enc(id), body); },
  /** DELETE /admin/venues/{id} (soft delete). */
  adminDeleteVenue(id) { return this.delete('/admin/venues/' + enc(id)); },
  /**
   * POST /admin/venues/{id}/{action} with `{ note? }` (verify, unverify) or `{ reason? }` (close, reopen, archive, unarchive).
   * @param {'verify'|'unverify'|'close'|'reopen'|'archive'|'unarchive'} action
   */
  adminVenueAction(id, action, text) {
    const allowed = ['verify', 'unverify', 'close', 'reopen', 'archive', 'unarchive'];
    if (!allowed.includes(action)) throw new Error('Unknown venue action: ' + action);
    const key = action === 'verify' || action === 'unverify' ? 'note' : 'reason';
    return this.post('/admin/venues/' + enc(id) + '/' + action, text ? { [key]: text } : {});
  },
  /** PUT /admin/venues/{id}/instructors/{instructorId} with `{ role? }` (`Head`, `Regular`, `Guest`). */
  adminLinkVenueInstructor(venueId, instructorId, role) { return this.put('/admin/venues/' + enc(venueId) + '/instructors/' + enc(instructorId), role ? { role } : {}); },
  /** DELETE /admin/venues/{id}/instructors/{instructorId}. */
  adminUnlinkVenueInstructor(venueId, instructorId) { return this.delete('/admin/venues/' + enc(venueId) + '/instructors/' + enc(instructorId)); },

  // ---- Admin: instructors ----

  /** POST /admin/instructors/enumerate. */
  adminEnumerateInstructors(query = {}, signal) { return this.post('/admin/instructors/enumerate', enumBody(query, ADMIN), { signal }); },
  /** POST /admin/instructors. */
  adminCreateInstructor(body) { return this.post('/admin/instructors', body); },
  /** GET /admin/instructors/{id}. */
  adminGetInstructor(id, signal) { return this.get('/admin/instructors/' + enc(id), { signal }); },
  /** PUT /admin/instructors/{id}. */
  adminUpdateInstructor(id, body) { return this.put('/admin/instructors/' + enc(id), body); },
  /** DELETE /admin/instructors/{id}. */
  adminDeleteInstructor(id) { return this.delete('/admin/instructors/' + enc(id)); },
  /** POST /admin/instructors/{id}/link with `{ userId }` (the member is notified and can unlink). */
  adminLinkInstructorUser(id, userId) { return this.post('/admin/instructors/' + enc(id) + '/link', { userId }); },
  /** POST /admin/instructors/{id}/unlink. */
  adminUnlinkInstructorUser(id) { return this.post('/admin/instructors/' + enc(id) + '/unlink'); },

  // ---- Admin: submissions (section 3.7) ----

  /** POST /admin/directory/submissions/enumerate. Filters `status` (default `Pending`), `type`, `submitterUserId`, `targetId`; orderings `CreatedAscending` (default), `CreatedDescending`. */
  adminEnumerateSubmissions(query = {}, signal) { return this.post('/admin/directory/submissions/enumerate', enumBody(query, ADMIN), { signal }); },
  /** GET /admin/directory/submissions/{id} (fresh `changes` diff). */
  adminGetSubmission(id, signal) { return this.get('/admin/directory/submissions/' + enc(id), { signal }); },
  /** PUT /admin/directory/submissions/{id} with `{ fields }` (reviewer overrides before approval). */
  adminUpdateSubmission(id, fields) { return this.put('/admin/directory/submissions/' + enc(id), { fields: fields || {} }); },
  /** POST /admin/directory/submissions/{id}/approve with `{ reviewNote?, notifySubmitter, geo? }`; returns `{ submission, target }`. */
  adminApproveSubmission(id, { reviewNote, notifySubmitter = true, geo } = {}) {
    const body = { notifySubmitter: Boolean(notifySubmitter) };
    if (reviewNote) body.reviewNote = reviewNote;
    if (geo && Number.isFinite(Number(geo.lat)) && Number.isFinite(Number(geo.lng))) body.geo = { lat: Number(geo.lat), lng: Number(geo.lng) };
    return this.post('/admin/directory/submissions/' + enc(id) + '/approve', body);
  },
  /** POST /admin/directory/submissions/{id}/reject with `{ reviewNote, notifySubmitter }`. */
  adminRejectSubmission(id, { reviewNote, notifySubmitter = true } = {}) { return this.post('/admin/directory/submissions/' + enc(id) + '/reject', { reviewNote, notifySubmitter: Boolean(notifySubmitter) }); },

  // ---- Admin: import, geocoding, export, programs, settings ----

  /** POST /admin/directory/import with `{ rows (max 1000), dryRun }`. */
  adminImportDirectory({ rows = [], dryRun = true } = {}) { return this.post('/admin/directory/import', { rows: rows.slice(0, 1000), dryRun: Boolean(dryRun) }); },
  /** POST /admin/directory/regeocode with `{ venueIds?, onlyMissing }`; returns `{ queued }`. */
  adminRegeocode({ venueIds, onlyMissing = true } = {}) { return this.post('/admin/directory/regeocode', venueIds && venueIds.length ? { venueIds: venueIds.slice(0, 500), onlyMissing: Boolean(onlyMissing) } : { onlyMissing: Boolean(onlyMissing) }); },
  /**
   * GET /admin/directory/metrics?days= (7 to 365, default 30): the counters plus `submissionsByDay`,
   * `approvalsByDay`, and `venuesCreatedByDay` (`[{ date, count }]`).
   */
  adminDirectoryMetrics(query = {}, signal) { return this.post('/admin/directory/metrics/enumerate', toEnumerationBody(query, { maxLimit: 500 }), { signal }); },
  /** GET /admin/directory/export.csv as text. */
  adminExportDirectoryCsv(signal) { return this.get('/admin/directory/export.csv', { responseType: 'text', signal, headers: { Accept: 'text/csv' } }); },
  /** POST /admin/directory/programs/enumerate (including retired programs). */
  adminEnumerateDirectoryPrograms(query = {}, signal) { return this.post('/admin/directory/programs/enumerate', enumBody(query, ADMIN), { signal }); },
  /** POST /admin/directory/programs with `{ key, label }` (SystemAdmin). */
  adminCreateDirectoryProgram(body) { return this.post('/admin/directory/programs', body); },
  /** PUT /admin/directory/programs/{id}. */
  adminUpdateDirectoryProgram(id, body) { return this.put('/admin/directory/programs/' + enc(id), body); },
  /** DELETE /admin/directory/programs/{id} (a program in use is retired). */
  adminDeleteDirectoryProgram(id) { return this.delete('/admin/directory/programs/' + enc(id)); },
  /** PUT /admin/directory/programs/order with `{ programIds }`. */
  adminOrderDirectoryPrograms(programIds) { return this.put('/admin/directory/programs/order', { programIds: programIds || [] }); },
  /** GET /admin/directory/settings (SystemAdmin). */
  adminGetDirectorySettings(query = {}, signal) { return this.post('/admin/directory/settings/enumerate', toEnumerationBody(query, { maxLimit: 500 }), { signal }); },
  /** PUT /admin/directory/settings (labels, submission and claim switches, geocoder, map). */
  adminUpdateDirectorySettings(body) { return this.put('/admin/directory/settings', body); },
  /** POST /admin/moderators with `{ userId, resourceType: 'Directory', resourceId: '*' }` (grants DirectoryModerator). */
  grantDirectoryModerator(userId) { return this.post('/admin/moderators', { userId, resourceType: 'Directory', resourceId: '*' }); },
  /** DELETE /admin/moderators/{userId}?resourceType=Directory&resourceId=*. */
  revokeDirectoryModerator(userId) { return this.delete('/admin/moderators/' + enc(userId), { query: { resourceType: 'Directory', resourceId: '*' } }); }
};
