import { test } from 'node:test';
import assert from 'node:assert/strict';
import { ApiClient, catalogEntryBody, catalogEntryForm, loadCatalogRelationships } from '../src/index.js';

test('new catalog entry accepts an absent object', () => {
  assert.equal(catalogEntryForm(null).name, '');
  assert.equal(catalogEntryForm(null).kind, 'Exercise');
});

test('catalog entry edits retain ordered instructions, sources and metadata with optimistic revision', () => {
  const entry = { name: 'Example movement', kind: 'Technique', instructions: ['Set up', 'Move', 'Set up'], lessonIds: ['les_example'], sources: [{ url: 'https://example.test/source', title: 'Source', license: 'CC0', attribution: 'Contributor', method: 'Model', model: 'example-model', retrievedUtc: '2026-11-23T00:00:00Z' }], labels: ['reviewed'], tags: { category: 'example' }, revision: 7 };
  const form = catalogEntryForm(entry);
  form.summary = '  Changed  ';
  const body = catalogEntryBody(form, entry.revision);
  assert.deepEqual(body.instructions, entry.instructions);
  assert.deepEqual(body.sources, entry.sources);
  assert.deepEqual(body.lessonIds, entry.lessonIds);
  assert.deepEqual(body.labels, entry.labels);
  assert.deepEqual(body.tags, entry.tags);
  assert.equal(body.expectedRevision, 7);
  assert.equal(body.summary, 'Changed');
  assert.equal(body.status, undefined);
});

test('catalog API escapes ids, bounds enumeration, separates public requests and passes review revision', async () => {
  const client = new ApiClient({ baseUrl: 'https://example.test/api/v1' });
  const calls = [];
  client.post = async (...args) => calls.push(args);
  client.get = async (...args) => calls.push(args);
  await client.enumeratePublicCatalogEntries({ maxResults: 999, filters: { kind: 'Technique' }, labels: ['a'], tags: { category: 'example' } });
  assert.equal(calls[0][0], '/public/catalog/entries/enumerate');
  assert.equal(calls[0][1].maxResults, 100);
  assert.equal(calls[0][2].retryOnUnauthorized, false);
  await client.getCatalogEntry('a/b');
  assert.equal(calls[1][0], '/catalog/entries/a%2Fb');
  await client.adminApproveCatalogEntry('a/b', { expectedRevision: 4, note: 'Checked sources' });
  assert.deepEqual(calls[2].slice(0, 2), ['/admin/catalog/entries/a%2Fb/approve', { expectedRevision: 4, note: 'Checked sources' }]);
});

test('relationship aggregate loads every bounded page and refuses partial/looped data', async () => {
  const client = { adminEnumerateCatalogRelationships: async (_id, q) => q.continuationToken ? { objects: [{ id: 'b' }], endOfResults: true } : { objects: [{ id: 'a' }], continuationToken: 'next' } };
  assert.deepEqual(await loadCatalogRelationships(client, 'entry'), [{ id: 'a' }, { id: 'b' }]);
  await assert.rejects(() => loadCatalogRelationships({ adminEnumerateCatalogRelationships: async () => ({ objects: [], continuationToken: 'loop' }) }, 'entry'), /Incomplete/);
  await assert.rejects(() => loadCatalogRelationships({ adminEnumerateCatalogRelationships: async () => ({ error: 'bad' }) }, 'entry'), /Incomplete/);
});

test('catalog writes normalize address casing without silently truncating invalid addresses', () => {
  assert.equal(catalogEntryBody(catalogEntryForm({ slug: ' Example-Movement ' })).slug, 'example-movement');
  const long = 'a'.repeat(81);
  assert.equal(catalogEntryBody(catalogEntryForm({ slug: long })).slug, long);
});

test('catalog import sources use bounded metadata enumeration and revisioned explicit review', async () => {
  const client = new ApiClient({ baseUrl: 'https://example.test/api/v1' }); const calls = [];
  client.post = async (...args) => calls.push(args); client.put = async (...args) => calls.push(args);
  await client.adminEnumerateCatalogImportSources({ maxResults: 999, labels: ['source'], filters: { reviewed: 'true' } });
  assert.equal(calls[0][0], '/admin/catalog/import-sources/enumerate'); assert.equal(calls[0][1].maxResults, 100);
  await client.adminReviewCatalogImportSource('a/b', { expectedRevision: 9, licenseReviewed: true });
  assert.deepEqual(calls[1].slice(0, 2), ['/admin/catalog/import-sources/a%2Fb/review', { expectedRevision: 9, licenseReviewed: true }]);
  await client.adminUpdateCatalogImportSource('a/b', { expectedRevision: 10, enabled: false });
  assert.equal(calls[2][0], '/admin/catalog/import-sources/a%2Fb');
});

test('modality writes preserve omitted legacy fields, explicitly clear, and normalize Unicode without changing scripts', async () => {
  const { catalogModalityProblem, catalogDescriptorOptions } = await import('../src/exerciseCatalog.js');
  assert.equal(Object.hasOwn(catalogEntryBody(catalogEntryForm({})), 'modalityDetails'), false);
  assert.equal(Object.hasOwn(catalogEntryBody({ ...catalogEntryForm({}), modalityDetails: null }), 'modalityDetails'), false);
  assert.deepEqual(catalogEntryBody({ ...catalogEntryForm({}), modalityDetails: {} }).modalityDetails, { localizedNames: [], familyKey: null, attireKey: null, levelKey: null, notationSystemKey: null, notationText: null });
  const details = { familyKey: 'standing', localizedNames: [{ languageTag: 'en', text: '  Cafe\u0301  ' }, { languageTag: 'en', text: 'Other name' }] };
  const config = { descriptorProfiles: [{ programKey: 'general', families: [{ key: 'standing', label: 'Standing' }], nameLanguages: [{ key: 'en', label: 'English' }] }] };
  const body = catalogEntryBody(catalogEntryForm({ modalityDetails: details }), 8);
  assert.equal(body.modalityDetails.localizedNames[0].text, 'Café');
  assert.equal(body.expectedRevision, 8);
  assert.equal(details.localizedNames[0].text, '  Cafe\u0301  ');
  assert.equal(catalogModalityProblem(details, config, 'general'), null);
  assert.equal(catalogModalityProblem(details, config, 'other'), 'familyKey');
  assert.equal(catalogModalityProblem({ localizedNames: [...details.localizedNames, { languageTag: 'en', text: 'Café' }] }, config, 'general'), 'duplicate');
  assert.equal(catalogModalityProblem({ localizedNames: [{ languageTag: 'xx', text: 'Text' }] }, config, 'general'), 'language');
  assert.equal(catalogModalityProblem({ localizedNames: [{ languageTag: 'en', text: ' ' }] }, config, 'general'), 'text');
  assert.equal(catalogModalityProblem({ localizedNames: Array(11).fill(details.localizedNames[0]) }, config, 'general'), 'count');
  assert.deepEqual(catalogDescriptorOptions(config, 'other', 'families', 'retired'), [{ key: 'retired', label: 'retired' }]);
});

test('typed program descriptors round-trip with names, preserve fetched members, and require paired literal notation', async () => {
  const { catalogModalityProblem } = await import('../src/exerciseCatalog.js');
  const profile = { programKey: 'general', attires: [{ key: 'standard', label: 'Standard' }], levels: [{ key: 'intro', label: 'Introductory' }], notationSystems: [{ key: 'sample', label: 'Sample notation' }] };
  const config = { descriptorProfiles: [profile] };
  const details = { localizedNames: [], familyKey: null, attireKey: 'standard', levelKey: 'intro', notationSystemKey: 'sample', notationText: '  1 → 2\nR + L  ', futureField: 'retained' };
  const body = catalogEntryBody(catalogEntryForm({ modalityDetails: details }), 12);
  assert.deepEqual(body.modalityDetails, { ...details, notationText: '1 → 2\nR + L' });
  assert.equal(catalogModalityProblem(details, config, 'general'), null);
  for (const key of ['attireKey', 'levelKey', 'notationSystemKey']) assert.equal(catalogModalityProblem({ [key]: 'retired' }, config, 'general'), key);
  assert.equal(catalogModalityProblem({ notationSystemKey: 'sample' }, config, 'general'), 'notationPair');
  assert.equal(catalogModalityProblem({ notationText: '1-2' }, config, 'general'), 'notationPair');
  assert.equal(catalogModalityProblem({ notationSystemKey: 'sample', notationText: 'x'.repeat(501) }, config, 'general'), 'notationText');
  assert.equal(catalogModalityProblem({ notationSystemKey: 'sample', notationText: 'x'.repeat(500) }, config, 'general'), null);
  assert.equal(body.expectedRevision, 12);
  assert.equal(details.notationText, '  1 → 2\nR + L  ');
});

test('apparatus setup is a separate omission-preserving typed aggregate with literal Unicode and independent clear', async () => {
  const { CatalogApparatusFields } = await import('../src/exerciseCatalog.js');
  const setup = { apparatusKey: ' apparatus ', manufacturer: ' Cafe\u0301 maker ', model: ' Sample model ', setupText: ' Position A\nPosition B ', springSetupText: ' Setting A ' };
  const form = catalogEntryForm({ apparatusSetup: setup, modalityDetails: { familyKey: 'standing', localizedNames: [] } });
  const body = catalogEntryBody(form, 14);
  assert.deepEqual(body.apparatusSetup, { apparatusKey: 'apparatus', manufacturer: 'Café maker', model: 'Sample model', setupText: 'Position A\nPosition B', springSetupText: 'Setting A' });
  assert.equal(body.expectedRevision, 14);
  assert.equal(setup.apparatusKey, ' apparatus ');
  for (const value of [undefined, null]) assert.equal(Object.hasOwn(catalogEntryBody(catalogEntryForm({ apparatusSetup: value })), 'apparatusSetup'), false);
  form.apparatusSetup = {};
  const cleared = catalogEntryBody(form, 14);
  assert.deepEqual(cleared.apparatusSetup, Object.fromEntries(CatalogApparatusFields.map((key) => [key, null])));
  assert.equal(cleared.modalityDetails.familyKey, 'standing');
});

test('apparatus requires configured selected equipment and bounded source-specific spring context', async () => {
  const { catalogApparatusOptions, catalogApparatusProblem } = await import('../src/exerciseCatalog.js');
  const config = { equipment: [{ key: 'apparatus', label: 'Example apparatus' }, { key: 'other', label: 'Other equipment' }] };
  const setup = { apparatusKey: 'apparatus', manufacturer: 'Example maker', model: 'Example model', springSetupText: 'Literal setting' };
  assert.equal(catalogApparatusProblem(setup, config, ['apparatus']), null);
  assert.equal(catalogApparatusProblem(setup, config, 'apparatus\nother'), null);
  assert.equal(catalogApparatusProblem(setup, config, []), 'apparatusKey');
  assert.equal(catalogApparatusProblem(setup, { equipment: [] }, ['apparatus']), 'apparatusKey');
  assert.equal(catalogApparatusProblem({ setupText: 'Position A' }, config, []), 'apparatusKey');
  assert.equal(catalogApparatusProblem({ ...setup, manufacturer: null }, config, ['apparatus']), 'modelManufacturer');
  assert.equal(catalogApparatusProblem({ ...setup, model: null }, config, ['apparatus']), 'springContext');
  for (const key of ['manufacturer', 'model', 'setupText', 'springSetupText']) {
    const max = key.endsWith('Text') ? 500 : 120;
    assert.equal(catalogApparatusProblem({ ...setup, [key]: 'x'.repeat(max) }, config, ['apparatus']), null);
    assert.equal(catalogApparatusProblem({ ...setup, [key]: 'x'.repeat(max + 1) }, config, ['apparatus']), key);
  }
  assert.deepEqual(catalogApparatusOptions(config, ['apparatus'], 'retired'), [{ key: 'apparatus', label: 'Example apparatus' }, { key: 'retired', label: 'retired' }]);
  assert.deepEqual(catalogApparatusOptions(config, [], 'apparatus'), [{ key: 'apparatus', label: 'Example apparatus' }]);
});

test('the catalog feature is off unless the property turns it on', async () => {
  const { propertyFeatureEnabled, CatalogFeatures, AccessFeature } = await import('../src/index.js');
  assert.equal(propertyFeatureEnabled({ features: {} }, 'catalog'), false);
  assert.equal(propertyFeatureEnabled({ features: { catalog: true } }, 'catalog'), true);
  assert.equal(propertyFeatureEnabled({ features: {} }, 'library'), true);
  assert.ok(CatalogFeatures.includes(AccessFeature.Catalog));
});
