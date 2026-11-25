import { dailyMetricPage } from './metricFake.js';

/**
 * An in-memory fake of the Phase 4 directory routes (docs/PHASE4_CONTRACT.md section 3), for client
 * tests and for building screens before the backend exists. It follows the contract shapes: venue
 * and instructor reads with visibility of `Approximate` and `LocalityOnly` addresses, `near` and
 * `bbox` filters, map points and grid clusters, place search, submissions with the duplicate check
 * step and the staff review queue, claims, venue reviews (section 3.12) and member media (section
 * 3.13), and the main error codes; it is not a second backend.
 *
 * Use `fake.handle({ method, path, query, body })` or `fake.fetchImpl`.
 */

import { fakeUser } from './forumsFake.js';
import { formatAddress, haversineKm, roundCoordinate } from '../directory.js';
import { asList, clone, error, fetchOver, gridClusters, matchesMeta, matchesSearch, noContent, ok, page, splitPath, textResponse, truthy } from './fakeUtils.js';

function slugify(text) {
  return String(text).toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
}

const VENUE_VIEWER = Object.freeze({ canEdit: false, canSuggestEdit: true, canReportClosed: true, canReport: true, canModerate: false, canReview: true, canAddMedia: true, canManageMedia: false, canReplyToReviews: false });
const INSTRUCTOR_VIEWER = Object.freeze({ canEdit: false, canSuggestEdit: true, canClaim: true, canReport: true, canModerate: false });

/** A Venue (section 2.2). `extra.lat`/`extra.lng` set an exact position. */
export function makeVenue(id, extra = {}) {
  const { lat = 37.7749, lng = -122.4194, ...rest } = extra;
  const name = rest.name || 'Venue ' + id;
  const address = rest.address || { line1: '100 Example Street', line2: null, locality: 'Exampleville', region: 'EX', postalCode: '00000', countryCode: 'US' };
  return {
    id, name, slug: rest.slug || slugify(rest.name || id.replace(/^ven_/, '')), affiliation: null, summary: 'About ' + name + '.', description: 'Description of ' + name + '.',
    address: { ...address, formatted: formatAddress(address, 'Exact', { isStaff: true }) }, addressVisibility: 'Exact',
    geo: { lat, lng, precision: 'Exact', source: 'Manual', geocodedUtc: null }, distanceKm: null, timeZone: 'America/Los_Angeles',
    phone: null, email: null, websiteUrl: null, socialLinks: [], schedule: null, scheduleText: null, programs: [], visitorPolicy: 'Welcome', visitorPolicyNote: null, photos: [],
    instructors: { objects: [], totalRecords: 0, enumerate: '/api/v1/venues/' + id + '/instructors/enumerate' }, instructorCount: 0, coverPhoto: null,
    isVerified: false, verifiedUtc: null, status: 'Active', audience: 'SignedIn', locked: false, labels: [], tags: {}, viewer: { ...VENUE_VIEWER },
    ratingAverage: null, ratingCount: 0, mediaCount: 0,
    createdUtc: '2026-11-01T00:00:00Z', updatedUtc: '2026-11-01T00:00:00Z',
    ...rest,
    ...(rest.address ? { address: { ...rest.address, formatted: rest.address.formatted || formatAddress(rest.address, 'Exact', { isStaff: true }) } } : {})
  };
}

/** An Instructor (section 2.3). */
export function makeInstructor(id, extra = {}) {
  const displayName = extra.displayName || 'Instructor ' + id;
  return {
    id, displayName, slug: extra.slug || slugify(extra.displayName || id.replace(/^ins_/, '')), bio: 'Bio of ' + displayName + '.', photo: null, rank: null, rankLevel: 0,
    affiliation: null, linkedUser: null, venues: { objects: [], totalRecords: 0, enumerate: '/api/v1/instructors/' + id + '/venues/enumerate' }, venueCount: 0,
    websiteUrl: null, socialLinks: [], status: 'Active', audience: 'SignedIn', locked: false, labels: [], tags: {}, viewer: { ...INSTRUCTOR_VIEWER },
    createdUtc: '2026-11-01T00:00:00Z', updatedUtc: '2026-11-01T00:00:00Z',
    ...extra
  };
}

/** A VenueReview (section 3.12) by `author` (defaults to `usr_2`); `viewer` is computed by the fake for the caller. */
export function makeVenueReview(id, extra = {}) {
  return {
    id, venueId: 'ven_1', author: fakeUser('usr_2', 'Kim Park'), rating: 4, body: 'Friendly and well organized.', editedUtc: null, reply: null,
    moderationState: 'None', labels: [], tags: {}, createdUtc: '2026-11-05T00:00:00Z', updatedUtc: '2026-11-05T00:00:00Z', ...extra
  };
}

/** A ready Media object for tests: an image with thumb and medium variants, or a video (`extra.kind: 'Video'`) with an MP4 source. */
export function makeFakeMedia(id, extra = {}) {
  const kind = extra.kind || 'Image';
  const base = { id, kind, status: 'Ready', altText: null, contentType: kind === 'Video' ? 'video/mp4' : 'image/jpeg', failureReason: null };
  const image = kind === 'Image' ? { variants: { thumb: { width: 320, height: 240, webpUrl: '/m/' + id + '-t.webp', jpegUrl: '/m/' + id + '-t.jpg' }, medium: { width: 1080, height: 810, webpUrl: '/m/' + id + '-m.webp', jpegUrl: '/m/' + id + '-m.jpg' } } } : null;
  const video = kind === 'Video' ? { hlsUrl: null, mp4Url: '/m/' + id + '.mp4', posterUrl: '/m/' + id + '-poster.jpg', durationSeconds: 12 } : null;
  return { ...base, image, video, ...extra };
}

/** A VenueMediaItem (section 3.13) uploaded by `uploader` (defaults to `usr_2`); `viewer` is computed by the fake for the caller. */
export function makeVenueMediaItem(id, extra = {}) {
  return {
    id, venueId: 'ven_1', media: makeFakeMedia('med_' + id), uploader: fakeUser('usr_2', 'Kim Park'), moderationState: 'None', labels: [], tags: {},
    createdUtc: '2026-11-05T00:00:00Z', updatedUtc: '2026-11-05T00:00:00Z', ...extra
  };
}

/** A Pending DirectorySubmission (section 2.4) by `submitter` (defaults to the fake's member `usr_1`). */
export function makeSubmission(id, extra = {}) {
  return {
    id, type: 'EditVenue', status: 'Pending', target: { type: 'Venue', id: 'ven_1', name: 'Venue ven_1' }, submitter: fakeUser('usr_1', 'Sam Rivera'), note: null,
    changes: [], possibleDuplicates: { objects: [], totalRecords: 0, enumerate: null }, reviewer: null, reviewNote: null, decidedUtc: null, labels: [], tags: {},
    createdUtc: '2026-11-10T00:00:00Z', updatedUtc: '2026-11-10T00:00:00Z', fields: {}, ...extra
  };
}

/** Viewer capabilities of directory staff (merge into `viewer`). */
export const directoryStaffViewer = Object.freeze({ canEdit: true, canModerate: true });

function similar(a, b) {
  const x = String(a || '').toLowerCase().replace(/[^a-z0-9]/g, '');
  const y = String(b || '').toLowerCase().replace(/[^a-z0-9]/g, '');
  if (!x || !y) return false;
  return x === y || x.includes(y) || y.includes(x);
}

function inBox(lat, lng, box) {
  if (lat < box.south || lat > box.north) return false;
  return box.west <= box.east ? lng >= box.west && lng <= box.east : lng >= box.west || lng <= box.east;
}

/**
 * Create a fake.
 * @param {{ me?: object, venues?: object[], instructors?: object[], links?: Array<{ venueId: string, instructorId: string, role?: string }>, submissions?: object[],
 *   programs?: object[], geocodeCandidates?: object[], geocoderConfigured?: boolean, submissionsEnabled?: boolean, claimsEnabled?: boolean, maxPending?: number,
 *   settings?: object, now?: () => string, killed?: boolean, reviews?: object[], mediaItems?: object[], reviewsEnabled?: boolean, memberMediaEnabled?: boolean,
 *   reviewLimits?: { minLength?: number, maxLength?: number, replyMaxLength?: number }, directoryStaff?: boolean, uploads?: Record<string, object> }} [seed]
 *
 * Reviews and media items are seeded with `makeVenueReview` and `makeVenueMediaItem`; `uploads` maps a media id to the Media
 * object an add (`POST /venues/{id}/media`) attaches (a ready image otherwise). `directoryStaff` makes the caller directory
 * staff; a caller linked to an instructor entry of the venue may reply to reviews and remove media.
 */
export function createDirectoryFake(seed = {}) {
  let n = 0;
  const nextId = (prefix) => prefix + 'fake' + (++n);
  const now = seed.now || (() => '2026-11-11T12:00:00Z');
  const state = {
    me: seed.me || fakeUser('usr_1', 'Sam Rivera'),
    venues: (seed.venues || []).map(clone),
    instructors: (seed.instructors || []).map(clone),
    links: (seed.links || []).map(clone),
    submissions: (seed.submissions || []).map(clone),
    programs: (seed.programs || []).map((p, i) => ({ id: p.id || 'dpg_' + p.key, sortOrder: (i + 1) * 10, isRetired: false, labels: [], tags: {}, ...p })),
    geocodeCandidates: (seed.geocodeCandidates || [{ label: 'Exampleville, EX', lat: 37.77, lng: -122.42, bbox: null }]).map(clone),
    geocoderConfigured: seed.geocoderConfigured !== false,
    submissionsEnabled: seed.submissionsEnabled !== false,
    claimsEnabled: seed.claimsEnabled !== false,
    maxPending: seed.maxPending || 5,
    settings: { venueLabel: 'Venue', venueLabelPlural: 'Venues', instructorLabel: 'Instructor', instructorLabelPlural: 'Instructors', sessionLabel: 'Session', sessionLabelPlural: 'Sessions', submissions: true, claims: true, geocoder: { provider: 'None' }, map: { provider: 'None' }, ...(seed.settings || {}) },
    killed: Boolean(seed.killed),
    regeocodeQueued: 0,
    reviews: (seed.reviews || []).map(clone),
    mediaItems: (seed.mediaItems || []).map(clone),
    reviewsEnabled: seed.reviewsEnabled !== false,
    memberMediaEnabled: seed.memberMediaEnabled !== false,
    reviewLimits: { minLength: 10, maxLength: 2000, replyMaxLength: 2000, ...(seed.reviewLimits || {}) },
    directoryStaff: Boolean(seed.directoryStaff),
    uploads: clone(seed.uploads || {})
  };
  const calls = [];

  const findVenue = (idOrSlug) => state.venues.find((v) => (v.id === idOrSlug || v.slug === idOrSlug) && !v.deletedUtc);
  const findInstructor = (idOrSlug) => state.instructors.find((i) => (i.id === idOrSlug || i.slug === idOrSlug) && !i.deletedUtc);
  const canOpen = (item) => !item.locked && !(item.viewer && item.viewer.canOpen === false);
  const programObjects = (keys) => (keys || []).map((k) => (k && typeof k === 'object' ? k : (state.programs.find((p) => p.key === k) || { key: k, label: k }))).map((p) => ({ key: p.key, label: p.label }));
  const instructorsOf = (venueId) => state.links.filter((l) => l.venueId === venueId).map((l) => findInstructor(l.instructorId)).filter(Boolean);
  const venuesOf = (instructorId) => state.links.filter((l) => l.instructorId === instructorId).map((l) => findVenue(l.venueId)).filter(Boolean);
  /** The caller's instructor entry linked to the venue, if any (replies and media management). */
  const linkedInstructorOf = (venueId) => instructorsOf(venueId).find((i) => i.linkedUser && i.linkedUser.id === state.me.id) || null;
  const isLinkedInstructor = (venueId) => Boolean(linkedInstructorOf(venueId));
  const liveReviews = (venueId) => state.reviews.filter((r) => r.venueId === venueId && !r.deletedUtc && (r.moderationState || 'None') === 'None');
  const liveMedia = (venueId) => state.mediaItems.filter((x) => x.venueId === venueId && !x.deletedUtc && (x.moderationState || 'None') === 'None');
  const newestFirst = (a, c) => String(c.createdUtc).localeCompare(String(a.createdUtc)) || c.id.localeCompare(a.id);

  /** `ratingAverage`, `ratingCount`, `mediaCount` from the source rows (the seeded values when the fake has no rows for the venue). */
  function venueAggregates(v) {
    const live = liveReviews(v.id);
    const hasReviews = state.reviews.some((r) => r.venueId === v.id);
    const hasMedia = state.mediaItems.some((x) => x.venueId === v.id);
    const total = live.reduce((sum, r) => sum + r.rating, 0);
    return {
      ratingAverage: hasReviews ? (live.length ? Math.round((total / live.length) * 10) / 10 : null) : (v.ratingAverage === undefined ? null : v.ratingAverage),
      ratingCount: hasReviews ? live.length : (v.ratingCount || 0),
      mediaCount: hasMedia ? liveMedia(v.id).length : (v.mediaCount || 0)
    };
  }

  function presentReview(r) {
    const isAuthor = r.author.id === state.me.id;
    const replier = isLinkedInstructor(r.venueId);
    const ownsReply = Boolean(r.reply && r.reply.author && r.reply.author.id === state.me.id);
    const { deletedUtc: _d, viewer: seededViewer, ...rest } = r;
    return {
      ...clone(rest),
      viewer: {
        isAuthor, canEdit: isAuthor, canDelete: isAuthor, canReply: replier && !isAuthor, canDeleteReply: Boolean(r.reply) && (ownsReply || state.directoryStaff),
        canReport: !isAuthor, canModerate: state.directoryStaff, ...(seededViewer || {})
      }
    };
  }

  function presentMediaItem(x) {
    const isUploader = x.uploader.id === state.me.id;
    const { deletedUtc: _d, removedReason: _r, viewer: seededViewer, ...rest } = x;
    return { ...clone(rest), viewer: { isUploader, canRemove: isUploader || state.directoryStaff || isLinkedInstructor(x.venueId), canReport: !isUploader, ...(seededViewer || {}) } };
  }

  function reviewProblems(rating, body) {
    const fieldErrors = {};
    if (!Number.isInteger(rating) || rating < 1 || rating > 5) fieldErrors.rating = 'Choose 1 to 5 stars.';
    const text = typeof body === 'string' ? body.trim() : '';
    if (text.length < state.reviewLimits.minLength) fieldErrors.body = 'Too short.';
    else if (text.length > state.reviewLimits.maxLength) fieldErrors.body = 'Too long.';
    return Object.keys(fieldErrors).length ? error(400, 'ValidationFailed', 'Check the review.', fieldErrors) : null;
  }

  const REVIEW_ORDERS = {
    CreatedDescending: newestFirst,
    CreatedAscending: (a, c) => -newestFirst(a, c),
    RatingDescending: (a, c) => c.rating - a.rating || newestFirst(a, c),
    RatingAscending: (a, c) => a.rating - c.rating || newestFirst(a, c)
  };

  function handleReviews(m, parts, b, v) {
    if (parts[3] === 'enumerate' && m === 'POST') {
      const f = b.filters || {};
      let list = state.reviews.filter((r) => r.venueId === v.id && !r.deletedUtc && ((r.moderationState || 'None') === 'None' || state.directoryStaff));
      const ratings = asList(f.rating).map(Number);
      if (ratings.length) list = list.filter((r) => ratings.includes(r.rating));
      if (f.authorUserId) list = list.filter((r) => r.author.id === f.authorUserId);
      if (truthy(f.hasReply)) list = list.filter((r) => r.reply);
      if (f.hasReply === false || f.hasReply === 'false') list = list.filter((r) => !r.reply);
      list = list.filter((r) => matchesMeta(r, b));
      return ok(page(list.slice().sort(REVIEW_ORDERS[b.ordering] || REVIEW_ORDERS.CreatedDescending).map(presentReview), b));
    }
    if (!parts[3] && m === 'POST') {
      if (!state.reviewsEnabled) return error(403, 'ReviewsDisabled', 'Reviews are turned off.');
      if (state.reviews.some((r) => r.venueId === v.id && !r.deletedUtc && r.author.id === state.me.id)) return error(409, 'AlreadyReviewed', 'You already reviewed this venue.');
      const problem = reviewProblems(b.rating, b.body);
      if (problem) return problem;
      const r = makeVenueReview(nextId('vrv_'), { venueId: v.id, author: state.me, rating: b.rating, body: b.body.trim(), labels: b.labels || [], tags: b.tags || {}, createdUtc: now(), updatedUtc: now() });
      state.reviews.push(r);
      return ok(presentReview(r), 201);
    }
    const r = state.reviews.find((x) => x.id === parts[3] && x.venueId === v.id && !x.deletedUtc);
    if (!r) return error(404, 'NotFound', 'Not found.');
    if (!parts[4]) {
      if (m === 'GET') return ok(presentReview(r));
      if (r.author.id !== state.me.id) return error(403, 'Forbidden', 'Only the author can change this review.');
      if (m === 'PUT') {
        const rating = b.rating === undefined ? r.rating : b.rating;
        const body = b.body === undefined ? r.body : b.body;
        const problem = reviewProblems(rating, body);
        if (problem) return problem;
        Object.assign(r, { rating, body: body.trim(), editedUtc: now(), updatedUtc: now() });
        if (b.labels) r.labels = b.labels;
        if (b.tags) r.tags = b.tags;
        return ok(presentReview(r));
      }
      if (m === 'DELETE') {
        r.deletedUtc = now();
        return noContent();
      }
      return undefined;
    }
    if (parts[4] !== 'reply') return undefined;
    if (m === 'PUT') {
      const instructor = linkedInstructorOf(v.id);
      if (!instructor || r.author.id === state.me.id) return error(403, 'Forbidden', 'Only a linked instructor of the venue can reply.');
      const text = typeof b.body === 'string' ? b.body.trim() : '';
      if (!text || text.length > state.reviewLimits.replyMaxLength) return error(400, 'ValidationFailed', 'Check the reply.', { body: text ? 'Too long.' : 'Required.' });
      r.reply = r.reply
        ? { ...r.reply, body: text, editedUtc: now() }
        : { body: text, author: state.me, instructorId: instructor.id, instructorName: instructor.displayName, createdUtc: now(), editedUtc: null };
      return ok(presentReview(r));
    }
    if (m === 'DELETE') {
      if (!r.reply) return error(404, 'NotFound', 'Not found.');
      if (!(r.reply.author && r.reply.author.id === state.me.id) && !state.directoryStaff) return error(403, 'Forbidden', 'You cannot remove this reply.');
      r.reply = null;
      return ok(presentReview(r));
    }
    return undefined;
  }

  function handleMedia(m, parts, b, v) {
    if (parts[3] === 'enumerate' && m === 'POST') {
      const f = b.filters || {};
      let list = state.mediaItems.filter((x) => x.venueId === v.id && !x.deletedUtc && ((x.moderationState || 'None') === 'None' || state.directoryStaff));
      if (f.kind) list = list.filter((x) => asList(f.kind).includes(x.media.kind));
      if (f.uploaderUserId) list = list.filter((x) => x.uploader.id === f.uploaderUserId);
      const order = b.ordering === 'CreatedAscending' ? (a, c) => -newestFirst(a, c) : newestFirst;
      return ok(page(list.filter((x) => matchesMeta(x, b)).sort(order).map(presentMediaItem), b));
    }
    if (!parts[3] && m === 'POST') {
      if (!state.memberMediaEnabled) return error(403, 'MemberMediaDisabled', 'Member photos and videos are turned off.');
      const ids = Array.isArray(b.mediaIds) ? b.mediaIds : [];
      if (ids.length < 1 || ids.length > 10) return error(400, 'ValidationFailed', 'Attach 1 to 10 uploads.', { mediaIds: 'Attach 1 to 10 uploads.' });
      if (ids.some((id) => state.mediaItems.some((x) => x.media.id === id && !x.deletedUtc))) return error(409, 'MediaNotAttachable', 'That upload is already attached.');
      const created = ids.map((id) => {
        const item = makeVenueMediaItem(nextId('vmd_'), { venueId: v.id, media: state.uploads[id] ? clone(state.uploads[id]) : makeFakeMedia(id), uploader: state.me, labels: b.labels || [], tags: b.tags || {}, createdUtc: now(), updatedUtc: now() });
        state.mediaItems.push(item);
        return presentMediaItem(item);
      });
      return ok({ objects: created }, 201);
    }
    const item = state.mediaItems.find((x) => x.id === parts[3] && x.venueId === v.id && !x.deletedUtc);
    if (!item) return error(404, 'NotFound', 'Not found.');
    if (!parts[4] && m === 'DELETE') {
      if (item.uploader.id === state.me.id) item.deletedUtc = now();
      else if (state.directoryStaff || isLinkedInstructor(v.id)) Object.assign(item, { moderationState: 'Removed', removedReason: b.reason || null });
      else return error(403, 'Forbidden', 'You cannot remove this item.');
      return noContent();
    }
    return undefined;
  }

  /** Apply address visibility for a member caller (section 2.1). */
  function visibleGeo(v, { staff = false } = {}) {
    if (!v.geo || v.geo.lat === null || v.geo.lat === undefined) return { geo: { lat: null, lng: null, precision: 'None', ...(staff ? { source: (v.geo && v.geo.source) || 'None', geocodedUtc: null } : {}) }, address: v.address };
    if (staff || v.addressVisibility === 'Exact') return { geo: { ...v.geo, lat: roundCoordinate(v.geo.lat, 5), lng: roundCoordinate(v.geo.lng, 5), precision: 'Exact' }, address: v.address };
    const coarse = { line1: null, line2: null, locality: v.address.locality, region: v.address.region, postalCode: null, countryCode: v.address.countryCode };
    const address = { ...coarse, formatted: formatAddress(coarse, 'Approximate') };
    if (v.addressVisibility === 'Approximate') return { geo: { lat: roundCoordinate(v.geo.lat, 2), lng: roundCoordinate(v.geo.lng, 2), precision: 'Approximate' }, address };
    return { geo: { lat: null, lng: null, precision: 'None' }, address };
  }

  function instructorSummary(i) {
    const { bio: _b, venues: _v, socialLinks: _s, deletedUtc: _d, ...rest } = i;
    return { ...rest, venueCount: venuesOf(i.id).length };
  }

  function presentVenue(v, { staff = false, distanceKm = null } = {}) {
    const vis = visibleGeo(v, { staff });
    const out = { ...v, ...vis, programs: programObjects(v.programs), distanceKm };
    const instructors = instructorsOf(v.id).filter(canOpen);
    out.instructors = { objects: instructors.slice(0, 10).map(instructorSummary), totalRecords: instructors.length, enumerate: '/api/v1/venues/' + v.id + '/instructors/enumerate' };
    out.instructorCount = instructors.length;
    if (!canOpen(v)) Object.assign(out, { description: null, phone: null, email: null, socialLinks: [], schedule: null, scheduleText: null, photos: [] });
    if (staff) out.viewer = { ...v.viewer, ...directoryStaffViewer };
    Object.assign(out, venueAggregates(v));
    const seeded = out.viewer || {};
    out.viewer = { ...VENUE_VIEWER, ...seeded, canReview: state.reviewsEnabled && seeded.canReview !== false, canAddMedia: state.memberMediaEnabled && seeded.canAddMedia !== false };
    if (state.directoryStaff || isLinkedInstructor(v.id)) out.viewer.canManageMedia = true;
    if (isLinkedInstructor(v.id)) out.viewer.canReplyToReviews = true;
    delete out.deletedUtc;
    return out;
  }

  /** The venue detail's `reviews` block for the caller. */
  function reviewsBlock(v) {
    const live = liveReviews(v.id);
    const distribution = { '1': 0, '2': 0, '3': 0, '4': 0, '5': 0 };
    for (const r of live) distribution[String(r.rating)] += 1;
    const mine = state.reviews.find((r) => r.venueId === v.id && !r.deletedUtc && r.author.id === state.me.id);
    const agg = venueAggregates(v);
    return { enabled: state.reviewsEnabled, averageRating: agg.ratingAverage, count: agg.ratingCount, distribution, mine: mine ? presentReview(mine) : null, enumerate: '/api/v1/venues/' + v.id + '/reviews/enumerate' };
  }

  function venueSummary(v, options) {
    const { description: _d, socialLinks: _s, scheduleText: _t, schedule: _schedule, instructors: _i, photos, reviews: _r, ...rest } = presentVenue(v, options);
    return { ...rest, coverPhoto: (photos || [])[0] || null };
  }

  function presentInstructor(i, { staff = false } = {}) {
    const venues = venuesOf(i.id).filter(canOpen);
    const out = { ...i, venues: { objects: venues.slice(0, 10).map((v) => venueSummary(v)), totalRecords: venues.length, enumerate: '/api/v1/instructors/' + i.id + '/venues/enumerate' }, venueCount: venues.length };
    if (!canOpen(i)) Object.assign(out, { bio: null, socialLinks: [], linkedUser: null });
    if (staff) out.viewer = { ...i.viewer, ...directoryStaffViewer };
    delete out.deletedUtc;
    return out;
  }

  function filterVenues(list, body, { admin = false } = {}) {
    const f = body.filters || {};
    let out = list.filter((v) => !v.deletedUtc);
    if (admin && f.status) out = out.filter((v) => asList(f.status).includes(v.status));
    else if (!admin) out = out.filter((v) => v.status === 'Active' || (truthy(f.includeClosed) && v.status === 'Closed'));
    if (admin && f.geoSource) out = out.filter((v) => (v.geo ? v.geo.source : 'None') === f.geoSource || (f.geoSource === 'None' && (!v.geo || v.geo.lat === null)));
    if (admin && f.verified !== undefined) out = out.filter((v) => Boolean(v.isVerified) === truthy(f.verified));
    const programs = asList(f.programs);
    if (programs.length) out = out.filter((v) => (v.programs || []).some((p) => programs.includes(typeof p === 'object' ? p.key : p)));
    for (const key of ['countryCode', 'region', 'locality']) if (f[key]) out = out.filter((v) => String(v.address[key] || '').toLowerCase() === String(f[key]).toLowerCase());
    if (f.visitorPolicy) out = out.filter((v) => asList(f.visitorPolicy).includes(v.visitorPolicy));
    if (truthy(f.verifiedOnly)) out = out.filter((v) => v.isVerified);
    if (f.instructorId) out = out.filter((v) => state.links.some((l) => l.venueId === v.id && l.instructorId === f.instructorId));
    if (f.audience) out = out.filter((v) => asList(f.audience).includes(v.audience));
    out = out.filter((v) => matchesMeta(v, body) && matchesSearch(v, body.search, ['name', 'affiliation', 'summary']));
    let distances = {};
    if (f.near) {
      const near = f.near;
      const radius = Number(near.radiusKm) || 50;
      distances = Object.fromEntries(out.filter((v) => v.geo && v.geo.lat !== null).map((v) => [v.id, haversineKm(near, v.geo)]));
      out = out.filter((v) => distances[v.id] !== undefined && distances[v.id] <= radius);
    }
    if (f.bbox) out = out.filter((v) => v.geo && v.geo.lat !== null && inBox(v.geo.lat, v.geo.lng, f.bbox));
    const ordering = body.ordering || (f.near ? 'DistanceAscending' : 'NameAscending');
    const by = {
      NameAscending: (a, b) => a.name.localeCompare(b.name) || a.id.localeCompare(b.id),
      DistanceAscending: (a, b) => (distances[a.id] || 0) - (distances[b.id] || 0) || a.id.localeCompare(b.id),
      CreatedDescending: (a, b) => String(b.createdUtc).localeCompare(String(a.createdUtc)),
      CreatedAscending: (a, b) => String(a.createdUtc).localeCompare(String(b.createdUtc)),
      UpdatedDescending: (a, b) => String(b.updatedUtc).localeCompare(String(a.updatedUtc)),
      Relevance: () => 0
    };
    return { list: out.sort(by[ordering] || by.NameAscending), distances };
  }

  function filterInstructors(list, body) {
    const f = body.filters || {};
    let out = list.filter((i) => !i.deletedUtc && (i.status === 'Active' || f.status));
    if (f.status) out = out.filter((i) => asList(f.status).includes(i.status));
    if (f.venueId) out = out.filter((i) => state.links.some((l) => l.instructorId === i.id && l.venueId === f.venueId));
    if (f.rank) out = out.filter((i) => asList(f.rank).includes(i.rank));
    if (truthy(f.claimed)) out = out.filter((i) => i.linkedUser);
    if (f.claimed === false || f.claimed === 'false') out = out.filter((i) => !i.linkedUser);
    if (f.linkedUserId) out = out.filter((i) => i.linkedUser && i.linkedUser.id === f.linkedUserId);
    if (f.audience) out = out.filter((i) => asList(f.audience).includes(i.audience));
    out = out.filter((i) => matchesMeta(i, body) && matchesSearch(i, body.search, ['displayName', 'affiliation', 'bio']));
    return out.sort((a, b) => a.displayName.localeCompare(b.displayName) || a.id.localeCompare(b.id));
  }

  function mapFeatures(body) {
    const box = body.bbox;
    if (!box) return error(400, 'InvalidGeo', 'A bounding box is required.');
    const max = Math.max(1, Math.min(1000, Number(state.settings.map.maxFeatures) || 500));
    const { list } = filterVenues(state.venues, { ...body, filters: { ...(body.filters || {}), bbox: box } });
    const points = list.filter((v) => v.addressVisibility !== 'LocalityOnly').map((v) => ({ v, g: visibleGeo(v).geo })).filter((x) => x.g.lat !== null);
    const point = (x) => ({ kind: 'Point', venue: { id: x.v.id, name: x.v.name, geo: x.g, programs: (x.v.programs || []).map((p) => (typeof p === 'object' ? p.key : p)), isVerified: Boolean(x.v.isVerified), coverThumbUrl: null } });
    const objects = points.length <= max ? points.map(point) : gridClusters(points, box, max, point);
    return ok({ ...page(objects.map((item, rank) => ({ id: 'feature_' + rank, labels: [], tags: {}, ...item })), body, { max: 1000 }), matchedRecords: points.length });
  }

  function submissionTarget(type, targetId) {
    if (!targetId) return null;
    if (type === 'EditInstructor' || type === 'ClaimInstructor') {
      const i = findInstructor(targetId);
      return i ? { type: 'Instructor', id: i.id, name: i.displayName, entity: i } : null;
    }
    const v = findVenue(targetId);
    return v ? { type: 'Venue', id: v.id, name: v.name, entity: v } : null;
  }

  function changesOf(type, fields, target) {
    const current = target ? target.entity : null;
    return Object.entries(fields || {}).map(([field, proposed]) => ({ field, current: current ? clone(current[field] === undefined ? null : current[field]) : null, proposed: clone(proposed) }));
  }

  function presentSubmission(s) {
    const out = { ...s };
    delete out.fields;
    return out;
  }

  function submit(body) {
    const type = body.type;
    const isClaim = type === 'ClaimInstructor';
    if (!state.submissionsEnabled || (isClaim && !state.claimsEnabled)) return error(403, 'SubmissionsDisabled', 'Submissions are off.');
    const pending = state.submissions.filter((s) => s.submitter.id === state.me.id && s.status === 'Pending');
    if (pending.length >= state.maxPending) return error(409, 'SubmissionLimitReached', 'You have too many pending submissions.');
    const target = submissionTarget(type, body.targetId);
    if (['EditVenue', 'EditInstructor', 'ClaimInstructor', 'ReportClosed'].includes(type) && !target) return error(404, 'NotFound', 'Not found.');
    if (isClaim) {
      if (target.entity.linkedUser) return error(409, 'AlreadyClaimed', 'This entry is already linked to an account.');
      if (pending.some((s) => s.type === 'ClaimInstructor' && s.target && s.target.id === target.id)) return error(409, 'AlreadyClaimed', 'You already have a pending claim.');
      if (String(body.note || '').trim().length < 10) return error(400, 'ValidationFailed', 'Explain the claim.', { note: 'Too short' });
    }
    const fields = body.fields || {};
    let duplicates = [];
    if (type === 'NewVenue') duplicates = state.venues.filter((v) => !v.deletedUtc && similar(v.name, fields.name)).slice(0, 5).map((v) => venueSummary(v));
    if (type === 'NewInstructor') duplicates = state.instructors.filter((i) => !i.deletedUtc && similar(i.displayName, fields.displayName)).slice(0, 5).map(instructorSummary);
    const base = {
      type, target: target ? { type: target.type, id: target.id, name: target.name } : null, submitter: state.me, note: body.note || null,
      changes: changesOf(type, fields, target), possibleDuplicates: { objects: duplicates, totalRecords: duplicates.length, enumerate: null },
      reviewer: null, reviewNote: null, decidedUtc: null, labels: body.labels || [], tags: body.tags || {}, createdUtc: now(), updatedUtc: now()
    };
    if (duplicates.length && !body.confirmNotDuplicate) return ok({ id: null, status: 'Draft', ...base });
    const s = { id: nextId('dsb_'), status: 'Pending', ...base, fields: clone(fields) };
    state.submissions.push(s);
    return ok(presentSubmission(s), 201);
  }

  function applySubmission(s, body) {
    const fields = s.fields || {};
    let target = null;
    if (s.type === 'NewVenue') {
      const geo = body.geo ? { lat: body.geo.lat, lng: body.geo.lng } : { lat: null, lng: null };
      target = makeVenue(nextId('ven_'), { ...fields, ...geo, geo: body.geo ? { lat: body.geo.lat, lng: body.geo.lng, precision: 'Exact', source: 'Manual', geocodedUtc: null } : null, createdUtc: now() });
      state.venues.push(target);
    } else if (s.type === 'NewInstructor') {
      target = makeInstructor(nextId('ins_'), { ...fields, createdUtc: now() });
      state.instructors.push(target);
    } else if (s.type === 'EditVenue' || s.type === 'ReportClosed') {
      target = findVenue(s.target.id);
      if (s.type === 'ReportClosed') target.status = 'Closed';
      else Object.assign(target, fields);
      if (body.geo) target.geo = { lat: body.geo.lat, lng: body.geo.lng, precision: 'Exact', source: 'Manual', geocodedUtc: null };
    } else {
      target = findInstructor(s.target.id);
      if (s.type === 'ClaimInstructor') target.linkedUser = s.submitter;
      else Object.assign(target, fields);
    }
    target.updatedUtc = now();
    return target;
  }

  function handleAdminDirectory(method, parts, body) {
    if (parts[2] === 'metrics' && (method === 'GET' || (method === 'POST' && parts[3] === 'enumerate'))) {
      const byDay = (list, field) => {
        const counts = new Map();
        for (const item of list) {
          const day = String(item[field] || '').slice(0, 10);
          if (day) counts.set(day, (counts.get(day) || 0) + 1);
        }
        return Array.from(counts.entries()).sort((a, b) => a[0].localeCompare(b[0])).map(([date, count]) => ({ date, count }));
      };
      const venues = state.venues.filter((v) => !v.deletedUtc);
      return ok(dailyMetricPage({
        activeVenues: venues.filter((v) => v.status === 'Active').length,
        closedVenues: venues.filter((v) => v.status === 'Closed').length,
        archivedVenues: venues.filter((v) => v.status === 'Archived').length,
        verifiedVenues: venues.filter((v) => v.verified).length,
        needsPin: venues.filter((v) => !v.geo).length,
        instructors: state.instructors.length,
        claimedInstructors: state.instructors.filter((i) => i.linkedUser).length,
        pendingSubmissions: state.submissions.filter((x) => x.status === 'Pending').length,
        overdueSubmissions: 0,
        submissionsLast30Days: state.submissions.length,
        approvedLast30Days: state.submissions.filter((x) => x.status === 'Approved').length,
        submissionsByDay: byDay(state.submissions, 'createdUtc'),
        approvalsByDay: byDay(state.submissions.filter((x) => x.status === 'Approved'), 'reviewedUtc'),
        venuesCreatedByDay: byDay(venues, 'createdUtc')
      }, body));
    }
    if (parts[2] === 'submissions') {
      if (parts[3] === 'enumerate') {
        const f = body.filters || {};
        const statuses = asList(f.status);
        let list = state.submissions.filter((s) => (statuses.length ? statuses.includes(s.status) : s.status === 'Pending'));
        if (f.type) list = list.filter((s) => asList(f.type).includes(s.type));
        if (f.submitterUserId) list = list.filter((s) => s.submitter.id === f.submitterUserId);
        if (f.targetId) list = list.filter((s) => s.target && s.target.id === f.targetId);
        list = list.filter((s) => matchesMeta(s, body));
        list.sort((a, b) => (body.ordering === 'CreatedDescending' ? String(b.createdUtc).localeCompare(String(a.createdUtc)) : String(a.createdUtc).localeCompare(String(b.createdUtc))));
        return ok(page(list.map(presentSubmission), body, { max: 500 }));
      }
      const s = state.submissions.find((x) => x.id === parts[3]);
      if (!s) return error(404, 'NotFound', 'Not found.');
      if (!parts[4]) {
        if (method === 'GET') return ok(presentSubmission({ ...s, changes: changesOf(s.type, s.fields, submissionTarget(s.type, s.target && s.target.id)) }));
        if (method === 'PUT') {
          s.fields = { ...(s.fields || {}), ...(body.fields || {}) };
          s.changes = changesOf(s.type, s.fields, submissionTarget(s.type, s.target && s.target.id));
          return ok(presentSubmission(s));
        }
      }
      if (s.status !== 'Pending') return error(409, 'Conflict', 'This submission was already decided.');
      if (parts[4] === 'approve') {
        if (s.stale) return error(409, 'SubmissionStale', 'The entry changed since this diff was loaded.');
        const target = applySubmission(s, body);
        Object.assign(s, { status: 'Approved', reviewer: state.me, reviewNote: body.reviewNote || null, decidedUtc: now() });
        const isInstructor = s.type.includes('Instructor');
        return ok({ submission: presentSubmission(s), target: isInstructor ? presentInstructor(target, { staff: true }) : presentVenue(target, { staff: true }) });
      }
      if (parts[4] === 'reject') {
        if (!body.reviewNote || String(body.reviewNote).trim().length < 3) return error(400, 'ValidationFailed', 'A review note is required.', { reviewNote: 'Required' });
        Object.assign(s, { status: 'Rejected', reviewer: state.me, reviewNote: body.reviewNote, decidedUtc: now() });
        return ok(presentSubmission(s));
      }
      return undefined;
    }
    if (parts[2] === 'import') {
      const rows = body.rows || [];
      const errors = rows.map((r, i) => (!r.name ? { row: i + 1, field: 'name', message: 'A name is required.' } : null)).filter(Boolean);
      const valid = rows.filter((r) => r.name);
      const created = valid.filter((r) => !state.venues.some((v) => v.name === r.name)).length;
      const updated = valid.length - created;
      if (!body.dryRun) for (const r of valid) {
        const existing = state.venues.find((v) => v.name === r.name);
        if (existing) Object.assign(existing, r);
        else state.venues.push(makeVenue(nextId('ven_'), { ...r, lat: null, lng: null, geo: null }));
      }
      return ok({ created, updated, skipped: errors.length, errors: { objects: errors.slice(0, 100), totalRecords: errors.length } });
    }
    if (parts[2] === 'regeocode') {
      const queued = body.venueIds && body.venueIds.length ? body.venueIds.length : state.venues.filter((v) => !body.onlyMissing || !v.geo || v.geo.lat === null).length;
      state.regeocodeQueued += queued;
      return ok({ queued }, 202);
    }
    if (parts[2] === 'export.csv') return textResponse('type,id,name\n' + state.venues.map((v) => 'Venue,' + v.id + ',' + v.name).join('\n'), 'text/csv');
    if (parts[2] === 'programs') {
      if (parts[3] === 'enumerate') return ok(page(state.programs, body, { max: 500 }));
      if (parts[3] === 'order' && method === 'PUT') {
        const ids = body.programIds || [];
        state.programs.sort((a, b) => ids.indexOf(a.id) - ids.indexOf(b.id));
        state.programs.forEach((p, i) => { p.sortOrder = (i + 1) * 10; });
        return noContent();
      }
      if (!parts[3] && method === 'POST') {
        if (state.programs.some((p) => p.key.toLowerCase() === String(body.key || '').toLowerCase())) return error(409, 'Conflict', 'That key is in use.');
        const p = { id: nextId('dpg_'), key: body.key, label: body.label, sortOrder: (state.programs.length + 1) * 10, isRetired: false, labels: [], tags: {} };
        state.programs.push(p);
        return ok(p, 201);
      }
      const p = state.programs.find((x) => x.id === parts[3]);
      if (!p) return error(404, 'NotFound', 'Not found.');
      if (method === 'PUT') {
        Object.assign(p, body);
        return ok(p);
      }
      if (method === 'DELETE') {
        p.isRetired = true;
        return noContent();
      }
    }
    if (parts[2] === 'settings') {
      const configKeys = ['venueLabel', 'venueLabelPlural', 'instructorLabel', 'instructorLabelPlural', 'sessionLabel', 'sessionLabelPlural', 'instructorRankFieldKey', 'submissions', 'claims'];
      if (method === 'PUT') {
        for (const key of configKeys) if (body[key] !== undefined) state.settings[key] = body[key];
        for (const [key, value] of Object.entries(body.values || {})) {
          const [, section, name] = key.split('.');
          state.settings[section] = { ...state.settings[section], [name]: value };
        }
      }
      const config = Object.fromEntries(configKeys.map((key) => [key, state.settings[key]]));
      const settings = ['map', 'geocoder'].flatMap((section) => Object.entries(state.settings[section] || {}).map(([key, value]) => ({ key: 'directory.' + section + '.' + key, value, type: 'String', labels: [], tags: {} })));
      return ok(method === 'PUT' ? { config, settings, geocoderConfigured: state.geocoderConfigured } : { ...page(settings, body, { max: 500 }), config, geocoderConfigured: state.geocoderConfigured });
    }
    return undefined;
  }

  function adminVenue(v) {
    return { ...presentVenue(v, { staff: true }), openReportCount: 0, pendingSubmissionCount: state.submissions.filter((s) => s.status === 'Pending' && s.target && s.target.id === v.id).length, geocodedUtc: v.geo ? v.geo.geocodedUtc : null };
  }

  function handleAdmin(method, parts, body) {
    if (parts[1] === 'directory') return handleAdminDirectory(method, parts, body);
    if (parts[1] === 'venues') {
      if (parts[2] === 'enumerate') {
        const { list } = filterVenues(state.venues, body, { admin: true });
        const result = page(list, body, { max: 500 });
        result.objects = result.objects.map((o) => adminVenue(findVenue(o.id)));
        return ok(result);
      }
      if (!parts[2] && method === 'POST') {
        if (!body.name || String(body.name).trim().length < 2) return error(400, 'ValidationFailed', 'A name is required.', { name: 'Too short' });
        const v = makeVenue(nextId('ven_'), { ...body, lat: body.geo ? body.geo.lat : null, lng: body.geo ? body.geo.lng : null, geo: body.geo ? { ...body.geo, precision: 'Exact', source: 'Manual', geocodedUtc: null } : null, createdUtc: now() });
        if (!v.geo) v.geo = { lat: null, lng: null, precision: 'None', source: 'None', geocodedUtc: null };
        state.venues.push(v);
        return ok(adminVenue(v), 201);
      }
      const v = findVenue(parts[2]);
      if (!v) return error(404, 'NotFound', 'Not found.');
      if (!parts[3]) {
        if (method === 'GET') return ok(adminVenue(v));
        if (method === 'PUT') {
          const { geo, ...rest } = body;
          Object.assign(v, rest, { updatedUtc: now() });
          if (geo) v.geo = { lat: geo.lat, lng: geo.lng, precision: 'Exact', source: 'Manual', geocodedUtc: null };
          return ok(adminVenue(v));
        }
        if (method === 'DELETE') {
          v.deletedUtc = now();
          state.links = state.links.filter((l) => l.venueId !== v.id);
          return noContent();
        }
      }
      const actions = { verify: () => { v.isVerified = true; v.verifiedUtc = now(); }, unverify: () => { v.isVerified = false; v.verifiedUtc = null; }, close: () => { v.status = 'Closed'; }, reopen: () => { v.status = 'Active'; }, archive: () => { v.status = 'Archived'; }, unarchive: () => { v.status = 'Active'; } };
      if (actions[parts[3]] && method === 'POST') {
        actions[parts[3]]();
        return ok(adminVenue(v));
      }
      if (parts[3] === 'instructors' && parts[4]) {
        const i = findInstructor(parts[4]);
        if (!i) return error(404, 'NotFound', 'Not found.');
        if (method === 'PUT') {
          const existing = state.links.find((l) => l.venueId === v.id && l.instructorId === i.id);
          if (existing) existing.role = body.role || existing.role;
          else state.links.push({ id: nextId('vin_'), venueId: v.id, instructorId: i.id, role: body.role || 'Regular' });
          return ok({ venueId: v.id, instructorId: i.id, role: body.role || 'Regular' });
        }
        if (method === 'DELETE') {
          state.links = state.links.filter((l) => !(l.venueId === v.id && l.instructorId === i.id));
          return noContent();
        }
      }
      return undefined;
    }
    if (parts[1] === 'instructors') {
      if (parts[2] === 'enumerate') {
        const result = page(filterInstructors(state.instructors, { ...body, filters: { status: ['Active', 'Archived'], ...(body.filters || {}) } }), body, { max: 500 });
        result.objects = result.objects.map((o) => presentInstructor(findInstructor(o.id), { staff: true }));
        return ok(result);
      }
      if (!parts[2] && method === 'POST') {
        if (!body.displayName || String(body.displayName).trim().length < 2) return error(400, 'ValidationFailed', 'A name is required.', { displayName: 'Too short' });
        const i = makeInstructor(nextId('ins_'), { ...body, createdUtc: now() });
        state.instructors.push(i);
        return ok(presentInstructor(i, { staff: true }), 201);
      }
      const i = findInstructor(parts[2]);
      if (!i) return error(404, 'NotFound', 'Not found.');
      if (!parts[3]) {
        if (method === 'GET') return ok(presentInstructor(i, { staff: true }));
        if (method === 'PUT') {
          Object.assign(i, body, { updatedUtc: now() });
          return ok(presentInstructor(i, { staff: true }));
        }
        if (method === 'DELETE') {
          i.deletedUtc = now();
          return noContent();
        }
      }
      if (parts[3] === 'link' && method === 'POST') {
        if (i.linkedUser) return error(409, 'AlreadyClaimed', 'This entry is already linked.');
        i.linkedUser = fakeUser(body.userId, 'Member ' + body.userId);
        return ok(presentInstructor(i, { staff: true }));
      }
      if (parts[3] === 'unlink' && method === 'POST') {
        i.linkedUser = null;
        return ok(presentInstructor(i, { staff: true }));
      }
    }
    return undefined;
  }

  function handlePublic(method, parts, body) {
    const visible = state.venues.filter((v) => v.audience === 'Public' && v.status === 'Active' && !v.deletedUtc);
    const strip = (v) => {
      const { viewer: _v, email: _e, phone: _p, ...rest } = venueSummary(v);
      return rest;
    };
    if (parts[1] === 'venues') {
      if (parts[2] === 'enumerate') {
        const { list } = filterVenues(visible, body);
        const result = page(list, body);
        result.objects = result.objects.map((o) => strip(findVenue(o.id)));
        return ok(result);
      }
      const v = visible.find((x) => x.id === parts[2]);
      return v ? ok(strip(v)) : error(404, 'NotFound', 'Not found.');
    }
    if (parts[1] === 'directory' && parts[2] === 'map') return mapFeatures({ ...body, filters: { ...(body.filters || {}), audience: ['Public'] } });
    return undefined;
  }

  /**
   * Answer one request.
   * @param {{ method: string, path: string, query?: object, body?: any }} call
   */
  function handle({ method, path, query = {}, body }) {
    const parts = splitPath(path);
    const m = String(method || 'GET').toUpperCase();
    const b = body && typeof body === 'object' ? body : {};
    const known = ['venues', 'instructors', 'directory'].includes(parts[0]) || (parts[0] === 'me' && (parts[1] === 'directory' || parts[1] === 'instructor-links'))
      || (parts[0] === 'public' && (parts[1] === 'venues' || parts[1] === 'directory')) || (parts[0] === 'admin' && ['venues', 'instructors', 'directory'].includes(parts[1]));
    if (!known) return undefined;
    calls.push({ method: m, path: '/' + parts.join('/'), query, body: b });
    if (state.killed && parts[0] !== 'admin' && parts[0] !== 'public') return error(503, 'FeatureDisabled', 'The directory is temporarily unavailable.');
    if (parts[0] === 'admin') return handleAdmin(m, parts, b);
    if (parts[0] === 'public') return handlePublic(m, parts, b);
    if (parts[0] === 'venues') {
      if (parts[1] === 'enumerate') {
        const f = b.filters || {};
        if (f.near && f.bbox) return error(400, 'InvalidGeo', 'Use near or bbox, not both.');
        if (f.near && (Math.abs(Number(f.near.lat)) > 90 || Math.abs(Number(f.near.lng)) > 180)) return error(400, 'InvalidGeo', 'That position is not valid.');
        const { list, distances } = filterVenues(state.venues, b);
        const result = page(list, b);
        result.objects = result.objects.map((o) => venueSummary(findVenue(o.id), { distanceKm: distances[o.id] !== undefined ? Math.round(distances[o.id] * 10) / 10 : null }));
        return ok(result);
      }
      const v = findVenue(parts[1]);
      if (!v || (v.status === 'Archived')) return error(404, 'NotFound', 'Not found.');
      if (!parts[2]) return ok({ ...presentVenue(v), reviews: reviewsBlock(v) });
      if (parts[2] === 'instructors' && parts[3] === 'enumerate') return ok(page(instructorsOf(v.id).filter(canOpen).map(instructorSummary), b));
      if (parts[2] === 'reviews') return handleReviews(m, parts, b, v);
      if (parts[2] === 'media') return handleMedia(m, parts, b, v);
      return undefined;
    }
    if (parts[0] === 'instructors') {
      if (parts[1] === 'enumerate') return ok(page(filterInstructors(state.instructors, b).map(instructorSummary), b));
      const i = findInstructor(parts[1]);
      if (!i) return error(404, 'NotFound', 'Not found.');
      if (!parts[2]) return ok(presentInstructor(i));
      if (parts[2] === 'venues' && parts[3] === 'enumerate') return ok(page(venuesOf(i.id).filter(canOpen).map((v) => venueSummary(v)), b));
      return undefined;
    }
    if (parts[0] === 'directory') {
      if (parts[1] === 'map' && parts[2] === 'enumerate') return mapFeatures(b);
      if (parts[1] === 'geocode') {
        if (!state.geocoderConfigured) return error(503, 'IntegrationNotConfigured', 'Place search is not configured.');
        const q = String(b.filters?.query || '').trim();
        if (q.length < 2) return error(400, 'ValidationFailed', 'Type at least 2 characters.', { query: 'Too short' });
        return ok(page(state.geocodeCandidates.filter((c) => c.label.toLowerCase().includes(q.toLowerCase())).slice(0, 5).map((candidate, rank) => ({ ...candidate, id: 'candidate_' + rank, rank, labels: [], tags: {} })), b));
      }
      if (parts[1] === 'programs' && parts[2] === 'enumerate') return ok(page(state.programs.filter((p) => !p.isRetired), b));
      if (parts[1] === 'submissions') {
        if (!parts[2] && m === 'POST') return submit(b);
        const s = state.submissions.find((x) => x.id === parts[2] && x.submitter.id === state.me.id);
        if (!s) return error(404, 'NotFound', 'Not found.');
        if (parts[3] === 'withdraw' && m === 'POST') {
          if (s.status !== 'Pending') return error(409, 'Conflict', 'Only pending submissions can be withdrawn.');
          s.status = 'Withdrawn';
          return ok(presentSubmission(s));
        }
      }
      return undefined;
    }
    if (parts[0] === 'me') {
      if (parts[1] === 'directory' && parts[2] === 'submissions' && parts[3] === 'enumerate') {
        const f = b.filters || {};
        let list = state.submissions.filter((s) => s.submitter.id === state.me.id);
        if (f.status) list = list.filter((s) => asList(f.status).includes(s.status));
        if (f.type) list = list.filter((s) => asList(f.type).includes(s.type));
        return ok(page(list.sort((a, c) => String(c.createdUtc).localeCompare(String(a.createdUtc))).map(presentSubmission), b));
      }
      if (parts[1] === 'instructor-links' && parts[3] === 'unlink' && m === 'POST') {
        const i = findInstructor(parts[2]);
        if (!i || !i.linkedUser || i.linkedUser.id !== state.me.id) return error(404, 'NotFound', 'Not found.');
        i.linkedUser = null;
        return noContent();
      }
    }
    return undefined;
  }

  return { state, calls, handle, fetchImpl: fetchOver(handle), venue: findVenue, instructor: findInstructor };
}
