/**
 * Phase 5 broadcast and announcement API methods (docs/PHASE5_CONTRACT.md section 8, as-built
 * note D), mixed into `ApiClient.prototype`. Admin broadcast routes are for `SystemAdmin` and
 * `Communicator`; announcements are the member view of received broadcasts.
 */

import { ENUMERATION_MAX_RESULTS_ADMIN, toEnumerationBody } from './enumeration.js';

const enc = encodeURIComponent;
const ADMIN = { maxLimit: ENUMERATION_MAX_RESULTS_ADMIN };

/** Broadcast statuses (contract section 2.5). */
export const BroadcastStatus = Object.freeze({ Draft: 'Draft', Scheduled: 'Scheduled', Sending: 'Sending', Sent: 'Sent', Canceled: 'Canceled' });

/** Broadcast categories (contract section 8.3). */
export const BroadcastCategory = Object.freeze({ Optional: 'Optional', Essential: 'Essential' });

/** Broadcast channels (contract section 2.5). */
export const BroadcastChannel = Object.freeze({ InApp: 'inApp', Push: 'push', Email: 'email' });

/** Broadcast error codes (contract section 0.4). */
export const BroadcastError = Object.freeze({ SegmentInvalid: 'SegmentInvalid', BroadcastNotEditable: 'BroadcastNotEditable', BroadcastLimitReached: 'BroadcastLimitReached' });

function enumBody(query, limits) {
  return toEnumerationBody(query || {}, limits);
}

function idOf(value) {
  return typeof value === 'string' ? value : value && value.id;
}

function broadcastPath(id) {
  return '/admin/broadcasts/' + enc(idOf(id));
}

/** @this {import('./ApiClient.js').ApiClient} */
export const broadcastsMethods = {
  // ---- Broadcasts (section 8.2) ----

  /** POST /admin/broadcasts/enumerate. Filters `status`, `channel`. */
  adminEnumerateBroadcasts(query = {}, signal) { return this.post('/admin/broadcasts/enumerate', enumBody(query, ADMIN), { signal }); },
  /** POST /admin/broadcasts (section 2.5 fields, `segmentId` or inline `segment`); resolves a Draft. */
  adminCreateBroadcast(body) { return this.post('/admin/broadcasts', body); },
  /** GET /admin/broadcasts/{id}. */
  adminGetBroadcast(id, signal) { return this.get(broadcastPath(id), { signal }); },
  /** PUT /admin/broadcasts/{id} (`409 BroadcastNotEditable` once sending). */
  adminUpdateBroadcast(id, body) { return this.put(broadcastPath(id), body); },
  /** DELETE /admin/broadcasts/{id} (Drafts only). */
  adminDeleteBroadcast(id) { return this.delete(broadcastPath(id)); },
  /** POST /admin/broadcasts/{id}/test with `{ channels }` (sends to the caller only; 202). */
  adminTestBroadcast(id, channels) { return this.post(broadcastPath(id) + '/test', { channels: Array.isArray(channels) ? channels.slice() : [] }); },
  /** POST /admin/broadcasts/{id}/schedule with `{ sendAtUtc }`. */
  adminScheduleBroadcast(id, sendAtUtc) { return this.post(broadcastPath(id) + '/schedule', { sendAtUtc }); },
  /** POST /admin/broadcasts/{id}/send with `{ confirmCount }` (the preview count the sender confirmed). */
  adminSendBroadcast(id, confirmCount) { return this.post(broadcastPath(id) + '/send', { confirmCount: Math.max(0, Math.floor(Number(confirmCount) || 0)) }); },
  /** POST /admin/broadcasts/{id}/cancel. */
  adminCancelBroadcast(id) { return this.post(broadcastPath(id) + '/cancel'); },
  /** POST /admin/broadcasts/{id}/deliveries/enumerate. Filters `channel`, `status`. */
  adminEnumerateBroadcastDeliveries(id, query = {}, signal) { return this.post(broadcastPath(id) + '/deliveries/enumerate', enumBody(query, ADMIN), { signal }); },

  // ---- Segments (section 8.1) ----

  /**
   * POST /admin/broadcasts/segments/preview with `{ segmentId }` or `{ segment }` (inline filters);
   * resolves `{ count, byChannel: { inApp, push, email }, sample: { objects, totalRecords } }`.
   */
  adminPreviewSegment(body) { return this.post('/admin/broadcasts/segments/preview', body || {}); },
  /** POST /admin/broadcasts/segments/enumerate. */
  adminEnumerateSegments(query = {}, signal) { return this.post('/admin/broadcasts/segments/enumerate', enumBody(query, ADMIN), { signal }); },
  /** POST /admin/broadcasts/segments with `{ name, description, filters, labels?, tags? }`. */
  adminCreateSegment(body) { return this.post('/admin/broadcasts/segments', body); },
  /** GET /admin/broadcasts/segments/{id}. */
  adminGetSegment(id, signal) { return this.get('/admin/broadcasts/segments/' + enc(idOf(id)), { signal }); },
  /** PUT /admin/broadcasts/segments/{id}. */
  adminUpdateSegment(id, body) { return this.put('/admin/broadcasts/segments/' + enc(idOf(id)), body); },
  /** DELETE /admin/broadcasts/segments/{id}. */
  adminDeleteSegment(id) { return this.delete('/admin/broadcasts/segments/' + enc(idOf(id))); },

  // ---- Announcements (member view, section 8.2) ----

  /** GET /announcements/{id}. */
  getAnnouncement(id, signal) { return this.get('/announcements/' + enc(idOf(id)), { signal }); },
  /** POST /announcements/enumerate (the caller's received broadcasts). */
  enumerateAnnouncements(query = {}, signal) { return this.post('/announcements/enumerate', enumBody(query), { signal }); }
};
