import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  ApiClient, PublicPageKind, deriveSlug, isValidSlug, normalizeSlugInput, publicPagePath, publicPagePathWord, publicPageUrl, publicPagesConfig, shareUrlOf, slugProblem
} from '../src/index.js';

const property = { domains: { web: 'example.test' }, publicPages: { venuesPath: 'places' } };

test('publicPagesConfig fills the engine defaults', () => {
  assert.deepEqual(publicPagesConfig({}), { enabled: true, venuesPath: 'venues', eventsPath: 'events', profilesPath: 'profiles', catalogPath: 'catalog' });
  assert.equal(publicPagesConfig(property).venuesPath, 'places');
  assert.equal(publicPagePathWord({ publicPages: { catalogPath: 'techniques' } }, PublicPageKind.Catalog), 'techniques');
  assert.equal(publicPagesConfig({ publicPages: { enabled: false } }).enabled, false);
});

test('slug rules: length, single hyphens, reserved words', () => {
  assert.equal(slugProblem(''), 'required');
  assert.equal(slugProblem('ab'), 'length');
  assert.equal(slugProblem('a'.repeat(81)), 'length');
  assert.equal(slugProblem('double--hyphen'), 'format');
  assert.equal(slugProblem('-leading'), 'format');
  assert.equal(slugProblem('Upper'), 'format');
  assert.equal(slugProblem('admin'), 'reserved');
  assert.ok(isValidSlug('example-club-exampleville-ex'));
});

test('deriveSlug joins name, locality, and region', () => {
  assert.equal(deriveSlug('Example Club', 'Exampleville', 'EX'), 'example-club-exampleville-ex');
  assert.equal(deriveSlug('Café  &  Co.', null, ''), 'cafe-co');
  const long = deriveSlug('word '.repeat(30));
  assert.ok(long.length <= 80 && !long.endsWith('-'));
  assert.equal(normalizeSlugInput('My  Page__Name!'), 'my-page-name');
});

test('page paths and URLs use the configured path words', () => {
  assert.equal(publicPagePath(property, PublicPageKind.Venue, 'example-club'), '/places/example-club');
  assert.equal(publicPagePath(property, PublicPageKind.Event, 'meetup'), '/events/meetup');
  assert.equal(publicPagePath(property, PublicPageKind.Catalog, 'example-movement'), '/catalog/example-movement');
  assert.equal(publicPagePath({ publicPages: { catalogPath: 'movements' } }, PublicPageKind.Catalog, 'example-movement'), '/movements/example-movement');
  assert.equal(publicPageUrl(property, PublicPageKind.Profile, 'sam-member'), 'https://example.test/profiles/sam-member');
  assert.equal(publicPageUrl(property, PublicPageKind.Profile, 'sam-member', 'http://localhost:8080/'), 'http://localhost:8080/profiles/sam-member');
  assert.equal(publicPagePath(property, PublicPageKind.Venue, ''), null);
  assert.equal(shareUrlOf({ publicUrl: 'https://example.test/places/x' }, 'fallback'), 'https://example.test/places/x');
  assert.equal(shareUrlOf({ publicUrl: null }, 'fallback'), 'fallback');
});

test('public page ApiClient methods map to their routes', async () => {
  const calls = [];
  const fetchImpl = async (url, init) => {
    calls.push({ method: init.method, url: url.replace('/api/v1', '') });
    return new Response('{}', { status: 200, headers: { 'content-type': 'application/json' } });
  };
  const client = new ApiClient({ fetchImpl });
  await client.getMyPublicPage();
  await client.updateMyPublicPage({ enabled: true });
  await client.checkPublicPageSlug({ kind: 'Profile', slug: 'sam' });
  await client.adminGetMemberPublicPage('usr_1');
  await client.adminUpdateMemberPublicPage('usr_1', { enabled: false });
  assert.deepEqual(calls.map((c) => c.method + ' ' + c.url), [
    'GET /me/public-page', 'PUT /me/public-page', 'POST /public-pages/slugs/check', 'GET /admin/members/usr_1/public-page', 'PUT /admin/members/usr_1/public-page'
  ]);
});
