/**
 * Phase 4 events API methods (docs/PHASE4_CONTRACT.md sections 4 and 16, plus the agreed
 * recurrence and proposal additions of resolved decisions 10 and 11), mixed into
 * `ApiClient.prototype`. Only `get`, `post`, `put`, `delete`, `request`, and `url` are used.
 *
 * Every occurrence of a recurring event is its own `evt_` event with its own RSVP. Admin edits and
 * cancellations take `scope` (`One`, `Following`, `All`; default `One`).
 */

import { ENUMERATION_MAX_RESULTS_ADMIN, toEnumerationBody } from './enumeration.js';
import { roundNear } from './directory.js';
import { mapEnumerationBody } from './directoryApi.js';

const enc = encodeURIComponent;
const ADMIN = { maxLimit: ENUMERATION_MAX_RESULTS_ADMIN };
const PUBLIC = { maxLimit: 20 };

/** Edit and cancel scopes for recurring events. */
export const EventEditScope = Object.freeze({ One: 'One', Following: 'Following', All: 'All' });

function enumBody(query, limits) {
  const q = query || {};
  // A `near` filter is always rounded to 2 decimal places before it leaves the device (section 3.5).
  const filters = q.filters && q.filters.near ? { ...q.filters, near: roundNear(q.filters.near) } : q.filters;
  return toEnumerationBody(filters === q.filters ? q : { ...q, filters }, limits);
}

function scoped(body, scope) {
  const out = { ...(body || {}) };
  const value = scope || out.scope;
  if (value) out.scope = value;
  else delete out.scope;
  return out;
}

/** @this {import('./ApiClient.js').ApiClient} */
export const eventMethods = {
  // ---- Events (sections 4.1 and 4.2) ----

  /** POST /events/enumerate. Orderings `StartAscending` (default), `StartDescending`, `CreatedDescending`, `CreatedAscending`, `DistanceAscending`, `Relevance`; filters `when`, `startsAfterUtc`, `startsBeforeUtc`, `category`, `format`, `venueId`, `hostUserId`, `instructorId`, `near`, `bbox`, `includeOnline`, `rsvpStatus`, `hasSpots`, `includeCanceled`, `audience`, `recurrenceId`. */
  enumerateEvents(query = {}, signal) { return this.post('/events/enumerate', enumBody(query), { signal }); },
  /** POST /events/map/enumerate with `{ bbox, zoom, maxResults, labels, tags, search, filters }`; items are `Point` (`event`) or `Cluster` MapFeatures. Online-only events are never on the map. */
  enumerateEventMap(query = {}, signal) { return this.post('/events/map/enumerate', mapEnumerationBody(query), { signal }); },
  /** GET /events/{idOrSlug}. */
  getEvent(idOrSlug, signal) { return this.get('/events/' + enc(idOrSlug), { signal }); },
  /** POST /events/{id}/occurrences/enumerate: the sibling occurrences of a recurring event the caller may see. */
  enumerateEventOccurrences(id, query = {}, signal) { return this.post('/events/' + enc(id) + '/occurrences/enumerate', enumBody(query), { signal }); },
  /** PUT /events/{id}/rsvp with `{ status: 'Going' | 'Interested', guestCount?, hideFromAttendeeLists? }`; returns the Event. */
  rsvpEvent(id, { status, guestCount, hideFromAttendeeLists } = {}) {
    const body = { status };
    if (guestCount !== undefined && guestCount !== null) body.guestCount = Math.max(0, Math.floor(Number(guestCount) || 0));
    if (hideFromAttendeeLists !== undefined) body.hideFromAttendeeLists = Boolean(hideFromAttendeeLists);
    return this.put('/events/' + enc(id) + '/rsvp', body);
  },
  /** DELETE /events/{id}/rsvp ("Not going"); returns the Event with `myRsvp: null`. */
  cancelRsvp(id) { return this.delete('/events/' + enc(id) + '/rsvp'); },
  /** POST /events/{id}/attendees/enumerate. Filter `status` (`Going` default). */
  enumerateAttendees(id, query = {}, signal) { return this.post('/events/' + enc(id) + '/attendees/enumerate', enumBody(query), { signal }); },
  /** The URL of GET /events/{id}/ics (`scope: 'Series'` for the whole series with RRULE and exceptions). */
  getEventIcsUrl(id, { scope } = {}) { return this.url('/events/' + enc(id) + '/ics', scope === 'Series' ? { scope: 'Series' } : undefined); },
  /** GET /events/{id}/ics as text (with the caller's credentials), for a client-side download. */
  downloadEventIcs(id, { scope, signal } = {}) { return this.get('/events/' + enc(id) + '/ics', { query: scope === 'Series' ? { scope: 'Series' } : undefined, responseType: 'text', signal, headers: { Accept: 'text/calendar' } }); },
  /** POST /me/events/enumerate. Filters `rsvpStatus`, `upcoming`, `past`. */
  enumerateMyEvents(query = {}, signal) { return this.post('/me/events/enumerate', enumBody(query), { signal }); },
  /** POST /events/categories/enumerate. */
  enumerateEventCategories(query = {}, signal) { return this.post('/events/categories/enumerate', enumBody(query), { signal }); },

  // ---- Calendar feed (section 4.5) ----

  /** GET /me/calendar-feed (creates one on first call); returns `{ url, createdUtc }`. */
  getCalendarFeed(signal) { return this.get('/me/calendar-feed', { signal }); },
  /** POST /me/calendar-feed/rotate; returns `{ url, createdUtc }` with a new secret. */
  rotateCalendarFeed() { return this.post('/me/calendar-feed/rotate'); },
  /** DELETE /me/calendar-feed (204). */
  deleteCalendarFeed() { return this.delete('/me/calendar-feed'); },

  // ---- Comments, reactions, promotion (sections 4.1, 5.8, 6) ----

  /** POST /events/{id}/comments/enumerate. */
  enumerateEventComments(id, query = {}, signal) { return this.post('/events/' + enc(id) + '/comments/enumerate', enumBody(query), { signal }); },
  /** POST /events/{id}/comments (409 EventCanceled on canceled events). */
  createEventComment(id, body) { return this.post('/events/' + enc(id) + '/comments', body); },
  /** PUT /events/{id}/reactions/{key}. */
  putEventReaction(id, key) { return this.request('PUT', '/events/' + enc(id) + '/reactions/' + enc(key)); },
  /** DELETE /events/{id}/reactions/{key}. */
  deleteEventReaction(id, key) { return this.delete('/events/' + enc(id) + '/reactions/' + enc(key)); },
  /** POST /events/{id}/reactions/enumerate. */
  enumerateEventReactions(id, query = {}, signal) { return this.post('/events/' + enc(id) + '/reactions/enumerate', enumBody(query), { signal }); },
  /** POST /events/{id}/promote (`notifyMembers` defaults to false for events). */
  promoteEvent(id, body = {}) { return this.post('/events/' + enc(id) + '/promote', { notifyMembers: false, ...body }); },
  /** POST /events/{id}/unpromote. */
  unpromoteEvent(id) { return this.post('/events/' + enc(id) + '/unpromote'); },

  // ---- Member proposals (resolved decision 10) ----

  /** POST /events/proposals (event fields without hosts or recurrence); returns the Event with `status: 'Proposed'`. */
  proposeEvent(body) {
    const { recurrence: _r, hostUserIds: _h, ...rest } = body || {};
    return this.post('/events/proposals', rest);
  },
  /** POST /me/events/proposals/enumerate. Filter `status`. */
  enumerateMyEventProposals(query = {}, signal) { return this.post('/me/events/proposals/enumerate', enumBody(query), { signal }); },
  /** POST /events/proposals/{id}/withdraw. */
  withdrawEventProposal(id) { return this.post('/events/proposals/' + enc(id) + '/withdraw'); },

  // ---- Public events (section 4.9, anonymous) ----

  /** POST /public/events/enumerate (`maxResults` at most 20). */
  enumeratePublicEvents(query = {}, signal) { return this.post('/public/events/enumerate', enumBody({ maxResults: 20, ...query }, PUBLIC), { signal, retryOnUnauthorized: false }); },
  /** GET /public/events/{id}. */
  getPublicEvent(id, signal) { return this.get('/public/events/' + enc(id), { signal, retryOnUnauthorized: false }); },

  // ---- Admin: events (section 4.7, event staff and hosts) ----

  /** POST /admin/events/enumerate (admin maximum). Member filters plus `status` (including `Draft`, `Proposed`), `hostUserId`. */
  adminEnumerateEvents(query = {}, signal) { return this.post('/admin/events/enumerate', enumBody(query, ADMIN), { signal }); },
  /** POST /admin/events (local wall time plus `timeZone`; optional `recurrence` rule); returns a Draft Event. */
  adminCreateEvent(body) { return this.post('/admin/events', body); },
  /** GET /admin/events/{id}. */
  adminGetEvent(id, signal) { return this.get('/admin/events/' + enc(id), { signal }); },
  /** PUT /admin/events/{id} with `notifyAttendees` and `scope` (`One` default, `Following`, `All`). */
  adminUpdateEvent(id, body, scope) { return this.put('/admin/events/' + enc(id), scoped(body, scope)); },
  /** POST /admin/events/{id}/publish with `{ notifyMembers }` (default false). */
  adminPublishEvent(id, { notifyMembers = false } = {}) { return this.post('/admin/events/' + enc(id) + '/publish', { notifyMembers: Boolean(notifyMembers) }); },
  /** POST /admin/events/{id}/cancel with `{ reason, notifyAttendees, scope }`. */
  adminCancelEvent(id, { reason, notifyAttendees = true, scope } = {}) { return this.post('/admin/events/' + enc(id) + '/cancel', scoped({ reason, notifyAttendees: Boolean(notifyAttendees) }, scope)); },
  /** POST /admin/events/{id}/duplicate with `{ startLocal, timeZone? }`; returns the new Draft. */
  adminDuplicateEvent(id, { startLocal, timeZone } = {}) { return this.post('/admin/events/' + enc(id) + '/duplicate', timeZone ? { startLocal, timeZone } : { startLocal }); },
  /** DELETE /admin/events/{id} (Drafts only). */
  adminDeleteEvent(id) { return this.delete('/admin/events/' + enc(id)); },
  /** POST /admin/events/{id}/rsvps/enumerate. Filters `status`, `checkedIn`. */
  adminEnumerateRsvps(eventId, query = {}, signal) { return this.post('/admin/events/' + enc(eventId) + '/rsvps/enumerate', enumBody(query, ADMIN), { signal }); },
  /**
   * POST /admin/events/{id}/rsvps/{rsvpId}/{action} with `{ reason?, notifyMember }`.
   * @param {'promote'|'move-to-waitlist'|'remove'|'check-in'|'undo-check-in'} action
   */
  adminRsvpAction(eventId, rsvpId, action, { reason, notifyMember = true } = {}) {
    const allowed = ['promote', 'move-to-waitlist', 'remove', 'check-in', 'undo-check-in'];
    if (!allowed.includes(action)) throw new Error('Unknown RSVP action: ' + action);
    const body = { notifyMember: Boolean(notifyMember) };
    if (reason) body.reason = reason;
    return this.post('/admin/events/' + enc(eventId) + '/rsvps/' + enc(rsvpId) + '/' + action, body);
  },
  /** Check a member in (event staff and hosts). */
  adminCheckInRsvp(eventId, rsvpId) { return this.adminRsvpAction(eventId, rsvpId, 'check-in', { notifyMember: false }); },
  /** Undo a check-in. */
  adminUndoCheckInRsvp(eventId, rsvpId) { return this.adminRsvpAction(eventId, rsvpId, 'undo-check-in', { notifyMember: false }); },
  /** GET /admin/events/{id}/rsvps/export.csv as text. */
  adminExportRsvpsCsv(eventId, signal) { return this.get('/admin/events/' + enc(eventId) + '/rsvps/export.csv', { responseType: 'text', signal, headers: { Accept: 'text/csv' } }); },
  /** GET /admin/metrics/events?days= (1 to 365). */
  adminEventMetrics(query = {}, signal) { return this.post('/admin/metrics/events/enumerate', toEnumerationBody(query, { maxLimit: 500 }), { signal }); },

  // ---- Admin: proposals (resolved decision 10) ----

  /** POST /admin/events/proposals/enumerate. Filter `status` (default `Proposed`). */
  adminEnumerateEventProposals(query = {}, signal) { return this.post('/admin/events/proposals/enumerate', enumBody(query, ADMIN), { signal }); },
  /** POST /admin/events/{id}/approve with `{ reviewNote?, publish, notifyMembers }`. */
  adminApproveEvent(id, { reviewNote, publish = true, notifyMembers = false } = {}) {
    const body = { publish: Boolean(publish), notifyMembers: Boolean(notifyMembers) };
    if (reviewNote) body.reviewNote = reviewNote;
    return this.post('/admin/events/' + enc(id) + '/approve', body);
  },
  /** POST /admin/events/{id}/reject with `{ reviewNote }` (3 to 1000 characters). */
  adminRejectEvent(id, { reviewNote } = {}) { return this.post('/admin/events/' + enc(id) + '/reject', { reviewNote }); },

  // ---- Admin: categories (SystemAdmin) ----

  /** POST /admin/events/categories/enumerate (including retired categories). */
  adminEnumerateEventCategories(query = {}, signal) { return this.post('/admin/events/categories/enumerate', enumBody(query, ADMIN), { signal }); },
  /** POST /admin/events/categories with `{ key, label, color? }`. */
  adminCreateEventCategory(body) { return this.post('/admin/events/categories', body); },
  /** PUT /admin/events/categories/{id}. */
  adminUpdateEventCategory(id, body) { return this.put('/admin/events/categories/' + enc(id), body); },
  /** DELETE /admin/events/categories/{id} (a category in use is retired). */
  adminDeleteEventCategory(id) { return this.delete('/admin/events/categories/' + enc(id)); },
  /** PUT /admin/events/categories/order with `{ categoryIds }`. */
  adminOrderEventCategories(categoryIds) { return this.put('/admin/events/categories/order', { categoryIds: categoryIds || [] }); },
  /** POST /admin/moderators with `{ userId, resourceType: 'Events', resourceId: '*' }` (grants EventManager). */
  grantEventManager(userId) { return this.post('/admin/moderators', { userId, resourceType: 'Events', resourceId: '*' }); },
  /** DELETE /admin/moderators/{userId}?resourceType=Events&resourceId=*. */
  revokeEventManager(userId) { return this.delete('/admin/moderators/' + enc(userId), { query: { resourceType: 'Events', resourceId: '*' } }); }
};
