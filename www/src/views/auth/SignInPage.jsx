import { useState } from 'react';
import { Link, Navigate, useNavigate, useSearchParams } from 'react-router-dom';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import { authErrorMessage } from '../../utils/authErrors.js';
import { getPendingCode } from '../../utils/pendingCode.js';
import { routeAfterAuth } from '../../utils/postAuth.js';
import { safeNext } from '../../utils/onboarding.js';
import PasswordInput from '../../components/PasswordInput.jsx';
import SocialButtons from '../../components/SocialButtons.jsx';
import { Banner, ErrorBanner, Loading } from '../../components/States.jsx';
import AuthCard from './AuthCard.jsx';
import useSocialSignIn from './useSocialSignIn.js';

const a = strings.auth;

/** /sign-in: email and password plus enabled social providers. */
function SignInPage() {
  const auth = useAuth();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const next = safeNext(params.get('next'), '/app');
  const [pending] = useState(() => getPendingCode());
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [errors, setErrors] = useState({});
  const [formError, setFormError] = useState('');
  const [busy, setBusy] = useState(false);
  const onSocial = useSocialSignIn({ next, setBusy, setError: setFormError });
  useDocumentMeta(a.signInMeta, a.metaDescription);

  if (auth.isRestoring) return <div className="center-page"><Loading label={strings.member.restoring} /></div>;
  if (auth.isSignedIn && !busy) return <Navigate to={next} replace />;

  const submit = async (e) => {
    e.preventDefault();
    const found = {};
    // Sign-in only needs an email to send; the server decides whether it matches an account (administrator emails
    // such as admin@<host> have no dot in the domain).
    if (!email.trim()) found.email = a.errors.emailRequired;
    if (!password) found.password = a.errors.passwordRequired;
    setErrors(found);
    setFormError('');
    if (Object.keys(found).length) return;
    setBusy(true);
    try {
      const response = await auth.signIn(email.trim(), password);
      const path = await routeAfterAuth({ me: response.user, isNewAccount: response.isNewAccount, api: auth.api, setMe: auth.setMe, next });
      navigate(path, { replace: true });
    } catch (err) {
      setFormError(authErrorMessage(err));
      setBusy(false);
    }
  };

  return (
    <AuthCard
      title={a.signInTitle}
      subtitle={a.signInSubtitle}
      footer={<p>{a.noAccount} <Link to="/sign-up">{a.createAccountLink}</Link></p>}
    >
      {pending && <Banner tone="info" icon="info">{a.codeBannerSignIn(pending.code)}</Banner>}
      <ErrorBanner message={formError} />
      <form className="auth-form" onSubmit={submit} noValidate>
        <div className="field">
          <label className="label-text" htmlFor="signin-email">{a.email}</label>
          <input
            id="signin-email"
            type="email"
            autoComplete="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            disabled={busy}
            aria-invalid={Boolean(errors.email)}
            aria-describedby={errors.email ? 'signin-email-error' : undefined}
          />
          {errors.email && <span id="signin-email-error" className="field-error">{errors.email}</span>}
        </div>
        <div className="field">
          <label className="label-text" htmlFor="signin-password">{a.password}</label>
          <PasswordInput
            id="signin-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            disabled={busy}
            invalid={Boolean(errors.password)}
            describedBy={errors.password ? 'signin-password-error' : undefined}
          />
          {errors.password && <span id="signin-password-error" className="field-error">{errors.password}</span>}
        </div>
        <div className="auth-actions">
          <button type="submit" className="button button-primary" disabled={busy}>
            {busy ? a.submittingSignIn : a.submitSignIn}
          </button>
          <Link to="/forgot-password" className="small">{a.forgotLink}</Link>
        </div>
      </form>
      <SocialButtons onCredential={onSocial} onError={setFormError} busy={busy} mode="signIn" />
    </AuthCard>
  );
}

export default SignInPage;
