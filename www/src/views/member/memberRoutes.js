import { MaxWebLabelLength, WebDestinationIds, catalogAccess, catalogFeatureEnabled, cleanNavigationLabels, hasServerCapability, signedOutBrowsable } from '@trelliskit/shared';
import strings from '../../strings.js';

const n = strings.member.nav;

/**
 * Member app sections. `feature` names the property `features` switch that turns a section off
 * (a missing switch counts as on). `capability` names the server capability (a `features` flag
 * the server must report as `true`) the section's backend needs; until it does, the section
 * stays in the navigation and opens a "coming soon" page that makes no API calls. `live`
 * sections have their own routes; the rest render the coming-soon page. `access` names the access
 * feature key (ACCESS_MODELS_CONTRACT.md section 2) that drives lock badges, member-only pages,
 * and signed-out browsing. `catalog` sections (library, events, venue directory) use `catalogAccess`:
 * signed-in free accounts browse them with locked stubs, and signed-out visitors browse their
 * `Public` objects under anonymous browsing (docs/PHASE4_CONTRACT.md resolved decision 2).
 * `coveredBy` names the section that embeds this one (the library lives inside Training): the
 * covered section leaves the navigation while its cover is shown, and keeps its own routes.
 */
export const MEMBER_ROUTES = [
  { id: 'feed', label: n.feed, icon: 'feed', live: true, access: 'feed', killSwitch: 'feed' },
  { id: 'forums', label: n.forums, icon: 'forum', capability: 'forums', live: true, access: 'forums', killSwitch: 'forums' },
  { id: 'search', label: n.search, icon: 'search', capability: 'search', live: true, hiddenWithoutCapability: true, access: 'search', signedInOnly: true, killSwitch: 'search' },
  { id: 'members', label: n.members, icon: 'users', live: true, access: 'directory' },
  { id: 'library', label: n.library, icon: 'book', feature: 'library', live: true, access: 'library', catalog: true, killSwitch: 'library', coveredBy: 'training' },
  { id: 'training', label: n.training, icon: 'barbell', feature: 'training', live: true, access: 'training', catalog: true, killSwitch: 'training' },
  { id: 'events', label: n.events, icon: 'calendar', feature: 'events', live: true, access: 'events', catalog: true, killSwitch: 'events' },
  { id: 'directory', label: n.directory, icon: 'pin', feature: 'directory', live: true, access: 'directory', catalog: true, killSwitch: 'directory' },
  // The reviewed exercise and technique catalog is opt-in (features.catalog) and uses the library access rules.
  { id: 'catalog', label: strings.exerciseCatalog.nav, icon: 'book', feature: 'catalog', live: true, access: 'library', catalog: true, killSwitch: 'library' },
  { id: 'messages', label: n.messages, icon: 'message', feature: 'messaging', live: true, access: 'messaging', killSwitch: 'messaging' },
  { id: 'ideas', label: n.ideas, icon: 'trending', feature: 'ideas', live: true, lock: 'ideas' },
  { id: 'announcements', label: n.announcements, icon: 'megaphone', feature: 'broadcasts', live: true },
  { id: 'support', label: n.support, icon: 'help', live: true },
  { id: 'assistants', label: n.assistants, icon: 'message', capability: 'ai', live: true, hiddenWithoutCapability: true, access: 'assistants', killSwitch: 'assistants' },
  { id: 'notifications', label: n.notifications, icon: 'bell', live: true },
  { id: 'profile', label: n.profile, icon: 'user', live: true }
];

/**
 * Whether a property feature switch is on. Missing switches count as on, except the opt-in
 * catalog (`features.catalog`), which is on only when set to `true`.
 */
export function featureEnabled(property, feature) {
  if (!feature) return true;
  if (feature === 'catalog') return catalogFeatureEnabled(property);
  const features = (property && property.features) || {};
  return features[feature] !== false;
}

/** Whether a route's server capability is available (routes without one always are). */
export function capabilityAvailable(property, capability) {
  return !capability || hasServerCapability(property, capability);
}

/** Whether a member section opens its real screens (live and its capability is available). */
export function routeAvailable(property, route) {
  return Boolean(route.live) && capabilityAvailable(property, route.capability);
}

/**
 * Order sections by the property's `web.navigation.primary` (listed ids first, in order, then the
 * rest in engine order), apply `web.navigation.labels`, and drop a covered section while its cover
 * is in the list. Without `web.navigation` the engine order and labels apply.
 * @param {object} property
 * @param {object[]} routes Visible sections, in engine order.
 * @returns {object[]}
 */
function placeRoutes(property, routes) {
  const navigation = property && property.web && property.web.navigation;
  const primary = navigation && Array.isArray(navigation.primary) ? navigation.primary : [];
  const labels = cleanNavigationLabels(navigation && navigation.labels, WebDestinationIds, MaxWebLabelLength);
  const ids = new Set(routes.map((route) => route.id));
  const visible = routes.filter((route) => !(route.coveredBy && ids.has(route.coveredBy) && !primary.includes(route.id)));
  const rank = (route) => (primary.includes(route.id) ? primary.indexOf(route.id) : primary.length + MEMBER_ROUTES.indexOf(route));
  return [...visible].sort((a, b) => rank(a) - rank(b)).map((route) => (labels[route.id] ? { ...route, label: labels[route.id] } : route));
}

/**
 * Member sections enabled for a property, in the property's order. Sections marked
 * `hiddenWithoutCapability` (assistants, an opt-in feature, and search, which `features.search` can
 * turn off) are left out entirely until the server reports their capability. `hidden` (for example
 * a kill-switch check) removes sections before covered sections are resolved, so a library whose
 * Training section is switched off returns to the list.
 * @param {object} property
 * @param {(route: object) => boolean} [hidden]
 */
export function memberRoutesFor(property, hidden = null) {
  return placeRoutes(property, MEMBER_ROUTES.filter((route) => featureEnabled(property, route.feature) && (!route.hiddenWithoutCapability || capabilityAvailable(property, route.capability)) && !(hidden && hidden(route))));
}

/**
 * Sections a signed-out visitor may browse (anonymous browsing on, the section open to signed-out
 * callers, and its real screens available), in the property's order.
 * @param {object} property
 * @param {(route: object) => boolean} [hidden]
 */
export function guestRoutesFor(property, hidden = null) {
  return memberRoutesFor(property, (route) => Boolean(hidden && hidden(route)) || !(routeAvailable(property, route) && route.access && !route.signedInOnly && (route.catalog ? catalogAccess(property, null, route.access, 'read').allowed : signedOutBrowsable(property, route.access))));
}
