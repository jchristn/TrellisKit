import { useState } from 'react';
import { Link, Navigate, useLocation } from 'react-router-dom';
import strings from '../../strings.js';
import { fill } from '../../property/current.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import { authErrorMessage } from '../../utils/authErrors.js';
import Icon from '../../components/Icon.jsx';
import ThemeToggle from '../../components/ThemeToggle.jsx';
import PasswordInput from '../../components/PasswordInput.jsx';
import { Banner, Loading } from '../../components/States.jsx';

const l = strings.admin.login;

function TokenForm({ onSubmit }) {
  const [token, setToken] = useState('');
  const [reveal, setReveal] = useState(false);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  const submit = async (e) => {
    e.preventDefault();
    if (!token.trim()) {
      setError(l.tokenRequired);
      return;
    }
    setBusy(true);
    setError('');
    try {
      await onSubmit(token);
    } catch (err) {
      setError(err && (err.status === 401 || err.status === 403) ? l.invalidToken : (err && err.message) || strings.common.unreachable);
      setBusy(false);
    }
  };

  return (
    <form onSubmit={submit} noValidate>
      <label className="field" htmlFor="admin-token">
        <span className="label-text">{l.tokenLabel}</span>
      </label>
      <div className="input-with-button">
        <input
          id="admin-token"
          type={reveal ? 'text' : 'password'}
          autoComplete="off"
          spellCheck="false"
          value={token}
          onChange={(e) => setToken(e.target.value)}
          placeholder={l.tokenPlaceholder}
          disabled={busy}
          aria-invalid={Boolean(error)}
          aria-describedby={error ? 'admin-token-error' : undefined}
        />
        <button type="button" className="icon-button" onClick={() => setReveal((v) => !v)} aria-label={reveal ? l.hideToken : l.showToken} title={reveal ? l.hideToken : l.showToken}>
          <Icon name={reveal ? 'eyeOff' : 'eye'} />
        </button>
      </div>
      {error && <p id="admin-token-error" className="field-error" role="alert">{error}</p>}
      <button type="submit" className="button button-primary button-block" disabled={busy}>
        {busy ? l.submitting : l.submit}
      </button>
      <p className="muted small"><Icon name="lock" size={12} /> {l.storageNote}</p>
      <p className="muted small">{l.localHint}</p>
    </form>
  );
}

function PasswordForm({ onSubmit }) {
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
      await onSubmit(email.trim(), password);
    } catch (err) {
      setError(authErrorMessage(err));
      setBusy(false);
    }
  };

  return (
    <form onSubmit={submit} noValidate>
      <label className="field" htmlFor="admin-email">
        <span className="label-text">{l.emailLabel}</span>
        <input id="admin-email" type="email" autoComplete="username" value={email} onChange={(e) => setEmail(e.target.value)} disabled={busy} aria-invalid={Boolean(error)} />
      </label>
      <div className="field">
        <label className="label-text" htmlFor="admin-password">{l.passwordLabel}</label>
        <PasswordInput id="admin-password" value={password} onChange={(e) => setPassword(e.target.value)} disabled={busy} invalid={Boolean(error)} describedBy={error ? 'admin-password-error' : undefined} />
      </div>
      {error && <p id="admin-password-error" className="field-error" role="alert">{error}</p>}
      <button type="submit" className="button button-primary button-block" disabled={busy}>
        {busy ? l.passwordSubmitting : l.submit}
      </button>
      <p className="muted small"><Icon name="lock" size={12} /> {l.sessionNote}</p>
      <p className="muted small">{l.bootstrapHint}</p>
    </form>
  );
}

/**
 * Admin sign in: email and password for SystemAdmin members (primary), or the break-glass
 * admin token (secondary). A signed-in non-admin sees "This account is not an administrator."
 */
function AdminLoginView() {
  const { loginWithToken, loginWithPassword, logout, isAuthenticated, isLoading, isNotAdmin, me, delegationEnded } = useAdminAuth();
  const location = useLocation();
  const [useToken, setUseToken] = useState(false);
  useDocumentMeta(strings.admin.metaTitle(l.submit), l.subtitle);

  const from = (location.state && location.state.from) || '/admin/home';
  if (isLoading) return <div className="center-page"><Loading label={l.restoring} /></div>;
  if (isAuthenticated) return <Navigate to={from.startsWith('/admin/login') ? '/admin/home' : from} replace />;

  return (
    <div className="login-page">
      <div className="login-theme"><ThemeToggle /></div>
      <main className="login-card">
        <div className="login-brand">
          <img className="brand-mark brand-mark-lg" src="/brand-mark.png" alt="" aria-hidden="true" width="44" height="44" />
          <div>
            <h1>{fill(l.title)}</h1>
            <p className="muted">{useToken ? l.tokenSubtitle : l.subtitle}</p>
          </div>
        </div>
        <dl className="login-context">
          <div><dt>{l.serverLabel}</dt><dd><code className="mono">{window.location.origin}</code></dd></div>
        </dl>
        {delegationEnded && (
          <Banner tone="warning" icon="clock">
            {strings.admin.delegation.expired} <a href="/platform">{strings.platform.consoleName}</a>
          </Banner>
        )}
        {isNotAdmin && (
          <Banner tone="warning" icon="alert">
            <strong>{l.notAdmin}</strong>{me ? ' ' + l.signedInAs(me.displayName || me.email) + '.' : ''}
          </Banner>
        )}
        {isNotAdmin && !useToken && (
          <div className="login-actions">
            <button type="button" className="button button-secondary button-block" onClick={logout}>{l.signOutOther}</button>
            <Link to="/app" className="button button-secondary button-block">{l.openMemberApp}</Link>
          </div>
        )}
        {!isNotAdmin && !useToken && <PasswordForm onSubmit={loginWithPassword} />}
        {useToken && <TokenForm onSubmit={loginWithToken} />}
        <button type="button" className="link-button small" onClick={() => setUseToken((v) => !v)}>
          {useToken ? l.usePassword : l.useToken}
        </button>
        <Link to="/" className="small">{l.backToSite}</Link>
      </main>
    </div>
  );
}

export default AdminLoginView;
