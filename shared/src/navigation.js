/**
 * Property-owned navigation surfaces. `mobile.navigation` (mobileNavigation.js) places the app's
 * destinations in the drawer, bottom shortcuts, and account menu; the optional `web.navigation`
 * orders the website's member sections. Both validate through `validateNavigation(surface)`, one
 * set of rules, so the surfaces cannot drift. `web.navigation` is a build input: the server
 * validates it in `property.json` but does not serve it.
 */
import { MaxMobileLabelLength, MaxMobileShortcuts, MobileDestinationIds } from './mobileNavigation.js';
import { validateNavigationSection } from './navigationRules.js';

export { cleanNavigationLabels } from './navigationRules.js';

/** Navigation surfaces a property configures. */
export const NavigationSurface = Object.freeze({ Mobile: 'mobile', Web: 'web' });

/**
 * Website member section identifiers (the member app's `MEMBER_ROUTES`, in engine order). The
 * server keeps the same list (`PropertyValidator`).
 */
export const WebDestinationIds = Object.freeze([
  'feed', 'forums', 'search', 'members', 'library', 'training', 'events', 'directory', 'messages',
  'ideas', 'announcements', 'support', 'assistants', 'notifications', 'profile'
]);

/** At most eight member sections are listed first on the website. */
export const MaxWebPrimary = 8;
/** Longest website section label override, after trimming. */
export const MaxWebLabelLength = 24;

const SURFACES = Object.freeze({
  mobile: () => ({ prefix: 'mobile.navigation', known: MobileDestinationIds, max: MaxMobileLabelLength,
    lists: [['drawerItems', MobileDestinationIds.length], ['shortcuts', MaxMobileShortcuts], ['accountMenu', MobileDestinationIds.length]] }),
  web: () => ({ prefix: 'web.navigation', known: WebDestinationIds, max: MaxWebLabelLength, lists: [['primary', MaxWebPrimary]] })
});

/**
 * Validate a surface's navigation section, naming invalid configuration paths.
 * @param {'mobile'|'web'} surface
 * @param {any} navigation The `mobile.navigation` or `web.navigation` value (missing or null is valid).
 * @returns {string[]}
 */
export function validateNavigation(surface, navigation) {
  const spec = SURFACES[surface];
  if (!spec) throw new Error('Unknown navigation surface: ' + surface);
  const { prefix, known, max, lists } = spec();
  return validateNavigationSection(prefix, navigation, lists, known, max);
}
