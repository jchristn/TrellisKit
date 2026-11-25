/**
 * Training hub helpers for the mobile app (docs/TRELLISKIT.md section 15). Pure functions only:
 * the overview's shape, which sections and Library segments to show, the hub's routes, where a
 * saved item opens, how website Training links map to native screens, and the per-member overview
 * cache used offline. Nothing here names a property.
 */
import { catalogAccess, catalogFeatureEnabled, propertyFeatureEnabled } from '@trelliskit/shared';

/** Sections this build has screens for, in display order. Unknown or later sections are ignored. */
export const SupportedTrainingSections = Object.freeze(['Overview', 'Library', 'Schedule', 'Progress']);

/** Library segments, in display order (route values). */
export const LibrarySegments = Object.freeze(['courses', 'techniques', 'saved']);

/** Server segment names (`sections[].segments`) by route value. */
export const ServerSegmentNames = Object.freeze({ courses: 'Courses', techniques: 'Techniques', saved: 'Saved' });

/** How long a cached overview may stand in for the server while offline. */
export const OverviewCacheMaxAgeMs = 24 * 60 * 60 * 1000;

/** Routes inside the Training stack that count as the Training destination (the bar stays visible). */
export const TrainingSectionPaths = Object.freeze(['/training', '/training/library', '/training/library/courses', '/training/library/techniques', '/training/library/saved', '/training/saved', '/training/schedule', '/training/progress']);

const emptyPage = () => ({ objects: [], totalRecords: 0, endOfResults: true, continuationToken: null });

/** An EnumerationResult with an `objects` array, whatever arrived. */
export function pageOf(value) {
  if (!value || typeof value !== 'object') return emptyPage();
  const objects = Array.isArray(value.objects) ? value.objects.filter(Boolean) : Array.isArray(value) ? value.filter(Boolean) : [];
  return { ...emptyPage(), ...(Array.isArray(value) ? {} : value), objects };
}

/**
 * Normalize the rank card of an overview: `{ rank: EffectiveRankView, next: RankNextView|null,
 * curriculum: CurriculumView|null, showVerificationLabels }` (null when ranks are off). The
 * `GET /me/rank` shape (`fields: [{ rank, next }]`) and a `curricula` list are accepted too.
 * Returns null when there is nothing to show.
 * @param {object|null} raw
 * @returns {{ entries: { rank: object, next: object|null }[], curricula: object[], showVerificationLabels: boolean }|null}
 */
export function normalizeRankCard(raw) {
  if (!raw || typeof raw !== 'object') return null;
  const entries = Array.isArray(raw.fields)
    ? raw.fields.filter((entry) => entry && entry.rank).map((entry) => ({ rank: entry.rank, next: entry.next || null }))
    : raw.rank && typeof raw.rank === 'object' ? [{ rank: raw.rank, next: raw.next || null }] : [];
  const curricula = Array.isArray(raw.curricula) ? raw.curricula.filter((c) => c && c.id) : raw.curriculum && raw.curriculum.id ? [raw.curriculum] : [];
  if (!entries.length && !curricula.length) return null;
  return { entries, curricula, showVerificationLabels: raw.showVerificationLabels !== false };
}

/**
 * Normalize a `GET /me/training` response: every list is an EnumerationResult, missing cards are
 * empty, and `sections` keeps only well-formed entries.
 * @param {object} raw
 * @returns {object}
 */
export function normalizeOverview(raw) {
  const value = raw && typeof raw === 'object' ? raw : {};
  const sections = Array.isArray(value.sections)
    ? value.sections
      .filter((section) => section && typeof section.key === 'string')
      .map((section) => ({ key: section.key, available: section.available === true, reason: section.reason || null, segments: Array.isArray(section.segments) ? section.segments.filter((s) => typeof s === 'string') : null }))
    : [];
  const continuePage = pageOf(value.continue);
  return {
    sections,
    upNext: pageOf(value.upNext),
    continue: { ...continuePage, objects: continuePage.objects.filter((item) => item && item.lesson && !(item.progress && item.progress.completed)) },
    recentSaves: pageOf(value.recentSaves),
    newForYou: pageOf(value.newForYou),
    rank: normalizeRankCard(value.rank),
    coach: value.coach && value.coach.available === true ? { available: true, placement: value.coach.placement || null } : { available: false, placement: null },
    firstRun: value.firstRun === true,
    labels: Array.isArray(value.labels) ? value.labels : [],
    tags: value.tags && typeof value.tags === 'object' ? value.tags : {}
  };
}

/**
 * Sections the hub shows, in display order: only those the server reports available and this
 * build has screens for.
 * @param {object|null} overview Normalized overview.
 * @returns {string[]}
 */
export function visibleSections(overview) {
  const available = new Set(((overview && overview.sections) || []).filter((section) => section.available).map((section) => section.key));
  return SupportedTrainingSections.filter((key) => available.has(key));
}

/** Whether the property's Library is open to this caller (signed-out callers need anonymous browsing). */
export function libraryOpen(property, user) {
  return propertyFeatureEnabled(property, 'library') && catalogAccess(property, user || null, 'library', 'read').allowed;
}

/** Whether the technique catalog is on and open to this caller. */
export function techniquesOpen(property, user) {
  return catalogFeatureEnabled(property) && catalogAccess(property, user || null, 'catalog', 'read').allowed;
}

/** Whether saves are on for this caller (signed in and `features.saves` not off). */
export function savesOn(property, user) {
  return Boolean(user) && propertyFeatureEnabled(property, 'saves');
}

/**
 * Library segments for this caller, in display order. Signed in: Courses while the library is on,
 * Techniques only when the overview's Library section lists it, Saved while saves are on (and the
 * overview, when it lists segments, includes it). Signed out: Courses and, when the catalog is
 * public, Techniques; never Saved.
 * @param {{ overview?: object|null, property: object, user: object|null }} context
 * @returns {string[]}
 */
export function librarySegmentsFor({ overview = null, property, user }) {
  const out = [];
  if (libraryOpen(property, user)) out.push('courses');
  if (!user) {
    if (techniquesOpen(property, null)) out.push('techniques');
    return out;
  }
  const library = ((overview && overview.sections) || []).find((section) => section.key === 'Library');
  const listed = library && Array.isArray(library.segments) ? library.segments : null;
  if (listed && listed.includes(ServerSegmentNames.techniques) && techniquesOpen(property, user)) out.push('techniques');
  if (savesOn(property, user) && (!listed || listed.includes(ServerSegmentNames.saved))) out.push('saved');
  return out;
}

/**
 * Whether a destination (`library`, `catalog`) is covered by Training for this caller: Training is
 * placed and visible, and the destination itself is placed on no surface.
 * @param {object|null} appNavigation The app navigation context (`useAppNavigation()`).
 * @param {string} id Destination identifier.
 * @returns {boolean}
 */
export function coveredByTraining(appNavigation, id) {
  const labels = appNavigation && appNavigation.enabled ? appNavigation.labels || {} : null;
  return Boolean(labels && labels.training && !labels[id]);
}

/** The route of a hub section. */
export function sectionHref(key) {
  switch (key) {
    case 'Library': return '/training/library';
    case 'Schedule': return '/training/schedule';
    case 'Progress': return '/training/progress';
    default: return '/training';
  }
}

/** The route of a Library segment. */
export function segmentHref(segment) {
  return '/training/library/' + (LibrarySegments.includes(segment) ? segment : 'courses');
}

/** App route for a saved target, or null for an unknown type. */
export function saveTargetPath(targetType, targetId) {
  if (!targetId) return null;
  const id = encodeURIComponent(targetId);
  switch (targetType) {
    case 'Lesson': return '/lessons/' + id;
    case 'Series': return '/series/' + id;
    case 'CatalogEntry': return '/catalog/' + id;
    case 'Venue': return '/venues/' + id;
    case 'Instructor': return '/instructors/' + id;
    case 'Event': return '/events/' + id;
    case 'Post': return '/feed/' + id;
    case 'Thread': return '/threads/' + id;
    default: return null;
  }
}

/** App route for an Up next item (events today; other kinds when the app has their screens). */
export function upNextPath(item) {
  if (!item || !item.id) return null;
  if (!item.kind || item.kind === 'Event') return '/events/' + encodeURIComponent(item.id);
  return null;
}

const IDENTIFIER = /^[A-Za-z0-9_-]{1,128}$/;
const TRAINING_DETAIL = { series: '/series/', lessons: '/lessons/', catalog: '/catalog/', curricula: '/training/curricula/' };

/**
 * The native route for a website Training path (`/training...`, after the `/app` prefix is
 * removed), or null when it is not one. Section and segment routes are the same in the app; the
 * series, lesson, and catalog details under `/training/` open their own native routes, and detail
 * routes keep their query string (for example `?comment=`).
 * @param {string} pathname Path without a trailing slash.
 * @param {string} [search] Query string including `?`, or empty.
 * @returns {string|null}
 */
export function trainingWebPath(pathname, search = '') {
  if (TrainingSectionPaths.includes(pathname)) return pathname;
  const match = /^\/training\/(series|lessons|catalog|curricula)\/([^/]+)$/.exec(pathname || '');
  if (match && IDENTIFIER.test(match[2])) return TRAINING_DETAIL[match[1]] + match[2] + (search || '');
  return null;
}

function cacheKey(userId) {
  return 'training-overview-' + String(userId).replace(/[^A-Za-z0-9_-]/g, '_');
}

/**
 * The member's last overview, when it is at most 24 hours old.
 * @param {{ getItem: (key: string) => Promise<string|null> }} storage
 * @param {string} userId
 * @param {number} [now]
 * @returns {Promise<{ overview: object, savedUtc: string }|null>}
 */
export async function readCachedOverview(storage, userId, now = Date.now()) {
  if (!storage || !userId) return null;
  try {
    const raw = await storage.getItem(cacheKey(userId));
    if (!raw) return null;
    const entry = JSON.parse(raw);
    const saved = new Date(entry && entry.savedUtc).getTime();
    if (!entry || entry.userId !== userId || !Number.isFinite(saved) || now - saved > OverviewCacheMaxAgeMs || now < saved - 60000) return null;
    return { overview: normalizeOverview(entry.overview), savedUtc: entry.savedUtc };
  } catch {
    return null;
  }
}

/** Keep the member's latest overview for offline use. Failures are ignored. */
export async function writeCachedOverview(storage, userId, overview, now = Date.now()) {
  if (!storage || !userId || !overview) return;
  try {
    await storage.setItem(cacheKey(userId), JSON.stringify({ userId, savedUtc: new Date(now).toISOString(), overview }));
  } catch {
    // A full disk only costs the offline copy.
  }
}

/** Forget a member's cached overview (sign-out). */
export async function clearCachedOverview(storage, userId) {
  if (!storage || !userId || typeof storage.removeItem !== 'function') return;
  try {
    await storage.removeItem(cacheKey(userId));
  } catch {
    // Already gone.
  }
}
