import { useState } from 'react';
import { AppPlatform, DomainKind, PROPERTY_MAX_APPS, PROPERTY_MAX_DOMAINS, domainsAndAppsBody, domainsAndAppsProblems } from '@trelliskit/shared';
import strings from '../strings.js';
import { errorMessage, getFieldErrors } from '../utils/api.js';
import { formatDateTime } from '../utils/format.js';
import Icon from './Icon.jsx';
import { Badge } from './Badges.jsx';
import { ErrorBanner } from './States.jsx';

const e = strings.admin.domainsEditor;
const kinds = strings.platform.domainKinds;

let nextKey = 1;
const key = () => 'row' + (nextKey++);

function toRows(record) {
  return {
    domains: ((record && record.domains) || []).map((d) => ({ key: key(), id: d.id, host: d.host, kind: d.kind, verifiedUtc: d.verifiedUtc })),
    apps: ((record && record.apps) || []).map((a) => ({ key: key(), id: a.id, platform: a.platform, appId: a.appId, storeAppId: a.storeAppId || '', enabled: a.enabled !== false }))
  };
}

/** Verification state of a domain row: the backend's `verifiedUtc` (null means pending), or unsaved. */
export function domainVerification(row) {
  if (!row.id) return { tone: 'neutral', label: e.newRow, title: '' };
  if (row.verifiedUtc) return { tone: 'success', label: e.verified, title: e.verifiedTitle(formatDateTime(row.verifiedUtc)) };
  return { tone: 'warning', label: e.pending, title: '' };
}

/**
 * Editable domain and app id lists of a property record (`PUT /platform/properties/{id}` replaces
 * both lists). Shows each domain's verification state from `verifiedUtc`, checks uniqueness and
 * shape locally, and maps the server's `fieldErrors` and 409 conflicts back to the rows.
 *
 * @param {{ record: object, onSave: (body: { domains: object[], apps: object[] }) => Promise<object>, onSaved?: (record: object) => void, disabled?: boolean }} props
 */
function DomainsAppsEditor({ record, onSave, onSaved, disabled = false }) {
  const [rows, setRows] = useState(() => toRows(record));
  const [problems, setProblems] = useState({});
  const [error, setError] = useState('');
  const [saving, setSaving] = useState(false);
  const [savedNote, setSavedNote] = useState('');

  const setDomain = (k, patch) => setRows((r) => ({ ...r, domains: r.domains.map((d) => (d.key === k ? { ...d, ...patch, ...(patch.host !== undefined && d.id && patch.host !== d.host ? { id: null, verifiedUtc: null } : {}) } : d)) }));
  const setApp = (k, patch) => setRows((r) => ({ ...r, apps: r.apps.map((a) => (a.key === k ? { ...a, ...patch } : a)) }));
  const addDomain = () => setRows((r) => ({ ...r, domains: [...r.domains, { key: key(), id: null, host: '', kind: r.domains.some((d) => d.kind === DomainKind.Web) ? DomainKind.Api : DomainKind.Web, verifiedUtc: null }] }));
  const addApp = () => setRows((r) => ({ ...r, apps: [...r.apps, { key: key(), id: null, platform: AppPlatform.Ios, appId: '', storeAppId: '', enabled: true }] }));
  const removeDomain = (k) => setRows((r) => ({ ...r, domains: r.domains.filter((d) => d.key !== k) }));
  const removeApp = (k) => setRows((r) => ({ ...r, apps: r.apps.filter((a) => a.key !== k) }));

  const reset = () => {
    setRows(toRows(record));
    setProblems({});
    setError('');
  };

  const save = async (ev) => {
    ev.preventDefault();
    setSavedNote('');
    // Drop empty rows first so row indexes match the indexes of the server's fieldErrors.
    const kept = {
      domains: rows.domains.filter((d) => d.host.trim()),
      apps: rows.apps.filter((a) => a.appId.trim())
    };
    setRows(kept);
    const body = domainsAndAppsBody(kept.domains, kept.apps);
    const local = domainsAndAppsProblems(body.domains, body.apps);
    setProblems(local);
    if (Object.keys(local).length) {
      setError(e.fixProblems);
      return;
    }
    setError('');
    setSaving(true);
    try {
      const updated = await onSave(body);
      setRows(toRows(updated));
      setProblems({});
      setSavedNote(e.saved);
      if (onSaved) onSaved(updated);
    } catch (err) {
      setProblems(getFieldErrors(err));
      setError(errorMessage(err, strings.common.errorGeneric));
    } finally {
      setSaving(false);
    }
  };

  const problemText = (name) => {
    const value = problems[name];
    if (!value) return '';
    return e.problems[value] || value;
  };
  const busy = disabled || saving;

  return (
    <form className="stack-form domains-editor" onSubmit={save} noValidate>
      <ErrorBanner message={error} onDismiss={() => setError('')} />
      <fieldset className="settings-section" disabled={busy}>
        <legend><span>{e.domainsLegend(rows.domains.length, PROPERTY_MAX_DOMAINS)}</span></legend>
        {problems.domains && <span className="field-error" role="alert">{problemText('domains')}</span>}
        {rows.domains.length === 0 && <p className="muted small">{e.noDomains}</p>}
        {rows.domains.length > 0 && (
          <div className="table-scroll">
            <table className="data-table compact">
              <thead><tr><th scope="col">{e.host}</th><th scope="col">{e.kind}</th><th scope="col">{e.status}</th><th scope="col"><span className="visually-hidden">{strings.common.delete}</span></th></tr></thead>
              <tbody>
                {rows.domains.map((d, i) => {
                  const v = domainVerification(d);
                  const fieldProblem = problemText('domains[' + i + '].host');
                  return (
                    <tr key={d.key}>
                      <td>
                        <input className="mono" aria-label={e.host + ' ' + (i + 1)} value={d.host} placeholder={e.hostPlaceholder} onChange={(x) => setDomain(d.key, { host: x.target.value })} aria-invalid={Boolean(fieldProblem)} />
                        {fieldProblem && <span className="field-error" role="alert">{fieldProblem}</span>}
                      </td>
                      <td>
                        <select aria-label={e.kind + ' ' + (i + 1)} value={d.kind} onChange={(x) => setDomain(d.key, { kind: x.target.value })}>
                          {Object.values(DomainKind).map((k) => <option key={k} value={k}>{kinds[k]}</option>)}
                        </select>
                      </td>
                      <td><span title={v.title}><Badge tone={v.tone}>{v.label}</Badge></span></td>
                      <td className="cell-actions">
                        <button type="button" className="icon-button" onClick={() => removeDomain(d.key)} aria-label={e.removeDomain(d.host)} title={e.removeDomain(d.host)}><Icon name="trash" size={14} /></button>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
        <div>
          <button type="button" className="button button-secondary button-sm" onClick={addDomain} disabled={busy || rows.domains.length >= PROPERTY_MAX_DOMAINS}><Icon name="plus" size={14} /><span>{e.addDomain}</span></button>
        </div>
      </fieldset>

      <fieldset className="settings-section" disabled={busy}>
        <legend><span>{e.appsLegend(rows.apps.length, PROPERTY_MAX_APPS)}</span></legend>
        {problems.apps && <span className="field-error" role="alert">{problemText('apps')}</span>}
        {rows.apps.length === 0 && <p className="muted small">{e.noApps}</p>}
        {rows.apps.length > 0 && (
          <div className="table-scroll">
            <table className="data-table compact">
              <thead><tr><th scope="col">{e.platform}</th><th scope="col">{e.appId}</th><th scope="col">{e.storeAppId}</th><th scope="col">{e.enabled}</th><th scope="col"><span className="visually-hidden">{strings.common.delete}</span></th></tr></thead>
              <tbody>
                {rows.apps.map((a, i) => {
                  const fieldProblem = problemText('apps[' + i + '].appId');
                  return (
                    <tr key={a.key}>
                      <td>
                        <select aria-label={e.platform + ' ' + (i + 1)} value={a.platform} onChange={(x) => setApp(a.key, { platform: x.target.value })}>
                          {Object.values(AppPlatform).map((p) => <option key={p} value={p}>{e.platforms[p]}</option>)}
                        </select>
                      </td>
                      <td>
                        <input className="mono" aria-label={e.appId + ' ' + (i + 1)} value={a.appId} placeholder={e.appIdPlaceholder} onChange={(x) => setApp(a.key, { appId: x.target.value })} aria-invalid={Boolean(fieldProblem)} />
                        {fieldProblem && <span className="field-error" role="alert">{fieldProblem}</span>}
                      </td>
                      <td><input className="mono" aria-label={e.storeAppId + ' ' + (i + 1)} value={a.storeAppId} placeholder={e.storeAppIdPlaceholder} onChange={(x) => setApp(a.key, { storeAppId: x.target.value })} /></td>
                      <td><input type="checkbox" aria-label={e.enabled + ' ' + (i + 1)} checked={a.enabled} onChange={(x) => setApp(a.key, { enabled: x.target.checked })} /></td>
                      <td className="cell-actions">
                        <button type="button" className="icon-button" onClick={() => removeApp(a.key)} aria-label={e.removeApp(a.appId)} title={e.removeApp(a.appId)}><Icon name="trash" size={14} /></button>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
        <div>
          <button type="button" className="button button-secondary button-sm" onClick={addApp} disabled={busy || rows.apps.length >= PROPERTY_MAX_APPS}><Icon name="plus" size={14} /><span>{e.addApp}</span></button>
        </div>
      </fieldset>

      <div className="form-actions">
        <span className="muted small" aria-live="polite">{savedNote}</span>
        <button type="button" className="button button-secondary" onClick={reset} disabled={busy}>{e.reset}</button>
        <button type="submit" className="button button-primary" disabled={busy}>{saving ? e.saving : e.save}</button>
      </div>
    </form>
  );
}

export default DomainsAppsEditor;
