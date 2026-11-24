import { useState } from 'react';
import { Link, Navigate, useNavigate } from 'react-router-dom';
import { buildAcceptedDocuments, documentFieldErrors, isAdult, isDisplayName, isEmail, isSignupCode, normalizeSignupCode, parseDateOnly, passwordProblems, minimumAgeOf, purchasesEnabled, signupCodesEnabled } from '@trelliskit/shared';
import { useProperty } from '../../context/PropertyContext.jsx';
import strings from '../../strings.js';
import { currentProperty, fill } from '../../property/current.js';
import { useAuth } from '../../context/AuthContext.jsx';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import { getFieldErrors } from '../../utils/api.js';
import { authErrorMessage } from '../../utils/authErrors.js';
import { clearPendingCode, getPendingCode, setPendingCode } from '../../utils/pendingCode.js';
import { routeAfterAuth } from '../../utils/postAuth.js';
import PasswordInput from '../../components/PasswordInput.jsx';
import PasswordRules from '../../components/PasswordRules.jsx';
import SocialButtons from '../../components/SocialButtons.jsx';
import { Banner, ErrorBanner, Loading } from '../../components/States.jsx';
import { DocumentCheckboxes, TermsLabel, documentErrorKey, signupDocumentsFor, validateDocumentChecks } from '../../components/documents/DocumentChecks.jsx';
import AuthCard from './AuthCard.jsx';
import useSocialSignIn from './useSocialSignIn.js';
import useSignupMetric from '../../hooks/useSignupMetric.js';

const a = strings.auth;
const e = a.errors;

/** Today's date as YYYY-MM-DD in local time. */
export function todayIso(today = new Date()) {
  const pad = (n) => String(n).padStart(2, '0');
  return today.getFullYear() + '-' + pad(today.getMonth() + 1) + '-' + pad(today.getDate());
}

/**
 * Validate a date of birth string. Returns an error message or ''.
 * @param {string} value YYYY-MM-DD
 * @param {Date} [today]
 * @param {object} [property] the active property (minimum age and name)
 */
export function validateDateOfBirth(value, today = new Date(), property = currentProperty()) {
  if (!value) return e.dobRequired;
  const date = parseDateOnly(value);
  if (!date) return e.dobInvalid;
  if (date > today) return e.dobFuture;
  if (!isAdult(date, today, minimumAgeOf(property))) return fill(e.underage, {}, property);
  return '';
}

/**
 * Validate the sign-up form. Returns a map of field name to message (empty when valid). Each
 * document in `separate` (RequiredAtSignup with its own checkbox) must be checked in
 * `values.documents`; its error is keyed `documents.<key>`.
 * @param {{ email: string, password: string, displayName: string, dateOfBirth: string, acceptTerms: boolean, code?: string, documents?: Record<string, boolean> }} values
 * @param {Date} [today]
 * @param {object[]} [separate]
 */
export function validateSignUp(values, today = new Date(), separate = []) {
  const errors = {};
  if (!values.email.trim()) errors.email = e.emailRequired;
  else if (!isEmail(values.email)) errors.email = e.emailInvalid;
  const problems = passwordProblems(values.password, values.email);
  if (!values.password) errors.password = e.passwordRequired;
  else if (problems.includes('tooShort')) errors.password = e.passwordTooShort;
  else if (problems.includes('tooLong')) errors.password = e.passwordTooLong;
  else if (problems.includes('matchesEmail')) errors.password = e.passwordMatchesEmail;
  if (!isDisplayName(values.displayName)) errors.displayName = e.displayNameInvalid;
  const dob = validateDateOfBirth(values.dateOfBirth, today);
  if (dob) errors.dateOfBirth = dob;
  if (!values.acceptTerms) errors.acceptTerms = e.termsRequired;
  if (values.code && values.code.trim() && !isSignupCode(values.code)) errors.code = e.codeInvalidShape;
  Object.assign(errors, validateDocumentChecks(separate, values.documents, strings.documents.signupRequired));
  return errors;
}

/**
 * The register body for the sign-up form. With a `documents` list the body carries
 * `acceptedDocuments` for the checked separate documents, at the live version the property
 * reports (`GET /api/v1/property`), else the build-time version.
 */
export function signUpBody(values, property) {
  const body = {
    email: values.email.trim(),
    password: values.password,
    displayName: values.displayName.trim(),
    dateOfBirth: values.dateOfBirth,
    acceptTerms: true
  };
  const docs = signupDocumentsFor(property);
  if (docs.hasList) body.acceptedDocuments = buildAcceptedDocuments(docs.separate, values.documents || {}, property.documents);
  return body;
}

/** /sign-up: email registration with an 18+ check, terms acceptance, required documents, and an optional sign-up code. */
function SignUpPage() {
  const auth = useAuth();
  const navigate = useNavigate();
  const { property, runtimeLoaded } = useProperty();
  useSignupMetric(auth.client, property, runtimeLoaded, auth.isSignedIn);
  const sells = purchasesEnabled(property);
  const codes = signupCodesEnabled(property);
  const docs = signupDocumentsFor(property);
  const [pending] = useState(() => (codes ? getPendingCode() : null));
  const [values, setValues] = useState({ email: '', password: '', displayName: '', dateOfBirth: '', acceptTerms: false, documents: {}, code: pending ? pending.code : '' });
  const [showCode, setShowCode] = useState(Boolean(pending));
  const [errors, setErrors] = useState({});
  const [formError, setFormError] = useState('');
  const [busy, setBusy] = useState(false);
  const onSocial = useSocialSignIn({ next: '/app', setBusy, setError: setFormError });
  useDocumentMeta(a.signUpMeta, a.metaDescription);

  if (auth.isRestoring) return <div className="center-page"><Loading label={strings.member.restoring} /></div>;
  if (auth.isSignedIn && !busy) return <Navigate to={pending ? '/app/membership?code=' + encodeURIComponent(pending.code) : '/app'} replace />;

  const set = (key) => (ev) => {
    const value = ev.target.type === 'checkbox' ? ev.target.checked : ev.target.value;
    setValues((v) => ({ ...v, [key]: value }));
  };

  const toggleDocument = (key, checked) => setValues((v) => ({ ...v, documents: { ...v.documents, [key]: checked } }));

  const rememberCode = () => {
    const code = normalizeSignupCode(values.code);
    if (code) setPendingCode(code, { auto: true });
    else clearPendingCode();
  };

  const submit = async (ev) => {
    ev.preventDefault();
    const found = validateSignUp(values, new Date(), docs.separate);
    setErrors(found);
    setFormError(Object.keys(found).length ? e.fixBelow : '');
    if (Object.keys(found).length) return;
    rememberCode();
    setBusy(true);
    try {
      const response = await auth.register(signUpBody(values, property));
      const path = await routeAfterAuth({ me: response.user, isNewAccount: true, api: auth.api, setMe: auth.setMe, next: '/app' });
      navigate(path, { replace: true });
    } catch (err) {
      const fieldErrors = getFieldErrors(err);
      const mapped = {};
      for (const key of ['email', 'password', 'displayName', 'dateOfBirth', 'acceptTerms']) {
        const value = fieldErrors[key];
        if (value) mapped[key] = Array.isArray(value) ? value.join(' ') : String(value);
      }
      for (const [key, message] of Object.entries(documentFieldErrors(fieldErrors))) mapped[documentErrorKey(key)] = message;
      setErrors(mapped);
      setFormError(Object.keys(mapped).length && err.code === 'ValidationFailed' ? e.fixBelow : authErrorMessage(err));
      setBusy(false);
    }
  };

  const onSocialWithCode = (provider, body) => {
    rememberCode();
    onSocial(provider, body);
  };

  const fieldError = (key) => errors[key] && <span id={'signup-' + key + '-error'} className="field-error">{errors[key]}</span>;
  const describedBy = (key, extra) => [errors[key] ? 'signup-' + key + '-error' : '', extra || ''].filter(Boolean).join(' ') || undefined;
  const code = normalizeSignupCode(values.code);

  return (
    <AuthCard
      title={a.signUpTitle}
      subtitle={fill(sells ? a.signUpSubtitle : a.signUpSubtitleFree)}
      footer={<p>{a.haveAccount} <Link to="/sign-in">{a.signInLink}</Link></p>}
    >
      {code && isSignupCode(code) && <Banner tone="info" icon="info">{a.codeBanner(code)}</Banner>}
      <ErrorBanner message={formError} />
      <form className="auth-form" onSubmit={submit} noValidate>
        <div className="field">
          <label className="label-text" htmlFor="signup-email">{a.email}</label>
          <input id="signup-email" type="email" autoComplete="email" value={values.email} onChange={set('email')} disabled={busy} aria-invalid={Boolean(errors.email)} aria-describedby={describedBy('email')} />
          {fieldError('email')}
        </div>
        <div className="field">
          <label className="label-text" htmlFor="signup-password">{a.password}</label>
          <PasswordInput id="signup-password" autoComplete="new-password" value={values.password} onChange={set('password')} disabled={busy} invalid={Boolean(errors.password)} describedBy={describedBy('password', 'signup-password-rules')} />
          <PasswordRules id="signup-password-rules" password={values.password} email={values.email} />
          {fieldError('password')}
        </div>
        <div className="field">
          <label className="label-text" htmlFor="signup-displayName">{a.displayName}</label>
          <input id="signup-displayName" type="text" autoComplete="nickname" maxLength={60} value={values.displayName} onChange={set('displayName')} disabled={busy} aria-invalid={Boolean(errors.displayName)} aria-describedby={describedBy('displayName', 'signup-displayName-hint')} />
          <span id="signup-displayName-hint" className="muted small">{a.displayNameHint}</span>
          {fieldError('displayName')}
        </div>
        <div className="field">
          <label className="label-text" htmlFor="signup-dateOfBirth">{a.dateOfBirth}</label>
          <input id="signup-dateOfBirth" type="date" autoComplete="bday" max={todayIso()} value={values.dateOfBirth} onChange={set('dateOfBirth')} disabled={busy} aria-invalid={Boolean(errors.dateOfBirth)} aria-describedby={describedBy('dateOfBirth', 'signup-dob-hint')} />
          <span id="signup-dob-hint" className="muted small">{fill(a.dateOfBirthHint)}</span>
          {fieldError('dateOfBirth')}
        </div>
        {!codes ? null : showCode ? (
          <div className="field">
            <label className="label-text" htmlFor="signup-code">{a.signupCode}</label>
            <input id="signup-code" type="text" className="mono" autoComplete="off" spellCheck="false" maxLength={40} value={values.code} onChange={set('code')} disabled={busy} aria-invalid={Boolean(errors.code)} aria-describedby={describedBy('code', 'signup-code-hint')} />
            <span id="signup-code-hint" className="muted small">{a.signupCodeHint}</span>
            {fieldError('code')}
          </div>
        ) : (
          <button type="button" className="link-button small" onClick={() => setShowCode(true)}>{a.haveCode}</button>
        )}
        <div className="field">
          <label className="check-field" htmlFor="signup-acceptTerms">
            <input id="signup-acceptTerms" type="checkbox" checked={values.acceptTerms} onChange={set('acceptTerms')} disabled={busy} aria-invalid={Boolean(errors.acceptTerms)} aria-describedby={describedBy('acceptTerms')} />
            <TermsLabel docs={docs} />
          </label>
          {fieldError('acceptTerms')}
        </div>
        <DocumentCheckboxes idPrefix="signup" documents={docs.separate} checked={values.documents} onToggle={toggleDocument} errors={errors} disabled={busy} />
        <button type="submit" className="button button-primary button-block" disabled={busy}>
          {busy ? a.submittingSignUp : a.submitSignUp}
        </button>
      </form>
      <SocialButtons onCredential={onSocialWithCode} onError={setFormError} busy={busy} mode="signUp" />
    </AuthCard>
  );
}

export default SignUpPage;
