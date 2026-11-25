import strings from '../../strings.js';
import { capabilityAvailable, featureEnabled } from '../member/memberRoutes.js';

const r = strings.admin.routes;
const g = strings.admin.groups;

/**
 * Admin route inventory. Drives the sidebar, route table, page titles, and placeholder views.
 * `comingSoon` marks sections whose backend has not shipped yet; `planned` lists what the page
 * will operate and `endpoint` names the missing backend capability. `feature` names the property
 * `features` flag that hides a section. `capability` names the server capability (a `features`
 * flag `GET /api/v1/property` must report as `true`) a section needs; until then it stays in the
 * sidebar and opens a coming-soon page that makes no API calls. `moderator` marks the sections moderators see (everything
 * else is SystemAdmin only); `forumModerator` marks the sections forum moderators also see
 * (PHASE3_CONTRACT.md section 11.5). `staffArea` marks the Phase 4 sections that their staff role
 * also sees (DirectoryModerator, EventManager, LibraryEditor). `supportStaff` marks the sections
 * SupportAgent holders see and `communicator` the ones Communicator holders see (PHASE5_CONTRACT.md
 * as-built note D). `releaseManager` marks the Releases sections ReleaseManager holders see (every one except
 * signing and deploy keys). `hiddenWithoutCapability` leaves an opt-in section (AI) out of the
 * sidebar until its capability is available.
 */
export const ADMIN_ROUTES = [
  { id: 'home', group: g.overview, icon: 'home', moderator: true, ...r.home },
  { id: 'metrics/health', group: strings.chartKit.title, icon: 'activity', label: strings.chartKit.health },

  { id: 'members', group: g.community, icon: 'users', ...r.members },
  { id: 'profile-fields', group: g.community, icon: 'user', ...r['profile-fields'] },
  { id: 'subscriptions', group: g.community, icon: 'card', ...r.subscriptions },
  { id: 'signup-codes', group: g.community, icon: 'key', feature: 'signupCodes', ...r['signup-codes'] },
  { id: 'feed', group: g.community, icon: 'feed', ...r.feed },
  { id: 'forums', group: g.community, icon: 'forum', capability: 'forums', endpoint: 'POST /api/v1/admin/forums/enumerate', ...r.forums },
  { id: 'threads', group: g.community, icon: 'message', capability: 'forums', endpoint: 'POST /api/v1/admin/threads/enumerate', forumModerator: true, ...r.threads },
  { id: 'trending', group: g.community, icon: 'trending', capability: 'forums', endpoint: 'POST /api/v1/admin/trending/enumerate', forumModerator: true, ...r.trending },
  { id: 'moderation', group: g.community, icon: 'shield', moderator: true, ...r.moderation },
  { id: 'content-review', group: g.community, icon: 'clipboard', endpoint: 'POST /api/v1/admin/content-review/enumerate', ...r['content-review'] },

  { id: 'media', group: g.content, icon: 'image', ...r.media },
  { id: 'library', group: g.content, icon: 'book', feature: 'library', staffArea: 'library', endpoint: 'POST /api/v1/admin/series/enumerate', ...r.library },
  { id: 'events', group: g.content, icon: 'calendar', feature: 'events', staffArea: 'events', endpoint: 'POST /api/v1/admin/events/enumerate', ...r.events },
  { id: 'directory', group: g.content, icon: 'pin', feature: 'directory', staffArea: 'directory', endpoint: 'POST /api/v1/admin/venues/enumerate', ...r.directory },
  { id: 'catalog', group: g.content, icon: 'book', feature: 'catalog', staffArea: 'library', endpoint: 'POST /api/v1/admin/catalog/entries/enumerate', label: strings.exerciseCatalog.nav, summary: strings.exerciseCatalog.adminSummary },
  { id: 'ranks', group: g.content, icon: 'trending', feature: 'ranks', staffArea: 'ranks', endpoint: 'POST /api/v1/admin/ranks/awards/enumerate', label: strings.ranks.admin.nav, summary: strings.ranks.admin.summary },
  { id: 'curricula', group: g.content, icon: 'list', feature: 'ranks', staffArea: ['library', 'ranks'], endpoint: 'POST /api/v1/admin/curricula/enumerate', label: strings.ranks.admin.curriculaNav, summary: strings.ranks.admin.curriculaSummary },
  { id: 'attendance', group: g.content, icon: 'calendar', feature: 'ranks', staffArea: 'ranks', endpoint: 'POST /api/v1/admin/attendance/enumerate', label: strings.ranks.admin.attendanceNav, summary: strings.ranks.admin.attendanceSummary },

  { id: 'support', group: g.engagement, icon: 'help', feature: 'support', supportStaff: true, ...r.support },
  { id: 'ideas', group: g.engagement, icon: 'trending', feature: 'ideas', supportStaff: true, ...r.ideas },
  { id: 'broadcasts', group: g.engagement, icon: 'megaphone', feature: 'broadcasts', communicator: true, ...r.broadcasts },

  { id: 'updates', group: g.releases, icon: 'upload', releaseManager: true, ...r.updates },
  { id: 'update-channels', group: g.releases, icon: 'list', releaseManager: true, ...r['update-channels'] },
  // Signing and deploy keys stay SystemAdmin only (no releaseManager).
  { id: 'update-keys', group: g.releases, icon: 'key', ...r['update-keys'] },
  { id: 'flags', group: g.releases, icon: 'flag', releaseManager: true, ...r.flags },
  { id: 'kill-switches', group: g.releases, icon: 'power', releaseManager: true, ...r['kill-switches'] },
  { id: 'strings', group: g.releases, icon: 'edit', releaseManager: true, ...strings.appContent.routes.strings },
  { id: 'banners', group: g.releases, icon: 'megaphone', releaseManager: true, ...strings.appContent.routes.banners },
  { id: 'assets', group: g.releases, icon: 'image', releaseManager: true, ...strings.appContent.routes.assets },
  { id: 'ai-settings', group: g.ai, icon: 'power', ...r['ai-settings'] },
  { id: 'ai-endpoints', group: g.ai, icon: 'link', capability: 'ai', hiddenWithoutCapability: true, endpoint: 'POST /api/v1/admin/ai/endpoints/enumerate', ...r['ai-endpoints'] },
  { id: 'ai-assistants', group: g.ai, icon: 'message', capability: 'ai', hiddenWithoutCapability: true, endpoint: 'POST /api/v1/admin/ai/assistants/enumerate', ...r['ai-assistants'] },
  { id: 'ai-conversations', group: g.ai, icon: 'list', capability: 'ai', hiddenWithoutCapability: true, endpoint: 'POST /api/v1/admin/ai/conversations/enumerate', ...r['ai-conversations'] },
  { id: 'ai-analytics', group: g.ai, icon: 'activity', capability: 'ai', hiddenWithoutCapability: true, endpoint: 'GET /api/v1/admin/ai/analytics/overview', ...r['ai-analytics'] },
  { id: 'ai-prompts', group: g.ai, icon: 'edit', endpoint: 'GET /api/v1/admin/ai/prompts', ...r['ai-prompts'] },
  { id: 'ai-roles', group: g.ai, icon: 'columns', endpoint: 'GET /api/v1/admin/ai/roles', ...r['ai-roles'] },

  { id: 'property', group: g.system, icon: 'globe', ...r.property },
  { id: 'branding', group: g.system, icon: 'image', ...r.branding },
  { id: 'documents', group: g.system, icon: 'clipboard', ...r.documents },
  { id: 'access', group: g.system, icon: 'lock', ...r.access },
  { id: 'integrations', group: g.system, icon: 'link', ...r.integrations },
  { id: 'email', group: g.system, icon: 'message', ...r.email },
  { id: 'requests', group: g.system, icon: 'list', ...r.requests },
  { id: 'explorer', group: g.system, icon: 'play', ...r.explorer },
  { id: 'settings', group: g.system, icon: 'gear', ...r.settings },
  { id: 'audit', group: g.system, icon: 'clipboard', ...r.audit },
  { id: 'observability', group: g.system, icon: 'activity', ...r.observability },
  { id: 'usage', group: g.system, icon: 'trending', ...r.usage },
  { id: 'jobs', group: g.system, icon: 'clock', endpoint: 'GET /api/v1/admin/jobs', ...r.jobs }
];

/** Placeholder sections (those whose backend has not shipped). */
export const PLACEHOLDER_ROUTES = ADMIN_ROUTES.filter((route) => route.comingSoon);

/** Whether an admin section opens its real page (not coming soon, capability available). */
export function adminRouteAvailable(property, route) {
  return Boolean(route) && !route.comingSoon && capabilityAvailable(property, route.capability);
}

/** True when the server reports the route's capability turned off for this property (not merely unreported). */
export function routeTurnedOff(property, route) {
  return Boolean(route) && !route.comingSoon && Boolean(route.capability) && Boolean(property && property.features) && property.features[route.capability] === false;
}

/** Find route metadata by id. */
export function metricAdminRoutes(pages = []) {
  return pages.filter((page) => /^[a-zA-Z0-9_-]+$/.test(page.id)).slice(0, 30).map((page) => ({ id: 'metrics/' + page.id, group: strings.chartKit.title, icon: 'activity', label: page.title }));
}

export function findAdminRoute(id, metricPages = []) {
  return [...metricAdminRoutes(metricPages), ...ADMIN_ROUTES].find((route) => route.id === id) || null;
}

/**
 * Whether a non-admin staff member sees a section: Home always; `moderator` sections when they
 * moderate; `forumModerator` sections with a forum assignment; the Phase 4 sections of the
 * `staffAreas` they hold; `supportStaff` and `communicator` sections with those roles.
 */
function staffSees(route, { forumModerator, moderator, supportStaff, communicator, releaseManager, staffAreas }) {
  if (route.id === 'home') return true;
  if (route.moderator && moderator) return true;
  if (route.forumModerator && forumModerator) return true;
  if (route.supportStaff && supportStaff) return true;
  if (route.communicator && communicator) return true;
  if (route.releaseManager && releaseManager) return true;
  if (route.staffArea && [].concat(route.staffArea).some((area) => staffAreas.includes(area))) return true;
  return false;
}

/**
 * Group routes enabled for a property for the sidebar, preserving order. Non-admin staff
 * (`moderatorOnly`) see only their sections: moderators the `moderator` sections (plus
 * `forumModerator` sections with a forum assignment), SupportAgent holders the support inbox and
 * ideas, Communicator holders broadcasts, and Phase 4 staff the sections of their `staffAreas`. `moderator` defaults to true for backward compatibility.
 */
export function groupedAdminRoutes(property, { moderatorOnly = false, forumModerator = false, staffAreas = [], moderator = true, supportStaff = false, communicator = false, releaseManager = false, metricPages = [] } = {}) {
  const groups = [];
  const who = { forumModerator, moderator, supportStaff, communicator, releaseManager, staffAreas };
  const dynamic = metricAdminRoutes(metricPages);
  const routes = [...ADMIN_ROUTES.filter((r) => !dynamic.some((d) => d.id === r.id)), ...dynamic];
  for (const route of routes.filter((x) => featureEnabled(property, x.feature) && (!x.hiddenWithoutCapability || capabilityAvailable(property, x.capability)) && (!moderatorOnly || staffSees(x, who)))) {
    let group = groups.find((x) => x.label === route.group);
    if (!group) {
      group = { label: route.group, routes: [] };
      groups.push(group);
    }
    group.routes.push(route);
  }
  return groups;
}
