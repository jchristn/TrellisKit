/**
 * Route metadata for the public marketing site, built from a property's site bundle: titles,
 * descriptions, canonical URLs, Open Graph and Twitter tags, JSON-LD structured data, the
 * sitemap, and robots.txt. Plain JavaScript with no React or DOM use so the build (Vite plugin,
 * prerender) and the browser share it.
 *
 * Routes come from three places:
 * - Content pages: every web/content/*.md file in the property (front matter gives the title,
 *   description, Open Graph type, sitemap priority, navigation label, and JSON-LD entities).
 * - Legal pages: the property's `documents` list (path `/<slug>`, heading and link label from the
 *   title, Markdown from each document's file) when property.json declares one; otherwise the
 *   standard legal documents in the property's legal/ folder (LEGAL_ROUTES).
 * - /join: head tags only (the page redirects on the client).
 */
import { fillTemplate, propertyTokens } from '@trelliskit/shared';
import strings from '../../strings.js';
import { contentPages, listValue, stripPurchaseSentences } from '../../property/content.js';

const mk = strings.marketing;

/** Standard legal documents: file in the property's legal/ folder, public path, and strings key. */
export const LEGAL_ROUTES = Object.freeze([
  { file: 'privacy-policy.md', path: '/privacy', key: 'privacy' },
  { file: 'terms-of-service.md', path: '/terms', key: 'terms' },
  { file: 'subscription-terms.md', path: '/subscription-terms', key: 'subscriptionTerms' },
  { file: 'community-guidelines.md', path: '/community-guidelines', key: 'guidelines' },
  { file: 'account-deletion.md', path: '/delete-account', key: 'deleteAccount' }
]);

/** Paths crawlers must not visit. */
export const DISALLOWED_PATHS = ['/app', '/admin', '/api'];

/** API paths public landing pages load images from (crawlers may fetch them); matches the backend's robots.txt. */
export const PUBLIC_MEDIA_PATHS = ['/api/v1/media/files/public/', '/api/v1/media/avatars/', '/api/v1/property/assets/'];

/** Social preview image (generated or provided at build time; see scripts/brand-assets.mjs). */
export const OG_IMAGE = { path: '/og-image.png', width: 1200, height: 630 };

/** Strip the query, hash, and trailing slash from a path. */
export function normalizePath(path) {
  const bare = String(path || '/').split(/[?#]/)[0];
  if (bare.length > 1 && bare.endsWith('/')) return bare.replace(/\/+$/, '') || '/';
  return bare || '/';
}

function compact(obj) {
  const out = {};
  for (const [key, value] of Object.entries(obj)) {
    if (value === '' || value === null || value === undefined) continue;
    if (Array.isArray(value) && value.length === 0) continue;
    out[key] = value;
  }
  return out;
}

/** Escape text for HTML attribute values and text nodes. */
export function escapeHtml(value) {
  return String(value)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

/** Serialize head tag descriptors to HTML. JSON-LD is escaped so it cannot close the script tag. */
export function headTagsToHtml(tags) {
  return tags.map((t) => {
    const attrs = Object.entries(t.attrs || {}).map(([k, v]) => ' ' + k + '="' + escapeHtml(v) + '"').join('');
    if (t.tag === 'title') return '<title>' + escapeHtml(t.text) + '</title>';
    if (t.tag === 'script') return '<script' + attrs + '>' + t.text.replace(/</g, '\\u003c') + '</script>';
    return '<' + t.tag + attrs + ' />';
  }).join('\n    ');
}

/**
 * Build the metadata helpers for a site bundle `{ property, content, legal }`.
 * @param {object} site
 */
export function createSiteMeta(site) {
  const property = site.property;
  const tokens = propertyTokens(property);
  const fill = (text) => fillTemplate(String(text || ''), property);
  const siteUrl = tokens.siteUrl;
  const pages = contentPages(site);
  const legal = site.legal || {};

  // JSON-LD entities defined by content front matter (person.*, book.*, organization.*), merged
  // across pages in order. Entities that no page defines are never published.
  const entities = { person: null, book: null, organization: {} };
  for (const page of pages) {
    for (const key of ['person', 'book', 'organization']) {
      const value = page.meta[key];
      if (value && typeof value === 'object') entities[key] = { ...(entities[key] || {}), ...value, page: entities[key] && entities[key].page ? entities[key].page : page.path };
    }
  }

  const contentRoutes = pages.map((page) => ({
    path: page.path,
    kind: 'content',
    file: page.file,
    title: fill(page.meta.metaTitle || (page.meta.title ? page.meta.title + ' | {name}' : '{name}')),
    // Price sentences drop out when the property sells nothing (the Open access model).
    description: fill(stripPurchaseSentences(page.meta.metaDescription || property.description, property)),
    ogType: page.meta.ogType || 'website',
    priority: page.meta.priority || '0.5',
    changefreq: page.meta.changefreq || 'monthly',
    jsonLd: listValue(page.meta.jsonLd).filter((key) => entities[key]),
    nav: page.meta.nav ? fill(page.meta.nav) : '',
    sitemap: page.meta.sitemap !== 'false',
    body: true
  }));
  const legalRoutes = Array.isArray(site.documents) ? documentRoutes(site.documents, fill) : LEGAL_ROUTES.filter((r) => legal[r.file] !== undefined).map((r) => ({
    path: r.path,
    kind: 'legal',
    file: r.file,
    key: r.key,
    documentKey: null,
    version: '',
    footer: true,
    heading: mk.legal[r.key].title,
    linkLabel: mk.legal[r.key].link,
    title: fill(mk.legal[r.key].metaTitle),
    description: fill(mk.legal[r.key].metaDescription),
    ogType: 'article',
    priority: '0.3',
    changefreq: 'yearly',
    jsonLd: [],
    sitemap: true,
    body: true
  }));
  const joinRoute = { path: '/join', kind: 'join', title: fill(mk.join.metaTitle), description: fill(mk.join.metaDescription), ogType: 'website', jsonLd: [], sitemap: false, body: false };
  const routes = [...contentRoutes, ...legalRoutes, joinRoute].map((route) => ({ prerender: true, ...route }));

  const getRoute = (path) => {
    const clean = normalizePath(path);
    return routes.find((route) => route.path === clean) || null;
  };
  const canonicalUrl = (path) => {
    const clean = normalizePath(path);
    return siteUrl + (clean === '/' ? '/' : clean);
  };
  const absolute = (url) => {
    if (!url) return '';
    return /^https?:/.test(url) ? url : siteUrl + (url.startsWith('/') ? url : '/' + url);
  };
  const ids = {
    organization: siteUrl + '/#organization',
    website: siteUrl + '/#website',
    person: entities.person ? siteUrl + (entities.person.page === '/' ? '/' : entities.person.page) + '#person' : '',
    book: entities.book ? siteUrl + (entities.book.page === '/' ? '/' : entities.book.page) + '#book' : ''
  };

  /** JSON-LD graph for a path: Organization and WebSite everywhere, plus the entities the page lists. */
  const buildJsonLd = (path) => {
    const route = getRoute(path);
    const org = entities.organization || {};
    const graph = [
      compact({
        '@type': 'Organization',
        '@id': ids.organization,
        name: property.name,
        url: siteUrl + '/',
        logo: absolute('/logo.png'),
        email: org.email !== undefined ? fill(org.email) : tokens.supportEmail,
        founder: entities.person ? { '@id': ids.person } : '',
        sameAs: listValue(org.sameAs)
      }),
      {
        '@type': 'WebSite',
        '@id': ids.website,
        name: property.name,
        url: siteUrl + '/',
        inLanguage: property.locale || 'en-US',
        publisher: { '@id': ids.organization }
      }
    ];
    const extras = route ? route.jsonLd : [];
    if (extras.includes('person')) {
      const p = entities.person;
      graph.push(compact({
        '@type': 'Person',
        '@id': ids.person,
        name: fill(p.name),
        url: canonicalUrl(p.page),
        description: fill(p.description),
        image: absolute(p.image),
        affiliation: p.affiliation ? { '@type': 'Organization', name: fill(p.affiliation) } : '',
        sameAs: listValue(p.sameAs)
      }));
    }
    if (extras.includes('book')) {
      const b = entities.book;
      graph.push(compact({
        '@type': 'Book',
        '@id': ids.book,
        name: fill(b.name),
        url: canonicalUrl(b.page),
        author: entities.person ? { '@id': ids.person } : (b.author ? { '@type': 'Person', name: fill(b.author) } : ''),
        image: absolute(b.image),
        isbn: b.isbn,
        bookFormat: listValue(b.formats).map((f) => (f.startsWith('http') ? f : 'https://schema.org/' + f)),
        inLanguage: b.language || 'en',
        offers: b.offer ? { '@type': 'Offer', url: b.offer } : ''
      }));
    }
    return { '@context': 'https://schema.org', '@graph': graph };
  };

  /**
   * Head tags for a path as plain descriptors: `{ tag: 'title', text }`, `{ tag: 'meta', attrs }`,
   * `{ tag: 'link', attrs }`, `{ tag: 'script', attrs, text }`.
   */
  const buildHeadTags = (path) => {
    const route = getRoute(path);
    if (!route) return [];
    const url = canonicalUrl(route.path);
    const image = absolute(OG_IMAGE.path);
    const alt = fill(mk.ogImageAlt);
    const { title, description } = route;
    const tags = [
      { tag: 'title', text: title },
      { tag: 'meta', attrs: { name: 'description', content: description } },
      { tag: 'link', attrs: { rel: 'canonical', href: url } },
      { tag: 'meta', attrs: { property: 'og:site_name', content: property.name } },
      { tag: 'meta', attrs: { property: 'og:type', content: route.ogType } },
      { tag: 'meta', attrs: { property: 'og:title', content: title } },
      { tag: 'meta', attrs: { property: 'og:description', content: description } },
      { tag: 'meta', attrs: { property: 'og:url', content: url } },
      { tag: 'meta', attrs: { property: 'og:image', content: image } },
      { tag: 'meta', attrs: { property: 'og:image:width', content: String(OG_IMAGE.width) } },
      { tag: 'meta', attrs: { property: 'og:image:height', content: String(OG_IMAGE.height) } },
      { tag: 'meta', attrs: { property: 'og:image:alt', content: alt } },
      { tag: 'meta', attrs: { property: 'og:locale', content: (property.locale || 'en-US').replace('-', '_') } },
      { tag: 'meta', attrs: { name: 'twitter:card', content: 'summary_large_image' } },
      { tag: 'meta', attrs: { name: 'twitter:title', content: title } },
      { tag: 'meta', attrs: { name: 'twitter:description', content: description } },
      { tag: 'meta', attrs: { name: 'twitter:image', content: image } },
      { tag: 'meta', attrs: { name: 'twitter:image:alt', content: alt } }
    ];
    if (!route.sitemap) tags.push({ tag: 'meta', attrs: { name: 'robots', content: 'noindex, follow' } });
    tags.push({ tag: 'script', attrs: { type: 'application/ld+json' }, text: JSON.stringify(buildJsonLd(route.path)) });
    return tags;
  };

  /** sitemap.xml for every indexable route (or any route list). */
  const buildSitemap = (list = routes) => {
    const urls = list.filter((route) => route.sitemap).map((route) => [
      '  <url>',
      '    <loc>' + escapeHtml(canonicalUrl(route.path)) + '</loc>',
      route.changefreq ? '    <changefreq>' + route.changefreq + '</changefreq>' : null,
      route.priority ? '    <priority>' + route.priority + '</priority>' : null,
      '  </url>'
    ].filter(Boolean).join('\n'));
    return '<?xml version="1.0" encoding="UTF-8"?>\n'
      + '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n'
      + urls.join('\n') + '\n'
      + '</urlset>\n';
  };

  /** robots.txt allowing the marketing site and disallowing the app, admin console, and API. */
  const buildRobots = () => 'User-agent: *\n'
    + 'Allow: /\n'
    + PUBLIC_MEDIA_PATHS.map((p) => 'Allow: ' + p + '\n').join('')
    + DISALLOWED_PATHS.map((p) => 'Disallow: ' + p + '\n').join('')
    + '\nSitemap: ' + siteUrl + '/sitemap.xml\n';

  return {
    property, siteUrl, pages, routes, entities,
    contentRoutes, legalRoutes,
    footerRoutes: legalRoutes.filter((r) => r.footer),
    navRoutes: contentRoutes.filter((r) => r.nav),
    getRoute, canonicalUrl, buildJsonLd, buildHeadTags, buildSitemap, buildRobots, fill
  };
}

/** Marketing routes for a property's `documents` (each with a slug). */
function documentRoutes(documents, fill) {
  const md = mk.documents;
  return documents.filter((doc) => doc && doc.key && doc.slug).map((doc) => ({
    path: '/' + doc.slug,
    kind: 'legal',
    file: doc.file || '',
    key: doc.key,
    documentKey: doc.key,
    version: doc.version ? String(doc.version) : '',
    footer: doc.footer === true,
    heading: doc.title || doc.key,
    linkLabel: doc.title || doc.key,
    title: fill(md.metaTitle(doc.title || doc.key)),
    description: fill(doc.description || md.metaDescription(doc.title || doc.key)),
    ogType: 'article',
    priority: '0.3',
    changefreq: 'yearly',
    jsonLd: [],
    sitemap: true,
    body: true
  }));
}

/** The build-time Markdown of a legal route (`documents` content, or the legal/ folder file). */
export function legalSource(site, route) {
  if (!route) return '';
  if (route.documentKey) {
    const doc = (site.documents || []).find((d) => d && d.key === route.documentKey);
    return doc && typeof doc.content === 'string' ? doc.content : '';
  }
  return (site.legal || {})[route.file] || '';
}

const cache = new WeakMap();

/** Cached `createSiteMeta(site)`. */
export function siteMetaFor(site) {
  if (!cache.has(site)) cache.set(site, createSiteMeta(site));
  return cache.get(site);
}
