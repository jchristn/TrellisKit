import { useState } from 'react';
import { Link } from 'react-router-dom';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import { authErrorMessage } from '../../utils/authErrors.js';
import { Banner, ErrorBanner } from '../../components/States.jsx';
import AuthCard from './AuthCard.jsx';

const a = strings.auth;

/** /forgot-password: always reports success (no account enumeration). */
function ForgotPasswordPage() {
  const { api } = useAuth();
  const [email, setEmail] = useState('');
  const [fieldError, setFieldError] = useState('');
  const [error, setError] = useState('');
  const [sent, setSent] = useState(false);
  const [busy, setBusy] = useState(false);
  useDocumentMeta(a.forgotMeta, a.metaDescription);

  const submit = async (e) => {
    e.preventDefault();
    const problem = !email.trim() ? a.errors.emailRequired : '';
    setFieldError(problem);
    setError('');
    if (problem) return;
    setBusy(true);
    try {
      await api.forgotPassword(email.trim());
      setSent(true);
    } catch (err) {
      setError(authErrorMessage(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <AuthCard title={a.forgotTitle} subtitle={sent ? null : a.forgotBody} footer={<Link to="/sign-in">{a.backToSignIn}</Link>}>
      {sent ? <Banner tone="success" icon="check">{a.forgotSent}</Banner> : (
        <>
          <ErrorBanner message={error} />
          <form className="auth-form" onSubmit={submit} noValidate>
            <div className="field">
              <label className="label-text" htmlFor="forgot-email">{a.email}</label>
              <input id="forgot-email" type="email" autoComplete="email" value={email} onChange={(e) => setEmail(e.target.value)} disabled={busy} aria-invalid={Boolean(fieldError)} aria-describedby={fieldError ? 'forgot-email-error' : undefined} />
              {fieldError && <span id="forgot-email-error" className="field-error">{fieldError}</span>}
            </div>
            <button type="submit" className="button button-primary button-block" disabled={busy}>{busy ? a.forgotSubmitting : a.forgotSubmit}</button>
          </form>
        </>
      )}
    </AuthCard>
  );
}

export default ForgotPasswordPage;
