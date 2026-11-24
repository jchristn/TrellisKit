import { useState } from 'react';
import { Navigate, useNavigate } from 'react-router-dom';
import strings from '../../strings.js';
import { fill } from '../../property/current.js';
import { useAuth } from '../../context/AuthContext.jsx';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import { authErrorMessage } from '../../utils/authErrors.js';
import { onboardingRedirect } from '../../utils/onboarding.js';
import { routeAfterAuth } from '../../utils/postAuth.js';
import { ErrorBanner } from '../../components/States.jsx';
import { todayIso, validateDateOfBirth } from '../auth/SignUpPage.jsx';
import { buildAcceptedDocuments, documentFieldErrors, hasSeparateCheckbox, pendingDocumentsOf } from '@trelliskit/shared';
import { DocumentCheckboxes, TermsLabel, documentErrorKey, validateDocumentChecks } from '../../components/documents/DocumentChecks.jsx';

const o = strings.onboarding;

/**
 * The documents the age step asks for: the member's pending documents (`Me.pendingDocuments`),
 * split like sign-up into those covered by the terms checkbox and those with their own checkbox.
 */
export function attestationDocuments(me) {
  const pending = pendingDocumentsOf(me);
  return { hasList: true, separate: pending.filter(hasSeparateCheckbox), withTerms: pending.filter((d) => !hasSeparateCheckbox(d)) };
}

/**
 * /app/onboarding/age: required once after a first social sign-in (contract section 2). A social
 * account starts with no document acceptances, so when documents are pending this step also asks
 * for the terms checkbox and each separate document, like email sign-up, and sends them with the
 * attestation (`acceptTerms`, `acceptedDocuments`).
 */
function AgeAttestationView() {
  const auth = useAuth();
  const navigate = useNavigate();
  const [dob, setDob] = useState('');
  const [fieldError, setFieldError] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [acceptTerms, setAcceptTerms] = useState(false);
  const [checked, setChecked] = useState({});
  const [docErrors, setDocErrors] = useState({});
  const [docs] = useState(() => attestationDocuments(auth.me));
  const asksDocuments = docs.separate.length > 0 || docs.withTerms.length > 0;
  useDocumentMeta(o.ageMeta, strings.member.metaDescription);

  if (auth.me && auth.me.ageAttested && !busy) return <Navigate to={onboardingRedirect(auth.me) || '/app'} replace />;

  const submit = async (e) => {
    e.preventDefault();
    const problem = validateDateOfBirth(dob);
    const found = asksDocuments ? validateDocumentChecks(docs.separate, checked) : {};
    if (docs.withTerms.length && !acceptTerms) found.acceptTerms = strings.auth.errors.termsRequired;
    setFieldError(problem);
    setDocErrors(found);
    setError('');
    if (problem || Object.keys(found).length) return;
    setBusy(true);
    try {
      const extra = asksDocuments ? { acceptTerms: docs.withTerms.length > 0 && acceptTerms, acceptedDocuments: buildAcceptedDocuments(docs.separate, checked) } : null;
      const me = await auth.api.attestAge(dob, extra);
      auth.setMe(me);
      const path = await routeAfterAuth({ me, api: auth.api, setMe: auth.setMe, next: '/app' });
      navigate(path, { replace: true });
    } catch (err) {
      if (err && err.code === 'AgeRequirementNotMet') {
        navigate('/account-deleted', { replace: true, state: { title: o.ageTitle, message: fill(o.ageClosed), endSession: true } });
        return;
      }
      const mapped = {};
      for (const [key, message] of Object.entries(documentFieldErrors(err && err.body && err.body.fieldErrors))) mapped[documentErrorKey(key)] = message;
      setDocErrors(mapped);
      setError(authErrorMessage(err));
      setBusy(false);
    }
  };

  return (
    <div className="member-view onboarding">
      <p className="muted small">{o.stepLabel(1, 3)}</p>
      <section className="panel">
        <h1>{o.ageTitle}</h1>
        <p className="muted">{fill(o.ageBody)}</p>
        <ErrorBanner message={error} />
        <form className="auth-form" onSubmit={submit} noValidate>
          <div className="field">
            <label className="label-text" htmlFor="age-dob">{strings.auth.dateOfBirth}</label>
            <input id="age-dob" type="date" autoComplete="bday" max={todayIso()} value={dob} onChange={(e) => setDob(e.target.value)} disabled={busy} aria-invalid={Boolean(fieldError)} aria-describedby={fieldError ? 'age-dob-error' : undefined} />
            {fieldError && <span id="age-dob-error" className="field-error">{fieldError}</span>}
          </div>
          {docs.withTerms.length > 0 && (
            <div className="field">
              <label className="check-field" htmlFor="age-acceptTerms">
                <input id="age-acceptTerms" type="checkbox" checked={acceptTerms} onChange={(e) => setAcceptTerms(e.target.checked)} disabled={busy} aria-invalid={Boolean(docErrors.acceptTerms)} aria-describedby={docErrors.acceptTerms ? 'age-acceptTerms-error' : undefined} />
                <TermsLabel docs={docs} />
              </label>
              {docErrors.acceptTerms && <span id="age-acceptTerms-error" className="field-error">{docErrors.acceptTerms}</span>}
            </div>
          )}
          <DocumentCheckboxes idPrefix="age" documents={docs.separate} checked={checked} onToggle={(key, value) => setChecked((v) => ({ ...v, [key]: value }))} errors={docErrors} disabled={busy} />
          <div className="form-actions">
            <button type="submit" className="button button-primary" disabled={busy}>{busy ? o.ageSubmitting : o.ageSubmit}</button>
          </div>
        </form>
      </section>
    </div>
  );
}

export default AgeAttestationView;
