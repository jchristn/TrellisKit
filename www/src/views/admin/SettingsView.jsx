import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import { errorMessage, getFieldErrors } from '../../utils/api.js';
import { computeChanges, groupFields, initialDrafts, isChanged, validateField } from '../../utils/settings.js';
import PageHeader from '../../components/PageHeader.jsx';
import ConfirmModal from '../../components/ConfirmModal.jsx';
import CopyButton from '../../components/CopyButton.jsx';
import Icon from '../../components/Icon.jsx';
import { Badge } from '../../components/Badges.jsx';
import { Banner, EmptyState, ErrorBanner, Loading } from '../../components/States.jsx';

const s = strings.admin.settings;
const RESTART_PENDING_KEY = 'tk.admin.restartPending';
const RESTART_POLL_MS = 2000;
const RESTART_TIMEOUT_MS = 120000;

function readRestartPending() {
  try {
    return window.sessionStorage.getItem(RESTART_PENDING_KEY) === '1';
  } catch {
    return false;
  }
}

function writeRestartPending(value) {
  try {
    if (value) window.sessionStorage.setItem(RESTART_PENDING_KEY, '1');
    else window.sessionStorage.removeItem(RESTART_PENDING_KEY);
  } catch {
    // Ignore storage failures; the banner then lasts for this page view only.
  }
}

function fieldId(key) {
  return 'setting-' + key.replace(/[^A-Za-z0-9_-]/g, '-');
}

function SecretInput({ id, field, value, onChange, disabled, describedBy, invalid }) {
  const [reveal, setReveal] = useState(false);
  return (
    <div className="input-with-button">
      <input
        id={id}
        type={reveal ? 'text' : 'password'}
        autoComplete="new-password"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={field.isSet ? s.secretPlaceholderSet : s.secretPlaceholderNotSet}
        disabled={disabled}
        aria-describedby={describedBy}
        aria-invalid={invalid}
      />
      <button type="button" className="icon-button" onClick={() => setReveal((v) => !v)} disabled={disabled} aria-label={reveal ? s.hideSecret : s.revealSecret} title={reveal ? s.hideSecret : s.revealSecret}>
        <Icon name={reveal ? 'eyeOff' : 'eye'} />
      </button>
    </div>
  );
}

function SettingField({ field, draft, onChange, error, disabled }) {
  const id = fieldId(field.key);
  const helpId = id + '-help';
  const errorId = id + '-error';
  const locked = disabled || field.overriddenByEnvironment;
  const describedBy = [helpId, error ? errorId : null].filter(Boolean).join(' ');
  const changed = isChanged(field, draft);

  let control;
  switch (field.type) {
    case 'Boolean':
      control = (
        <label className="toggle">
          <input id={id} type="checkbox" role="switch" checked={Boolean(draft)} onChange={(e) => onChange(e.target.checked)} disabled={locked} aria-describedby={describedBy} />
          <span className="toggle-track" aria-hidden="true"><span className="toggle-thumb" /></span>
          <span>{draft ? s.enabled : s.disabled}</span>
        </label>
      );
      break;
    case 'Integer':
      control = (
        <input
          id={id}
          type="number"
          inputMode="numeric"
          step="1"
          min={typeof field.min === 'number' ? field.min : undefined}
          max={typeof field.max === 'number' ? field.max : undefined}
          value={draft}
          onChange={(e) => onChange(e.target.value)}
          disabled={locked}
          aria-describedby={describedBy}
          aria-invalid={Boolean(error)}
          className="input-number"
        />
      );
      break;
    case 'Enum':
      control = (
        <select id={id} value={draft} onChange={(e) => onChange(e.target.value)} disabled={locked} aria-describedby={describedBy} aria-invalid={Boolean(error)} className="select-field">
          {!(field.options || []).includes(draft) && <option value={draft}>{draft}</option>}
          {(field.options || []).map((opt) => <option key={opt} value={opt}>{opt}</option>)}
        </select>
      );
      break;
    case 'Secret':
      control = <SecretInput id={id} field={field} value={draft} onChange={onChange} disabled={locked} describedBy={describedBy} invalid={Boolean(error)} />;
      break;
    default:
      control = <input id={id} type="text" value={draft} onChange={(e) => onChange(e.target.value)} disabled={locked} aria-describedby={describedBy} aria-invalid={Boolean(error)} />;
  }

  return (
    <div className={'setting-row' + (error ? ' has-error' : '') + (changed ? ' is-changed' : '')} data-testid={'setting-' + field.key}>
      <div className="setting-label">
        <label htmlFor={id} className="setting-name">{field.label}</label>
        <div className="setting-badges">
          {field.applyMode === 'Restart'
            ? <Badge tone="warning" title={s.restartTitle}>{s.restartRequired}</Badge>
            : <Badge tone="success" title={s.liveTitle}>{s.live}</Badge>}
          {field.overriddenByEnvironment && <Badge tone="info" title={s.overriddenNote}><Icon name="lock" size={12} /> {s.overridden}</Badge>}
          {field.type === 'Secret' && <Badge tone={field.isSet ? 'success' : 'neutral'}>{field.isSet ? s.secretSet : s.secretNotSet}</Badge>}
        </div>
        <code className="mono muted small setting-key">{field.key}</code>
      </div>
      <div className="setting-control">
        {control}
        <p id={helpId} className="setting-help muted small">
          {field.description}
          {field.type === 'Integer' && typeof field.min === 'number' && typeof field.max === 'number' && <> {s.rangeHint(field.min, field.max)}.</>}
          {field.type === 'Secret' && <> {s.secretHint}</>}
          {field.overriddenByEnvironment && <> {s.overriddenNote}</>}
        </p>
        {error && <p id={errorId} className="field-error" role="alert">{error}</p>}
      </div>
    </div>
  );
}

/**
 * Settings form generated from server field metadata. Only changed keys are sent; secrets are
 * sent only when a new value is typed; environment-overridden fields are read-only.
 */
export function SettingsPanel({ api, title = strings.admin.routes.settings.label, summary = strings.admin.routes.settings.summary, authText, platformManaged = false, platformConsoleUrl = null }) {
  const toast = useToast();
  const [fields, setFields] = useState(null);
  const [drafts, setDrafts] = useState({});
  const [loadError, setLoadError] = useState(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [serverErrors, setServerErrors] = useState({});
  const [summaryError, setSummaryError] = useState('');
  const [restartPending, setRestartPending] = useState(readRestartPending);
  const [confirmRestart, setConfirmRestart] = useState(false);
  const [restartState, setRestartState] = useState('idle');
  const [nonce, setNonce] = useState(0);
  const pollRef = useRef(null);

  const applyFields = useCallback((list) => {
    setFields(list);
    setDrafts(initialDrafts(list));
    setServerErrors({});
  }, []);

  useEffect(() => {
    // A platform-managed deployment (MultiProperty) answers 403 to property administrators; when
    // the caller is known to be in one (a delegated platform session), do not ask at all.
    if (platformManaged) { setLoading(false); return undefined; }
    const controller = new AbortController();
    let active = true;
    setLoading(true);
    setLoadError(null);
    api.getSettings(controller.signal).then(
      (res) => { if (active) { applyFields((res && res.fields) || []); setLoading(false); } },
      (err) => { if (active) { setLoadError(err); setLoading(false); } }
    );
    return () => { active = false; controller.abort(); };
  }, [api, applyFields, nonce, platformManaged]);

  useEffect(() => () => clearTimeout(pollRef.current), []);

  const groups = useMemo(() => groupFields(fields || []), [fields]);
  const changes = useMemo(() => computeChanges(fields || [], drafts), [fields, drafts]);
  const changedKeys = Object.keys(changes);
  const clientErrors = useMemo(() => {
    const result = {};
    for (const field of fields || []) {
      const message = validateField(field, drafts[field.key]);
      if (message) result[field.key] = message;
    }
    return result;
  }, [fields, drafts]);
  const restarting = restartState === 'restarting';

  const setDraft = (key, value) => {
    setDrafts((prev) => ({ ...prev, [key]: value }));
    setServerErrors((prev) => {
      if (!prev[key]) return prev;
      const next = { ...prev };
      delete next[key];
      return next;
    });
  };

  const discard = () => {
    setDrafts(initialDrafts(fields || []));
    setServerErrors({});
    setSummaryError('');
  };

  const save = async (e) => {
    e.preventDefault();
    if (changedKeys.length === 0) {
      toast.info(s.noChanges);
      return;
    }
    if (Object.keys(clientErrors).length) {
      setSummaryError(s.clientInvalid);
      return;
    }
    setSaving(true);
    setSummaryError('');
    try {
      const needsRestart = (fields || []).some((f) => changedKeys.includes(f.key) && f.applyMode === 'Restart');
      const res = await api.updateSettings(changes);
      applyFields((res && res.fields) || fields || []);
      if (needsRestart) {
        setRestartPending(true);
        writeRestartPending(true);
      }
      toast.success(needsRestart ? s.saved : s.saved + ' ' + s.savedLive);
    } catch (err) {
      const fe = getFieldErrors(err);
      setServerErrors(fe);
      setSummaryError(Object.keys(fe).length ? s.validationSummary : errorMessage(err, strings.common.errorGeneric));
    } finally {
      setSaving(false);
    }
  };

  const restart = async () => {
    setConfirmRestart(false);
    let startedBefore = null;
    try {
      const before = await api.getHealth();
      startedBefore = before && before.startedUtc;
    } catch {
      startedBefore = null;
    }
    try {
      await api.restartServer();
    } catch (err) {
      toast.error(s.restartFailed + ' ' + errorMessage(err, ''));
      return;
    }
    setRestartState('restarting');
    const deadline = Date.now() + RESTART_TIMEOUT_MS;
    let sawDown = false;
    const poll = async () => {
      try {
        const health = await api.getHealth();
        const restarted = sawDown || (startedBefore && health && health.startedUtc !== startedBefore);
        if (restarted) {
          setRestartState('idle');
          setRestartPending(false);
          writeRestartPending(false);
          toast.success(s.restarted);
          setNonce((n) => n + 1);
          return;
        }
      } catch {
        // The connection drop during restart is expected.
        sawDown = true;
      }
      if (Date.now() > deadline) {
        setRestartState('timeout');
        return;
      }
      pollRef.current = setTimeout(poll, RESTART_POLL_MS);
    };
    pollRef.current = setTimeout(poll, RESTART_POLL_MS);
  };

  const origin = window.location.origin;
  // In MultiProperty mode /settings is 403 for property administrators: explain where settings live instead of an error.
  const managedElsewhere = platformManaged || Boolean(loadError && loadError.status === 403);

  return (
    <div className="view">
      <PageHeader
        title={title}
        summary={summary}
        actions={managedElsewhere ? null : (
          <button type="button" className="button button-danger-outline button-sm" onClick={() => setConfirmRestart(true)} disabled={restarting}>
            <Icon name="power" size={14} /><span>{s.restartButton}</span>
          </button>
        )}
      />

      {restarting && <Banner tone="warning" icon="refresh">{s.restarting}</Banner>}
      {restartState === 'timeout' && <ErrorBanner message={s.restartTimeout} onDismiss={() => setRestartState('idle')} />}
      {restartPending && !restarting && !managedElsewhere && (
        <Banner
          tone="warning"
          icon="alert"
          actions={<button type="button" className="button button-secondary button-sm" onClick={() => setConfirmRestart(true)}>{s.restartButton}</button>}
        >
          {s.restartBanner}
        </Banner>
      )}

      {loading && !fields && <Loading />}
      {managedElsewhere && (
        <section className="panel" aria-labelledby="settings-managed-title">
          <h2 id="settings-managed-title">{s.platformManagedTitle}</h2>
          <p className="muted">{s.platformManagedBody}</p>
          <div className="row-actions">
            <Link to="/admin/integrations" className="button button-secondary button-sm">{s.platformManagedIntegrations}</Link>
            {platformConsoleUrl && <a href={platformConsoleUrl} className="button button-secondary button-sm">{s.platformManagedConsole}</a>}
          </div>
        </section>
      )}
      {loadError && !managedElsewhere && <ErrorBanner message={s.loadError + ' ' + errorMessage(loadError, '')} onRetry={() => setNonce((n) => n + 1)} />}

      {fields && fields.length === 0 && <EmptyState icon="gear" title={s.empty} />}

      {fields && fields.length > 0 && (
        <form className="settings-form" onSubmit={save} noValidate>
          {summaryError && <ErrorBanner message={summaryError} onDismiss={() => setSummaryError('')} />}
          {groups.map((group) => (
            <fieldset key={group.section} className="settings-section" disabled={saving || restarting}>
              <legend>
                <span>{group.section}</span>
                <span className="muted small">{s.sectionCount(group.fields.length)}</span>
              </legend>
              {group.fields.map((field) => (
                <SettingField
                  key={field.key}
                  field={field}
                  draft={drafts[field.key]}
                  onChange={(value) => setDraft(field.key, value)}
                  error={serverErrors[field.key] || clientErrors[field.key]}
                  disabled={saving || restarting}
                />
              ))}
            </fieldset>
          ))}
          <div className="form-actions sticky-actions">
            <span className="muted small" aria-live="polite">{changedKeys.length > 0 ? s.changedCount(changedKeys.length) : ''}</span>
            <button type="button" className="button button-secondary" onClick={discard} disabled={saving || changedKeys.length === 0}>{s.reset}</button>
            <button type="submit" className="button button-primary" disabled={saving || restarting || changedKeys.length === 0}>{saving ? s.saving : s.save}</button>
          </div>
        </form>
      )}

      <section className="panel" aria-labelledby="server-info-title">
        <div className="panel-header"><h2 id="server-info-title">{s.serverInfoTitle}</h2></div>
        <dl className="kv-grid kv-grid-wide">
          <div className="kv-item"><dt>{s.endpoint}</dt><dd className="copy-row"><code className="mono">{origin + '/api/v1'}</code><CopyButton value={origin + '/api/v1'} label={s.endpoint} /></dd></div>
          <div className="kv-item"><dt>{s.openApi}</dt><dd className="copy-row"><code className="mono">{origin + '/openapi.json'}</code><CopyButton value={origin + '/openapi.json'} label={s.openApi} /></dd></div>
          <div className="kv-item"><dt>{s.auth}</dt><dd>{authText}</dd></div>
        </dl>
      </section>

      <ConfirmModal
        open={confirmRestart}
        title={s.restartConfirmTitle}
        message={s.restartConfirmBody}
        confirmLabel={s.restartButton}
        onConfirm={restart}
        onCancel={() => setConfirmRestart(false)}
      />
    </div>
  );
}

/** The admin console's Settings page (GET/PUT /settings) inside the admin auth context. */
function SettingsView() {
  const { api, mode, principal } = useAdminAuth();
  const authText = mode === 'delegated' ? strings.admin.delegation.authValue : mode === 'session' ? s.authSession(principal) : s.authValue;
  // Delegated sessions exist only in MultiProperty deployments, opened from the platform console on
  // this host, so deployment settings are platform-managed and their editor is one link away.
  const delegated = mode === 'delegated';
  return <SettingsPanel api={api} authText={authText} platformManaged={delegated} platformConsoleUrl={delegated ? '/platform/settings' : null} />;
}

export default SettingsView;
