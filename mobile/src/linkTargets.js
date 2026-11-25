import { resolveNotificationPath } from './notifications/deepLinks.js';
import { trainingWebPath } from './training/hub.js';

const IDENTIFIER = '[A-Za-z0-9_-]{1,128}';
const STATIC_PATHS = new Set([
  '/feed', '/forums', '/forums/search', '/forums/following', '/profile', '/profile/about', '/map', '/library', '/library/downloads',
  '/members', '/notifications', '/assistants', '/announcements', '/ideas', '/ideas/new', '/messages', '/messages/requests', '/messages/search',
  '/support', '/support/new', '/account', '/account/blocked', '/account/edit-profile', '/account/password', '/account/notifications',
  '/account/delete', '/account/sign-in-methods', '/account/membership', '/account/messaging', '/account/appearance', '/account/legal',
  '/directory/submit', '/directory/submissions', '/events/calendar-feed', '/events/propose', '/events/proposals', '/compose', '/compose-thread', '/catalog',
  '/rank', '/curricula', '/check-in'
]);
const DETAIL_PATH = new RegExp('^/(?:feed|forums|members|assistants|announcements|ideas|messages|threads|venues|instructors|events|series|lessons|documents|catalog|curricula)/' + IDENTIFIER + '$');

/** HTTP(S) only, with no embedded credentials, control characters, or ambiguous slashes. */
export function safeWebUrl(value) {
  if (typeof value !== 'string' || /[\\\u0000-\u0020\u007f]/.test(value)) return null;
  try {
    const url = new URL(value);
    return ['https:', 'http:'].includes(url.protocol) && !url.username && !url.password && url.hostname ? url : null;
  } catch { return null; }
}

function propertyHosts(property) {
  return Object.values(property?.domains || {}).flat().flatMap((value) => {
    if (typeof value !== 'string') return [];
    const url = safeWebUrl(value.includes('://') ? value : 'https://' + value);
    return url ? [url.host, ...(url.hostname.startsWith('www.') ? [] : ['www.' + url.host])] : [];
  });
}

/** Resolve links without platform dependencies. Unknown first-party pages never escape to a browser. */
export function resolveAppLink(value, property) {
  if (typeof value !== 'string') return null;
  const href = value.trim();
  if (!href || /[\\\u0000-\u001f\u007f]/.test(href) || href.startsWith('//')) return null;
  const relative = href.startsWith('/');
  // This loopback origin is used only to parse relative paths; it is never requested.
  const url = safeWebUrl(relative ? 'https://127.0.0.1' + href : href);
  if (!url) return null;
  if (!relative && !propertyHosts(property).includes(url.host)) return { kind: 'web', url: url.href };
  let pathname = url.pathname.replace(/^\/app(?=\/|$)/, '') || '/';
  pathname = pathname.replace(/\/$/, '') || '/';
  const document = (property?.documents || []).find((item) => {
    const slug = '/' + String(item.slug || item.key || '').replace(/^\/+|\/+$/g, '');
    return slug === pathname || (pathname === '/terms' && item.kind === 'Terms') || (pathname === '/privacy' && item.kind === 'Privacy');
  });
  if (document && /^[A-Za-z0-9_-]{1,64}$/.test(document.key)) return { kind: 'native', path: '/documents/' + document.key };
  if (pathname === '/terms' || pathname === '/privacy') return { kind: 'native', path: '/documents/' + pathname.slice(1) };
  if (pathname === '/') return { kind: 'native', path: '/feed' };
  if (pathname === '/directory') return { kind: 'native', path: '/map?section=places' };
  if (pathname === '/events') return { kind: 'native', path: '/map?section=events' };
  const path = pathname + url.search;
  const notificationPath = resolveNotificationPath(path);
  if (notificationPath) return { kind: 'native', path: notificationPath };
  // The website's Training hub (`/app/training/...`): sections, Library segments, curricula, and
  // the series, lessons, and catalog entries shown under it open their native screens.
  const trainingPath = trainingWebPath(pathname, url.search);
  if (trainingPath) return { kind: 'native', path: trainingPath };
  if (STATIC_PATHS.has(pathname) || DETAIL_PATH.test(pathname)) return { kind: 'native', path };
  return { kind: 'unsupported' };
}
