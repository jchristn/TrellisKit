import { useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import strings from '../../strings.js';
import { usePlatformAuth } from '../../context/PlatformAuthContext.jsx';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import Icon from '../../components/Icon.jsx';
import ThemeToggle from '../../components/ThemeToggle.jsx';
import PasswordInput from '../../components/PasswordInput.jsx';
import { Banner } from '../../components/States.jsx';

const l = strings.platform.login;

function loginError(err) {
  if (err && err.status === 401) return l.invalid;
  if (err && err.status === 423) return l.locked;
  if (err && err.status === 0) return strings.common.unreachable;
  return (err && err.message) || strings.common.errorGeneric;
}

function SignInForm({ onDone }) {
  const { login } = usePlatformAuth();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  const submit = async (e) => {
    e.preventDefault();
    if (!email.trim() || !password) {
      setError(l.credentialsRequired);
      return;
    }
    setBusy(true);
    setError('');
    try {
      await login(email.trim(), password);
      if (onDone) onDone();
    } catch (err) {
      setError(loginError(err));
      setBusy(false);
    }
  };

  return (
    <form onSubmit={submit} noValidate>
      <label className="field" htmlFor="platform-email">
        <span className="label-text">{l.emailLabel}</span>
        <input id="platform-email" type="email" autoComplete="username" value={email} onChange={(e) => setEmail(e.target.value)} disabled={busy} aria-invalid={Boolean(error)} />
      </label>
      <div className="field">
        <label className="label-text" htmlFor="platform-password">{l.passwordLabel}</label>
        <PasswordInput id="platform-password" value={password} onChange={(e) => setPassword(e.target.value)} disabled={busy} invalid={Boolean(error)} describedBy={error ? 'platform-login-error' : undefined} />
      </div>
      {error && <p id="platform-login-error" className="field-error" role="alert">{error}</p>}
      <button type="submit" className="button button-primary button-block" disabled={busy}>{busy ? l.submitting : l.submit}</button>
      <p className="muted small"><Icon name="lock" size={12} /> {l.storageNote}</p>
      <p className="muted small">{l.separateNote}</p>
    </form>
  );
}

function AcceptInviteForm({ onAccepted, initialToken = '' }) {
  const { acceptInvite } = usePlatformAuth();
  const [token, setToken] = useState(initialToken);
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  const submit = async (e) => {
    e.preventDefault();
    if (!token.trim() || !password) {
      setError(l.inviteRequired);
      return;
    }
    setBusy(true);
    setError('');
    try {
      await acceptInvite(token.trim(), password);
      onAccepted();
    } catch (err) {
      const fieldErrors = err && err.body && err.body.fieldErrors;
      if (err && err.code === 'InvalidToken') setError(l.inviteInvalid);
      else if (fieldErrors && typeof fieldErrors === 'object') setError(Object.values(fieldErrors).join(' '));
      else setError(loginError(err));
      setBusy(false);
    }
  };

  return (
    <form onSubmit={submit} noValidate>
      <label className="field" htmlFor="platform-invite-token">
        <span className="label-text">{l.tokenLabel}</span>
        <input id="platform-invite-token" className="mono" autoComplete="off" spellCheck="false" value={token} onChange={(e) => setToken(e.target.value)} disabled={busy} />
      </label>
      <div className="field">
        <label className="label-text" htmlFor="platform-new-password">{l.newPasswordLabel}</label>
        <PasswordInput id="platform-new-password" autoComplete="new-password" value={password} onChange={(e) => setPassword(e.target.value)} disabled={busy} invalid={Boolean(error)} describedBy={error ? 'platform-invite-error' : undefined} />
      </div>
      {error && <p id="platform-invite-error" className="field-error" role="alert">{error}</p>}
      <button type="submit" className="button button-primary button-block" disabled={busy}>{busy ? l.accepting : l.acceptSubmit}</button>
    </form>
  );
}

/**
 * Platform administrator sign-in, plus accepting an invitation (sets the password). The emailed
 * invitation link (`/platform/accept-invite?token=...`) opens the invitation form with the token.
 */
function PlatformLoginView() {
  const [searchParams] = useSearchParams();
  const linkToken = searchParams.get('token') || '';
  const [mode, setMode] = useState(linkToken ? 'invite' : 'signIn');
  const [accepted, setAccepted] = useState(false);
  useDocumentMeta(strings.platform.metaTitle(l.submit), l.subtitle);

  return (
    <div className="login-page">
      <div className="login-theme"><ThemeToggle /></div>
      <main className="login-card">
        <div className="login-brand">
          <img className="brand-mark brand-mark-lg" src="/brand-mark.png" alt="" aria-hidden="true" width="44" height="44" />
          <div>
            <h1>{mode === 'invite' ? l.inviteTitle : l.title}</h1>
            <p className="muted">{mode === 'invite' ? l.inviteSubtitle : l.subtitle}</p>
          </div>
        </div>
        <dl className="login-context">
          <div><dt>{strings.platform.topbar.host}</dt><dd><code className="mono">{window.location.origin}</code></dd></div>
        </dl>
        {accepted && mode === 'signIn' && <Banner tone="info" icon="check">{l.accepted}</Banner>}
        {mode === 'signIn' ? <SignInForm /> : <AcceptInviteForm initialToken={linkToken} onAccepted={() => { setAccepted(true); setMode('signIn'); }} />}
        <button type="button" className="link-button small" onClick={() => setMode((m) => (m === 'signIn' ? 'invite' : 'signIn'))}>
          {mode === 'signIn' ? l.haveInvite : l.haveAccount}
        </button>
        <Link to="/" className="small">{strings.platform.unavailable.backToSite}</Link>
      </main>
    </div>
  );
}

export default PlatformLoginView;
