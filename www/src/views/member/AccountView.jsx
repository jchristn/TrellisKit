import ThemeToggle from '../../components/ThemeToggle.jsx';
import ProfileMedia from '../../components/profile/ProfileMedia.jsx';
import { useCallback, useState } from 'react';
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import { featureEnabled } from './memberRoutes.js';
import { useToast } from '../../context/ToastContext.jsx';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import { browser } from '../../utils/browser.js';
import { mediaUrl } from '../../utils/engagement.js';
import { authErrorMessage } from '../../utils/authErrors.js';
import ProfileForm from '../../components/ProfileForm.jsx';
import MetadataPanel from '../../components/MetadataPanel.jsx';
import { isStaffUser, publicPagesConfig } from '@trelliskit/shared';
import { PublicProfilePageSettings } from '../../components/PublicPageFields.jsx';
import PasswordInput from '../../components/PasswordInput.jsx';
import PasswordRules from '../../components/PasswordRules.jsx';
import SocialButtons from '../../components/SocialButtons.jsx';
import ConfirmModal from '../../components/ConfirmModal.jsx';
import Modal from '../../components/Modal.jsx';
import { Badge } from '../../components/Badges.jsx';
import { ErrorBanner } from '../../components/States.jsx';
import { validateNewPassword } from '../auth/ResetPasswordPage.jsx';

/** Account export polling: every 3 seconds, for up to 2 minutes. */
const EXPORT_POLL_MS = 3000;
const EXPORT_TIMEOUT_MS = 120000;

const ac = strings.account;
const PROVIDERS = [
  { id: 'google', method: 'Google', label: 'Google' },
  { id: 'apple', method: 'Apple', label: 'Apple' },
  { id: 'facebook', method: 'Facebook', label: 'Facebook' }
];

function PasswordSection({ me, onChanged }) {
  const { api } = useAuth();
  const toast = useToast();
  const hasPassword = Array.isArray(me.signInMethods) && me.signInMethods.includes('Email');
  const [current, setCurrent] = useState('');
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [errors, setErrors] = useState({});
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  const submit = async (e) => {
    e.preventDefault();
    const found = validateNewPassword(password, confirm, me.email);
    if (hasPassword && !current) found.current = strings.auth.errors.passwordRequired;
    setErrors(found);
    setError('');
    if (Object.keys(found).length) return;
    setBusy(true);
    try {
      const body = { newPassword: password };
      if (hasPassword) body.currentPassword = current;
      await api.setPassword(body);
      setCurrent('');
      setPassword('');
      setConfirm('');
      toast.success(ac.passwordSaved);
      onChanged();
    } catch (err) {
      if (err && err.code === 'InvalidCredentials') setErrors({ current: ac.passwordWrong });
      else setError(authErrorMessage(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <form className="subsection auth-form" onSubmit={submit} noValidate>
      <h3>{hasPassword ? ac.passwordTitleSet : ac.passwordTitleNew}</h3>
      {!hasPassword && <p className="muted small">{ac.passwordNewHint}</p>}
      <ErrorBanner message={error} />
      {hasPassword && (
        <div className="field">
          <label className="label-text" htmlFor="account-current-password">{strings.auth.currentPassword}</label>
          <PasswordInput id="account-current-password" value={current} onChange={(e) => setCurrent(e.target.value)} disabled={busy} invalid={Boolean(errors.current)} />
          {errors.current && <span className="field-error">{errors.current}</span>}
        </div>
      )}
      <div className="field">
        <label className="label-text" htmlFor="account-new-password">{strings.auth.newPassword}</label>
        <PasswordInput id="account-new-password" autoComplete="new-password" value={password} onChange={(e) => setPassword(e.target.value)} disabled={busy} invalid={Boolean(errors.password)} describedBy="account-password-rules" />
        <PasswordRules id="account-password-rules" password={password} email={me.email} />
        {errors.password && <span className="field-error">{errors.password}</span>}
      </div>
      <div className="field">
        <label className="label-text" htmlFor="account-confirm-password">{strings.auth.confirmPassword}</label>
        <PasswordInput id="account-confirm-password" autoComplete="new-password" value={confirm} onChange={(e) => setConfirm(e.target.value)} disabled={busy} invalid={Boolean(errors.confirm)} />
        {errors.confirm && <span className="field-error">{errors.confirm}</span>}
      </div>
      <div className="form-actions form-actions-start">
        <button type="submit" className="button button-primary" disabled={busy}>{busy ? ac.passwordSaving : ac.passwordSave}</button>
      </div>
    </form>
  );
}

function SignInMethodsSection({ me }) {
  const auth = useAuth();
  const toast = useToast();
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [unlinking, setUnlinking] = useState(null);
  const methods = Array.isArray(me.signInMethods) ? me.signInMethods : [];
  const unlinked = PROVIDERS.filter((p) => !methods.includes(p.method));

  const link = async (provider, body) => {
    setBusy(true);
    setError('');
    try {
      auth.setMe(await auth.api.linkIdentity(provider, body));
      toast.success(ac.linkedToast(PROVIDERS.find((p) => p.id === provider).label));
    } catch (err) {
      setError(authErrorMessage(err));
    } finally {
      setBusy(false);
    }
  };

  const unlink = async () => {
    const provider = unlinking;
    setBusy(true);
    setError('');
    try {
      auth.setMe(await auth.api.unlinkIdentity(provider.id));
      toast.success(ac.unlinked(provider.label));
      setUnlinking(null);
    } catch (err) {
      setError(err && err.code === 'LastSignInMethod' ? ac.lastMethod : authErrorMessage(err));
      setUnlinking(null);
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="panel" aria-labelledby="account-signin-title">
      <h2 id="account-signin-title">{ac.sections.signIn}</h2>
      <ErrorBanner message={error} onDismiss={() => setError('')} />
      <ul className="method-list">
        <li className="method-row">
          <span className="method-name">{strings.enums.signInMethod.Email}</span>
          <Badge tone={methods.includes('Email') ? 'success' : 'neutral'}>{methods.includes('Email') ? ac.linked : ac.notLinked}</Badge>
        </li>
        {PROVIDERS.filter((p) => methods.includes(p.method)).map((p) => (
          <li key={p.id} className="method-row">
            <span className="method-name">{p.label}</span>
            <Badge tone="success">{ac.linked}</Badge>
            <button type="button" className="button button-secondary button-sm" onClick={() => setUnlinking(p)} disabled={busy}>{ac.unlink}</button>
          </li>
        ))}
      </ul>
      {unlinked.length > 0 && (
        <div className="subsection">
          <h3>{ac.link}</h3>
          <SocialButtons onCredential={link} onError={setError} busy={busy} only={unlinked.map((p) => p.id)} showDivider={false} />
        </div>
      )}
      <PasswordSection me={me} onChanged={() => auth.reloadMe().catch(() => {})} />
      <ConfirmModal
        open={Boolean(unlinking)}
        title={unlinking ? ac.unlinkTitle(unlinking.label) : ''}
        message={unlinking ? ac.unlinkBody(unlinking.label) : ''}
        confirmLabel={ac.unlink}
        busy={busy}
        onConfirm={unlink}
        onCancel={() => setUnlinking(null)}
      />
    </section>
  );
}

function DeleteAccountSection() {
  const auth = useAuth();
  const navigate = useNavigate();
  const [open, setOpen] = useState(false);
  const [typed, setTyped] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const confirmed = typed.trim() === ac.deleteConfirmWord;

  const close = () => {
    if (busy) return;
    setOpen(false);
    setTyped('');
    setError('');
  };

  const remove = async () => {
    if (!confirmed) return;
    setBusy(true);
    setError('');
    try {
      const result = await auth.api.deleteAccount();
      navigate('/account-deleted', { replace: true, state: { message: result && result.message, storeSubscriptionNote: result && result.storeSubscriptionNote, endSession: true } });
    } catch (err) {
      setError(authErrorMessage(err));
      setBusy(false);
    }
  };

  return (
    <section className="panel panel-danger" aria-labelledby="account-delete-title">
      <h2 id="account-delete-title">{ac.sections.danger}</h2>
      <p className="muted">{ac.deleteBody}</p>
      <div className="form-actions form-actions-start">
        <button type="button" className="button button-danger" onClick={() => setOpen(true)}>{ac.deleteButton}</button>
      </div>
      <Modal
        open={open}
        onClose={close}
        title={ac.deleteTitle}
        size="sm"
        footer={(
          <>
            <button type="button" className="button button-secondary" onClick={close} disabled={busy}>{strings.common.cancel}</button>
            <button type="button" className="button button-danger" onClick={remove} disabled={busy || !confirmed}>{busy ? ac.deleting : ac.deleteConfirm}</button>
          </>
        )}
      >
        <p>{ac.deleteBody}</p>
        <ErrorBanner message={error} />
        <div className="field">
          <label className="label-text" htmlFor="account-delete-confirm">{ac.deleteConfirmLabel}</label>
          <input id="account-delete-confirm" type="text" autoComplete="off" spellCheck="false" value={typed} onChange={(e) => setTyped(e.target.value)} disabled={busy} />
        </div>
      </Modal>
    </section>
  );
}

/** /app/profile: profile edit, sign-in methods, email verification, data export, and deletion. */
function AccountView() {
  const auth = useAuth();
  const { property, fill } = useProperty();
  const toast = useToast();
  const me = auth.me;
  const [resending, setResending] = useState(false);
  const [exporting, setExporting] = useState(false);
  const [error, setError] = useState('');
  useDocumentMeta(ac.meta, strings.member.metaDescription);
  const client = auth.client;
  const loadPublicPage = useCallback((signal) => client.getMyPublicPage(signal), [client]);
  const savePublicPage = useCallback((body) => client.updateMyPublicPage(body), [client]);
  const pp = strings.publicPages.profile;

  const resend = async () => {
    setResending(true);
    setError('');
    try {
      await auth.api.resendVerification();
      toast.success(ac.resent);
    } catch (err) {
      setError(authErrorMessage(err));
    } finally {
      setResending(false);
    }
  };

  // Account export is an asynchronous job (REST_API.md "Account export (asynchronous)"): queue it,
  // poll until Ready, then follow the signed, root-relative download link (no Authorization needed).
  const download = async () => {
    setExporting(true);
    setError('');
    try {
      const queued = await auth.api.requestExport();
      const id = queued && queued.exportId;
      if (!id) throw new Error(ac.exportFailed);
      const deadline = Date.now() + EXPORT_TIMEOUT_MS;
      for (;;) {
        const job = await auth.api.getExport(id);
        if (job && job.status === 'Ready' && job.downloadUrl) {
          browser.assign(mediaUrl(job.downloadUrl) || job.downloadUrl);
          toast.success(ac.exportReady);
          return;
        }
        if (!job || job.status === 'Failed' || job.status === 'Expired') throw new Error(ac.exportFailed);
        if (Date.now() + EXPORT_POLL_MS > deadline) {
          toast.success(ac.exportPending);
          return;
        }
        await new Promise((resolve) => setTimeout(resolve, EXPORT_POLL_MS));
      }
    } catch (err) {
      setError(err && err.code === 'ExportInProgress' ? ac.exportInProgress : authErrorMessage(err));
    } finally {
      setExporting(false);
    }
  };

  return (
    <div className="member-view">
      <header className="page-header">
        <div className="page-header-text">
          <h1>{ac.title}</h1>
          <p className="page-summary">{ac.summary}</p>
        </div>
        <ThemeToggle />
      </header>
      <ErrorBanner message={error} onDismiss={() => setError('')} />

      <section className="panel" aria-labelledby="account-profile-title">
        <h2 id="account-profile-title">{ac.sections.profile}</h2>
        <ProfileForm
          key={me.id}
          me={me}
          onSaved={(next) => { auth.setMe(next); toast.success(strings.profileForm.saved); }}
          onMeChange={(next, message) => { auth.setMe(next); if (message) toast.success(message); }}
        />
      </section>

      {publicPagesConfig(property).enabled && (
        <PublicProfilePageSettings
          client={client} load={loadPublicPage} save={savePublicPage} objectId={me.id} idPrefix="account-public-page"
          title={pp.title} intro={fill(pp.intro)} toggleLabel={pp.toggle} toggleHint={pp.toggleHint} savedText={pp.saved}
          onSaved={(page) => auth.setMe((current) => ({ ...current, publicPage: { enabled: Boolean(page.enabled), slug: page.slug || null, path: page.path || null, url: page.url || null } }))}
          onToast={(text) => toast.success(text)}
        />
      )}

      <ProfileMedia user={me} />

      <SignInMethodsSection me={me} />

      <section className="panel" aria-labelledby="account-email-title">
        <h2 id="account-email-title">{ac.sections.email}</h2>
        <dl className="kv-grid">
          <div className="kv-item"><dt>{ac.emailAddress}</dt><dd>{me.email}</dd></div>
          <div className="kv-item"><dt>{strings.membershipApp.statusTitle}</dt><dd><Badge tone={me.emailVerified ? 'success' : 'warning'}>{me.emailVerified ? ac.verified : ac.unverified}</Badge></dd></div>
        </dl>
        {!me.emailVerified && (
          <>
            <p className="muted small">{ac.unverifiedBody}</p>
            <div className="form-actions form-actions-start">
              <button type="button" className="button button-secondary" onClick={resend} disabled={resending}>{resending ? ac.resending : ac.resend}</button>
            </div>
          </>
        )}
      </section>

      <section className="panel" aria-labelledby="account-data-title">
        <h2 id="account-data-title">{ac.sections.data}</h2>
        <p className="muted">{ac.exportBody}</p>
        <div className="form-actions form-actions-start">
          <button type="button" className="button button-secondary" onClick={download} disabled={exporting}>{exporting ? ac.exporting : ac.exportButton}</button>
        </div>
      </section>

      {auth.me && (
        <section className="panel" aria-labelledby="account-metadata-title">
          <h2 id="account-metadata-title">{strings.metadata.editorTitle}</h2>
          <p className="muted small">{strings.metadata.profileHint}</p>
          <MetadataPanel client={auth.client} targetType="User" targetId={auth.me.id} isStaff={isStaffUser(auth.me)} initial={auth.me.labels ? auth.me : null} onSaved={(meta) => auth.setMe((me) => ({ ...me, ...meta }))} />
        </section>
      )}

      <section className="panel" aria-labelledby="account-more-title">
        <h2 id="account-more-title">{ac.sectionsMore}</h2>
        <ul className="link-list">
          <li>
            <Link to="/app/settings/notifications">{ac.notificationsLink}</Link>
            <span className="muted small">{ac.notificationsLinkBody}</span>
          </li>
          <li>
            <Link to="/app/settings/appearance">{ac.appearanceLink}</Link>
            <span className="muted small">{ac.appearanceLinkBody}</span>
          </li>
          <li>
            <Link to="/app/settings/blocked">{ac.blockedLink}</Link>
            <span className="muted small">{ac.blockedLinkBody}</span>
          </li>
          {featureEnabled(property, 'messaging') && (
            <li>
              <Link to="/app/settings/messaging">{strings.messaging.settings.link}</Link>
              <span className="muted small">{strings.messaging.settings.linkBody}</span>
            </li>
          )}
          <li>
            <Link to="/app/support">{strings.support.title}</Link>
            <span className="muted small">{strings.support.summary}</span>
          </li>
        </ul>
      </section>

      <DeleteAccountSection />
    </div>
  );
}

export default AccountView;

/** /app/account: the earlier address of the account page, kept for links and bookmarks; sends to /app/profile. */
export function AccountRedirect() {
  const location = useLocation();
  return <Navigate to={'/app/profile' + location.search + location.hash} replace />;
}
