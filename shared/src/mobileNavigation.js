/** Property-owned mobile navigation. Destinations share existing routes and access rules. */
import { catalogAccess, propertyFeatureEnabled, showsLockBadge, signedOutBrowsable } from './access.js';

/** Known destinations; identifiers are stable configuration values, never arbitrary routes. */
export const MobileDestinations = Object.freeze([
  { id: 'feed', href: '/feed', icon: 'home', label: 'Feed', feature: 'feed' },
  { id: 'forums', href: '/forums', icon: 'chatbox-ellipses', label: 'Forums', feature: 'forums' },
  { id: 'members', href: '/members', icon: 'people', label: 'Members', feature: 'members', signedIn: true },
  { id: 'library', href: '/library', icon: 'library', label: 'Library', feature: 'library', catalog: true },
  { id: 'events', href: '/map?section=events', icon: 'calendar', label: 'Events', feature: 'events', catalog: true },
  { id: 'directory', href: '/map?section=places', icon: 'map', label: 'Venues', feature: 'directory', catalog: true },
  { id: 'messages', href: '/messages', icon: 'chatbubbles', label: 'Messages', feature: 'messaging', signedIn: true },
  { id: 'assistants', href: '/assistants', icon: 'sparkles', label: 'Assistants', feature: 'assistants' },
  { id: 'announcements', href: '/announcements', icon: 'megaphone', label: 'Announcements', signedIn: true },
  { id: 'ideas', href: '/ideas', icon: 'bulb', label: 'Ideas', feature: 'ideas', signedIn: true },
  { id: 'profile', href: '/profile', icon: 'person-circle', label: 'Profile', section: 'account' },
  { id: 'notifications', href: '/notifications', icon: 'notifications', label: 'Notifications', signedIn: true, section: 'account' },
  { id: 'support', href: '/support', icon: 'help-circle', label: 'Support', feature: 'support', signedIn: true, section: 'account' },
  { id: 'settings', href: '/account', icon: 'settings', label: 'Settings', signedIn: true, section: 'account' },
  { id: 'about', href: '/profile/about', icon: 'information-circle', label: 'About', section: 'account' }
].map((entry) => Object.freeze({ section: 'community', ...entry })));

/** At most four bottom shortcuts leave sufficient room for readable labels. */
export const MaxMobileShortcuts = 4;
/** Default frequent destinations; everything else remains in the complete drawer. */
export const DefaultMobileShortcuts = Object.freeze(['feed', 'forums', 'messages', 'profile']);
/** Every supported destination identifier. */
export const MobileDestinationIds = Object.freeze(MobileDestinations.map((entry) => entry.id));

const object = (value) => value !== null && typeof value === 'object' && !Array.isArray(value);
const known = (value) => typeof value === 'string' && MobileDestinationIds.includes(value);

/** Validate the optional mobile.navigation section, naming invalid configuration paths. */
export function validateMobileNavigation(navigation) {
  if (navigation === undefined || navigation === null) return [];
  if (!object(navigation)) return ['mobile.navigation must be an object'];
  const problems = [];
  for (const [key, max] of [['drawerItems', MobileDestinationIds.length], ['shortcuts', MaxMobileShortcuts]]) {
    const values = navigation[key];
    if (values === undefined || values === null) continue;
    const field = 'mobile.navigation.' + key;
    if (!Array.isArray(values)) { problems.push(field + ' must be an array'); continue; }
    if (values.length > max) problems.push(field + ' must contain at most ' + max + ' destinations');
    const seen = new Set();
    values.forEach((id, i) => {
      if (!known(id)) problems.push(field + '[' + i + '] must be a known destination');
      else if (seen.has(id)) problems.push(field + '[' + i + '] duplicates ' + id);
      seen.add(id);
    });
  }
  return problems;
}

/**
 * Effective destination order. Omitted drawer entries are appended, so configuration cannot
 * strand enabled features. Null/missing arrays use defaults; an empty shortcuts array hides the bar.
 */
export function mobileNavigationOf(property) {
  const value = property?.mobile?.navigation;
  const raw = object(value) ? value : {};
  const clean = (values, fallback, max) => Array.isArray(values)
    ? [...new Set(values.filter(known))].slice(0, max)
    : [...fallback];
  const order = clean(raw.drawerItems, MobileDestinationIds, MobileDestinationIds.length);
  return {
    drawerItems: [...order, ...MobileDestinationIds.filter((id) => !order.includes(id))],
    shortcuts: clean(raw.shortcuts, DefaultMobileShortcuts, MaxMobileShortcuts)
  };
}

function labelOf(destination, property, labels) {
  const text = destination.id === 'members' ? property?.terminology?.members
    : destination.id === 'directory' ? property?.directory?.venueLabelPlural : null;
  if (typeof text === 'string' && text.trim()) {
    const trimmed = text.trim();
    return trimmed[0].toUpperCase() + trimmed.slice(1);
  }
  return labels[destination.id] || destination.label;
}

/**
 * Resolve both surfaces from one registry and one caller. Hidden/disabled destinations never
 * become shortcuts. Partially gated signed-in destinations remain visible with a lock; screens
 * and the server retain authorization. A null user is an anonymous visitor, not a member.
 * @param {object} property Effective or bundled property configuration.
 * @param {object|null} user Current Me response, null for a guest.
 * @param {Record<string,string>} labels Application string-catalog labels, keyed by destination.
 * @returns {{drawerItems: object[], shortcuts: object[]}}
 */
export function resolveMobileNavigation(property, user = null, labels = {}) {
  const config = mobileNavigationOf(property);
  const destinations = MobileDestinations.filter((entry) => {
    if (entry.id === 'assistants' && property?.features?.ai !== true) return false;
    if (entry.feature && !propertyFeatureEnabled(property, entry.feature)) return false;
    if (user) return true;
    if (entry.signedIn) return false;
    if (!entry.feature) return true;
    return entry.catalog ? catalogAccess(property, null, entry.feature, 'read').allowed : signedOutBrowsable(property, entry.feature);
  }).map((entry) => ({ ...entry, label: labelOf(entry, property, labels), locked: Boolean(user && entry.feature && showsLockBadge(property, user, entry.feature)) }));
  const byId = new Map(destinations.map((entry) => [entry.id, entry]));
  return {
    drawerItems: config.drawerItems.map((id) => byId.get(id)).filter(Boolean),
    shortcuts: config.shortcuts.map((id) => byId.get(id)).filter(Boolean)
  };
}
