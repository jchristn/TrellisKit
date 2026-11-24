import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import {
  affiliationConfig, clampRankLevel, fillTemplate, findReaction, formatRank, formatTemplate, formatUsdCents, goalLabel, goalOptions,
  isAdult, isFreeTextGoal, hasServerCapability, isValidPropertyId, isValidPropertySlug, ServerCapability, propertySlug, isProfileSectionEnabled, loadProperty, mergeProperty, propertyTokens, rankColor, rankLabel, rankOptions,
  reactionList, validateProperty
} from '../src/index.js';

const fixture = (name) => readFileSync(new URL('./fixtures/' + name + '.json', import.meta.url), 'utf8');
const dojo = loadProperty(fixture('dojo'));
const bookclub = loadProperty(fixture('bookclub'));

test('loadProperty parses and validates a property', () => {
  assert.equal(dojo.name, 'Fixture Dojo');
  assert.equal(bookclub.profile.rank.enabled, false);
  assert.throws(() => loadProperty('{nope'), /not valid JSON/);
  assert.throws(() => loadProperty({ ...dojo, schemaVersion: 2 }), /schemaVersion must be 1/);
});

test('validateProperty names every problem', () => {
  assert.deepEqual(validateProperty(dojo), []);
  const broken = JSON.parse(fixture('dojo'));
  broken.brand.colors.light.primary = 'red';
  broken.membership.priceUsdCents = 0;
  broken.profile.rank.values.push({ key: 'Gold', label: 'Gold again' });
  broken.reactions.push({ key: 'x', label: 'X' });
  const problems = validateProperty(broken);
  assert.ok(problems.includes('brand.colors.light.primary must be a #RRGGBB color'));
  assert.ok(problems.includes('membership.priceUsdCents must be a positive whole number'));
  assert.ok(problems.includes('profile.rank.values has a duplicate key "Gold"'));
  assert.ok(problems.includes('reactions[2].emoji is required'));
  assert.deepEqual(validateProperty(null), ['property must be an object']);
});

test('property ids are generated PrettyIds (legacy slug-shaped ids still load) and the slug is cosmetic', () => {
  const generated = 'app_mwcnf2ia_Yk3hhcUgbET5kqGV';
  assert.equal(isValidPropertyId(generated), true);
  assert.equal(isValidPropertyId('dojo'), false);
  for (const bad of ['', 'platform', 'App_x', 'app_UPPER12_abcdefgh', 'has space', 'app_mwcnf2ia_short']) {
    assert.equal(isValidPropertyId(bad), false, bad);
  }
  assert.deepEqual(validateProperty({ ...dojo, id: generated, slug: 'fixture-dojo' }), []);
  assert.ok(validateProperty({ ...dojo, id: 'Bad Id' }).some((p) => p.startsWith('id must be a generated property id')));
  assert.ok(validateProperty({ ...dojo, id: generated, slug: 'Not_A_Slug' }).some((p) => p.startsWith('slug must be')));
  assert.equal(isValidPropertySlug('system'), false);
  assert.equal(propertySlug({ ...dojo, id: generated, slug: 'fixture-dojo' }), 'fixture-dojo');
  assert.equal(propertySlug({ ...dojo, id: generated, slug: undefined, shortName: 'Fixture  Dojo!' }), 'fixture-dojo');
  assert.equal(propertySlug(dojo), 'dojo');
  assert.equal(propertySlug({ id: generated }), 'property');
  assert.equal(propertyTokens({ ...dojo, id: generated, slug: 'fixture-dojo' }).slug, 'fixture-dojo');
});

test('server capabilities are available only when the server reports true', () => {
  assert.equal(hasServerCapability({ features: { forums: true } }, ServerCapability.Forums), true);
  assert.equal(hasServerCapability({ features: { forums: 'true' } }, ServerCapability.Forums), false);
  assert.equal(hasServerCapability({ features: {} }, ServerCapability.Search), false);
  assert.equal(hasServerCapability({ features: { search: false } }, ServerCapability.Search), false);
  assert.equal(hasServerCapability(null, ServerCapability.Search), false);
});

test('rank labels, colors, options, and levels come from configuration', () => {
  assert.equal(rankLabel(dojo, 'Gold'), 'Gold');
  assert.equal(rankLabel(dojo, 'Unknown'), 'Unknown');
  assert.equal(rankLabel(dojo, null), '');
  assert.equal(rankColor(dojo, 'Silver'), '#C0C0C0');
  assert.equal(rankColor(dojo, 'Nope'), null);
  assert.deepEqual(rankOptions(dojo).map((o) => o.value), ['Bronze', 'Silver', 'Gold']);
  assert.equal(clampRankLevel(dojo, 9), 3);
  assert.equal(clampRankLevel(dojo, -1), 0);
  assert.deepEqual(rankOptions(bookclub), []);
});

test('formatRank builds text without hard-coded words', () => {
  assert.equal(formatRank(dojo, 'Gold', 2), 'Gold tier, 2 stars');
  assert.equal(formatRank(dojo, 'Gold', 1), 'Gold tier, 1 star');
  assert.equal(formatRank(dojo, 'Bronze', 0), 'Bronze tier');
  assert.equal(formatRank(dojo, 'Silver', 2, { withLabel: false }), 'Silver, 2 stars');
  assert.equal(formatRank(dojo, null, 2), '');
  assert.equal(formatRank(bookclub, 'Gold', 2), '');
});

test('profile sections can be disabled', () => {
  assert.equal(isProfileSectionEnabled(dojo, 'rankHistory'), true);
  for (const key of ['rank', 'rankHistory', 'affiliation', 'goals']) assert.equal(isProfileSectionEnabled(bookclub, key), false);
  assert.equal(affiliationConfig(dojo).label, 'School');
  assert.equal(isProfileSectionEnabled({}, 'rank'), false);
});

test('goal options and free-text goals', () => {
  assert.deepEqual(goalOptions(dojo).map((g) => g.value), ['Speed', 'Form', 'Else']);
  assert.equal(goalLabel(dojo, 'Else'), 'Something else');
  assert.equal(isFreeTextGoal(dojo, 'Else'), true);
  assert.equal(isFreeTextGoal(dojo, 'Speed'), false);
  assert.deepEqual(goalOptions(bookclub), []);
});

test('reaction lookup', () => {
  assert.equal(findReaction(dojo, 'clap').emoji, '👏');
  assert.equal(findReaction(dojo, 'like'), null);
  assert.equal(reactionList(bookclub).length, 1);
  assert.deepEqual(reactionList({}), []);
});

test('tokens and template filling', () => {
  const tokens = propertyTokens(dojo);
  assert.equal(tokens.name, 'Fixture Dojo');
  assert.equal(tokens.price, '$45.00');
  assert.equal(tokens.priceAmount, '45.00');
  assert.equal(tokens.interval, 'month');
  assert.equal(tokens.minimumAge, '21');
  assert.equal(tokens.siteUrl, 'https://dojo.test');
  assert.equal(tokens.members, 'students');
  assert.equal(tokens.copyrightHolder, tokens.owner, 'copyright holder falls back to the owner name');
  assert.equal(propertyTokens({ ...dojo, owner: { name: 'A Person', copyrightHolder: 'A Company LLC' } }).copyrightHolder, 'A Company LLC');
  assert.equal(propertyTokens({ ...dojo, owner: { name: 'A Person', copyrightHolder: 'A Company LLC' } }).owner, 'A Person');
  assert.equal(fillTemplate('Join {name} for {price} per {interval}.', dojo), 'Join Fixture Dojo for $45.00 per month.');
  assert.equal(fillTemplate('{name}: {page}', bookclub, { page: 'Home' }), 'Fixture Book Club: Home');
  assert.equal(formatTemplate('Keep {unknown} and {name}', { name: 'X' }), 'Keep {unknown} and X');
  assert.equal(formatUsdCents(1250), '$12.50');
});

test('mergeProperty prefers valid runtime values and keeps build-only fields', () => {
  const base = { ...dojo, web: { theme: 'web/theme.css' } };
  const merged = mergeProperty(base, { ...dojo, name: 'Renamed Dojo' });
  assert.equal(merged.name, 'Renamed Dojo');
  assert.deepEqual(merged.web, { theme: 'web/theme.css' });
  assert.equal(mergeProperty(base, { schemaVersion: 99 }), base);
  assert.equal(mergeProperty(base, null), base);
});

test('isAdult honors the property minimum age', () => {
  const today = new Date(2026, 10, 11);
  assert.ok(isAdult(new Date(2005, 10, 11), today, dojo.membership.minimumAge));
  assert.ok(!isAdult(new Date(2005, 10, 12), today, dojo.membership.minimumAge));
  assert.ok(isAdult(new Date(2013, 10, 11), today, bookclub.membership.minimumAge));
});

test('the property document is revalidated on every load (live access model, palette, and AI switch)', async () => {
  const { ApiClient: Client } = await import('../src/ApiClient.js');
  const inits = [];
  const client = new Client({ fetchImpl: async (url, init) => { inits.push({ url, init }); return new Response('{}', { status: 200, headers: { 'content-type': 'application/json' } }); } });
  await client.property();
  assert.equal(inits[0].init.cache, 'no-cache');
  await client.get('/feed');
  assert.equal(inits[1].init.cache, undefined);
});
