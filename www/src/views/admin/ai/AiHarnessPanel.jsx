import useCollectionPage from '../../../hooks/useCollectionPage.js';
import TablePagination from '../../../components/TablePagination.jsx';
import { useCallback, useEffect, useState } from 'react';
import strings from '../../../strings.js';
import { useAdminAuth } from '../../../context/AdminAuthContext.jsx';
import { useToast } from '../../../context/ToastContext.jsx';
import { errorMessage } from '../../../utils/api.js';
import DataTable from '../../../components/DataTable.jsx';
import Modal from '../../../components/Modal.jsx';
import { Badge } from '../../../components/Badges.jsx';
import { ErrorBanner } from '../../../components/States.jsx';

const h = strings.ai.rolesAdmin.harness;

/** Editor state for a harness profile (the credential is never loaded; `credentialMode` keeps or replaces it). */
export function harnessForm(profile) {
  const p = profile || {};
  const str = (v) => (v === null || v === undefined ? '' : String(v));
  return {
    model: p.model || '', permissionMode: p.permissionMode || '', timeoutSeconds: str(p.timeoutSeconds), maxToolCalls: str(p.maxToolCalls),
    allowWebResearch: p.allowWebResearch !== false, baseUrl: p.baseUrl || '', credentialType: p.credentialType || 'Environment',
    credential: '', credentialMode: p.hasCredential ? 'keep' : 'replace', storedType: p.hasCredential ? p.credentialType : null
  };
}

/**
 * The PUT body for a harness profile. Empty fields are sent as null (the profile is replaced). The
 * credential is write-only: it is sent only when the administrator replaces it, and never with the
 * Environment type; omitting it keeps the stored value.
 */
export function harnessBody(form) {
  const number = (v) => (v === '' || v === null || v === undefined || !Number.isFinite(Number(v)) ? null : Number(v));
  const body = {
    model: form.model.trim() || null,
    permissionMode: form.permissionMode || null,
    timeoutSeconds: number(form.timeoutSeconds),
    maxToolCalls: number(form.maxToolCalls),
    allowWebResearch: form.allowWebResearch !== false,
    baseUrl: form.baseUrl.trim() || null,
    credentialType: form.credentialType || 'Environment'
  };
  const keeps = form.credentialMode === 'keep' && form.storedType === form.credentialType;
  if (body.credentialType !== 'Environment' && !keeps) body.credential = form.credential;
  return body;
}

/** What is wrong with a harness form before it is sent: `'credential'`, `'baseUrl'`, or null. */
export function harnessProblem(form) {
  if (form.credentialType === 'AuthToken' && !form.baseUrl.trim()) return 'baseUrl';
  const keeps = form.credentialMode === 'keep' && form.storedType === form.credentialType;
  if (form.credentialType !== 'Environment' && !keeps && !form.credential) return 'credential';
  return null;
}

function HarnessEditor({ profile, onClose, onSaved }) {
  const { client } = useAdminAuth();
  const toast = useToast();
  const f = h.form;
  const [form, setForm] = useState(() => harnessForm(profile));
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const set = (key) => (e) => {
    const value = e.target.type === 'checkbox' ? e.target.checked : e.target.value;
    setForm((v) => ({ ...v, [key]: value }));
  };
  const canKeep = form.storedType !== null && form.storedType === form.credentialType;
  const names = (form.credentialType === 'Environment' ? profile.environmentVariables : variablesFor(profile, form.credentialType)).join(', ');
  const save = async (e) => {
    e.preventDefault();
    const problem = harnessProblem(form);
    if (problem) {
      setError(problem === 'baseUrl' ? f.needsBaseUrl : f.needsCredential);
      return;
    }
    setBusy(true);
    setError('');
    try {
      await client.adminSaveHarness(profile.runtime, harnessBody(form));
      toast.success(f.saved);
      onSaved();
    } catch (err) {
      setError(errorMessage(err, strings.common.errorGeneric));
      setBusy(false);
    }
  };
  return (
    <Modal open size="md" onClose={busy ? () => {} : onClose} title={f.title(profile.runtime)} footer={(
      <>
        <button type="button" className="button button-secondary" onClick={onClose} disabled={busy}>{f.close}</button>
        <button type="submit" form="harness-form" className="button button-primary" disabled={busy}>{f.save}</button>
      </>
    )}>
      <form id="harness-form" className="stack-form" onSubmit={save} noValidate>
        <ErrorBanner message={error} />
        <label className="field"><span className="label-text">{f.model}</span>
          <input value={form.model} onChange={set('model')} maxLength={200} autoComplete="off" />
          <span className="muted small">{f.modelHint}</span>
        </label>
        <label className="field"><span className="label-text">{f.mode}</span>
          <select value={form.permissionMode} onChange={set('permissionMode')}>
            {Object.keys(f.modes).map((m) => <option key={m} value={m}>{f.modes[m]}</option>)}
          </select>
          <span className="muted small">{f.modeHint}</span>
        </label>
        <div className="ai-form-grid">
          <label className="field"><span className="label-text">{f.timeout}</span>
            <input type="number" min="30" max="3600" value={form.timeoutSeconds} onChange={set('timeoutSeconds')} />
            <span className="muted small">{f.timeoutHint(profile.defaultTimeoutSeconds)}</span>
          </label>
          <label className="field"><span className="label-text">{f.maxToolCalls}</span>
            <input type="number" min="1" max="500" value={form.maxToolCalls} onChange={set('maxToolCalls')} />
            <span className="muted small">{f.maxToolCallsHint(profile.defaultMaxToolCalls)}</span>
          </label>
        </div>
        <label className="checkbox-inline"><input type="checkbox" checked={form.allowWebResearch} onChange={set('allowWebResearch')} /> {f.web}</label>
        {profile.baseUrlVariable && (
          <label className="field"><span className="label-text">{f.baseUrl}</span>
            <input type="url" value={form.baseUrl} onChange={set('baseUrl')} maxLength={2000} autoComplete="off" />
            <span className="muted small">{f.baseUrlHint(profile.baseUrlVariable)}</span>
          </label>
        )}
        <label className="field"><span className="label-text">{f.credentialType}</span>
          <select value={form.credentialType} onChange={(e) => setForm((v) => ({ ...v, credentialType: e.target.value, credential: '', credentialMode: v.storedType === e.target.value ? 'keep' : 'replace' }))}>
            {profile.credentialTypes.map((t) => <option key={t} value={t}>{h.credentialTypes[t] || t}</option>)}
          </select>
          <span className="muted small">{(f.credentialHints[form.credentialType] || f.credentialHints.ApiKey)(names)}</span>
        </label>
        {form.credentialType !== 'Environment' && (
          <div className="field">
            <span className="label-text">{f.credential} <Badge tone={canKeep ? 'success' : 'neutral'}>{canKeep ? h.stored : h.notStored}</Badge></span>
            {canKeep && (
              <div className="badge-row" role="radiogroup" aria-label={f.credential}>
                {['keep', 'replace'].map((mode) => (
                  <label key={mode} className="checkbox-inline"><input type="radio" name="harness-credential-mode" checked={form.credentialMode === mode} onChange={() => setForm((v) => ({ ...v, credentialMode: mode }))} /> {mode === 'keep' ? f.keep : f.replace}</label>
                ))}
              </div>
            )}
            {(!canKeep || form.credentialMode === 'replace') && <input type="password" autoComplete="new-password" aria-label={f.credential} value={form.credential} onChange={set('credential')} maxLength={16384} />}
          </div>
        )}
      </form>
    </Modal>
  );
}

/** The variables a stored credential of a type is passed in, as the server reports them. */
function variablesFor(profile, type) {
  return (profile.credentialVariablesByType && profile.credentialVariablesByType[type]) || [];
}

function HarnessReveal({ profile, onClose }) {
  const { client } = useAdminAuth();
  const r = h.revealForm;
  const [reason, setReason] = useState('');
  const [value, setValue] = useState(null);
  const [error, setError] = useState('');
  const reveal = async () => {
    setError('');
    try {
      const result = await client.revealHarnessCredential(profile.runtime, { reason });
      setValue(result.value);
    } catch (err) {
      setError(errorMessage(err, strings.common.errorGeneric));
    }
  };
  return (
    <Modal open size="sm" onClose={onClose} title={r.title(profile.runtime)}
      footer={<button type="button" className="button button-primary" onClick={reveal} disabled={reason.trim().length < 10}>{r.submit}</button>}>
      <p className="muted small">{r.body}</p>
      <ErrorBanner message={error} />
      <label className="field"><span className="label-text">{r.reason}</span><input value={reason} onChange={(e) => setReason(e.target.value)} maxLength={500} /></label>
      {value !== null && <label className="field"><span className="label-text">{r.value}</span><input readOnly value={value} onFocus={(e) => e.target.select()} /></label>}
    </Modal>
  );
}

/** Admin, AI, Task roles: the harness profile of each CLI agent runtime (docs/HARNESSES.md, "Harness profiles"). */
function AiHarnessPanel() {
  const profilesPage = useCollectionPage();
  const { query: profilesPageQuery, setResult: profilesPageSetResult } = profilesPage;

  const { client } = useAdminAuth();
  const [profiles, setProfiles] = useState(null);
  const [error, setError] = useState(null);
  const [dialog, setDialog] = useState(null);
  const load = useCallback(async () => {
    try {
      const page = await client.adminListHarnesses(profilesPageQuery);
      setProfiles(page.objects);
      profilesPageSetResult(page);
      setError(null);
    } catch (err) {
      setError(errorMessage(err));
    }
  }, [client, profilesPageQuery, profilesPageSetResult]);
  useEffect(() => { load(); }, [load]);
  const c = h.columns;
  return (
    <>
      <h2 className="section-title">{h.title}</h2>
      <p className="muted small">{h.intro}</p>
      <ErrorBanner message={error} onRetry={load} />
      <TablePagination {...profilesPage.props} />
      <DataTable caption={h.title} rows={profiles || []} rowKey={(row) => row.runtime} loading={!profiles && !error} emptyMessage={strings.ai.rolesAdmin.none} columns={[
        { key: 'runtime', label: c.runtime, render: (row) => (
          <div className="cell-stack">
            <strong>{row.runtime}</strong>
            <div className="badge-row">
              <Badge tone={row.supportsJobTools ? 'info' : 'neutral'}>{row.supportsJobTools ? h.jobTools : h.noJobTools}</Badge>
              <Badge tone={row.runsOnThisInstance ? 'success' : 'neutral'}>{row.runsOnThisInstance ? h.runsHere : h.notHere}</Badge>
            </div>
          </div>
        ) },
        { key: 'model', label: c.model, render: (row) => (row.model ? <code className="mono">{row.model}</code> : <span className="muted">{h.defaultModel}</span>) },
        { key: 'mode', label: c.mode, render: (row) => row.permissionMode || <span className="muted">{h.modeFromJob}</span> },
        { key: 'credential', label: c.credential, render: (row) => (
          <div className="cell-stack">
            <span>{h.credentialTypes[row.credentialType] || row.credentialType}</span>
            {row.credentialType !== 'Environment' && <Badge tone={row.hasCredential ? 'success' : 'warning'}>{row.hasCredential ? h.stored : h.notStored}</Badge>}
          </div>
        ) },
        { key: 'limits', label: c.limits, render: (row) => h.limits(row.timeoutSeconds ?? row.defaultTimeoutSeconds, row.maxToolCalls ?? row.defaultMaxToolCalls) },
        { key: 'actions', label: c.actions, render: (row) => (
          <div className="badge-row">
            <button type="button" className="button button-secondary button-sm" aria-label={h.edit + ' ' + row.runtime} onClick={() => setDialog({ kind: 'edit', profile: row })}>{h.edit}</button>
            {row.hasCredential && <button type="button" className="button button-ghost button-sm" aria-label={h.reveal + ' ' + row.runtime} onClick={() => setDialog({ kind: 'reveal', profile: row })}>{h.reveal}</button>}
          </div>
        ) }
      ]} />
      {dialog && dialog.kind === 'edit' && <HarnessEditor profile={dialog.profile} onClose={() => setDialog(null)} onSaved={() => { setDialog(null); load(); }} />}
      {dialog && dialog.kind === 'reveal' && <HarnessReveal profile={dialog.profile} onClose={() => setDialog(null)} />}
    </>
  );
}

export default AiHarnessPanel;
