/**
 * Directory helpers (docs/PHASE4_CONTRACT.md sections 0.2, 2.1 to 2.4, 3): configured terminology,
 * coordinate rounding for privacy, address formatting by visibility, the map configuration, and
 * program lookups. Nothing here names a property: labels, programs, and the map come from
 * `GET /api/v1/property` (`directory`).
 */

import { formatTemplate } from './property.js';
import { validateVenueSchedule } from './venueSchedule.js';

/** Engine default labels (section 0.2). */
export const DEFAULT_DIRECTORY_TERMS = Object.freeze({ venue: 'Venue', venues: 'Venues', instructor: 'Instructor', instructors: 'Instructors' });

/** Address visibility values (section 2.1). */
export const AddressVisibility = Object.freeze({ Exact: 'Exact', Approximate: 'Approximate', LocalityOnly: 'LocalityOnly' });
/** Every address visibility, in the editor's order. */
export const AddressVisibilities = Object.freeze(Object.values(AddressVisibility));
/** Visitor policies (section 2.2). */
export const VisitorPolicies = Object.freeze(['Welcome', 'ByArrangement', 'MembersOnly', 'Unknown']);
/** Venue statuses. */
export const VenueStatuses = Object.freeze(['Active', 'Closed', 'Archived']);
/** Social link kinds (fixed list). */
export const SocialLinkKinds = Object.freeze(['Website', 'Instagram', 'Facebook', 'YouTube', 'TikTok', 'X', 'Other']);
/** Submission types (section 2.4). */
export const SubmissionType = Object.freeze({
  NewVenue: 'NewVenue', EditVenue: 'EditVenue', NewInstructor: 'NewInstructor', EditInstructor: 'EditInstructor', ClaimInstructor: 'ClaimInstructor', ReportClosed: 'ReportClosed'
});
/** Every submission type. */
export const SubmissionTypes = Object.freeze(Object.values(SubmissionType));
/** Submission statuses (`Draft` only appears on a duplicate-check response). */
export const SubmissionStatuses = Object.freeze(['Pending', 'Approved', 'Rejected', 'Withdrawn', 'Superseded']);
/** Venue to instructor link roles. */
export const VenueInstructorRoles = Object.freeze(['Head', 'Regular', 'Guest']);
/** Venue orderings (section 3.2). */
export const VenueOrderings = Object.freeze(['NameAscending', 'DistanceAscending', 'CreatedDescending', 'CreatedAscending', 'UpdatedDescending', 'Relevance']);
/** Map providers (section 3.6). */
export const MapProvider = Object.freeze({ None: 'None', MapLibre: 'MapLibre', PlatformNative: 'PlatformNative' });

/** Field length rules a client checks before sending (sections 2.2, 2.3, 3.7). */
export const DIRECTORY_LIMITS = Object.freeze({
  nameMin: 2, nameMax: 120, affiliationMax: 120, summaryMax: 300, descriptionMax: 5000, phoneMax: 40, scheduleMax: 500,
  visitorPolicyNoteMax: 300, noteMax: 1000, claimNoteMin: 10, reviewNoteMin: 3, reviewNoteMax: 1000, line1Max: 200, line2Max: 200,
  localityMax: 100, regionMax: 100, postalCodeMax: 20, maxSocialLinks: 8, maxPhotos: 10, defaultRadiusKm: 50, maxRadiusKm: 500
});

function isObject(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function text(value, fallback) {
  const s = typeof value === 'string' ? value.trim() : '';
  return s || fallback;
}

/** The property's `directory` section with engine defaults for anything missing. */
export function directoryConfig(property) {
  const d = isObject(property) && isObject(property.directory) ? property.directory : {};
  const map = isObject(d.map) ? d.map : {};
  const center = isObject(map.defaultCenter) ? map.defaultCenter : {};
  return {
    introduction: text(d.introduction, null),
    listingNotice: text(d.listingNotice, null),
    venueLabel: text(d.venueLabel, DEFAULT_DIRECTORY_TERMS.venue),
    venueLabelPlural: text(d.venueLabelPlural, DEFAULT_DIRECTORY_TERMS.venues),
    instructorLabel: text(d.instructorLabel, DEFAULT_DIRECTORY_TERMS.instructor),
    instructorLabelPlural: text(d.instructorLabelPlural, DEFAULT_DIRECTORY_TERMS.instructors),
    programs: Array.isArray(d.programs) ? d.programs.filter((p) => p && p.key) : [],
    instructorRankFieldKey: d.instructorRankFieldKey || null,
    submissions: d.submissions !== false,
    claims: d.claims !== false,
    geocoderConfigured: d.geocoderConfigured === true,
    map: {
      provider: Object.values(MapProvider).includes(map.provider) ? map.provider : MapProvider.None,
      styleUrl: map.styleUrl || null,
      darkStyleUrl: map.darkStyleUrl || null,
      publicKey: map.publicKey || null,
      attribution: map.attribution || null,
      defaultCenter: { lat: Number(center.lat) || 0, lng: Number(center.lng) || 0 },
      defaultZoom: Number.isFinite(Number(map.defaultZoom)) && map.defaultZoom !== null ? Number(map.defaultZoom) : 2,
      maxZoom: Number.isFinite(Number(map.maxZoom)) && map.maxZoom !== null ? Number(map.maxZoom) : 18,
      maxFeatures: Number(map.maxFeatures) > 0 ? Math.min(1000, Number(map.maxFeatures)) : 500
    }
  };
}

/**
 * The configured directory terms (section 0.2), for `{venue}`, `{venues}`, `{instructor}`, and
 * `{instructors}` placeholders, plus lowercase variants (`venueLower`, ...) for mid-sentence use.
 * @param {object} property
 */
export function directoryTerms(property) {
  const c = directoryConfig(property);
  const terms = { venue: c.venueLabel, venues: c.venueLabelPlural, instructor: c.instructorLabel, instructors: c.instructorLabelPlural };
  return { ...terms, venueLower: terms.venue.toLowerCase(), venuesLower: terms.venues.toLowerCase(), instructorLower: terms.instructor.toLowerCase(), instructorsLower: terms.instructors.toLowerCase() };
}

/**
 * Fill `{venue}`, `{venues}`, `{instructor}`, `{instructors}` (and the `...Lower` variants) plus any
 * extra values in a template. Unknown placeholders stay as written.
 * @param {string} template
 * @param {object} propertyOrTerms a property, or the result of `directoryTerms`
 * @param {Record<string, any>} [extra]
 */
export function fillDirectory(template, propertyOrTerms, extra = {}) {
  const terms = propertyOrTerms && typeof propertyOrTerms.venue === 'string' && typeof propertyOrTerms.instructors === 'string' ? propertyOrTerms : directoryTerms(propertyOrTerms);
  return formatTemplate(template, { ...terms, ...extra });
}

/**
 * Round a coordinate to `places` decimal places (2 is about 1 km, the precision clients send for
 * "near me"; 5 is about 1 m). Non-numbers return null.
 * @param {number|string} value
 * @param {number} [places]
 */
export function roundCoordinate(value, places = 2) {
  const n = Number(value);
  if (value === null || value === undefined || value === '' || !Number.isFinite(n)) return null;
  const p = Math.max(0, Math.min(8, Math.floor(Number(places) || 0)));
  const factor = 10 ** p;
  const rounded = Math.round(n * factor) / factor;
  return Object.is(rounded, -0) ? 0 : rounded;
}

/**
 * A `near` filter safe to send: lat and lng rounded to 2 places, `radiusKm` clamped to 0.1 to the
 * maximum (default 50).
 * @param {{ lat: number, lng: number, radiusKm?: number }} near
 * @param {{ maxRadiusKm?: number }} [limits]
 */
export function roundNear(near, { maxRadiusKm = DIRECTORY_LIMITS.maxRadiusKm } = {}) {
  if (!isObject(near)) return near;
  const out = { lat: roundCoordinate(near.lat, 2), lng: roundCoordinate(near.lng, 2) };
  if (near.radiusKm !== undefined && near.radiusKm !== null && near.radiusKm !== '') {
    const r = Number(near.radiusKm);
    out.radiusKm = Number.isFinite(r) ? Math.min(maxRadiusKm, Math.max(0.1, r)) : DIRECTORY_LIMITS.defaultRadiusKm;
  }
  return out;
}

/** Whether a position is valid WGS 84 (latitude -90 to 90, longitude -180 to 180). */
export function isValidPosition(lat, lng) {
  const a = Number(lat);
  const b = Number(lng);
  return lat !== null && lng !== null && lat !== '' && lng !== '' && Number.isFinite(a) && Number.isFinite(b) && a >= -90 && a <= 90 && b >= -180 && b <= 180;
}

/** Great-circle distance in kilometers (the server's Haversine formula, section 3.4). */
export function haversineKm(a, b) {
  const rad = (d) => (Number(d) * Math.PI) / 180;
  const dLat = rad(b.lat - a.lat);
  const dLng = rad(b.lng - a.lng);
  const h = Math.sin(dLat / 2) ** 2 + Math.cos(rad(a.lat)) * Math.cos(rad(b.lat)) * Math.sin(dLng / 2) ** 2;
  return 6371.0088 * 2 * Math.asin(Math.min(1, Math.sqrt(h)));
}

/** "2.4 km" or "850 m" (or "12 km" above 10 km); empty for a missing value. */
export function formatDistance(km) {
  const n = Number(km);
  if (km === null || km === undefined || !Number.isFinite(n)) return '';
  if (n < 1) return Math.max(10, Math.round((n * 1000) / 10) * 10) + ' m';
  if (n < 10) return (Math.round(n * 10) / 10).toFixed(1) + ' km';
  return Math.round(n) + ' km';
}

/**
 * Display text for an address respecting its visibility (section 2.1): `Exact` shows the full
 * address (`formatted` when the server sent it); `Approximate` and `LocalityOnly` show locality,
 * region, and country only. Staff (`isStaff`) always see the full address.
 * @param {object|null} address
 * @param {string} [visibility]
 * @param {{ isStaff?: boolean, includeCountry?: boolean }} [options]
 */
export function formatAddress(address, visibility = AddressVisibility.Exact, { isStaff = false, includeCountry = true } = {}) {
  if (!isObject(address)) return '';
  const exact = isStaff || visibility === AddressVisibility.Exact || !visibility;
  const regionLine = [address.region, address.postalCode && exact ? address.postalCode : null].filter(Boolean).join(' ');
  if (exact) {
    if (address.formatted && !isStaff) return address.formatted;
    const parts = [address.line1, address.line2, address.locality, regionLine, includeCountry ? address.countryCode : null].filter((x) => x && String(x).trim());
    return parts.join(', ') || address.formatted || '';
  }
  return [address.locality, address.region, includeCountry ? address.countryCode : null].filter((x) => x && String(x).trim()).join(', ');
}

/** The locality line of an address ("Exampleville, EX"). */
export function localityLine(address) {
  if (!isObject(address)) return '';
  return [address.locality, address.region].filter(Boolean).join(', ');
}

/** An empty address for an editor. */
export function emptyAddress(countryCode = '') {
  return { line1: '', line2: '', locality: '', region: '', postalCode: '', countryCode };
}

/** Whether a venue can appear as a map point (a position and not `LocalityOnly`). */
export function venueHasPin(venue) {
  const geo = venue && venue.geo;
  return Boolean(geo && geo.precision !== 'None' && isValidPosition(geo.lat, geo.lng) && venue.addressVisibility !== AddressVisibility.LocalityOnly);
}

/** Configured programs `{ key, label }`. */
export function programOptions(property) {
  return directoryConfig(property).programs.map((p) => ({ key: p.key, label: p.label || p.key }));
}

/** Label of a program key (the key itself when unknown, for example a retired program). */
export function programLabel(property, key) {
  const found = programOptions(property).find((p) => p.key === key);
  return found ? found.label : String(key || '');
}

/** Labels of a venue's `programs` (objects or keys). */
export function programLabels(property, programs) {
  return (programs || []).map((p) => (isObject(p) ? p.label || programLabel(property, p.key) : programLabel(property, p)));
}

/**
 * The map style to load on the website, or null for the list view: `MapLibre` with a style, or
 * `PlatformNative` with a style (the website falls back to MapLibre then, section 3.6).
 * @param {object} property
 * @param {{ dark?: boolean }} [options]
 */
export function webMapStyle(property, { dark = false } = {}) {
  const map = directoryConfig(property).map;
  if (map.provider === MapProvider.None || !map.styleUrl) return null;
  return dark && map.darkStyleUrl ? map.darkStyleUrl : map.styleUrl;
}

/**
 * The map attribution setting (plain text or Markdown links) as safe HTML for a map's attribution
 * control: text is escaped, and `[label](https://...)` becomes a link. Returns '' for no text.
 * @param {string|null|undefined} text
 * @returns {string}
 */
export function mapAttributionHtml(text) {
  const source = typeof text === 'string' ? text.trim() : '';
  if (!source) return '';
  const escape = (s) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
  let out = '';
  let last = 0;
  const link = /\[([^\]\n]{1,200})\]\((https?:\/\/[^\s)]{1,500})\)/g;
  for (let m = link.exec(source); m; m = link.exec(source)) {
    out += escape(source.slice(last, m.index)) + '<a href="' + escape(m[2]) + '" target="_blank" rel="noopener noreferrer">' + escape(m[1]) + '</a>';
    last = m.index + m[0].length;
  }
  return out + escape(source.slice(last));
}

const SCHEDULE_DAYS = ['mon', 'tue', 'wed', 'thu', 'fri', 'sat', 'sun'];
const SCHEDULE_ENTRY = /^(mon|tue|wed|thu|fri|sat|sun)[a-z]*\.?\s+(\d{1,2}):(\d{2})\s*(?:-|to)\s*(\d{1,2}):(\d{2})$/i;

/**
 * A venue's schedule text as structure for display. A first line of the form
 * `Hours: Mon 05:00-22:00; Tue 05:00-22:00; ...` (what discovery writes) becomes opening hours,
 * with consecutive days that share hours merged into one row; every other non-empty line is a
 * note. Text in any other shape is returned as notes only, so nothing is ever lost.
 * @param {string|null|undefined} text
 * @returns {{ hours: { from: number, to: number, open: string, close: string, allDay: boolean }[], notes: string[] }}
 *   `from` and `to` are day indexes (0 Monday to 6 Sunday); `open` and `close` are `HH:mm`.
 */
export function parseScheduleText(text) {
  const lines = String(text || '').split(/\r?\n/).map((l) => l.trim()).filter(Boolean);
  const result = { hours: [], notes: lines };
  const first = lines.length ? /^hours\s*:\s*(.+)$/i.exec(lines[0]) : null;
  if (!first) return result;
  const days = [];
  for (const part of first[1].split(';').map((p) => p.trim()).filter(Boolean)) {
    const m = SCHEDULE_ENTRY.exec(part);
    if (!m) return result;
    const hour = (h, min) => (Number(h) > 24 || Number(min) > 59 || (Number(h) === 24 && Number(min) !== 0) ? null : String(Number(h)).padStart(2, '0') + ':' + min);
    const open = hour(m[2], m[3]);
    const close = hour(m[4], m[5]);
    const day = SCHEDULE_DAYS.indexOf(m[1].toLowerCase());
    if (!open || !close || days.some((d) => d.day === day)) return result;
    days.push({ day, open, close });
  }
  if (!days.length) return result;
  days.sort((a, b) => a.day - b.day);
  const hours = [];
  for (const d of days) {
    const last = hours[hours.length - 1];
    if (last && last.to === d.day - 1 && last.open === d.open && last.close === d.close) last.to = d.day;
    else hours.push({ from: d.day, to: d.day, open: d.open, close: d.close, allDay: d.open === '00:00' && (d.close === '00:00' || d.close === '24:00' || d.close === '23:59') });
  }
  return { hours, notes: lines.slice(1) };
}

/**
 * `HH:mm` as a 12-hour clock time: `05:00` is `5 AM`, `17:30` is `5:30 PM`, `00:00` and `24:00` are `12 AM`.
 * @param {string} time
 * @returns {string}
 */
export function formatClockTime(time) {
  const m = /^(\d{1,2}):(\d{2})$/.exec(String(time || ''));
  if (!m) return String(time || '');
  const h = Number(m[1]) % 24;
  return (h % 12 === 0 ? 12 : h % 12) + (m[2] === '00' ? '' : ':' + m[2]) + (h < 12 ? ' AM' : ' PM');
}

/**
 * A venue description as structure for display. Lines that start with `Pricing:` and `Amenities:`
 * (what discovery writes) become a pricing text and a list of amenities; everything else stays as
 * the Markdown body.
 * @param {string|null|undefined} text
 * @returns {{ pricing: string|null, amenities: string[], body: string }}
 */
export function parseVenueDescription(text) {
  const out = { pricing: null, amenities: [], body: '' };
  const rest = [];
  for (const line of String(text || '').split(/\r?\n/)) {
    const pricing = /^\s*pricing\s*:\s*(.+)$/i.exec(line);
    const amenities = /^\s*amenities\s*:\s*(.+)$/i.exec(line);
    if (pricing && out.pricing === null) out.pricing = pricing[1].trim();
    else if (amenities && !out.amenities.length) out.amenities = amenities[1].split(',').map((a) => a.trim()).filter(Boolean);
    else rest.push(line);
  }
  out.body = rest.join('\n').trim();
  return out;
}

/** The provider a mobile client should use: `MapLibre` (needs a style), `PlatformNative`, or `None` (list view). */
export function mobileMapProvider(property) {
  const map = directoryConfig(property).map;
  if (map.provider === MapProvider.PlatformNative) return MapProvider.PlatformNative;
  if (map.provider === MapProvider.MapLibre && map.styleUrl) return MapProvider.MapLibre;
  return MapProvider.None;
}

/** A bounding box from MapLibre-style bounds (`getSouth()` ...) or a plain object, rounded to 4 places. */
export function toBbox(bounds) {
  if (!bounds) return null;
  const read = (name) => (typeof bounds['get' + name] === 'function' ? bounds['get' + name]() : bounds[name.toLowerCase()]);
  const box = { south: read('South'), west: read('West'), north: read('North'), east: read('East') };
  if (![box.south, box.west, box.north, box.east].every((v) => Number.isFinite(Number(v)))) return null;
  const clampLat = (v) => Math.max(-90, Math.min(90, Number(v)));
  const wrapLng = (v) => {
    let x = Number(v);
    if (x > 180 || x < -180) x = ((((x + 180) % 360) + 360) % 360) - 180;
    return x;
  };
  return { south: roundCoordinate(clampLat(box.south), 4), west: roundCoordinate(wrapLng(box.west), 4), north: roundCoordinate(clampLat(box.north), 4), east: roundCoordinate(wrapLng(box.east), 4) };
}

/** The item of a map `Point` feature: its `venue` (directory map) or its `event` (events map), else null. */
export function mapFeatureItem(feature) {
  if (!feature || feature.kind !== 'Point') return null;
  return feature.venue || feature.event || null;
}

/** A GeoJSON FeatureCollection of map `Point` features, venues or events (clusters are left to the client's own clustering). */
export function mapFeaturesToGeoJson(features) {
  return {
    type: 'FeatureCollection',
    features: (features || []).map((f) => ({ f, item: mapFeatureItem(f) })).filter(({ item }) => item && item.geo && isValidPosition(item.geo.lat, item.geo.lng)).map(({ f, item }) => ({
      type: 'Feature',
      id: item.id,
      geometry: { type: 'Point', coordinates: [Number(item.geo.lng), Number(item.geo.lat)] },
      properties: { id: item.id, name: item.name || item.title || '', isVerified: Boolean(item.isVerified), kind: f.venue ? 'venue' : 'event' }
    }))
  };
}

/** Whether a position lies in a bounding box (west greater than east crosses the antimeridian). */
export function isInBox(lat, lng, box) {
  if (!box || !isValidPosition(lat, lng)) return false;
  const a = Number(lat);
  const b = Number(lng);
  if (a < box.south || a > box.north) return false;
  return box.west <= box.east ? b >= box.west && b <= box.east : b >= box.west || b <= box.east;
}

/**
 * A map viewport from its URL form `lat,lng,zoom` (the `at` parameter of the map searches), or null.
 * @param {string|null|undefined} text
 */
export function parseViewport(text) {
  const parts = String(text || '').split(',').map((x) => x.trim());
  if (parts.length !== 3 || parts.some((x) => x === '')) return null;
  const [lat, lng, zoom] = parts.map(Number);
  if (!isValidPosition(lat, lng) || !Number.isFinite(zoom) || zoom < 0 || zoom > 22) return null;
  return { lat, lng, zoom };
}

/** The URL form of a viewport: `lat,lng,zoom` with 4 decimals for the center and 2 for the zoom. */
export function formatViewport(viewport) {
  if (!viewport || !isValidPosition(viewport.lat, viewport.lng)) return '';
  return [roundCoordinate(viewport.lat, 4), roundCoordinate(viewport.lng, 4), roundCoordinate(Math.max(0, Math.min(22, Number(viewport.zoom) || 0)), 2)].join(',');
}

/**
 * A bounding box from its URL form `south,west,north,east` (the `bbox` parameter), or null when it is
 * not a valid box (the server's rules: south below north, west and east differ, all in range).
 * @param {string|null|undefined} text
 */
export function parseBbox(text) {
  const parts = String(text || '').split(',').map((x) => x.trim());
  if (parts.length !== 4 || parts.some((x) => x === '')) return null;
  const [south, west, north, east] = parts.map(Number);
  if (![south, west, north, east].every(Number.isFinite)) return null;
  if (south < -90 || north > 90 || south >= north || west < -180 || west > 180 || east < -180 || east > 180 || west === east) return null;
  return { south, west, north, east };
}

/** The URL form of a bounding box (`south,west,north,east`), or an empty string. */
export function formatBbox(box) {
  if (!box) return '';
  return [box.south, box.west, box.north, box.east].map((x) => roundCoordinate(x, 4)).join(',');
}

/**
 * Whether two boxes differ enough for "Search this area" to matter: an edge moved by more than
 * `tolerance` of the box size (default 5%), or exactly one box is missing.
 */
export function bboxChanged(a, b, tolerance = 0.05) {
  if (!a || !b) return Boolean(a || b);
  const height = Math.max(1e-6, Math.abs(a.north - a.south));
  const width = Math.max(1e-6, ((a.east - a.west + 360) % 360) || 360);
  const dLng = (x, y) => Math.abs(((((x - y + 540) % 360) + 360) % 360) - 180);
  return Math.abs(a.south - b.south) > height * tolerance || Math.abs(a.north - b.north) > height * tolerance || dLng(a.west, b.west) > width * tolerance || dLng(a.east, b.east) > width * tolerance;
}

/** App path of a venue (website prefixes `/app`). */
export function venuePath(idOrSlug) {
  return '/venues/' + encodeURIComponent(idOrSlug);
}

/** Review orderings (section 3.12): newest, oldest, highest, lowest. */
export const ReviewOrderings = Object.freeze(['CreatedDescending', 'CreatedAscending', 'RatingDescending', 'RatingAscending']);

/** Engine defaults of the review rules (`directory.reviewMinLength`, `directory.reviewMaxLength`, `directory.reviewReplyMaxLength`). */
export const REVIEW_LIMITS = Object.freeze({ minRating: 1, maxRating: 5, minLength: 10, maxLength: 2000, replyMaxLength: 2000, maxMediaPerAdd: 10 });

/**
 * The review and member media switches of the property's `directory` section (`reviews { enabled, minLength,
 * maxLength, replyMaxLength }`, `memberMedia`), with engine defaults for anything missing.
 * @param {object} property
 */
export function venueContributionConfig(property) {
  const d = isObject(property) && isObject(property.directory) ? property.directory : {};
  const r = isObject(d.reviews) ? d.reviews : {};
  const num = (value, fallback) => (Number.isFinite(Number(value)) && value !== null && value !== '' ? Number(value) : fallback);
  return {
    reviews: {
      enabled: r.enabled !== false,
      minLength: num(r.minLength, REVIEW_LIMITS.minLength),
      maxLength: num(r.maxLength, REVIEW_LIMITS.maxLength),
      replyMaxLength: num(r.replyMaxLength, REVIEW_LIMITS.replyMaxLength)
    },
    memberMedia: d.memberMedia !== false
  };
}

/**
 * Field errors of a review draft `{ rating, body }` against the configured lengths (empty when valid).
 * @param {{ rating?: number, body?: string }} draft
 * @param {{ minLength?: number, maxLength?: number }} [limits]
 * @returns {Record<string, string>} field to error code (`required`, `range`, `tooShort`, `tooLong`)
 */
export function validateReview(draft, limits = {}) {
  const errors = {};
  const min = Number.isFinite(limits.minLength) ? limits.minLength : REVIEW_LIMITS.minLength;
  const max = Number.isFinite(limits.maxLength) ? limits.maxLength : REVIEW_LIMITS.maxLength;
  const rating = Number(draft && draft.rating);
  if (!draft || draft.rating === undefined || draft.rating === null || draft.rating === '' || !Number.isFinite(rating) || rating === 0) errors.rating = 'required';
  else if (!Number.isInteger(rating) || rating < REVIEW_LIMITS.minRating || rating > REVIEW_LIMITS.maxRating) errors.rating = 'range';
  const body = typeof (draft && draft.body) === 'string' ? draft.body.trim() : '';
  if (body.length === 0 && min > 0) errors.body = 'required';
  else if (body.length < min) errors.body = 'tooShort';
  else if (body.length > max) errors.body = 'tooLong';
  return errors;
}

/**
 * Display text of an average rating with one decimal ("4.5"), or null when there are no ratings.
 * @param {number|null|undefined} average
 * @param {number} [count]
 */
export function formatRating(average, count) {
  if (count === 0 || average === null || average === undefined || !Number.isFinite(Number(average))) return null;
  return (Math.round(Number(average) * 10) / 10).toFixed(1);
}

/**
 * Rows of a rating distribution for a summary chart, highest first: `[{ stars, count, percent }]`.
 * @param {Record<string, number>|null|undefined} distribution keys "1" to "5"
 */
export function ratingDistributionRows(distribution) {
  const d = isObject(distribution) ? distribution : {};
  const counts = [5, 4, 3, 2, 1].map((stars) => ({ stars, count: Math.max(0, Number(d[String(stars)]) || 0) }));
  const total = counts.reduce((sum, row) => sum + row.count, 0);
  return counts.map((row) => ({ ...row, percent: total === 0 ? 0 : Math.round((row.count / total) * 100) }));
}

/** App path of an instructor. */
export function instructorPath(idOrSlug) {
  return '/instructors/' + encodeURIComponent(idOrSlug);
}

const VENUE_FIELDS = ['name', 'affiliation', 'summary', 'description', 'address', 'addressVisibility', 'phone', 'email', 'websiteUrl', 'socialLinks', 'scheduleText', 'schedule', 'programs', 'visitorPolicy', 'visitorPolicyNote', 'photoMediaIds'];
const INSTRUCTOR_FIELDS = ['displayName', 'bio', 'photoMediaId', 'rank', 'rankLevel', 'affiliation', 'websiteUrl', 'socialLinks', 'venueIds'];

/** The editable submission fields of a target type (section 3.7). */
export function submissionFields(type) {
  return type === SubmissionType.NewInstructor || type === SubmissionType.EditInstructor ? INSTRUCTOR_FIELDS.slice() : VENUE_FIELDS.slice();
}

function isHttps(url) {
  return /^https:\/\/[^\s/$.?#].[^\s]*$/i.test(String(url || ''));
}

/**
 * Validate a submission draft before sending (mirrors the server's main checks). Returns
 * `{ field: code }` with codes `required`, `tooShort`, `tooLong`, `https`, `email`, `countryRequired`.
 * @param {{ type: string, targetId?: string|null, fields?: object, note?: string }} draft
 */
export function validateSubmission(draft) {
  const problems = {};
  const type = draft && draft.type;
  const f = (draft && draft.fields) || {};
  const note = String((draft && draft.note) || '');
  if (!SubmissionTypes.includes(type)) {
    problems.type = 'required';
    return problems;
  }
  if ([SubmissionType.EditVenue, SubmissionType.EditInstructor, SubmissionType.ClaimInstructor, SubmissionType.ReportClosed].includes(type) && !draft.targetId) problems.targetId = 'required';
  if (note.length > DIRECTORY_LIMITS.noteMax) problems.note = 'tooLong';
  if (type === SubmissionType.ClaimInstructor && note.trim().length < DIRECTORY_LIMITS.claimNoteMin) problems.note = 'tooShort';
  if (type === SubmissionType.NewVenue || (type === SubmissionType.EditVenue && 'name' in f)) {
    const name = String(f.name || '').trim();
    if (!name) problems.name = 'required';
    else if (name.length < DIRECTORY_LIMITS.nameMin) problems.name = 'tooShort';
    else if (name.length > DIRECTORY_LIMITS.nameMax) problems.name = 'tooLong';
  }
  if (type === SubmissionType.NewInstructor || (type === SubmissionType.EditInstructor && 'displayName' in f)) {
    const name = String(f.displayName || '').trim();
    if (!name) problems.displayName = 'required';
    else if (name.length < DIRECTORY_LIMITS.nameMin) problems.displayName = 'tooShort';
    else if (name.length > DIRECTORY_LIMITS.nameMax) problems.displayName = 'tooLong';
  }
  if ('schedule' in f) Object.assign(problems, validateVenueSchedule(f.schedule));
  if (f.summary && String(f.summary).length > DIRECTORY_LIMITS.summaryMax) problems.summary = 'tooLong';
  if (f.websiteUrl && !isHttps(f.websiteUrl)) problems.websiteUrl = 'https';
  if (f.email && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(String(f.email))) problems.email = 'email';
  if (isObject(f.address)) {
    const a = f.address;
    const any = ['line1', 'line2', 'locality', 'region', 'postalCode'].some((k) => a[k] && String(a[k]).trim());
    if (any && !String(a.countryCode || '').trim()) problems['address.countryCode'] = 'countryRequired';
    if (type === SubmissionType.NewVenue && !String(a.locality || '').trim()) problems['address.locality'] = 'required';
  } else if (type === SubmissionType.NewVenue) problems['address.locality'] = 'required';
  if (Array.isArray(f.socialLinks)) {
    if (f.socialLinks.length > DIRECTORY_LIMITS.maxSocialLinks) problems.socialLinks = 'tooLong';
    else if (f.socialLinks.some((l) => !l || !isHttps(l.url))) problems.socialLinks = 'https';
  }
  return problems;
}

/**
 * The fields of an edit submission: only the fields that differ from the current entry (section
 * 3.7: "only changed fields are sent"). Arrays and objects compare by value.
 * @param {object} current the venue or instructor being edited
 * @param {object} edited the edited form values
 * @param {string[]} [keys] fields to consider
 */
export function changedFields(current, edited, keys = Object.keys(edited || {})) {
  const out = {};
  const norm = (v) => JSON.stringify(v === undefined ? null : v);
  for (const key of keys) {
    if (!(key in (edited || {}))) continue;
    let before = current ? current[key] : undefined;
    if (key === 'programs' && Array.isArray(before)) before = before.map((p) => (isObject(p) ? p.key : p));
    if (key === 'address' && isObject(before)) {
      const { formatted: _f, ...rest } = before;
      before = rest;
    }
    if (norm(before) !== norm(edited[key])) out[key] = edited[key];
  }
  return out;
}

/** Display text for one side of a submission change (`current` or `proposed`). */
export function formatChangeValue(value) {
  if (value === null || value === undefined || value === '') return '';
  if (Array.isArray(value)) return value.map(formatChangeValue).join(', ');
  if (isObject(value)) {
    if ('line1' in value || 'locality' in value) return formatAddress(value, AddressVisibility.Exact, { isStaff: true });
    if ('url' in value) return (value.kind ? value.kind + ': ' : '') + value.url;
    if ('lat' in value && 'lng' in value) return roundCoordinate(value.lat, 5) + ', ' + roundCoordinate(value.lng, 5);
    return JSON.stringify(value);
  }
  return String(value);
}
