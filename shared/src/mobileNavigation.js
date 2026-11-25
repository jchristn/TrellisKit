/** Property-owned mobile navigation. Destinations share existing routes and access rules. */
import { catalogAccess, propertyFeatureEnabled, showsLockBadge, signedOutBrowsable } from './access.js';
import { cleanNavigationLabels, validateNavigationSection } from './navigationRules.js';

/**
 * Known destinations; identifiers are stable configuration values, never arbitrary routes.
 * `coveredBy` names the destination that embeds this one (the library and the technique catalog
 * live inside Training); `header` marks a destination the header always offers.
 */
export const MobileDestinations = Object.freeze([
  { id: 'feed', href: '/feed', icon: 'home', label: 'Feed', feature: 'feed' },
  { id: 'forums', href: '/forums', icon: 'chatbox-ellipses', label: 'Forums', feature: 'forums' },
  { id: 'members', href: '/members', icon: 'people', label: 'Members', feature: 'members', signedIn: true },
  { id: 'library', href: '/library', icon: 'library', label: 'Library', feature: 'library', catalog: true, coveredBy: 'training' },
  { id: 'training', href: '/training', icon: 'barbell', label: 'Training', feature: 'training', catalog: true },
  { id: 'catalog', href: '/training/library/techniques', icon: 'book', label: 'Techniques', feature: 'catalog', catalog: true, coveredBy: 'training' },
  { id: 'events', href: '/map?section=events', icon: 'calendar', label: 'Events', feature: 'events', catalog: true },
  { id: 'directory', href: '/map?section=places', icon: 'map', label: 'Venues', feature: 'directory', catalog: true },
  { id: 'messages', href: '/messages', icon: 'chatbubbles', label: 'Messages', feature: 'messaging', signedIn: true },
  { id: 'assistants', href: '/assistants', icon: 'sparkles', label: 'Assistants', feature: 'assistants' },
  { id: 'announcements', href: '/announcements', icon: 'megaphone', label: 'Announcements', signedIn: true },
  { id: 'ideas', href: '/ideas', icon: 'bulb', label: 'Ideas', feature: 'ideas', signedIn: true },
  { id: 'profile', href: '/profile', icon: 'person-circle', label: 'Profile', section: 'account' },
  { id: 'notifications', href: '/notifications', icon: 'notifications', label: 'Notifications', signedIn: true, section: 'account', header: true },
  { id: 'support', href: '/support', icon: 'help-circle', label: 'Support', feature: 'support', signedIn: true, section: 'account' },
  { id: 'settings', href: '/account', icon: 'settings', label: 'Settings', signedIn: true, section: 'account' },
  { id: 'about', href: '/profile/about', icon: 'information-circle', label: 'About', section: 'account' }
].map((entry) => Object.freeze({ section: 'community', ...entry })));

/** At most five bottom shortcuts leave sufficient room for readable labels. */
export const MaxMobileShortcuts = 5;
/**
 * Longest label shown under a bottom shortcut. A longer label falls back to the icon with the
 * label as its accessibility label at the largest text sizes; the drawer keeps `MaxMobileLabelLength`.
 */
export const MaxMobileBarLabelLength = 12;
/** Default frequent destinations; everything else remains in the complete drawer. */
export const DefaultMobileShortcuts = Object.freeze(['feed', 'forums', 'messages', 'profile']);
/** Every supported destination identifier. */
export const MobileDestinationIds = Object.freeze(MobileDestinations.map((entry) => entry.id));
/** Longest property label override for a destination; longer labels truncate in the bottom bar. */
export const MaxMobileLabelLength = 24;

/** Engine drawer order when a property sets none: every destination not embedded in another. */
export const DefaultMobileDrawerItems = Object.freeze(MobileDestinations.filter((entry) => !entry.coveredBy).map((entry) => entry.id));

const object = (value) => value !== null && typeof value === 'object' && !Array.isArray(value);
const known = (value) => typeof value === 'string' && MobileDestinationIds.includes(value);
const LIST_KEYS = Object.freeze([['drawerItems', MobileDestinationIds.length], ['shortcuts', MaxMobileShortcuts], ['accountMenu', MobileDestinationIds.length]]);

/**
 * Validate the optional mobile.navigation section, naming invalid configuration paths. The same
 * rules as `validateNavigation('mobile', navigation)`.
 */
export function validateMobileNavigation(navigation) {
  return validateNavigationSection('mobile.navigation', navigation, LIST_KEYS, MobileDestinationIds, MaxMobileLabelLength);
}

/**
 * Effective destination placement. Destinations omitted from every surface are appended to the
 * drawer, so configuration cannot strand enabled features. A destination counts as placed when it
 * is in the drawer, the bottom shortcuts, or the account menu; when the header always offers it
 * (`header`); or when the destination that embeds it (`coveredBy`) is placed. Null/missing arrays
 * use defaults; an empty shortcuts array hides the bar; an empty or missing account menu hides the
 * header account button.
 */
export function mobileNavigationOf(property) {
  const value = property?.mobile?.navigation;
  const raw = object(value) ? value : {};
  const clean = (values, fallback, max) => Array.isArray(values)
    ? [...new Set(values.filter(known))].slice(0, max)
    : [...fallback];
  const order = clean(raw.drawerItems, DefaultMobileDrawerItems, MobileDestinationIds.length);
  const shortcuts = clean(raw.shortcuts, DefaultMobileShortcuts, MaxMobileShortcuts);
  const accountMenu = clean(raw.accountMenu, [], MobileDestinationIds.length);
  const placed = new Set([...order, ...shortcuts, ...accountMenu]);
  const covered = (entry) => placed.has(entry.id) || entry.header === true || Boolean(entry.coveredBy && placed.has(entry.coveredBy));
  const labels = cleanNavigationLabels(raw.labels, MobileDestinationIds, MaxMobileLabelLength);
  return {
    drawerItems: [...order, ...MobileDestinations.filter((entry) => !covered(entry)).map((entry) => entry.id)],
    shortcuts,
    accountMenu,
    labels
  };
}

function labelOf(destination, property, labels, overrides) {
  if (overrides[destination.id]) return overrides[destination.id];
  const text = destination.id === 'members' ? property?.terminology?.members
    : destination.id === 'directory' ? property?.directory?.venueLabelPlural : null;
  if (typeof text === 'string' && text.trim()) {
    const trimmed = text.trim();
    return trimmed[0].toUpperCase() + trimmed.slice(1);
  }
  return labels[destination.id] || destination.label;
}

/**
 * Resolve every surface from one registry and one caller. Hidden/disabled destinations never
 * become shortcuts. Partially gated signed-in destinations remain visible with a lock; screens
 * and the server retain authorization. A null user is an anonymous visitor, not a member.
 * @param {object} property Effective or bundled property configuration.
 * @param {object|null} user Current Me response, null for a guest.
 * @param {Record<string,string>} labels Application string-catalog labels, keyed by destination.
 * @returns {{drawerItems: object[], shortcuts: object[], accountMenu: object[]}}
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
  }).map((entry) => ({ ...entry, label: labelOf(entry, property, labels, config.labels), locked: Boolean(user && entry.feature && showsLockBadge(property, user, entry.feature)) }));
  const byId = new Map(destinations.map((entry) => [entry.id, entry]));
  // A destination embedded in another stays reachable from the drawer when its cover is hidden
  // for this caller (feature off or not browsable) and the property placed it nowhere itself.
  const placed = new Set([...config.drawerItems, ...config.shortcuts, ...config.accountMenu]);
  const uncovered = destinations.filter((entry) => entry.coveredBy && !byId.has(entry.coveredBy) && !placed.has(entry.id)).map((entry) => entry.id);
  return {
    drawerItems: [...config.drawerItems, ...uncovered].map((id) => byId.get(id)).filter(Boolean),
    shortcuts: config.shortcuts.map((id) => byId.get(id)).filter(Boolean),
    accountMenu: config.accountMenu.map((id) => byId.get(id)).filter(Boolean)
  };
}
