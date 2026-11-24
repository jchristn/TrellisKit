import { useState } from 'react';
import { documentFieldErrors, hasUpdatedDocuments, isUpdatedDocument, pendingDocumentsOf } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useProperty } from '../../context/PropertyContext.jsx';
import { formatDate } from '../../utils/format.js';
import { errorMessage } from '../../utils/api.js';
import { ErrorBanner } from '../States.jsx';
import { DocumentLink, documentErrorKey, validateDocumentChecks } from './DocumentChecks.jsx';

const r = strings.documents.review;

/**
 * The blocking "Review and accept" step: every document in `Me.pendingDocuments` with its title,
 * version, a link to read it in full, and its own checkbox. Accepting calls
 * `POST /api/v1/me/documents/accept` and hands the updated `Me` to `onAccepted`. A stale version
 * (`fieldErrors["documents.<key>"]`) reloads `Me` so the latest version is shown.
 *
 * @param {{ me: object, client: import('@trelliskit/shared').ApiClient, onAccepted: (me: object) => (void|Promise<void>), onReloadMe?: () => Promise<object>, onSignOut?: () => void, headingLevel?: number }} props
 */
function DocumentsReviewPanel({ me, client, onAccepted, onReloadMe = null, onSignOut = null, headingLevel = 1 }) {
  const { fill } = useProperty();
  const pending = pendingDocumentsOf(me);
  const [checked, setChecked] = useState({});
  const [errors, setErrors] = useState({});
  const [formError, setFormError] = useState('');
  const [busy, setBusy] = useState(false);
  const Heading = headingLevel === 2 ? 'h2' : 'h1';

  const submit = async (e) => {
    e.preventDefault();
    const found = validateDocumentChecks(pending, checked);
    setErrors(found);
    setFormError('');
    if (Object.keys(found).length) return;
    setBusy(true);
    try {
      const next = await client.acceptDocuments({ documents: pending.map((d) => ({ key: d.key, version: d.version })) });
      await onAccepted(next);
    } catch (err) {
      const stale = documentFieldErrors(err && err.body && err.body.fieldErrors, 'documents');
      if (Object.keys(stale).length) {
        const mapped = {};
        for (const [key, message] of Object.entries(stale)) mapped[documentErrorKey(key)] = message;
        setErrors(mapped);
        setChecked({});
        setFormError(r.changed);
        if (onReloadMe) await onReloadMe().catch(() => null);
      } else {
        setFormError(errorMessage(err, r.saveError));
      }
      setBusy(false);
    }
  };

  return (
    <section className="panel documents-review" aria-labelledby="documents-review-title">
      <Heading id="documents-review-title">{r.title}</Heading>
      <p className="muted">{fill(r.body)}</p>
      {hasUpdatedDocuments(me) && <p className="muted">{r.updatedBody}</p>}
      <ErrorBanner message={formError} />
      <form className="auth-form" onSubmit={submit} noValidate>
        <ul className="documents-review-list" aria-label={r.listLabel}>
          {pending.map((doc) => {
            const id = 'review-doc-' + doc.key;
            const error = errors[documentErrorKey(doc.key)];
            return (
              <li key={doc.key} className="documents-review-item">
                <div className="documents-review-head">
                  <strong>{doc.title || doc.key}</strong>
                  <span className="muted small">
                    {doc.version ? r.version(doc.version) : ''}
                    {/* "Updated" only when this version replaces one the member accepted. */}
                    {doc.version && doc.updatedUtc && isUpdatedDocument(doc) ? ', ' : ''}
                    {doc.updatedUtc && isUpdatedDocument(doc) ? r.updated(formatDate(doc.updatedUtc)) : ''}
                  </span>
                </div>
                <DocumentLink doc={doc}>{r.read}</DocumentLink>
                <label className="check-field" htmlFor={id}>
                  <input id={id} type="checkbox" checked={Boolean(checked[doc.key])} onChange={(ev) => setChecked((v) => ({ ...v, [doc.key]: ev.target.checked }))} disabled={busy} aria-invalid={Boolean(error)} aria-describedby={error ? id + '-error' : undefined} />
                  <span>{r.accept(doc.title || doc.key)}</span>
                </label>
                {error && <span id={id + '-error'} className="field-error">{error}</span>}
              </li>
            );
          })}
        </ul>
        <div className="form-actions">
          <button type="submit" className="button button-primary" disabled={busy || pending.length === 0}>{busy ? r.submitting : r.submit}</button>
          {onSignOut && <button type="button" className="button button-secondary" onClick={onSignOut} disabled={busy}>{r.signOut}</button>}
        </div>
      </form>
    </section>
  );
}

export default DocumentsReviewPanel;
