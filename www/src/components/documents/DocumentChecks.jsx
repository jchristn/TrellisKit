import { Fragment } from 'react';
import { documentPath, hasDocumentsList, purchaseDocuments, signupDocuments } from '@trelliskit/shared';
import strings from '../../strings.js';
import { fill } from '../../property/current.js';

const a = strings.auth;
const d = strings.documents;

/** The error key of a document checkbox in a form's error map. */
export function documentErrorKey(key) {
  return 'documents.' + key;
}

/**
 * What a sign-up style form shows for a property: whether it declares a `documents` list, the
 * documents covered by the terms checkbox (`withTerms`), and those with their own checkbox
 * (`separate`). Without a list, `hasList` is false and the form keeps the standard links.
 * @param {object} property
 */
export function signupDocumentsFor(property) {
  return { hasList: hasDocumentsList(property), ...signupDocuments(property) };
}

/**
 * Errors for unchecked separate documents, keyed `documents.<key>`.
 * @param {object[]} separate
 * @param {Record<string, boolean>} checked
 * @param {(title: string) => string} [message]
 */
export function validateDocumentChecks(separate, checked, message = d.acceptRequired) {
  const errors = {};
  for (const doc of separate || []) {
    if (!(checked && checked[doc.key])) errors[documentErrorKey(doc.key)] = message(doc.title || doc.key);
  }
  return errors;
}

/** A link to read a document in full (its public page, in a new tab). */
export function DocumentLink({ doc, children }) {
  const href = documentPath(doc) || '#';
  return (
    <a href={href} target="_blank" rel="noreferrer">
      {children || doc.title || doc.key}
      <span className="visually-hidden">{' ' + strings.marketing.opensInNewTab}</span>
    </a>
  );
}

/** "A, B and C" with each document a link. */
export function DocumentLinks({ documents }) {
  return documents.map((doc, i) => (
    <Fragment key={doc.key}>
      {i > 0 && (i === documents.length - 1 ? a.and : a.commaSpace)}
      <DocumentLink doc={doc} />
    </Fragment>
  ));
}

/** The terms checkbox label: the age statement and links to every document it covers. */
export function TermsLabel({ docs }) {
  if (!docs.hasList) {
    return (
      <span>
        {fill(a.acceptTermsPrefix)}
        <a href="/terms" target="_blank" rel="noreferrer">{a.terms}</a>{a.commaSpace}
        <a href="/privacy" target="_blank" rel="noreferrer">{a.privacy}</a>{a.and}
        <a href="/community-guidelines" target="_blank" rel="noreferrer">{a.guidelines}</a>{a.period}
      </span>
    );
  }
  if (!docs.withTerms.length) return <span>{fill(d.ageOnly)}</span>;
  return <span>{fill(a.acceptTermsPrefix)}<DocumentLinks documents={docs.withTerms} />{a.period}</span>;
}

/** The documents a purchase screen links (acceptance `ShownAtPurchase`), or nothing. */
export function PurchaseDocumentsNote({ property, className = 'muted small' }) {
  const docs = purchaseDocuments(property);
  if (!docs.length) return null;
  return <p className={className + ' purchase-documents'}>{d.purchase.body}<DocumentLinks documents={docs} />{a.period}</p>;
}

/**
 * One required checkbox per separate document, each with a link to read it in full. `errors`
 * holds messages keyed `documents.<key>`.
 * @param {{ idPrefix: string, documents: object[], checked: Record<string, boolean>, onToggle: (key: string, value: boolean) => void, errors?: object, disabled?: boolean }} props
 */
export function DocumentCheckboxes({ idPrefix, documents, checked, onToggle, errors = {}, disabled = false }) {
  return documents.map((doc) => {
    const id = idPrefix + '-doc-' + doc.key;
    const error = errors[documentErrorKey(doc.key)];
    return (
      <div className="field" key={doc.key}>
        <label className="check-field" htmlFor={id}>
          <input id={id} type="checkbox" checked={Boolean(checked && checked[doc.key])} onChange={(e) => onToggle(doc.key, e.target.checked)} disabled={disabled} aria-invalid={Boolean(error)} aria-describedby={error ? id + '-error' : undefined} />
          <span>{d.acceptPrefix}<DocumentLink doc={doc} />{doc.version ? d.versionSuffix(doc.version) : ''}{a.period}</span>
        </label>
        {error && <span id={id + '-error'} className="field-error">{error}</span>}
      </div>
    );
  });
}
