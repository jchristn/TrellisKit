/**
 * Required documents (terms, privacy, waivers, and other legal pages a property declares in
 * `property.json` `documents`): API methods mixed into `ApiClient.prototype`, plus pure helpers the
 * website and the mobile app share (which documents sign-up shows, the footer and purchase links,
 * the `acceptedDocuments` body, and the `403 DocumentAcceptanceRequired` gate).
 *
 * Shapes (`DocumentSummary`, `Document`, `DocumentAcceptance`, `DocumentVersion`,
 * `AdminDocument`) follow the documents contract in REST_API.md.
 */

import { ApiError } from './ApiError.js';
import { ENUMERATION_MAX_RESULTS_ADMIN, ENUMERATION_MAX_RESULTS_MEMBER, toEnumerationBody } from './enumeration.js';

const enc = encodeURIComponent;
const ADMIN = { maxLimit: ENUMERATION_MAX_RESULTS_ADMIN };
const MEMBER = { maxLimit: ENUMERATION_MAX_RESULTS_MEMBER };
const NO_RETRY = { retryOnUnauthorized: false };

/** The most documents a property declares (`property.json` `documents`). */
export const DOCUMENTS_MAX = 30;

/** Document kinds. */
export const DocumentKind = Object.freeze({
  Terms: 'Terms', Privacy: 'Privacy', Guidelines: 'Guidelines', Refund: 'Refund', Subscription: 'Subscription', Waiver: 'Waiver', AccountDeletion: 'AccountDeletion', Other: 'Other'
});

/** How a document is accepted (`acceptance`). */
export const DocumentAcceptanceMode = Object.freeze({
  RequiredAtSignup: 'RequiredAtSignup', AcceptedWithTerms: 'AcceptedWithTerms', ShownAtPurchase: 'ShownAtPurchase', InformationOnly: 'InformationOnly'
});

/** How an acceptance was recorded (`DocumentAcceptance.method`). */
export const DocumentAcceptanceMethod = Object.freeze({ Signup: 'Signup', Reacceptance: 'Reacceptance' });

/** The client an acceptance came from (`DocumentAcceptance.client`). */
export const DocumentAcceptanceClient = Object.freeze({ Web: 'Web', Ios: 'Ios', Android: 'Android', Other: 'Other' });

/** Where a document version came from (`DocumentVersion.source`). */
export const DocumentVersionSource = Object.freeze({ Seed: 'Seed', Edit: 'Edit', DirectorySync: 'DirectorySync' });

/** Orderings of the versions enumeration. */
export const DocumentVersionOrdering = Object.freeze({ SequenceDescending: 'SequenceDescending', CreatedDescending: 'CreatedDescending', CreatedAscending: 'CreatedAscending' });

/** Collection filters of the acceptances enumerations. */
export const DOCUMENT_ACCEPTANCE_FILTER_KEYS = Object.freeze(['documentKey', 'userId', 'version', 'method', 'client']);

/** The error code of the document gate. */
export const DOCUMENT_ACCEPTANCE_REQUIRED = 'DocumentAcceptanceRequired';

/** Publish form bounds (contract: `version` 1-32, `changeSummary` 1-500, `title` 1-120). */
export const DOCUMENT_VERSION_MAX = 32;
export const DOCUMENT_CHANGE_SUMMARY_MAX = 500;
export const DOCUMENT_TITLE_MAX = 120;

function enumBody(query, limits) {
  return toEnumerationBody(query || {}, limits);
}

function docPath(key) {
  return '/admin/documents/' + enc(key);
}

function platformBase(propertyId) {
  return '/platform/properties/' + enc(propertyId) + '/documents';
}

/** @this {import('./ApiClient.js').ApiClient} */
export const documentsMethods = {
  // ---- Public ----

  /** GET /public/documents/{key}: the current version with content (anonymous). */
  getPublicDocument(key, signal) { return this.get('/public/documents/' + enc(key), { signal, ...NO_RETRY }); },

  // ---- The signed-in member ----

  /**
   * POST /me/documents/accept with `{ acceptTerms?, documents: [{ key, version }] }`; resolves the
   * updated `MeResponse`.
   */
  acceptDocuments({ acceptTerms, documents = [] } = {}) {
    const body = { documents: (documents || []).map((d) => ({ key: d.key, version: d.version })) };
    if (acceptTerms !== undefined) body.acceptTerms = Boolean(acceptTerms);
    return this.post('/me/documents/accept', body);
  },
  /** POST /me/documents/acceptances/enumerate: the caller's own acceptances (filter `documentKey`). */
  enumerateMyDocumentAcceptances(query = {}, signal) { return this.post('/me/documents/acceptances/enumerate', enumBody(query, MEMBER), { signal }); },

  // ---- Admin console ----

  /** POST /admin/documents/enumerate: document page with syncsFromFolder context. */
  adminListDocuments(query = {}, signal) { return this.post('/admin/documents/enumerate', enumBody(query, ADMIN), { signal }); },
  /** GET /admin/documents/{key}: `AdminDocument` with `content`. */
  adminGetDocument(key, signal) { return this.get(docPath(key), { signal }); },
  /** POST /admin/documents/{key}/versions/enumerate (orderings `SequenceDescending` default, `CreatedDescending`, `CreatedAscending`). */
  adminEnumerateDocumentVersions(key, query = {}, signal) { return this.post(docPath(key) + '/versions/enumerate', enumBody(query, ADMIN), { signal }); },
  /** GET /admin/documents/{key}/versions/{versionId}: one version with `content`. */
  adminGetDocumentVersion(key, versionId, signal) { return this.get(docPath(key) + '/versions/' + enc(versionId), { signal }); },
  /** POST /admin/documents/{key}/versions `{ version, content, changeSummary, title?, requireReacceptance? }`; 201 `AdminDocument`. */
  adminPublishDocumentVersion(key, body) { return this.post(docPath(key) + '/versions', documentPublishBody(body)); },
  /** POST /admin/documents/acceptances/enumerate (filters `documentKey`, `userId`, `version`, `method`, `client`; search on name and email). */
  adminEnumerateDocumentAcceptances(query = {}, signal) { return this.post('/admin/documents/acceptances/enumerate', enumBody(query, ADMIN), { signal }); },

  // ---- Platform console (any property) ----

  /** GET /platform/properties/{propertyId}/documents. */
  platformListDocuments(propertyId, query = {}, signal) { return this.post(platformBase(propertyId) + '/enumerate', enumBody(query, ADMIN), { signal }); },
  /** GET /platform/properties/{propertyId}/documents/{key}. */
  platformGetDocument(propertyId, key, signal) { return this.get(platformBase(propertyId) + '/' + enc(key), { signal }); },
  /** POST /platform/properties/{propertyId}/documents/{key}/versions/enumerate. */
  platformEnumerateDocumentVersions(propertyId, key, query = {}, signal) { return this.post(platformBase(propertyId) + '/' + enc(key) + '/versions/enumerate', enumBody(query, ADMIN), { signal }); },
  /** GET /platform/properties/{propertyId}/documents/{key}/versions/{versionId}. */
  platformGetDocumentVersion(propertyId, key, versionId, signal) { return this.get(platformBase(propertyId) + '/' + enc(key) + '/versions/' + enc(versionId), { signal }); },
  /** POST /platform/properties/{propertyId}/documents/{key}/versions (publish). */
  platformPublishDocumentVersion(propertyId, key, body) { return this.post(platformBase(propertyId) + '/' + enc(key) + '/versions', documentPublishBody(body)); },
  /** POST /platform/properties/{propertyId}/documents/acceptances/enumerate. */
  platformEnumerateDocumentAcceptances(propertyId, query = {}, signal) { return this.post(platformBase(propertyId) + '/acceptances/enumerate', enumBody(query, ADMIN), { signal }); }
};

/**
 * The publish body: trimmed `version` and `changeSummary`, `content` as given, `title` only when
 * set, and `requireReacceptance` only when a boolean.
 * @param {{ version: string, content: string, changeSummary: string, title?: string, requireReacceptance?: boolean }} body
 */
export function documentPublishBody({ version, content, changeSummary, title, requireReacceptance } = {}) {
  const out = { version: String(version || '').trim(), content: String(content || ''), changeSummary: String(changeSummary || '').trim() };
  const cleanTitle = String(title || '').trim();
  if (cleanTitle) out.title = cleanTitle;
  if (typeof requireReacceptance === 'boolean') out.requireReacceptance = requireReacceptance;
  return out;
}

/**
 * Validate the publish form. Returns a map of field to problem code (`required`, `tooLong`).
 * @param {{ version?: string, content?: string, changeSummary?: string, title?: string }} values
 * @param {{ maxContent?: number }} [limits]
 */
export function validatePublishDocument(values = {}, { maxContent = 0 } = {}) {
  const problems = {};
  const version = String(values.version || '').trim();
  const summary = String(values.changeSummary || '').trim();
  const title = String(values.title || '').trim();
  const content = String(values.content || '');
  if (!version) problems.version = 'required';
  else if (version.length > DOCUMENT_VERSION_MAX) problems.version = 'tooLong';
  if (!content.trim()) problems.content = 'required';
  else if (maxContent > 0 && content.length > maxContent) problems.content = 'tooLong';
  if (!summary) problems.changeSummary = 'required';
  else if (summary.length > DOCUMENT_CHANGE_SUMMARY_MAX) problems.changeSummary = 'tooLong';
  if (title.length > DOCUMENT_TITLE_MAX) problems.title = 'tooLong';
  return problems;
}

// ---- Pure helpers over a property's documents ----

/**
 * A property's documents (at most DOCUMENTS_MAX), or `[]` when it declares none. Accepts the
 * property or the list itself. Entries without a `key` are dropped.
 * @param {object|object[]|null|undefined} source
 * @returns {object[]}
 */
export function documentsOf(source) {
  const list = Array.isArray(source) ? source : (source && Array.isArray(source.documents) ? source.documents : []);
  return list.filter((d) => d && typeof d === 'object' && d.key).slice(0, DOCUMENTS_MAX);
}

/** Whether a property declares a `documents` list (even an empty one). */
export function hasDocumentsList(property) {
  return Boolean(property && Array.isArray(property.documents));
}

/** Whether a document gates the account (acceptance `RequiredAtSignup` or `AcceptedWithTerms`). */
export function isGatingDocument(doc) {
  return Boolean(doc) && (doc.acceptance === DocumentAcceptanceMode.RequiredAtSignup || doc.acceptance === DocumentAcceptanceMode.AcceptedWithTerms);
}

/** Whether a document has its own sign-up checkbox (RequiredAtSignup with `separateCheckbox`). */
export function hasSeparateCheckbox(doc) {
  return Boolean(doc) && doc.acceptance === DocumentAcceptanceMode.RequiredAtSignup && doc.separateCheckbox === true;
}

/**
 * The documents sign-up shows: `separate` (each gets its own required checkbox and goes in
 * `acceptedDocuments`) and `withTerms` (covered by the terms checkbox, `acceptTerms: true`).
 * @param {object|object[]} source a property or a documents list
 * @returns {{ separate: object[], withTerms: object[] }}
 */
export function signupDocuments(source) {
  const docs = documentsOf(source);
  return {
    separate: docs.filter(hasSeparateCheckbox),
    withTerms: docs.filter((d) => isGatingDocument(d) && !hasSeparateCheckbox(d))
  };
}

/** The documents the website footer lists (`footer: true`). */
export function footerDocuments(source) {
  return documentsOf(source).filter((d) => d.footer === true);
}

/** The documents purchase screens link (acceptance `ShownAtPurchase`). */
export function purchaseDocuments(source) {
  return documentsOf(source).filter((d) => d.acceptance === DocumentAcceptanceMode.ShownAtPurchase);
}

/** Find a document by key, or null. */
export function findDocumentByKey(source, key) {
  return documentsOf(source).find((d) => d.key === key) || null;
}

/** Find a document by its public slug, or null. */
export function findDocumentBySlug(source, slug) {
  const clean = String(slug || '').replace(/^\/+/, '').replace(/\/+$/, '');
  return documentsOf(source).find((d) => d.slug === clean) || null;
}

/** The public website path of a document (`/<slug>`), or '' without a slug. */
export function documentPath(doc) {
  return doc && doc.slug ? '/' + doc.slug : '';
}

/**
 * Overlay live document summaries (from `GET /property` or `GET /admin/documents`) on a list by
 * key, keeping fields only the base knows (for example the build-time `file`). Documents absent
 * from `live` keep their base values.
 * @param {object[]} base
 * @param {object[]|null|undefined} live
 */
export function mergeLiveDocuments(base, live) {
  const byKey = new Map(documentsOf(live).map((d) => [d.key, d]));
  return documentsOf(base).map((d) => (byKey.has(d.key) ? { ...d, ...byKey.get(d.key) } : d));
}

/**
 * The `acceptedDocuments` body for register and age attestation: `[{ key, version }]` for each
 * accepted document, with the version from `live` when it has the document (the current version
 * the server expects), else the document's own version.
 * @param {object[]} documents the documents shown (for example `signupDocuments(p).separate`)
 * @param {string[]|Record<string, boolean>} accepted the accepted keys, or a map of key to checked
 * @param {object[]} [live] live summaries to take versions from
 * @returns {{ key: string, version: string }[]}
 */
export function buildAcceptedDocuments(documents, accepted, live = null) {
  const keys = Array.isArray(accepted) ? new Set(accepted) : new Set(Object.entries(accepted || {}).filter(([, v]) => v).map(([k]) => k));
  const liveByKey = new Map(documentsOf(live).map((d) => [d.key, d]));
  return documentsOf(documents).filter((d) => keys.has(d.key)).map((d) => {
    const current = liveByKey.get(d.key);
    return { key: d.key, version: String((current && current.version) || d.version || '') };
  });
}

/** The documents a member still has to accept (`Me.pendingDocuments`), or `[]`. */
export function pendingDocumentsOf(me) {
  return me && Array.isArray(me.pendingDocuments) ? me.pendingDocuments.filter((d) => d && d.key) : [];
}

/**
 * Whether a pending document is an update of one the member already accepted (`previouslyAccepted`
 * on `Me.pendingDocuments`). A document the member never accepted is not "updated": clients ask them
 * to "Please review and accept" it. Older servers without the field count as never accepted.
 * @param {object|null} doc
 */
export function isUpdatedDocument(doc) {
  return Boolean(doc && doc.previouslyAccepted === true);
}

/** Whether any pending document replaces a version the member accepted (the "updated documents" wording applies). */
export function hasUpdatedDocuments(me) {
  return pendingDocumentsOf(me).some(isUpdatedDocument);
}

/** Whether a member has documents to accept before continuing. */
export function hasPendingDocuments(me) {
  return pendingDocumentsOf(me).length > 0;
}

/**
 * The document keys of a `403 DocumentAcceptanceRequired` error, or null for any other error.
 * @param {unknown} err
 * @returns {string[]|null}
 */
export function documentAcceptanceRequiredKeys(err) {
  if (!err || typeof err !== 'object') return null;
  const code = err.code || (err.body && err.body.error);
  if (code !== DOCUMENT_ACCEPTANCE_REQUIRED) return null;
  if (err instanceof ApiError && err.status && err.status !== 403) return null;
  const docs = err.body && Array.isArray(err.body.documents) ? err.body.documents : [];
  return docs.map((d) => (typeof d === 'string' ? d : d && d.key)).filter(Boolean);
}

/** Whether an error is the document gate. */
export function isDocumentAcceptanceRequired(err) {
  return documentAcceptanceRequiredKeys(err) !== null;
}

/**
 * Map `fieldErrors` keyed `<prefix>.<key>` (`acceptedDocuments.<key>` from register and age
 * attestation, `documents.<key>` from accept) to a map of document key to message.
 * @param {Record<string, string|string[]>} fieldErrors
 * @param {string} [prefix]
 */
export function documentFieldErrors(fieldErrors, prefix = 'acceptedDocuments') {
  const out = {};
  for (const [field, value] of Object.entries(fieldErrors || {})) {
    if (!field.startsWith(prefix + '.')) continue;
    const key = field.slice(prefix.length + 1);
    if (key) out[key] = Array.isArray(value) ? value.join(' ') : String(value);
  }
  return out;
}
