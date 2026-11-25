/**
 * Public landing pages (docs/PHASE4_CONTRACT.md section 18): server-rendered pages for member profiles (opt-in),
 * venues, events, and catalog entries at `/<path word>/<slug>`, where the path words come from the property's `publicPages`
 * section (engine defaults `venues`, `events`, `profiles`, `catalog`). The backend is authoritative; these helpers mirror its slug rules
 * for live validation and build the page URLs for share and copy actions.
 */

/** The kinds of public landing page. */
export const PublicPageKind = Object.freeze({ Venue: 'Venue', Event: 'Event', Profile: 'Profile', Catalog: 'Catalog' });

/** Engine defaults of `publicPages` (a property's property.json may set its own path words). */
export const PUBLIC_PAGE_DEFAULTS = Object.freeze({ enabled: true, venuesPath: 'venues', eventsPath: 'events', profilesPath: 'profiles', catalogPath: 'catalog' });

/** Slug length bounds. */
export const SLUG_MIN = 3;
export const SLUG_MAX = 80;

/** Lower case letters and digits in groups joined by single hyphens. */
export const SLUG_PATTERN = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;

/** Slugs no page may use (they collide with routes or read as system words). */
export const RESERVED_SLUGS = Object.freeze([
  'about', 'account', 'add', 'admin', 'all', 'api', 'app', 'assets', 'auth', 'contact', 'create', 'delete', 'edit', 'embed', 'help', 'index', 'join', 'login', 'logout',
  'map', 'me', 'new', 'null', 'platform', 'privacy', 'register', 'search', 'settings', 'sign-in', 'sign-out', 'sign-up', 'signin', 'signup', 'sitemap', 'support', 'terms',
  'undefined'
]);

/** The property's public page configuration with the engine defaults filled in. */
export function publicPagesConfig(property) {
  const raw = (property && property.publicPages) || {};
  return {
    enabled: raw.enabled !== false,
    venuesPath: raw.venuesPath || PUBLIC_PAGE_DEFAULTS.venuesPath,
    eventsPath: raw.eventsPath || PUBLIC_PAGE_DEFAULTS.eventsPath,
    profilesPath: raw.profilesPath || PUBLIC_PAGE_DEFAULTS.profilesPath,
    catalogPath: raw.catalogPath || PUBLIC_PAGE_DEFAULTS.catalogPath
  };
}

/** The path word of a kind (`Venue`, `Event`, `Profile`, or `Catalog`). */
export function publicPagePathWord(property, kind) {
  const config = publicPagesConfig(property);
  if (kind === PublicPageKind.Venue) return config.venuesPath;
  if (kind === PublicPageKind.Event) return config.eventsPath;
  if (kind === PublicPageKind.Catalog) return config.catalogPath;
  return config.profilesPath;
}

/**
 * Why a slug is not usable, or null when it is. Codes: `required`, `length`, `format`, `reserved`.
 * @param {string} slug
 * @returns {string|null}
 */
export function slugProblem(slug) {
  const s = String(slug || '').trim();
  if (s.length === 0) return 'required';
  if (s.length < SLUG_MIN || s.length > SLUG_MAX) return 'length';
  if (!SLUG_PATTERN.test(s)) return 'format';
  if (RESERVED_SLUGS.includes(s)) return 'reserved';
  return null;
}

/** Whether a slug passes the format rules (uniqueness is checked by the server). */
export function isValidSlug(slug) {
  return slugProblem(slug) === null;
}

/**
 * A slug from free text: lower case, accents removed, every run of other characters becomes one hyphen, at most 80
 * characters, cut at a hyphen when possible.
 * @param {...string} parts name, locality, region (empty parts are skipped)
 * @returns {string}
 */
export function deriveSlug(...parts) {
  const text = parts.filter((p) => p !== null && p !== undefined && String(p).trim() !== '').join(' ');
  let slug = String(text).normalize('NFKD').replace(/[̀-ͯ]/g, '').toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '');
  if (slug.length > SLUG_MAX) {
    slug = slug.slice(0, SLUG_MAX);
    const cut = slug.lastIndexOf('-');
    if (cut >= SLUG_MIN) slug = slug.slice(0, cut);
    slug = slug.replace(/-+$/g, '');
  }
  return slug;
}

/** Normalize what a person types into a slug field (lower case, spaces to hyphens, no doubled hyphens). */
export function normalizeSlugInput(value) {
  return String(value || '').toLowerCase().replace(/[\s_]+/g, '-').replace(/[^a-z0-9-]/g, '').replace(/-{2,}/g, '-').slice(0, SLUG_MAX);
}

/** The site-relative path of a page, for example `/venues/example-club-exampleville-ex`. */
export function publicPagePath(property, kind, slug) {
  if (!slug) return null;
  return '/' + publicPagePathWord(property, kind) + '/' + encodeURIComponent(slug);
}

/**
 * The absolute URL of a page. `origin` defaults to `https://` plus the property's `domains.web`.
 * @returns {string|null}
 */
export function publicPageUrl(property, kind, slug, origin) {
  const path = publicPagePath(property, kind, slug);
  if (!path) return null;
  const domain = property && property.domains && property.domains.web;
  const base = String(origin || (domain ? 'https://' + domain : '')).replace(/\/+$/, '');
  return base + path;
}

/** The URL to share for an object: its `publicUrl` when the server reports a landing page, else the fallback. */
export function shareUrlOf(object, fallback) {
  return (object && object.publicUrl) || fallback || null;
}

/** @this {import('./ApiClient.js').ApiClient} */
export const publicPageMethods = {
  /** GET /me/public-page: `{ enabled, slug, suggestedSlug, path, url }`. */
  getMyPublicPage(signal) { return this.get('/me/public-page', { signal }); },
  /** PUT /me/public-page `{ enabled?, slug? }`: 400 ValidationFailed (`fieldErrors.slug`), 409 SlugInUse. */
  updateMyPublicPage(body, signal) { return this.put('/me/public-page', body || {}, { signal }); },
  /** POST /public-pages/slugs/check `{ kind, slug, id? }`: `{ kind, slug, valid, available, problem, path, url }`. */
  checkPublicPageSlug(body, signal) { return this.post('/public-pages/slugs/check', body || {}, { signal }); },
  /** GET /admin/members/{id}/public-page (MemberAdmin). */
  adminGetMemberPublicPage(id, signal) { return this.get('/admin/members/' + encodeURIComponent(id) + '/public-page', { signal }); },
  /** PUT /admin/members/{id}/public-page `{ enabled?, slug? }` (MemberAdmin). */
  adminUpdateMemberPublicPage(id, body, signal) { return this.put('/admin/members/' + encodeURIComponent(id) + '/public-page', body || {}, { signal }); }
};
