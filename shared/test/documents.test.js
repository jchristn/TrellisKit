import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  ApiClient, ApiError, documentsMethods, documentsOf, hasDocumentsList, isGatingDocument, hasSeparateCheckbox, signupDocuments, footerDocuments, purchaseDocuments,
  findDocumentByKey, findDocumentBySlug, documentPath, mergeLiveDocuments, buildAcceptedDocuments, pendingDocumentsOf, hasPendingDocuments, documentAcceptanceRequiredKeys,
  isDocumentAcceptanceRequired, documentFieldErrors, documentPublishBody, validatePublishDocument, DOCUMENTS_MAX, DocumentAcceptanceMode, loadProperty
} from '../src/index.js';

const SAMPLE = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', 'samples', 'example', 'property.json');

function recorder() {
  const calls = [];
  const fetchImpl = async (url, init) => {
    calls.push({ method: init.method, url: url.replace('/api/v1', ''), body: init.body ? JSON.parse(init.body) : undefined });
    return new Response(JSON.stringify({ ok: true }), { status: 200, headers: { 'content-type': 'application/json' } });
  };
  return { calls, client: new ApiClient({ fetchImpl }) };
}

const DOCS = [
  { key: 'terms', title: 'Terms', version: '1.0', kind: 'Terms', acceptance: 'AcceptedWithTerms', separateCheckbox: false, slug: 'terms', footer: true },
  { key: 'privacy', title: 'Privacy', version: '1.0', kind: 'Privacy', acceptance: 'RequiredAtSignup', separateCheckbox: false, slug: 'privacy', footer: true },
  { key: 'waiver', title: 'Waiver', version: '2.0', kind: 'Waiver', acceptance: 'RequiredAtSignup', separateCheckbox: true, slug: 'waiver', footer: false },
  { key: 'refund', title: 'Refunds', version: '1.0', kind: 'Refund', acceptance: 'ShownAtPurchase', slug: 'refunds', footer: true },
  { key: 'deletion', title: 'Deletion', version: '1.0', kind: 'AccountDeletion', acceptance: 'InformationOnly', slug: 'delete-account' }
];

test('documents methods map to the contract routes', async () => {
  const names = ['getPublicDocument', 'acceptDocuments', 'enumerateMyDocumentAcceptances', 'adminListDocuments', 'adminGetDocument', 'adminEnumerateDocumentVersions',
    'adminGetDocumentVersion', 'adminPublishDocumentVersion', 'adminEnumerateDocumentAcceptances', 'platformListDocuments', 'platformGetDocument',
    'platformEnumerateDocumentVersions', 'platformGetDocumentVersion', 'platformPublishDocumentVersion', 'platformEnumerateDocumentAcceptances'];
  for (const name of names) assert.equal(typeof ApiClient.prototype[name], 'function', name);
  assert.equal(Object.keys(documentsMethods).length, names.length);

  const { calls, client } = recorder();
  await client.getPublicDocument('waiver');
  await client.acceptDocuments({ documents: [{ key: 'waiver', version: '2.0', title: 'ignored' }] });
  await client.acceptDocuments({ acceptTerms: true, documents: [] });
  await client.enumerateMyDocumentAcceptances({ filters: { documentKey: 'waiver' }, maxResults: 999 });
  await client.adminListDocuments();
  await client.adminGetDocument('a b');
  await client.adminEnumerateDocumentVersions('waiver', { ordering: 'CreatedDescending', maxResults: 999 });
  await client.adminGetDocumentVersion('waiver', 'dcv_1');
  await client.adminPublishDocumentVersion('waiver', { version: ' 2.1 ', content: '# W', changeSummary: ' Fix ', title: '', requireReacceptance: false });
  await client.adminEnumerateDocumentAcceptances({ search: 'sam', filters: { userId: 'usr_1', method: 'Signup' }, labels: ['x'] });
  await client.platformListDocuments('prp_1');
  await client.platformGetDocument('prp_1', 'terms');
  await client.platformEnumerateDocumentVersions('prp_1', 'terms', {});
  await client.platformGetDocumentVersion('prp_1', 'terms', 'dcv_2');
  await client.platformPublishDocumentVersion('prp_1', 'terms', { version: '3', content: 'x', changeSummary: 'y', title: 'New' });
  await client.platformEnumerateDocumentAcceptances('prp_1', { filters: { documentKey: 'terms' } });
  assert.deepEqual(calls.map((c) => c.method + ' ' + c.url), [
    'GET /public/documents/waiver', 'POST /me/documents/accept', 'POST /me/documents/accept', 'POST /me/documents/acceptances/enumerate', 'POST /admin/documents/enumerate',
    'GET /admin/documents/a%20b', 'POST /admin/documents/waiver/versions/enumerate', 'GET /admin/documents/waiver/versions/dcv_1', 'POST /admin/documents/waiver/versions',
    'POST /admin/documents/acceptances/enumerate', 'POST /platform/properties/prp_1/documents/enumerate', 'GET /platform/properties/prp_1/documents/terms',
    'POST /platform/properties/prp_1/documents/terms/versions/enumerate', 'GET /platform/properties/prp_1/documents/terms/versions/dcv_2',
    'POST /platform/properties/prp_1/documents/terms/versions', 'POST /platform/properties/prp_1/documents/acceptances/enumerate'
  ]);
  assert.deepEqual(calls[1].body, { documents: [{ key: 'waiver', version: '2.0' }] });
  assert.deepEqual(calls[2].body, { documents: [], acceptTerms: true });
  assert.equal(calls[3].body.maxResults, 100);
  assert.deepEqual(calls[3].body.filters, { documentKey: 'waiver' });
  assert.equal(calls[6].body.maxResults, 500);
  assert.equal(calls[6].body.ordering, 'CreatedDescending');
  assert.deepEqual(calls[8].body, { version: '2.1', content: '# W', changeSummary: 'Fix', requireReacceptance: false });
  assert.deepEqual(calls[9].body, { maxResults: 25, search: 'sam', labels: ['x'], labelMatch: 'All', filters: { userId: 'usr_1', method: 'Signup' } });
  assert.deepEqual(calls[14].body, { version: '3', content: 'x', changeSummary: 'y', title: 'New' });
});

test('the publish body and its validation', () => {
  assert.deepEqual(documentPublishBody({ version: '1', content: 'c', changeSummary: 's' }), { version: '1', content: 'c', changeSummary: 's' });
  assert.deepEqual(validatePublishDocument({ version: '', content: ' ', changeSummary: '' }), { version: 'required', content: 'required', changeSummary: 'required' });
  assert.deepEqual(validatePublishDocument({ version: 'x'.repeat(33), content: 'abc', changeSummary: 'y'.repeat(501), title: 't'.repeat(121) }, { maxContent: 2 }),
    { version: 'tooLong', content: 'tooLong', changeSummary: 'tooLong', title: 'tooLong' });
  assert.deepEqual(validatePublishDocument({ version: '1.1', content: '# ok', changeSummary: 'Clarified section 2.' }, { maxContent: 100 }), {});
});

test('sign-up, footer, and purchase documents from a property', () => {
  const property = { documents: DOCS };
  const signup = signupDocuments(property);
  assert.deepEqual(signup.separate.map((d) => d.key), ['waiver']);
  assert.deepEqual(signup.withTerms.map((d) => d.key), ['terms', 'privacy']);
  assert.deepEqual(footerDocuments(property).map((d) => d.key), ['terms', 'privacy', 'refund']);
  assert.deepEqual(purchaseDocuments(DOCS).map((d) => d.key), ['refund']);
  assert.equal(isGatingDocument(DOCS[0]), true);
  assert.equal(isGatingDocument(DOCS[3]), false);
  assert.equal(hasSeparateCheckbox(DOCS[2]), true);
  assert.equal(hasSeparateCheckbox({ ...DOCS[0], separateCheckbox: true }), false);
  // Absent documents: no documents, no gate.
  assert.deepEqual(signupDocuments({}), { separate: [], withTerms: [] });
  assert.deepEqual(documentsOf(null), []);
  assert.equal(hasDocumentsList({}), false);
  assert.equal(hasDocumentsList({ documents: [] }), true);
  // Bounded and tolerant of junk.
  const many = Array.from({ length: 40 }, (_, i) => ({ key: 'd' + i }));
  assert.equal(documentsOf([null, { title: 'no key' }, ...many]).length, DOCUMENTS_MAX);
});

test('the sample property declares a separate-checkbox document and terms documents', () => {
  const property = loadProperty(readFileSync(SAMPLE, 'utf8'));
  const signup = signupDocuments(property);
  assert.ok(signup.separate.length >= 1);
  for (const doc of signup.separate) assert.equal(doc.acceptance, DocumentAcceptanceMode.RequiredAtSignup);
  assert.ok(signup.withTerms.length >= 1);
  for (const doc of documentsOf(property)) assert.match(doc.slug, /^[a-z0-9-]+$/);
});

test('find by key or slug and public paths', () => {
  assert.equal(findDocumentByKey(DOCS, 'waiver').title, 'Waiver');
  assert.equal(findDocumentByKey({ documents: DOCS }, 'nope'), null);
  assert.equal(findDocumentBySlug(DOCS, '/refunds/').key, 'refund');
  assert.equal(findDocumentBySlug(DOCS, 'x'), null);
  assert.equal(documentPath(DOCS[4]), '/delete-account');
  assert.equal(documentPath({ key: 'x' }), '');
});

test('acceptedDocuments uses the live version and merges live summaries', () => {
  const live = [{ key: 'waiver', version: '2.1', title: 'Waiver (updated)' }];
  const signup = signupDocuments(DOCS);
  assert.deepEqual(buildAcceptedDocuments(signup.separate, { waiver: true }, live), [{ key: 'waiver', version: '2.1' }]);
  assert.deepEqual(buildAcceptedDocuments(signup.separate, ['waiver']), [{ key: 'waiver', version: '2.0' }]);
  assert.deepEqual(buildAcceptedDocuments(signup.separate, { waiver: false }), []);
  const merged = mergeLiveDocuments([{ key: 'waiver', file: 'legal/w.md', version: '2.0' }, { key: 'terms', version: '1.0' }], live);
  assert.deepEqual(merged, [{ key: 'waiver', file: 'legal/w.md', version: '2.1', title: 'Waiver (updated)' }, { key: 'terms', version: '1.0' }]);
});

test('pending documents and the DocumentAcceptanceRequired error', () => {
  assert.deepEqual(pendingDocumentsOf(null), []);
  assert.deepEqual(pendingDocumentsOf({ pendingDocuments: [{ key: 'waiver' }, null] }).map((d) => d.key), ['waiver']);
  assert.equal(hasPendingDocuments({ pendingDocuments: [] }), false);
  assert.equal(hasPendingDocuments({ pendingDocuments: [{ key: 'waiver' }] }), true);

  const gate = new ApiError('Accept first.', { status: 403, code: 'DocumentAcceptanceRequired', body: { error: 'DocumentAcceptanceRequired', message: 'Accept first.', documents: ['waiver', 'terms'] } });
  assert.deepEqual(documentAcceptanceRequiredKeys(gate), ['waiver', 'terms']);
  assert.equal(isDocumentAcceptanceRequired(gate), true);
  assert.deepEqual(documentAcceptanceRequiredKeys(new ApiError('x', { status: 403, code: 'DocumentAcceptanceRequired', body: null })), []);
  assert.equal(documentAcceptanceRequiredKeys(new ApiError('x', { status: 403, code: 'Forbidden' })), null);
  assert.equal(documentAcceptanceRequiredKeys(new ApiError('x', { status: 400, code: 'DocumentAcceptanceRequired' })), null);
  assert.equal(documentAcceptanceRequiredKeys(null), null);
  assert.equal(documentAcceptanceRequiredKeys(new Error('plain')), null);
});

test('the gate error from a real response', async () => {
  const fetchImpl = async () => new Response(JSON.stringify({ error: 'DocumentAcceptanceRequired', message: 'Review and accept.', documents: ['waiver'] }), { status: 403, headers: { 'content-type': 'application/json' } });
  const client = new ApiClient({ fetchImpl });
  await assert.rejects(client.get('/feed'), (err) => {
    assert.deepEqual(documentAcceptanceRequiredKeys(err), ['waiver']);
    return true;
  });
});

test('document field errors by key', () => {
  const fieldErrors = { 'acceptedDocuments.waiver': ['Accept the Waiver to create an account.'], email: 'Bad', 'documents.terms': 'Stale' };
  assert.deepEqual(documentFieldErrors(fieldErrors), { waiver: 'Accept the Waiver to create an account.' });
  assert.deepEqual(documentFieldErrors(fieldErrors, 'documents'), { terms: 'Stale' });
  assert.deepEqual(documentFieldErrors(null), {});
});
