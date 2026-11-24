import { describe, expect, it } from 'vitest';
import { LEGAL_ROUTES, createSiteMeta, headTagsToHtml, normalizePath } from '../src/views/marketing/siteMeta.js';
import strings from '../src/strings.js';
import { loadFixtureSite } from './helpers.jsx';

const dojo = createSiteMeta(loadFixtureSite('dojo'));
const bookclub = createSiteMeta(loadFixtureSite('bookclub'));

describe('route metadata from the property', () => {
  it('lists the content pages, the legal documents present, and /join, all prerendered', () => {
    expect(dojo.routes.map((r) => r.path).sort()).toEqual([
      '/', '/about', '/community-guidelines', '/delete-account', '/guide', '/join', '/membership', '/privacy', '/subscription-terms', '/support', '/terms'
    ]);
    for (const route of dojo.routes) expect(route.prerender).toBe(true);
    // A property without some legal documents or content pages simply has fewer routes.
    expect(bookclub.routes.map((r) => r.path).sort()).toEqual(['/', '/join', '/membership', '/privacy']);
  });

  it('builds titles and descriptions from front matter with property tokens filled', () => {
    expect(dojo.getRoute('/').title).toBe('Fixture Dojo | Practice together');
    expect(dojo.getRoute('/membership').title).toBe('Membership | Fixture Dojo');
    expect(dojo.getRoute('/membership').description).toBe('Fixture Dojo membership is $45.00 per month with full access. Members must be 21 or older.');
    expect(dojo.getRoute('/privacy').title).toBe('Privacy Policy | Fixture Dojo');
    expect(bookclub.getRoute('/privacy').title).toBe('Privacy Policy | Fixture Book Club');
    expect(bookclub.getRoute('/join').title).toBe('Join Fixture Book Club');
    const titles = dojo.routes.map((r) => r.title);
    expect(new Set(titles).size).toBe(titles.length);
  });

  it('orders navigation by navOrder and labels it from front matter', () => {
    expect(dojo.navRoutes.map((r) => [r.path, r.nav])).toEqual([['/guide', 'Guide'], ['/membership', 'Membership'], ['/about', 'About'], ['/support', 'Support']]);
    expect(bookclub.navRoutes.map((r) => r.path)).toEqual(['/membership']);
  });

  it('has engine legal strings that name no property', () => {
    expect(LEGAL_ROUTES.map((r) => r.path)).toEqual(['/privacy', '/terms', '/subscription-terms', '/community-guidelines', '/delete-account']);
    expect(JSON.stringify(strings.marketing)).not.toMatch(/\u2014/);
  });

  it('normalizes paths and builds https canonical URLs on the property web domain', () => {
    expect(normalizePath('/guide/?x=1#top')).toBe('/guide');
    expect(normalizePath('')).toBe('/');
    expect(dojo.canonicalUrl('/')).toBe('https://dojo.test/');
    expect(dojo.canonicalUrl('/terms/')).toBe('https://dojo.test/terms');
    expect(bookclub.canonicalUrl('/privacy')).toBe('https://books.test/privacy');
    expect(dojo.getRoute('/join?code=ABC').path).toBe('/join');
    expect(dojo.getRoute('/app')).toBeNull();
  });

  it('builds title, description, canonical, Open Graph, Twitter, and JSON-LD tags', () => {
    const tags = dojo.buildHeadTags('/membership');
    const find = (key, value) => tags.find((t) => t.attrs && t.attrs[key] === value);
    expect(tags[0]).toEqual({ tag: 'title', text: 'Membership | Fixture Dojo' });
    expect(find('name', 'description').attrs.content).toMatch(/\$45\.00/);
    expect(find('rel', 'canonical').attrs.href).toBe('https://dojo.test/membership');
    expect(find('property', 'og:site_name').attrs.content).toBe('Fixture Dojo');
    expect(find('property', 'og:image').attrs.content).toBe('https://dojo.test/og-image.png');
    expect(find('property', 'og:image:alt').attrs.content).toBe('Fixture Dojo logo');
    expect(find('name', 'twitter:card').attrs.content).toBe('summary_large_image');
    expect(find('name', 'robots')).toBeUndefined();
    expect(tags.find((t) => t.tag === 'script').attrs.type).toBe('application/ld+json');
    expect(dojo.buildHeadTags('/app')).toEqual([]);
    expect(dojo.buildHeadTags('/join').find((t) => t.attrs && t.attrs.name === 'robots').attrs.content).toBe('noindex, follow');
  });

  it('publishes Person and Book JSON-LD only when the property content defines them', () => {
    const types = (meta, path) => meta.buildJsonLd(path)['@graph'].map((n) => n['@type']);
    expect(types(dojo, '/')).toEqual(['Organization', 'WebSite', 'Person', 'Book']);
    expect(types(dojo, '/about')).toEqual(['Organization', 'WebSite', 'Person']);
    expect(types(dojo, '/privacy')).toEqual(['Organization', 'WebSite']);
    const book = dojo.buildJsonLd('/guide')['@graph'].find((n) => n['@type'] === 'Book');
    expect(book.name).toBe('The Fixture Guide');
    expect(book.offers.url).toBe('https://shop.dojo.test/guide');
    expect(book.bookFormat).toEqual(['https://schema.org/Paperback', 'https://schema.org/EBook']);
    const person = dojo.buildJsonLd('/about')['@graph'].find((n) => n['@type'] === 'Person');
    expect(person.name).toBe('Fixture Owner');
    expect(person['@id']).toBe('https://dojo.test/about#person');
    expect(person).not.toHaveProperty('image');
    const org = dojo.buildJsonLd('/')['@graph'][0];
    expect(org).toMatchObject({ name: 'Fixture Dojo', email: 'help@dojo.test', sameAs: ['https://social.dojo.test/fixture'], founder: { '@id': person['@id'] } });
    // The book club defines no person or book: only the organization and site are published.
    expect(types(bookclub, '/')).toEqual(['Organization', 'WebSite']);
    expect(bookclub.buildJsonLd('/')['@graph'][0]).not.toHaveProperty('founder');
  });

  it('escapes attribute values and keeps JSON-LD from closing its script tag', () => {
    const html = headTagsToHtml([
      { tag: 'meta', attrs: { name: 'description', content: 'A "quote" & <tag>' } },
      { tag: 'script', attrs: { type: 'application/ld+json' }, text: '{"x":"</script>"}' }
    ]);
    expect(html).toContain('content="A &quot;quote&quot; &amp; &lt;tag&gt;"');
    expect(html).toContain('\\u003c/script>');
    expect(html.match(/<\/script>/g)).toHaveLength(1);
  });
});

describe('sitemap and robots from domains.web', () => {
  it('lists every indexable route with its canonical URL', () => {
    const xml = dojo.buildSitemap();
    const locs = [...xml.matchAll(/<loc>([^<]+)<\/loc>/g)].map((m) => m[1]);
    expect(locs.sort()).toEqual(dojo.routes.filter((r) => r.path !== '/join').map((r) => 'https://dojo.test' + (r.path === '/' ? '/' : r.path)).sort());
    expect(xml.startsWith('<?xml version="1.0" encoding="UTF-8"?>')).toBe(true);
    expect(bookclub.buildSitemap()).toContain('<loc>https://books.test/privacy</loc>');
  });

  it('builds a sitemap from any route list', () => {
    const xml = dojo.buildSitemap([{ path: '/x', sitemap: true, priority: '0.5' }, { path: '/y', sitemap: false }]);
    expect(xml).toContain('<loc>https://dojo.test/x</loc>');
    expect(xml).toContain('<priority>0.5</priority>');
    expect(xml).not.toContain('/y');
  });

  it('allows the marketing site and disallows the app, admin console, and API', () => {
    const robots = bookclub.buildRobots();
    expect(robots).toContain('Allow: /\n');
    for (const p of ['/app', '/admin', '/api']) expect(robots).toContain('Disallow: ' + p + '\n');
    expect(robots).toContain('Sitemap: https://books.test/sitemap.xml');
  });
});
