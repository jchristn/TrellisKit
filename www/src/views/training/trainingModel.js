/**
 * Pure helpers for the website's Training hub (docs/TRELLISKIT.md, Training hub): member app paths,
 * the overview's section contract (`GET /me/training`), Library segments, and save target paths.
 * Nothing here names a property; everything comes from the property configuration and the server.
 */
import { catalogAccess, catalogFeatureEnabled, propertyFeatureEnabled, ranksFeatureEnabled } from '@trelliskit/shared';

/** Sections this website has views for, in display order. Unknown keys from the server are ignored. */
export const TrainingSections = Object.freeze(['Overview', 'Library', 'Schedule', 'Programs', 'Progress']);

/** Library segments (URL keys), in display order. */
export const LibrarySegments = Object.freeze(['courses', 'techniques', 'saved']);

/** Server segment names (`sections[].segments`) per URL key. */
export const SegmentNames = Object.freeze({ courses: 'Courses', techniques: 'Techniques', saved: 'Saved' });

/** Member app paths of the Training hub. */
export const trainingPaths = Object.freeze({
  overview: '/app/training',
  library: '/app/training/library',
  segment: (segment) => '/app/training/library/' + segment,
  schedule: '/app/training/schedule',
  progress: '/app/training/progress',
  curricula: '/app/training/curricula',
  curriculum: (id) => '/app/training/curricula/' + encodeURIComponent(id),
  series: (idOrSlug) => '/app/training/series/' + encodeURIComponent(idOrSlug),
  lesson: (idOrSlug) => '/app/training/lessons/' + encodeURIComponent(idOrSlug),
  catalog: (idOrSlug) => '/app/training/catalog/' + encodeURIComponent(idOrSlug)
});

/** The path of a section tab. */
export function sectionPath(key) {
  switch (key) {
    case 'Library': return trainingPaths.segment('courses');
    case 'Schedule': return trainingPaths.schedule;
    case 'Programs': return trainingPaths.curricula;
    case 'Progress': return trainingPaths.progress;
    default: return trainingPaths.overview;
  }
}

/** The section a member app path belongs to (`null` outside the hub's tabbed pages). */
export function sectionOfPath(pathname) {
  const path = String(pathname || '').replace(/\/+$/, '');
  if (path === trainingPaths.overview) return 'Overview';
  if (path.startsWith(trainingPaths.library)) return 'Library';
  if (path === trainingPaths.schedule) return 'Schedule';
  if (path === trainingPaths.curricula) return 'Programs';
  if (path === trainingPaths.progress) return 'Progress';
  return null;
}

/**
 * Whether the property runs the Training hub on the website: `features.training` and the library
 * are on. The `/app/library`, `/app/series`, `/app/lessons`, and `/app/catalog` addresses redirect
 * into the hub only then; otherwise they keep working on their own.
 */
export function trainingHubEnabled(property) {
  return propertyFeatureEnabled(property, 'training') && propertyFeatureEnabled(property, 'library');
}

const emptyPage = () => ({ objects: [], totalRecords: 0, endOfResults: true, continuationToken: null });

/** An EnumerationResult with a clean `objects` array (missing or malformed lists become empty pages). */
export function pageOf(value) {
  if (!value || typeof value !== 'object') return emptyPage();
  const objects = Array.isArray(value.objects) ? value.objects.filter(Boolean) : [];
  return { ...emptyPage(), ...value, objects };
}

/**
 * Normalize a `GET /me/training` response: well-formed sections only, every list an
 * EnumerationResult, completed lessons out of Continue training, and `rank` and `coach` null unless
 * present.
 * @param {object} raw
 */
export function normalizeOverview(raw) {
  const value = raw && typeof raw === 'object' ? raw : {};
  const sections = Array.isArray(value.sections)
    ? value.sections.filter((section) => section && typeof section.key === 'string').map((section) => ({
      key: section.key,
      available: section.available === true,
      reason: section.reason || null,
      segments: Array.isArray(section.segments) ? section.segments.filter((segment) => typeof segment === 'string') : null
    }))
    : [];
  const continuePage = pageOf(value.continue);
  return {
    sections,
    upNext: pageOf(value.upNext),
    continue: { ...continuePage, objects: continuePage.objects.filter((row) => row.lesson && !(row.progress && row.progress.completed)) },
    recentSaves: pageOf(value.recentSaves),
    newForYou: pageOf(value.newForYou),
    rank: value.rank && typeof value.rank === 'object' ? value.rank : null,
    coach: value.coach && value.coach.available === true ? { available: true, placement: value.coach.placement || null } : null,
    firstRun: value.firstRun === true,
    labels: Array.isArray(value.labels) ? value.labels : [],
    tags: value.tags && typeof value.tags === 'object' ? value.tags : {}
  };
}

/** Available sections this website has views for, in display order. */
export function visibleSections(overview) {
  const available = new Set(((overview && overview.sections) || []).filter((section) => section.available).map((section) => section.key));
  return TrainingSections.filter((key) => available.has(key));
}

/** Whether the library is on and open to this caller (signed-out visitors need anonymous browsing). */
export function libraryOpen(property, me) {
  return propertyFeatureEnabled(property, 'library') && catalogAccess(property, me || null, 'library', 'read').allowed;
}

/**
 * Library segments for this caller. Techniques needs `features.catalog` and, when the server
 * reports the Library's segments, its `Techniques` segment (the catalog may be on before any entry
 * is published). Saved needs a signed-in member, `features.saves`, and the server's `Saved` segment
 * when segments are reported.
 * @param {{ property: object, me: object|null, overview?: object|null }} options
 */
export function librarySegmentsFor({ property, me, overview = null }) {
  const library = overview && overview.sections ? overview.sections.find((section) => section.key === 'Library') : null;
  const reported = library && Array.isArray(library.segments) ? library.segments : null;
  const listed = (key) => !reported || reported.includes(SegmentNames[key]);
  return LibrarySegments.filter((key) => {
    if (key === 'courses') return true;
    if (key === 'techniques') return catalogFeatureEnabled(property) && listed(key) && (Boolean(me) || catalogAccess(property, null, 'library', 'read').allowed);
    return Boolean(me) && savesEnabled(property) && listed(key);
  });
}

/** Whether saves are on (`features.saves`; a missing switch counts as on). */
export function savesEnabled(property) {
  return propertyFeatureEnabled(property, 'saves');
}

/** Whether the ranks Overview cards and Progress panels apply (`features.ranks`, opt-in). */
export function ranksEnabled(property) {
  return ranksFeatureEnabled(property);
}

/**
 * The parts of the overview's `rank` card: `{ rank, next, curriculum, showVerificationLabels, href }`.
 * Accepts `{ rank: EffectiveRankView, next, curriculum }` or a bare EffectiveRankView.
 */
export function rankCardOf(card) {
  if (!card || typeof card !== 'object') return null;
  const rank = card.rank && typeof card.rank === 'object' ? card.rank : card.fieldKey || card.optionKey ? card : null;
  if (!rank) return null;
  return {
    rank,
    next: card.next || null,
    curriculum: card.curriculum && card.curriculum.id ? card.curriculum : null,
    showVerificationLabels: card.showVerificationLabels !== false
  };
}

/** Member app path of a save target, or null for an unknown type. */
export function saveTargetPath(targetType, targetId) {
  if (!targetId) return null;
  const id = encodeURIComponent(targetId);
  switch (targetType) {
    case 'Lesson': return trainingPaths.lesson(targetId);
    case 'Series': return trainingPaths.series(targetId);
    case 'CatalogEntry': return trainingPaths.catalog(targetId);
    case 'Venue': return '/app/venues/' + id;
    case 'Instructor': return '/app/instructors/' + id;
    case 'Event': return '/app/events/' + id;
    case 'Post': return '/app/feed/' + id;
    case 'Thread': return '/app/threads/' + id;
    default: return null;
  }
}

/** `m:ss` (or `h:mm:ss`) for a number of seconds. */
export function formatClock(seconds) {
  const total = Math.max(0, Math.floor(Number(seconds) || 0));
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = String(total % 60).padStart(2, '0');
  return h > 0 ? h + ':' + String(m).padStart(2, '0') + ':' + s : m + ':' + s;
}

/** Seconds from `m:ss`, `h:mm:ss`, or a plain number; `null` for blank; `NaN` when malformed. */
export function parseClock(text) {
  const value = String(text ?? '').trim();
  if (!value) return null;
  if (/^\d+$/.test(value)) return Number(value);
  if (!/^\d+(:[0-5]\d){1,2}$/.test(value)) return NaN;
  return value.split(':').reduce((sum, part) => sum * 60 + Number(part), 0);
}

/**
 * Member app paths of library objects for this property: inside the Training hub when it is on,
 * the standalone library addresses otherwise.
 */
export function libraryPathsFor(property) {
  if (trainingHubEnabled(property)) return { library: trainingPaths.segment('courses'), techniques: trainingPaths.segment('techniques'), series: trainingPaths.series, lesson: trainingPaths.lesson, catalog: trainingPaths.catalog };
  const enc = encodeURIComponent;
  return { library: '/app/library', techniques: '/app/catalog', series: (id) => '/app/series/' + enc(id), lesson: (id) => '/app/lessons/' + enc(id), catalog: (id) => '/app/catalog/' + enc(id) };
}
