import { catalogAccess, catalogFeatureEnabled, hasServerCapability, signedOutBrowsable } from '@trelliskit/shared';
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
 */
export const MEMBER_ROUTES = [
  { id: 'feed', label: n.feed, icon: 'feed', live: true, access: 'feed', killSwitch: 'feed' },
  { id: 'forums', label: n.forums, icon: 'forum', capability: 'forums', live: true, access: 'forums', killSwitch: 'forums' },
  { id: 'search', label: n.search, icon: 'search', capability: 'search', live: true, hiddenWithoutCapability: true, access: 'search', signedInOnly: true, killSwitch: 'search' },
  { id: 'members', label: n.members, icon: 'users', live: true, access: 'directory' },
  { id: 'library', label: n.library, icon: 'book', feature: 'library', live: true, access: 'library', catalog: true, killSwitch: 'library' },
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
 * Member sections enabled for a property. Sections marked `hiddenWithoutCapability` (assistants,
 * an opt-in feature, and search, which `features.search` can turn off) are left out entirely
 * until the server reports their capability.
 */
export function memberRoutesFor(property) {
  return MEMBER_ROUTES.filter((route) => featureEnabled(property, route.feature) && (!route.hiddenWithoutCapability || capabilityAvailable(property, route.capability)));
}

/**
 * Sections a signed-out visitor may browse (anonymous browsing on, the section open to signed-out
 * callers, and its real screens available).
 */
export function guestRoutesFor(property) {
  return memberRoutesFor(property).filter((route) => routeAvailable(property, route) && route.access && !route.signedInOnly && (route.catalog ? catalogAccess(property, null, route.access, 'read').allowed : signedOutBrowsable(property, route.access)));
}
