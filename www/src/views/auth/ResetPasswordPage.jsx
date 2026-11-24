import { useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { passwordProblems } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import { getFieldErrors } from '../../utils/api.js';
import { authErrorMessage } from '../../utils/authErrors.js';
import PasswordInput from '../../components/PasswordInput.jsx';
import PasswordRules from '../../components/PasswordRules.jsx';
import { Banner, ErrorBanner } from '../../components/States.jsx';
import AuthCard from './AuthCard.jsx';

const a = strings.auth;

/** Validate a new password and its confirmation. Returns `{ password?, confirm? }`. */
export function validateNewPassword(password, confirm, email = '') {
  const errors = {};
  const problems = passwordProblems(password, email);
  if (!password) errors.password = a.errors.passwordRequired;
  else if (problems.includes('tooShort')) errors.password = a.errors.passwordTooShort;
  else if (problems.includes('tooLong')) errors.password = a.errors.passwordTooLong;
  else if (problems.includes('matchesEmail')) errors.password = a.errors.passwordMatchesEmail;
  if (!errors.password && password !== confirm) errors.confirm = a.errors.passwordMismatch;
  return errors;
}

/** /reset-password?token=: choose a new password from the emailed link. */
function ResetPasswordPage() {
  const { api } = useAuth();
  const [params] = useSearchParams();
  const token = params.get('token') || '';
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [errors, setErrors] = useState({});
  const [error, setError] = useState('');
  const [invalidToken, setInvalidToken] = useState(false);
  const [done, setDone] = useState(false);
  const [busy, setBusy] = useState(false);
  useDocumentMeta(a.resetMeta, a.metaDescription);

  const submit = async (e) => {
    e.preventDefault();
    const found = validateNewPassword(password, confirm);
    setErrors(found);
    setError('');
    if (Object.keys(found).length) return;
    setBusy(true);
    try {
      await api.resetPassword(token, password);
      setDone(true);
    } catch (err) {
      if (err && err.code === 'InvalidToken') setInvalidToken(true);
      const fe = getFieldErrors(err);
      if (fe.newPassword) setErrors({ password: String(fe.newPassword) });
      else setError(authErrorMessage(err));
    } finally {
      setBusy(false);
    }
  };

  const footer = <Link to="/sign-in">{a.backToSignIn}</Link>;
  if (!token || invalidToken) {
    return (
      <AuthCard title={a.resetTitle} footer={footer}>
        <Banner tone="danger" icon="alert">{token ? a.resetInvalidToken : a.resetMissingToken}</Banner>
        <Link to="/forgot-password" className="button button-primary button-block">{a.requestNewLink}</Link>
      </AuthCard>
    );
  }
  if (done) {
    return (
      <AuthCard title={a.resetTitle} footer={null}>
        <Banner tone="success" icon="check">{a.resetDone}</Banner>
        <Link to="/sign-in" className="button button-primary button-block">{a.signInLink}</Link>
      </AuthCard>
    );
  }

  return (
    <AuthCard title={a.resetTitle} footer={footer}>
      <ErrorBanner message={error} />
      <form className="auth-form" onSubmit={submit} noValidate>
        <div className="field">
          <label className="label-text" htmlFor="reset-password">{a.newPassword}</label>
          <PasswordInput id="reset-password" autoComplete="new-password" value={password} onChange={(e) => setPassword(e.target.value)} disabled={busy} invalid={Boolean(errors.password)} describedBy="reset-password-rules" />
          <PasswordRules id="reset-password-rules" password={password} email="" />
          {errors.password && <span className="field-error">{errors.password}</span>}
        </div>
        <div className="field">
          <label className="label-text" htmlFor="reset-confirm">{a.confirmPassword}</label>
          <PasswordInput id="reset-confirm" autoComplete="new-password" value={confirm} onChange={(e) => setConfirm(e.target.value)} disabled={busy} invalid={Boolean(errors.confirm)} />
          {errors.confirm && <span className="field-error">{errors.confirm}</span>}
        </div>
        <button type="submit" className="button button-primary button-block" disabled={busy}>{busy ? a.resetSubmitting : a.resetSubmit}</button>
      </form>
    </AuthCard>
  );
}

export default ResetPasswordPage;
