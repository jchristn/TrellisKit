/**
 * Required documents (the property's `documents` list, docs/TRELLISKIT.md and the documents
 * contract). Pure helpers with no React Native dependency: which documents the sign-up form
 * covers with the terms checkbox, which get their own checkbox, which the purchase screens link,
 * and which a signed-in member still has to accept (`Me.pendingDocuments`).
 *
 * The documents come from the property the app is showing: the live copy from
 * GET /api/v1/property (current versions) merged over the bundled copy.
 */

/** Acceptance modes of a document. */
export const DocumentAcceptance = Object.freeze({
  RequiredAtSignup: 'RequiredAtSignup',
  AcceptedWithTerms: 'AcceptedWithTerms',
  ShownAtPurchase: 'ShownAtPurchase',
  InformationOnly: 'InformationOnly'
});

/** The most documents a property declares (and the app shows). */
export const MaxDocuments = 30;

/** The error code of a route refused until the member accepts pending documents. */
export const DocumentAcceptanceRequired = 'DocumentAcceptanceRequired';

const KEY = /^[A-Za-z0-9_-]{1,64}$/;

function isDocument(value) {
  return Boolean(value && typeof value === 'object' && typeof value.key === 'string' && KEY.test(value.key));
}

/**
 * The property's documents, in configuration order (valid entries only, at most MaxDocuments).
 * @param {object} property
 * @returns {object[]} DocumentSummary-like objects.
 */
export function documentsOf(property) {
  const list = property && Array.isArray(property.documents) ? property.documents : [];
  return list.filter(isDocument).slice(0, MaxDocuments);
}

/**
 * One document of the property by key, or null.
 * @param {object} property
 * @param {string} key
 */
export function documentByKey(property, key) {
  return documentsOf(property).find((d) => d.key === key) || null;
}

/**
 * Documents the terms checkbox accepts (`acceptTerms: true`): every `AcceptedWithTerms` document
 * and every `RequiredAtSignup` document without its own checkbox.
 * @param {object} property
 */
export function termsDocuments(property) {
  return documentsOf(property).filter((d) => d.acceptance === DocumentAcceptance.AcceptedWithTerms || (d.acceptance === DocumentAcceptance.RequiredAtSignup && !d.separateCheckbox));
}

/**
 * Documents with their own required checkbox at sign-up (`RequiredAtSignup` and
 * `separateCheckbox`), sent in `acceptedDocuments`.
 * @param {object} property
 */
export function separateDocuments(property) {
  return documentsOf(property).filter((d) => d.acceptance === DocumentAcceptance.RequiredAtSignup && d.separateCheckbox === true);
}

/**
 * Documents linked on the purchase screens (`ShownAtPurchase`).
 * @param {object} property
 */
export function purchaseDocuments(property) {
  return documentsOf(property).filter((d) => d.acceptance === DocumentAcceptance.ShownAtPurchase);
}

/**
 * Documents the paywall links: the Terms and Privacy documents (store review asks for both next
 * to a subscription) followed by every `ShownAtPurchase` document.
 * @param {object} property
 */
export function paywallDocuments(property) {
  const all = documentsOf(property);
  const legal = all.filter((d) => (d.kind === 'Terms' || d.kind === 'Privacy') && d.acceptance !== DocumentAcceptance.ShownAtPurchase);
  return [...legal, ...purchaseDocuments(property)];
}

/**
 * The `acceptedDocuments` body of sign-up: `[{ key, version }]` for the checked documents.
 * @param {object[]} documents Documents with their own checkbox.
 * @param {Record<string, boolean>} checked Checkbox state by key.
 * @param {Record<string, string>} [versions] Newer versions learned from the server, by key.
 * @returns {{ key: string, version: string }[]}
 */
export function acceptedDocumentsBody(documents, checked, versions = {}) {
  return (documents || [])
    .filter((d) => checked && checked[d.key])
    .map((d) => ({ key: d.key, version: String((versions && versions[d.key]) || d.version || '') }));
}

/**
 * The documents a signed-in member still has to accept (`Me.pendingDocuments`), with any missing
 * title or version filled from the property (a `403 DocumentAcceptanceRequired` names only keys).
 * @param {object|null} user Me.
 * @param {object} [property]
 * @returns {object[]}
 */
export function pendingDocumentsOf(user, property) {
  const list = user && Array.isArray(user.pendingDocuments) ? user.pendingDocuments : [];
  return list
    .filter(isDocument)
    .slice(0, MaxDocuments)
    .map((d) => {
      const known = property ? documentByKey(property, d.key) : null;
      return known ? { ...known, ...stripEmpty(d) } : d;
    });
}

function stripEmpty(value) {
  const out = {};
  for (const [k, v] of Object.entries(value)) if (v !== null && v !== undefined && v !== '') out[k] = v;
  return out;
}

/**
 * Whether the member has documents to accept before continuing.
 * @param {object|null} user Me.
 */
export function hasPendingDocuments(user) {
  return pendingDocumentsOf(user).length > 0;
}

/**
 * Whether an error is `403 DocumentAcceptanceRequired`.
 * @param {any} error
 */
export function isDocumentAcceptanceRequired(error) {
  return Boolean(error && error.status === 403 && error.code === DocumentAcceptanceRequired);
}

/**
 * Document keys named by a `403 DocumentAcceptanceRequired` body.
 * @param {any} error
 * @returns {string[]}
 */
export function documentKeysOf(error) {
  const keys = error && error.body && Array.isArray(error.body.documents) ? error.body.documents : [];
  return keys.filter((k) => typeof k === 'string' && KEY.test(k)).slice(0, MaxDocuments);
}

/**
 * The in-app route that shows a document in full.
 * @param {string} key
 */
export function documentPath(key) {
  return '/documents/' + encodeURIComponent(key);
}

/**
 * Join document titles for a sentence: "A", "A and B", "A, B, and C".
 * @param {string[]} titles
 */
export function joinTitles(titles) {
  const list = (titles || []).filter(Boolean);
  if (list.length <= 1) return list[0] || '';
  if (list.length === 2) return list[0] + ' and ' + list[1];
  return list.slice(0, -1).join(', ') + ', and ' + list[list.length - 1];
}

/**
 * Prepare document Markdown for the app's renderer: the leading `#` heading (the title, shown
 * by the screen) is dropped, other `#` headings become `##`, and deeper headings become `###`.
 * @param {string} content
 * @returns {string}
 */
export function documentMarkdown(content) {
  const lines = String(content || '').replace(/\r\n?/g, '\n').split('\n');
  const first = lines.findIndex((l) => l.trim());
  if (first >= 0 && /^# +/.test(lines[first])) lines.splice(first, 1);
  return lines
    .map((line) => {
      const match = /^(#{1,6}) +(.*)$/.exec(line);
      if (!match) return line;
      return (match[1].length <= 2 ? '## ' : '### ') + match[2];
    })
    .join('\n')
    .trim();
}

/**
 * A publish time as a long date ("November 12, 2026"), or ''.
 * @param {string} iso
 */
export function formatDocumentDate(iso) {
  if (!iso) return '';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '';
  return date.toLocaleDateString(undefined, { year: 'numeric', month: 'long', day: 'numeric' });
}
