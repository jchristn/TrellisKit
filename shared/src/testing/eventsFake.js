import { dailyMetricPage } from './metricFake.js';

/**
 * An in-memory fake of the Phase 4 events routes (docs/PHASE4_CONTRACT.md section 4 plus the agreed
 * recurrence and proposal additions), for client tests and for building screens before the backend
 * exists. It follows the contract shapes, the RSVP capacity and waitlist rules of section 4.3,
 * locked stubs, occurrences of a recurrence (each its own event with its own RSVP), the edit scopes,
 * and the main error codes; it is not a second backend.
 *
 * Use `fake.handle({ method, path, query, body })` or `fake.fetchImpl`.
 */

import { fakeUser } from './forumsFake.js';
import { recurrenceSummary } from '../events.js';
import { isInBox } from '../directory.js';
import { asList, clone, error, fetchOver, gridClusters, matchesMeta, matchesSearch, noContent, ok, page, splitPath, textResponse, truthy } from './fakeUtils.js';

function slugify(text) {
  return String(text).toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
}

const EVENT_VIEWER = Object.freeze({
  canRsvp: true, canRsvpGoing: true, canJoinWaitlist: true, canComment: true, canReact: true, canReport: true, canSeeAttendees: true,
  canEdit: false, canCancel: false, canCheckIn: false, canPromote: false, canOpen: true, requires: null
});

/** Viewer capabilities of event staff (merge into `viewer`). */
export const eventStaffViewer = Object.freeze({ canEdit: true, canCancel: true, canCheckIn: true, canPromote: true });

/** A Published Event (section 2.5). */
export function makeEvent(id, extra = {}) {
  const title = extra.title || 'Event ' + id;
  return {
    id, slug: extra.slug || slugify(extra.title || id.replace(/^evt_/, '')), title, summary: 'About ' + title + '.', description: 'Details of ' + title + '.',
    category: null, format: 'InPerson', status: 'Published', startUtc: '2026-12-05T18:00:00Z', endUtc: '2026-12-05T21:00:00Z', timeZone: 'America/Los_Angeles', isAllDay: false,
    location: { venue: null, name: 'Example Hall', address: { line1: '100 Example Street', line2: null, locality: 'Exampleville', region: 'EX', postalCode: '00000', countryCode: 'US', formatted: '100 Example Street, Exampleville, EX 00000' }, addressVisibility: 'Exact', geo: null, distanceKm: null },
    online: null, capacity: null, goingCount: 0, interestedCount: 0, waitlistCount: 0, spotsLeft: null, waitlistEnabled: true, maxGuestsPerRsvp: 0, rsvpAudience: 'SignedIn',
    rsvpOpensUtc: null, rsvpClosesUtc: null, attendeeVisibility: 'Attendees', attendeePreview: { objects: [], totalRecords: 0, enumerate: null }, externalUrl: null, cover: null,
    hosts: [], instructors: [], myRsvp: null, reminderOffsetsMinutes: [1440, 120], commentsEnabled: true, commentTarget: { type: 'Event', id }, commentCount: 0,
    reactionCounts: {}, reactionTotal: 0, myReactions: [], promotion: null, audience: 'SignedIn', canceledUtc: null, cancelReason: null, locked: false, recurrence: null,
    labels: [], tags: {}, viewer: { ...EVENT_VIEWER }, publishedUtc: '2026-11-01T00:00:00Z', createdUtc: '2026-11-01T00:00:00Z', updatedUtc: '2026-11-01T00:00:00Z',
    ...extra
  };
}

/** An event category. */
export function makeEventCategory(key, label, color = null, extra = {}) {
  return { id: 'ecg_' + key, key, label, color, sortOrder: 10, isRetired: false, labels: [], tags: {}, ...extra };
}

const STEP_DAYS = Object.freeze({ Daily: 1, Weekly: 7, Monthly: 30, Yearly: 365 });

/**
 * Create a fake.
 * @param {{ me?: object, events?: object[], categories?: object[], attendees?: object[], now?: () => string, killed?: boolean, proposals?: boolean }} [seed]
 */
export function createEventsFake(seed = {}) {
  let n = 0;
  const nextId = (prefix) => prefix + 'fake' + (++n);
  const now = seed.now || (() => '2026-11-11T12:00:00Z');
  const state = {
    me: seed.me || fakeUser('usr_1', 'Sam Rivera'),
    events: (seed.events || []).map(clone),
    categories: (seed.categories || []).map(clone),
    attendees: (seed.attendees || []).map(clone),
    rsvps: (seed.rsvps || []).map(clone),
    comments: [],
    feed: seed.feed === null ? null : { url: 'https://api.example.test/api/v1/calendar/cfd_fake.secret1.ics', createdUtc: '2026-11-01T00:00:00Z' },
    proposalsEnabled: seed.proposals !== false,
    maxFeatures: seed.maxFeatures || 500,
    killed: Boolean(seed.killed)
  };
  let feedSecret = 1;
  const calls = [];

  const find = (idOrSlug) => state.events.find((e) => (e.id === idOrSlug || e.slug === idOrSlug) && !e.deletedUtc);
  const canOpen = (e) => !e.locked && !(e.viewer && e.viewer.canOpen === false);
  const isMine = (e) => e.proposedBy && e.proposedBy.id === state.me.id;

  function recount(e) {
    const going = state.rsvps.filter((r) => r.eventId === e.id && r.status === 'Going');
    const guests = going.reduce((sum, r) => sum + (r.guestCount || 0), 0);
    e.goingCount = going.length;
    e.interestedCount = state.rsvps.filter((r) => r.eventId === e.id && r.status === 'Interested').length;
    e.waitlistCount = state.rsvps.filter((r) => r.eventId === e.id && r.status === 'Waitlisted').length;
    e.spotsLeft = e.capacity ? Math.max(0, e.capacity - going.length - guests) : null;
  }

  function promote(e) {
    if (!e.capacity) return;
    const waiting = state.rsvps.filter((r) => r.eventId === e.id && r.status === 'Waitlisted').sort((a, b) => String(a.waitlistedUtc).localeCompare(String(b.waitlistedUtc)) || a.id.localeCompare(b.id));
    for (const r of waiting) {
      recount(e);
      if (e.spotsLeft >= 1 + (r.guestCount || 0)) {
        r.status = 'Going';
        r.position = null;
      }
    }
    recount(e);
  }

  function myRsvpOf(e) {
    const r = state.rsvps.find((x) => x.eventId === e.id && x.userId === state.me.id);
    if (!r) return null;
    const waiting = state.rsvps.filter((x) => x.eventId === e.id && x.status === 'Waitlisted').sort((a, b) => String(a.waitlistedUtc).localeCompare(String(b.waitlistedUtc)) || a.id.localeCompare(b.id));
    return { id: r.id, status: r.status, guestCount: r.guestCount || 0, position: r.status === 'Waitlisted' ? waiting.findIndex((x) => x.id === r.id) + 1 : null, checkedInUtc: r.checkedInUtc || null, updatedUtc: r.updatedUtc };
  }

  function present(e) {
    recount(e);
    const out = { ...e, myRsvp: canOpen(e) ? myRsvpOf(e) : null };
    const rsvp = out.myRsvp;
    if (out.online && !(rsvp && rsvp.status === 'Going') && !(e.viewer && e.viewer.canEdit)) out.online = { ...out.online, joinUrl: null };
    if (!canOpen(e)) Object.assign(out, { description: null, online: null, attendeePreview: null, hosts: [] });
    delete out.deletedUtc;
    delete out.proposedBy;
    if (e.proposedBy) out.proposedBy = e.proposedBy;
    return out;
  }

  function summary(e) {
    const { description: _d, attendeePreview: _a, hosts, instructors: _i, ...rest } = present(e);
    return { ...rest, hostCount: (hosts || []).length, online: rest.online ? { ...rest.online, joinUrl: undefined } : null };
  }

  function filter(list, body, { admin = false } = {}) {
    const f = body.filters || {};
    const at = now();
    let out = list.filter((e) => !e.deletedUtc);
    if (!admin) out = out.filter((e) => e.status === 'Published' || (e.status === 'Canceled' && (truthy(f.includeCanceled) || myRsvpOf(e))));
    if (admin && f.status) out = out.filter((e) => asList(f.status).includes(e.status));
    if (admin && !f.status) out = out.filter((e) => e.status !== 'Proposed' && e.status !== 'Rejected' && e.status !== 'Withdrawn');
    if (!admin && !truthy(f.includeCanceled)) out = out.filter((e) => e.status !== 'Canceled' || myRsvpOf(e));
    const when = f.when || (admin ? 'All' : 'Upcoming');
    if (when === 'Upcoming') out = out.filter((e) => String(e.endUtc) > at);
    if (when === 'Past') out = out.filter((e) => String(e.endUtc) <= at);
    if (f.startsAfterUtc) out = out.filter((e) => String(e.startUtc) >= String(f.startsAfterUtc));
    if (f.startsBeforeUtc) out = out.filter((e) => String(e.startUtc) < String(f.startsBeforeUtc));
    const categories = asList(f.category);
    if (categories.length) out = out.filter((e) => e.category && categories.includes(e.category.key));
    const formats = asList(f.format);
    if (formats.length) out = out.filter((e) => formats.includes(e.format));
    if (f.venueId) out = out.filter((e) => e.location && e.location.venue && e.location.venue.id === f.venueId);
    if (f.hostUserId) out = out.filter((e) => (e.hosts || []).some((h) => h.id === f.hostUserId));
    if (f.recurrenceId) out = out.filter((e) => e.recurrence && e.recurrence.id === f.recurrenceId);
    if (f.rsvpStatus) {
      const wanted = asList(f.rsvpStatus);
      out = out.filter((e) => {
        const r = myRsvpOf(e);
        return wanted.includes(r ? r.status : 'None');
      });
    }
    if (truthy(f.hasSpots)) out = out.filter((e) => {
      recount(e);
      return !e.capacity || e.spotsLeft > 0;
    });
    if (f.audience) out = out.filter((e) => asList(f.audience).includes(e.audience));
    if (f.bbox) {
      out = out.filter((e) => (e.format !== 'Online' && e.location && e.location.geo && isInBox(e.location.geo.lat, e.location.geo.lng, f.bbox)) || (truthy(f.includeOnline) && e.format === 'Online'));
    }
    out = out.filter((e) => matchesMeta(e, body) && matchesSearch(e, body.search, ['title', 'summary', 'description']));
    const by = {
      StartAscending: (a, b) => String(a.startUtc).localeCompare(String(b.startUtc)) || a.id.localeCompare(b.id),
      StartDescending: (a, b) => String(b.startUtc).localeCompare(String(a.startUtc)),
      CreatedDescending: (a, b) => String(b.createdUtc).localeCompare(String(a.createdUtc)),
      CreatedAscending: (a, b) => String(a.createdUtc).localeCompare(String(b.createdUtc)),
      Relevance: () => 0
    };
    return out.sort(by[body.ordering] || by.StartAscending);
  }

  function rsvp(e, body) {
    if (!canOpen(e) || e.viewer.canRsvp === false) return { status: 402, body: { error: 'MembershipRequired', message: 'A membership is required to RSVP.', required: 'Membership', feature: 'events', targetType: 'Event', targetId: e.id } };
    if (e.status === 'Canceled') return error(409, 'EventCanceled', 'This event was canceled.');
    if (String(e.startUtc) <= now() || (e.rsvpClosesUtc && String(e.rsvpClosesUtc) <= now())) return error(409, 'RsvpClosed', 'RSVPs are closed.');
    const status = body.status === 'Interested' ? 'Interested' : 'Going';
    const guests = Math.max(0, Math.min(e.maxGuestsPerRsvp || 0, Number(body.guestCount) || 0));
    let r = state.rsvps.find((x) => x.eventId === e.id && x.userId === state.me.id);
    const previous = r ? { ...r } : null;
    if (!r) {
      r = { id: nextId('rsv_'), eventId: e.id, userId: state.me.id, user: state.me, status: null, guestCount: 0, createdUtc: now() };
      state.rsvps.push(r);
    }
    r.guestCount = guests;
    r.hideFromAttendeeLists = Boolean(body.hideFromAttendeeLists);
    r.updatedUtc = now();
    if (status === 'Interested') r.status = 'Interested';
    else {
      if (previous && previous.status === 'Going') r.status = 'Going';
      else {
        r.status = null;
        recount(e);
        const fits = !e.capacity || e.spotsLeft >= 1 + guests;
        if (fits) r.status = 'Going';
        else if (e.waitlistEnabled) {
          r.status = 'Waitlisted';
          r.waitlistedUtc = now() + ':' + String(n).padStart(6, '0');
        } else {
          if (previous) Object.assign(r, previous);
          else state.rsvps = state.rsvps.filter((x) => x !== r);
          return error(409, 'EventFull', 'This event is full.');
        }
      }
    }
    promote(e);
    return ok(present(e));
  }

  function ics(e, scope) {
    const lines = ['BEGIN:VCALENDAR', 'VERSION:2.0', 'PRODID:-//TrellisKit//Events//EN', 'BEGIN:VEVENT', 'UID:' + e.id + '@example.test', 'SUMMARY:' + e.title];
    if (scope === 'Series' && e.recurrence) lines.push('RRULE:FREQ=' + String(e.recurrence.rule.frequency).toUpperCase() + ';INTERVAL=' + (e.recurrence.rule.interval || 1));
    if (e.status === 'Canceled') lines.push('STATUS:CANCELLED');
    lines.push('END:VEVENT', 'END:VCALENDAR');
    return textResponse(lines.join('\r\n'), 'text/calendar');
  }

  function occurrencesFrom(base, rule, { count } = {}) {
    const step = STEP_DAYS[rule.frequency] || 7;
    const total = Math.min(rule.count || count || 4, 12);
    const recurrenceId = nextId('erc_');
    const out = [];
    for (let i = 0; i < total; i += 1) {
      const shift = (ms) => new Date(new Date(ms).getTime() + i * step * (rule.interval || 1) * 86400000).toISOString().replace('.000Z', 'Z');
      const id = i === 0 ? base.id : nextId('evt_');
      out.push(makeEvent(id, {
        ...base, id, slug: base.slug + (i ? '-' + (i + 1) : ''), startUtc: shift(base.startUtc), endUtc: shift(base.endUtc), commentTarget: { type: 'Event', id },
        recurrence: { id: recurrenceId, rule: clone(rule), summary: recurrenceSummary(rule), occurrenceIndex: i, isException: false, originalStartUtc: shift(base.startUtc) }
      }));
    }
    return out;
  }

  function siblings(e, scope) {
    if (!e.recurrence || !scope || scope === 'One') return [e];
    const all = state.events.filter((x) => x.recurrence && x.recurrence.id === e.recurrence.id && !x.deletedUtc).sort((a, b) => a.recurrence.occurrenceIndex - b.recurrence.occurrenceIndex);
    if (scope === 'All') return all.filter((x) => String(x.startUtc) > now() || x === e);
    return all.filter((x) => x.recurrence.occurrenceIndex >= e.recurrence.occurrenceIndex);
  }

  function fromBody(body, base = {}) {
    const category = body.category ? state.categories.find((c) => c.key === body.category) || { key: body.category, label: body.category, color: null } : base.category || null;
    const startUtc = body.startLocal ? new Date(body.startLocal + (body.startLocal.length === 16 ? ':00' : '') + 'Z').toISOString().replace('.000Z', 'Z') : base.startUtc;
    const endUtc = body.endLocal ? new Date(body.endLocal + (body.endLocal.length === 16 ? ':00' : '') + 'Z').toISOString().replace('.000Z', 'Z') : base.endUtc;
    const { startLocal: _s, endLocal: _e, scope: _scope, notifyAttendees: _n, recurrence: _r, hostUserIds: _h, coverMediaId: _c, instructorIds: _i, ...rest } = body;
    const out = { ...rest, category: category ? { key: category.key, label: category.label, color: category.color } : null };
    if (startUtc) out.startUtc = startUtc;
    if (endUtc) out.endUtc = endUtc;
    if (body.location) out.location = { venue: null, distanceKm: null, geo: null, addressVisibility: 'Exact', ...body.location };
    return out;
  }

  function mapFeatures(body) {
    const box = body.bbox;
    if (!box) return error(400, 'InvalidGeo', 'A bounding box is required.');
    const max = Math.max(1, Math.min(1000, Number(state.maxFeatures) || 500));
    const { bbox: _b, near: _n, includeOnline: _o, ...rest } = body.filters || {};
    const list = filter(state.events, { ...body, filters: { ...rest, bbox: box } });
    const points = list.filter((e) => e.location && e.location.addressVisibility !== 'LocalityOnly' && e.location.geo).map((e) => ({ e, g: e.location.geo }));
    const point = (x) => {
      const e = x.e;
      const address = (e.location && e.location.address) || {};
      return {
        kind: 'Point',
        event: {
          id: e.id, slug: e.slug, title: e.title, startUtc: e.startUtc, endUtc: e.endUtc, timeZone: e.timeZone, isAllDay: Boolean(e.isAllDay), format: e.format, status: e.status,
          category: e.category || null, locationName: (e.location && e.location.name) || null, locality: address.locality || null, region: address.region || null, countryCode: address.countryCode || null,
          venue: (e.location && e.location.venue) || null, geo: x.g, spotsLeft: e.spotsLeft === undefined ? null : e.spotsLeft, locked: Boolean(e.locked), coverThumbUrl: null
        }
      };
    };
    const objects = points.length <= max ? points.map(point) : gridClusters(points, box, max, point);
    return ok({ ...page(objects.map((item, rank) => ({ id: 'feature_' + rank, labels: [], tags: {}, ...item })), body, { max: 1000 }), matchedRecords: points.length });
  }

  function handleEvents(method, parts, query, body) {
    if (parts[1] === 'map' && parts[2] === 'enumerate' && method === 'POST') return mapFeatures(body);
    if (parts[1] === 'enumerate' && method === 'POST') {
      const f = body.filters || {};
      if (f.near && f.bbox) return error(400, 'InvalidGeo', 'near and bbox cannot be combined.');
      const result = page(filter(state.events, body), body);
      result.objects = result.objects.map((o) => summary(find(o.id)));
      return ok(result);
    }
    if (parts[1] === 'categories' && parts[2] === 'enumerate') return ok(page(state.categories.filter((c) => !c.isRetired), body));
    if (parts[1] === 'proposals') {
      if (!parts[2] && method === 'POST') {
        if (!state.proposalsEnabled) return error(403, 'ProposalsDisabled', 'Event proposals are off.');
        if (!body.title || String(body.title).trim().length < 3) return error(400, 'ValidationFailed', 'A title is required.', { title: 'Too short' });
        if (body.recurrence) return error(400, 'ValidationFailed', 'Proposals cannot repeat.', { recurrence: 'Not allowed' });
        const e = makeEvent(nextId('evt_'), { ...fromBody(body), status: 'Proposed', proposedBy: state.me, proposalNote: body.proposalNote || null, reviewNote: null, publishedUtc: null, createdUtc: now(), updatedUtc: now() });
        state.events.push(e);
        return ok(present(e), 201);
      }
      const e = find(parts[2]);
      if (!e || !isMine(e)) return error(404, 'NotFound', 'Not found.');
      if (parts[3] === 'withdraw' && method === 'POST') {
        if (e.status !== 'Proposed') return error(409, 'Conflict', 'Only pending proposals can be withdrawn.');
        e.status = 'Withdrawn';
        return ok(present(e));
      }
      return undefined;
    }
    const e = find(parts[1]);
    const visible = e && (e.status === 'Published' || e.status === 'Canceled' || isMine(e));
    if (!visible) return error(404, 'NotFound', 'Not found.');
    if (!parts[2] && method === 'GET') return ok(present(e));
    if (parts[2] === 'rsvp') {
      if (method === 'PUT') return rsvp(e, body);
      if (method === 'DELETE') {
        if (String(e.startUtc) <= now()) return error(409, 'RsvpClosed', 'The event has started.');
        state.rsvps = state.rsvps.filter((x) => !(x.eventId === e.id && x.userId === state.me.id));
        promote(e);
        return ok(present(e));
      }
    }
    if (parts[2] === 'attendees' && parts[3] === 'enumerate') {
      if (!e.viewer.canSeeAttendees) return error(404, 'NotFound', 'Not found.');
      const wanted = asList((body.filters || {}).status);
      const status = wanted.length ? wanted : ['Going'];
      const rows = [
        ...state.attendees.filter((a) => (!a.eventId || a.eventId === e.id) && status.includes(a.status || 'Going')),
        ...state.rsvps.filter((r) => r.eventId === e.id && status.includes(r.status) && !r.hideFromAttendeeLists).map((r) => ({ user: r.user, status: r.status, guestCount: r.guestCount, position: null, checkedInUtc: r.checkedInUtc || null, createdUtc: r.createdUtc }))
      ];
      return ok(page(rows, body));
    }
    if (parts[2] === 'occurrences' && parts[3] === 'enumerate') {
      const list = e.recurrence ? state.events.filter((x) => x.recurrence && x.recurrence.id === e.recurrence.id && !x.deletedUtc && (x.status === 'Published' || x.status === 'Canceled')) : [e];
      const result = page(list.sort((a, b) => String(a.startUtc).localeCompare(String(b.startUtc))), body);
      result.objects = result.objects.map((o) => summary(find(o.id)));
      return ok(result);
    }
    if (parts[2] === 'ics' && method === 'GET') return ics(e, query.scope);
    if (parts[2] === 'comments') {
      if (parts[3] === 'enumerate') return ok(page(state.comments.filter((c) => c.targetId === e.id), body));
      if (method === 'POST') {
        if (e.status === 'Canceled') return error(409, 'EventCanceled', 'This event was canceled.');
        const c = { id: nextId('cmt_'), targetType: 'Event', targetId: e.id, parentCommentId: body.parentCommentId || null, depth: body.parentCommentId ? 1 : 0, state: 'Visible', author: state.me, body: body.body, mentions: [], attachments: [], createdUtc: now(), childCount: 0, descendantCount: 0, reactionCounts: {}, reactionTotal: 0, myReactions: [], labels: [], tags: {}, viewer: { canEdit: true, canDelete: true, canReply: true, canReact: true, canReport: false, canModerate: false }, children: [] };
        state.comments.push(c);
        e.commentCount += 1;
        return ok(c, 201);
      }
    }
    if (parts[2] === 'reactions') {
      if (parts[3] === 'enumerate') return ok(page([], body));
      const key = parts[3];
      const had = e.myReactions.includes(key);
      if (method === 'PUT' && !had) {
        e.myReactions.push(key);
        e.reactionCounts[key] = (e.reactionCounts[key] || 0) + 1;
        e.reactionTotal += 1;
      }
      if (method === 'DELETE' && had) {
        e.myReactions = e.myReactions.filter((k) => k !== key);
        e.reactionCounts[key] = Math.max(0, (e.reactionCounts[key] || 1) - 1);
        e.reactionTotal = Math.max(0, e.reactionTotal - 1);
      }
      return ok({ reactionCounts: e.reactionCounts, reactionTotal: e.reactionTotal, myReactions: e.myReactions });
    }
    if (parts[2] === 'promote' && method === 'POST') {
      e.promotion = { feedPostId: nextId('pst_'), promotedUtc: now() };
      return ok({ id: e.promotion.feedPostId, kind: 'PromotedEvent', source: { type: 'Event', id: e.id, title: e.title }, commentTarget: { type: 'Event', id: e.id } }, 201);
    }
    if (parts[2] === 'unpromote' && method === 'POST') {
      e.promotion = null;
      return ok(present(e));
    }
    return undefined;
  }

  function handleMe(method, parts, body) {
    if (parts[1] === 'events') {
      if (parts[2] === 'enumerate') {
        const f = body.filters || {};
        let list = state.events.filter((e) => !e.deletedUtc && (myRsvpOf(e) || (e.hosts || []).some((h) => h.id === state.me.id)));
        if (f.rsvpStatus) list = list.filter((e) => asList(f.rsvpStatus).includes((myRsvpOf(e) || {}).status));
        if (truthy(f.upcoming)) list = list.filter((e) => String(e.endUtc) > now());
        if (truthy(f.past)) list = list.filter((e) => String(e.endUtc) <= now());
        const result = page(list.sort((a, b) => String(a.startUtc).localeCompare(String(b.startUtc))), body);
        result.objects = result.objects.map((o) => summary(find(o.id)));
        return ok(result);
      }
      if (parts[2] === 'proposals' && parts[3] === 'enumerate') {
        const f = body.filters || {};
        let list = state.events.filter((e) => isMine(e) && !e.deletedUtc);
        if (f.status) list = list.filter((e) => asList(f.status).includes(e.status));
        const result = page(list.sort((a, b) => String(b.createdUtc).localeCompare(String(a.createdUtc))), body);
        result.objects = result.objects.map((o) => present(find(o.id)));
        return ok(result);
      }
    }
    if (parts[1] === 'calendar-feed') {
      if (!parts[2] && method === 'GET') {
        if (!state.feed) state.feed = { url: 'https://api.example.test/api/v1/calendar/cfd_fake.secret' + feedSecret + '.ics', createdUtc: now() };
        return ok(state.feed);
      }
      if (parts[2] === 'rotate' && method === 'POST') {
        feedSecret += 1;
        state.feed = { url: 'https://api.example.test/api/v1/calendar/cfd_fake.secret' + feedSecret + '.ics', createdUtc: now() };
        return ok(state.feed);
      }
      if (!parts[2] && method === 'DELETE') {
        state.feed = null;
        return noContent();
      }
    }
    return undefined;
  }

  function adminEvent(e) {
    return { ...present(e), locked: false, viewer: { ...e.viewer, ...eventStaffViewer }, openReportCount: 0, checkedInCount: state.rsvps.filter((r) => r.eventId === e.id && r.checkedInUtc).length, reminderStatus: 'Scheduled' };
  }

  function adminRsvp(r) {
    return { id: r.id, user: r.user, status: r.status, guestCount: r.guestCount || 0, hideFromAttendeeLists: Boolean(r.hideFromAttendeeLists), checkedInUtc: r.checkedInUtc || null, createdUtc: r.createdUtc, updatedUtc: r.updatedUtc, labels: [], tags: {} };
  }

  function handleAdmin(method, parts, query, body) {
    if (parts[1] === 'metrics' && parts[2] === 'events') {
      return ok(dailyMetricPage({ days: Number((body.filters || query).days) || 30, eventsByDay: [], rsvpsByDay: [], checkInRate: 0, noShowRate: 0, byCategory: { objects: state.categories.slice(0, 20).map((c) => ({ key: c.key, label: c.label, events: 0, rsvps: 0 })), totalRecords: state.categories.length, enumerate: null } }, body));
    }
    if (parts[1] !== 'events') return undefined;
    if (parts[2] === 'enumerate') {
      const result = page(filter(state.events, body, { admin: true }), body, { max: 500 });
      result.objects = result.objects.map((o) => adminEvent(find(o.id)));
      return ok(result);
    }
    if (parts[2] === 'proposals' && parts[3] === 'enumerate') {
      const wanted = asList((body.filters || {}).status);
      const list = state.events.filter((e) => e.proposedBy && !e.deletedUtc && (wanted.length ? wanted.includes(e.status) : e.status === 'Proposed'));
      const result = page(list, body, { max: 500 });
      result.objects = result.objects.map((o) => adminEvent(find(o.id)));
      return ok(result);
    }
    if (parts[2] === 'categories') {
      if (parts[3] === 'enumerate') return ok(page(state.categories, body, { max: 500 }));
      if (parts[3] === 'order' && method === 'PUT') {
        const ids = body.categoryIds || [];
        state.categories.sort((a, b) => ids.indexOf(a.id) - ids.indexOf(b.id));
        return noContent();
      }
      if (!parts[3] && method === 'POST') {
        if (state.categories.some((c) => c.key.toLowerCase() === String(body.key || '').toLowerCase())) return error(409, 'Conflict', 'That key is in use.');
        const c = makeEventCategory(body.key, body.label, body.color || null, { id: nextId('ecg_'), sortOrder: (state.categories.length + 1) * 10 });
        state.categories.push(c);
        return ok(c, 201);
      }
      const c = state.categories.find((x) => x.id === parts[3]);
      if (!c) return error(404, 'NotFound', 'Not found.');
      if (method === 'PUT') {
        Object.assign(c, body);
        return ok(c);
      }
      if (method === 'DELETE') {
        c.isRetired = true;
        return noContent();
      }
      return undefined;
    }
    if (!parts[2] && method === 'POST') {
      if (!body.title || String(body.title).trim().length < 3) return error(400, 'ValidationFailed', 'A title is required.', { title: 'Too short' });
      if (body.startLocal && body.endLocal && body.endLocal <= body.startLocal) return error(400, 'InvalidSchedule', 'The end must be after the start.', { endLocal: 'Before start' });
      const base = makeEvent(nextId('evt_'), { ...fromBody(body), status: 'Draft', publishedUtc: null, createdUtc: now(), updatedUtc: now(), hosts: (body.hostUserIds || []).map((id) => fakeUser(id, 'Host ' + id)) });
      const created = body.recurrence ? occurrencesFrom(base, body.recurrence) : [base];
      state.events.push(...created);
      return ok(adminEvent(created[0]), 201);
    }
    const e = find(parts[2]);
    if (!e) return error(404, 'NotFound', 'Not found.');
    if (!parts[3]) {
      if (method === 'GET') return ok(adminEvent(e));
      if (method === 'PUT') {
        if (e.status === 'Canceled') return error(409, 'EventCanceled', 'This event was canceled.');
        const scope = body.scope || 'One';
        const changes = fromBody(body, e);
        delete changes.startUtc;
        delete changes.endUtc;
        for (const target of siblings(e, scope)) {
          Object.assign(target, changes, { updatedUtc: now() });
          if (scope === 'One' && target.recurrence) target.recurrence = { ...target.recurrence, isException: true };
        }
        if (body.startLocal) e.startUtc = fromBody(body, e).startUtc;
        if (body.endLocal) e.endUtc = fromBody(body, e).endUtc;
        return ok(adminEvent(e));
      }
      if (method === 'DELETE') {
        if (e.status !== 'Draft') return error(409, 'Conflict', 'Only drafts can be deleted; cancel published events.');
        e.deletedUtc = now();
        return noContent();
      }
    }
    if (parts[3] === 'publish') {
      for (const target of e.recurrence ? siblings(e, 'All') : [e]) if (target.status === 'Draft') {
        target.status = 'Published';
        target.publishedUtc = now();
      }
      return ok(adminEvent(e));
    }
    if (parts[3] === 'cancel') {
      if (!body.reason || String(body.reason).trim().length < 3) return error(400, 'ValidationFailed', 'A reason is required.', { reason: 'Required' });
      for (const target of siblings(e, body.scope || 'One')) Object.assign(target, { status: 'Canceled', canceledUtc: now(), cancelReason: body.reason });
      return ok(adminEvent(e));
    }
    if (parts[3] === 'duplicate') {
      const copyId = nextId('evt_');
      const copy = makeEvent(copyId, { ...clone(e), id: copyId, slug: e.slug + '-copy', status: 'Draft', recurrence: null, commentCount: 0, reactionCounts: {}, reactionTotal: 0, myReactions: [], createdUtc: now(), commentTarget: { type: 'Event', id: copyId } });
      if (body.startLocal) {
        const duration = new Date(e.endUtc).getTime() - new Date(e.startUtc).getTime();
        copy.startUtc = new Date(body.startLocal + ':00Z').toISOString().replace('.000Z', 'Z');
        copy.endUtc = new Date(new Date(copy.startUtc).getTime() + duration).toISOString().replace('.000Z', 'Z');
      }
      state.events.push(copy);
      return ok(adminEvent(copy), 201);
    }
    if (parts[3] === 'approve' || parts[3] === 'reject') {
      if (e.status !== 'Proposed') return error(409, 'Conflict', 'This proposal was already decided.');
      if (parts[3] === 'reject') {
        if (!body.reviewNote || String(body.reviewNote).trim().length < 3) return error(400, 'ValidationFailed', 'A review note is required.', { reviewNote: 'Required' });
        e.status = 'Rejected';
      } else {
        e.status = body.publish === false ? 'Draft' : 'Published';
        e.hosts = [e.proposedBy];
        if (e.status === 'Published') e.publishedUtc = now();
      }
      e.reviewNote = body.reviewNote || null;
      return ok(adminEvent(e));
    }
    if (parts[3] === 'rsvps') {
      if (parts[4] === 'enumerate') {
        const f = body.filters || {};
        let list = state.rsvps.filter((r) => r.eventId === e.id);
        if (f.status) list = list.filter((r) => asList(f.status).includes(r.status));
        if (truthy(f.checkedIn)) list = list.filter((r) => r.checkedInUtc);
        return ok(page(list.map(adminRsvp), body, { max: 500 }));
      }
      if (parts[4] === 'export.csv') return textResponse('displayName,handle,status,guests,checkedIn\n' + state.rsvps.filter((r) => r.eventId === e.id).map((r) => [r.user.displayName, r.user.handle, r.status, r.guestCount || 0, r.checkedInUtc ? 'yes' : 'no'].join(',')).join('\n'), 'text/csv');
      const r = state.rsvps.find((x) => x.id === parts[4] && x.eventId === e.id);
      if (!r) return error(404, 'NotFound', 'Not found.');
      const action = parts[5];
      if (action === 'check-in') r.checkedInUtc = now();
      else if (action === 'undo-check-in') r.checkedInUtc = null;
      else if (action === 'promote') r.status = 'Going';
      else if (action === 'move-to-waitlist') {
        r.status = 'Waitlisted';
        r.waitlistedUtc = now();
      } else if (action === 'remove') {
        state.rsvps = state.rsvps.filter((x) => x !== r);
        promote(e);
        return ok({ ...adminRsvp(r), status: 'Removed' });
      } else return undefined;
      recount(e);
      return ok(adminRsvp(r));
    }
    return undefined;
  }

  function handlePublic(method, parts, body) {
    if (parts[1] !== 'events') return undefined;
    const list = state.events.filter((e) => e.audience === 'Public' && e.status === 'Published' && !e.deletedUtc && String(e.endUtc) > now());
    const strip = (e) => {
      const { viewer: _v, myRsvp: _r, attendeePreview: _a, ...rest } = summary(e);
      return { ...rest, description: e.description };
    };
    if (parts[2] === 'enumerate') {
      const result = page(list, { ...body, maxResults: Math.min(20, body.maxResults || 20) }, { max: 20 });
      result.objects = result.objects.map((o) => strip(find(o.id)));
      return ok(result);
    }
    const e = list.find((x) => x.id === parts[2]);
    return e ? ok(strip(e)) : error(404, 'NotFound', 'Not found.');
  }

  /**
   * Answer one request.
   * @param {{ method: string, path: string, query?: object, body?: any }} call
   */
  function handle({ method, path, query = {}, body }) {
    const parts = splitPath(path);
    const m = String(method || 'GET').toUpperCase();
    const b = body && typeof body === 'object' ? body : {};
    const known = parts[0] === 'events' || (parts[0] === 'me' && (parts[1] === 'events' || parts[1] === 'calendar-feed')) || (parts[0] === 'public' && parts[1] === 'events')
      || (parts[0] === 'admin' && (parts[1] === 'events' || (parts[1] === 'metrics' && parts[2] === 'events')));
    if (!known) return undefined;
    calls.push({ method: m, path: '/' + parts.join('/'), query, body: b });
    if (state.killed && parts[0] !== 'admin' && parts[0] !== 'public') return error(503, 'FeatureDisabled', 'Events are temporarily unavailable.');
    switch (parts[0]) {
      case 'events': return handleEvents(m, parts, query, b);
      case 'me': return handleMe(m, parts, b);
      case 'public': return handlePublic(m, parts, b);
      case 'admin': return handleAdmin(m, parts, query, b);
      default: return undefined;
    }
  }

  return { state, calls, handle, fetchImpl: fetchOver(handle), event: find };
}
