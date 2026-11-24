import { collectEnumeration } from '@trelliskit/shared';
import useCollectionPage from '../../../hooks/useCollectionPage.js';
import TablePagination from '../../../components/TablePagination.jsx';
import { useCallback, useEffect, useState } from 'react';
import strings from '../../../strings.js';
import { useAdminAuth } from '../../../context/AdminAuthContext.jsx';
import { useToast } from '../../../context/ToastContext.jsx';
import { errorMessage } from '../../../utils/api.js';
import { formatDateTime } from '../../../utils/format.js';
import PageHeader from '../../../components/PageHeader.jsx';
import DataTable from '../../../components/DataTable.jsx';
import Modal from '../../../components/Modal.jsx';
import { Badge } from '../../../components/Badges.jsx';
import { ErrorBanner } from '../../../components/States.jsx';
import { usd } from './aiAdminUtils.js';
import AiCallDetail from './AiCallDetail.jsx';
import AiHarnessPanel from './AiHarnessPanel.jsx';

const s = strings.ai.rolesAdmin;
const RUNTIMES = ['ApiEndpoint', 'ClaudeCode', 'Codex', 'GeminiCli', 'CursorCli', 'Mux', 'OpenCode', 'Pi'];
const EFFORTS = ['Minimal', 'Low', 'Medium', 'High'];
const MAX_EXECUTORS = 8;

/** The PUT body for a role from the editor state. */
export function roleBody(form) {
  return {
    reasoningEffort: form.reasoningEffort || null,
    availabilityPolicy: form.availabilityPolicy,
    exhaustionPolicy: form.exhaustionPolicy,
    executors: form.executors.map((e) => ({
      runtime: e.runtime,
      endpointId: e.runtime === 'ApiEndpoint' || e.runtime === 'Mux' ? (e.endpointId || null) : null,
      model: e.runtime === 'ApiEndpoint' ? null : (e.model || null),
      enabled: e.enabled !== false
    }))
  };
}

function RoleEditor({ role, endpoints, onClose, onSaved }) {
  const { client } = useAdminAuth();
  const toast = useToast();
  const f = s.form;
  const [form, setForm] = useState(() => ({
    reasoningEffort: role.reasoningEffort || '',
    availabilityPolicy: role.availabilityPolicy,
    exhaustionPolicy: role.exhaustionPolicy,
    executors: role.executors.map((e) => ({ runtime: e.runtime, endpointId: e.endpointId || '', model: e.model && e.runtime !== 'ApiEndpoint' ? e.model : '', enabled: e.enabled }))
  }));
  const [busy, setBusy] = useState(false);
  const runtimes = role.allowsHarness ? RUNTIMES : ['ApiEndpoint'];
  const usable = endpoints.filter((e) => e.kind === role.kind);
  const setExec = (i, patch) => setForm((v) => ({ ...v, executors: v.executors.map((e, j) => (j === i ? { ...e, ...patch } : e)) }));
  const move = (i, d) => setForm((v) => {
    const list = [...v.executors];
    const j = i + d;
    if (j < 0 || j >= list.length) return v;
    [list[i], list[j]] = [list[j], list[i]];
    return { ...v, executors: list };
  });
  const save = async (e) => {
    e.preventDefault();
    setBusy(true);
    try {
      await client.adminSaveRole(role.role, roleBody(form));
      toast.success(f.saved);
      onSaved();
    } catch (err) {
      toast.error(errorMessage(err));
    } finally {
      setBusy(false);
    }
  };
  return (
    <Modal open onClose={onClose} size="lg" title={f.title(role.title)} subtitle={role.description} footer={(
      <>
        <button type="button" className="button button-secondary" onClick={onClose}>{f.close}</button>
        <button type="submit" form="role-form" className="button button-primary" disabled={busy}>{f.save}</button>
      </>
    )}>
      <form id="role-form" className="stack-form" onSubmit={save} noValidate>
        <p className="muted small">{s.suggested}: {role.suggested}</p>
        {role.kind === 'Completion' && (
          <label className="field"><span className="label-text">{f.effort}</span>
            <select value={form.reasoningEffort} onChange={(e) => setForm((v) => ({ ...v, reasoningEffort: e.target.value }))}>
              <option value="">{f.effortDefault}</option>
              {EFFORTS.map((x) => <option key={x} value={x}>{x}</option>)}
            </select>
          </label>
        )}
        <label className="field"><span className="label-text">{f.availability}</span>
          <select value={form.availabilityPolicy} onChange={(e) => setForm((v) => ({ ...v, availabilityPolicy: e.target.value }))}>
            {Object.keys(f.availabilityOptions).map((x) => <option key={x} value={x}>{f.availabilityOptions[x]}</option>)}
          </select>
        </label>
        <label className="field"><span className="label-text">{f.exhaustion}</span>
          <select value={form.exhaustionPolicy} onChange={(e) => setForm((v) => ({ ...v, exhaustionPolicy: e.target.value }))}>
            {Object.keys(f.exhaustionOptions).map((x) => <option key={x} value={x}>{f.exhaustionOptions[x]}</option>)}
          </select>
        </label>
        <fieldset className="field"><legend className="label-text">{f.executors}</legend>
          <ol className="stack">
            {form.executors.map((e, i) => (
              <li key={i} className="row-between" data-testid={'executor-' + i}>
                <select aria-label={f.runtime + ' ' + (i + 1)} value={e.runtime} onChange={(ev) => setExec(i, { runtime: ev.target.value })}>
                  {runtimes.map((r) => <option key={r} value={r}>{r}</option>)}
                </select>
                {(e.runtime === 'ApiEndpoint' || e.runtime === 'Mux') && (
                  e.runtime === 'ApiEndpoint'
                    ? (
                      <select aria-label={f.endpoint + ' ' + (i + 1)} value={e.endpointId} onChange={(ev) => setExec(i, { endpointId: ev.target.value })}>
                        <option value="">{f.chooseEndpoint}</option>
                        {usable.map((x) => <option key={x.id} value={x.id}>{x.name + ' (' + x.model + ')'}</option>)}
                      </select>
                    )
                    : <input aria-label={f.endpoint + ' ' + (i + 1)} value={e.endpointId} onChange={(ev) => setExec(i, { endpointId: ev.target.value })} />
                )}
                {e.runtime !== 'ApiEndpoint' && <input aria-label={f.model + ' ' + (i + 1)} value={e.model} placeholder={f.model} onChange={(ev) => setExec(i, { model: ev.target.value })} />}
                <label className="checkbox-row"><input type="checkbox" checked={e.enabled !== false} onChange={(ev) => setExec(i, { enabled: ev.target.checked })} /> {f.enabled}</label>
                <span className="badge-row">
                  <button type="button" className="button button-ghost button-sm" onClick={() => move(i, -1)} disabled={i === 0}>{f.up}</button>
                  <button type="button" className="button button-ghost button-sm" onClick={() => move(i, 1)} disabled={i === form.executors.length - 1}>{f.down}</button>
                  <button type="button" className="button button-ghost button-sm" onClick={() => setForm((v) => ({ ...v, executors: v.executors.filter((_, j) => j !== i) }))}>{f.remove}</button>
                </span>
              </li>
            ))}
          </ol>
          <button type="button" className="button button-secondary button-sm" disabled={form.executors.length >= MAX_EXECUTORS}
            onClick={() => setForm((v) => ({ ...v, executors: [...v.executors, { runtime: 'ApiEndpoint', endpointId: '', model: '', enabled: true }] }))}>{f.add}</button>
        </fieldset>
      </form>
    </Modal>
  );
}

function RoleTester({ role, onClose }) {
  const { client } = useAdminAuth();
  const toast = useToast();
  const t = s.testForm;
  const [prompt, setPrompt] = useState('');
  const [result, setResult] = useState(null);
  const [busy, setBusy] = useState(false);
  const send = async (e) => {
    e.preventDefault();
    setBusy(true);
    try {
      setResult(await client.adminTestRole(role.role, prompt));
    } catch (err) {
      toast.error(errorMessage(err));
    } finally {
      setBusy(false);
    }
  };
  return (
    <Modal open onClose={onClose} title={t.title(role.title)}>
      <form className="stack-form" onSubmit={send}>
        <label className="field"><span className="label-text">{t.prompt}</span><textarea rows={4} value={prompt} onChange={(e) => setPrompt(e.target.value)} maxLength={4000} /></label>
        <div className="form-actions-start"><button type="submit" className="button button-primary" disabled={busy || !prompt.trim()}>{t.send}</button></div>
      </form>
      {result && (
        <div className="stack" role="status">
          <strong>{result.success ? t.answeredBy(result.executorPosition, result.model) : t.failed(result.errorCode)}</strong>
          {result.text && <pre className="code-block">{result.text}</pre>}
          {result.escalations.length > 0 && (
            <>
              <span className="label-text">{t.escalations}</span>
              <ul className="stack">{result.escalations.map((x) => <li key={x} className="muted small">{x}</li>)}</ul>
            </>
          )}
          <span className="muted small">{result.latencyMs + ' ms, ' + usd(result.costUsd)}</span>
        </div>
      )}
    </Modal>
  );
}

/** Admin, AI, Task roles: executor hierarchies per role, harness profiles, and the call ledger with each call's telemetry (docs/INTEGRATION.md sections 30.2 to 30.5). */
function AiRolesView() {
  const rolesPage = useCollectionPage();
  const { query: rolesPageQuery, setResult: rolesPageSetResult } = rolesPage;
  const callsPage = useCollectionPage();
  const { query: callsPageQuery, setResult: callsPageSetResult } = callsPage;

  const { client } = useAdminAuth();
  const [roles, setRoles] = useState(null);
  const [endpoints, setEndpoints] = useState([]);
  const [calls, setCalls] = useState({ items: [], loading: true });
  const [editing, setEditing] = useState(null);
  const [testing, setTesting] = useState(null);
  const [viewing, setViewing] = useState(null);
  const [error, setError] = useState(null);

  const load = useCallback(async () => {
    try {
      const [list, page] = await Promise.all([client.adminListRoles(rolesPageQuery), client.adminEnumerateCalls(callsPageQuery)]);
      setRoles(list.objects);
      rolesPageSetResult(list); callsPageSetResult(page);
      setCalls({ items: page.objects || [], loading: false });
      setError(null);
    } catch (err) {
      setError(errorMessage(err));
      setCalls((c) => ({ ...c, loading: false }));
    }
    try {
      const eps = await collectEnumeration((query, signal) => client.adminEnumerateEndpoints(query, signal), { maxResults: 50, ordering: 'NameAscending' });
      setEndpoints(eps.objects || []);
    } catch {
      setEndpoints([]);
    }
  }, [client, rolesPageQuery, rolesPageSetResult, callsPageQuery, callsPageSetResult]);
  useEffect(() => { load(); }, [load]);

  const c = s.columns;
  return (
    <section className="stack">
      <PageHeader title={s.title} summary={s.intro} />
      <ErrorBanner message={error} onRetry={load} />
      <h2 className="section-title">{s.roles}</h2>
      <TablePagination {...rolesPage.props} />
      <DataTable caption={s.roles} rows={roles || []} rowKey={(row) => row.role} loading={!roles && !error} emptyMessage={s.none} columns={[
        { key: 'role', label: c.role, render: (row) => (
          <div className="cell-stack">
            <strong>{row.title}</strong>
            <span className="muted small">{row.description}</span>
            <div className="badge-row">
              {row.allowsHarness && <Badge tone="info">{s.harnessAllowed}</Badge>}
              {row.interactive && <Badge>{s.interactive}</Badge>}
            </div>
          </div>
        ) },
        { key: 'executors', label: c.executors, render: (row) => (row.executors.length === 0 ? <span className="muted">{s.none}</span> : (
          <ol className="cell-stack">
            {row.executors.map((e) => (
              <li key={e.position} className={e.enabled ? '' : 'muted'}>
                {e.runtime === 'ApiEndpoint' ? (e.endpointName || e.endpointId) : e.runtime}{e.model ? ' (' + e.model + ')' : ''}
                {e.problem && <> <Badge tone="warning">{e.problem}</Badge></>}
              </li>
            ))}
          </ol>
        )) },
        { key: 'effort', label: c.effort, render: (row) => row.reasoningEffort || '' },
        { key: 'status', label: c.status, render: (row) => (row.ready ? <Badge tone="success">{s.ready}</Badge> : <Badge tone="warning">{s.notReady}</Badge>) },
        { key: 'actions', label: c.actions, render: (row) => (
          <div className="badge-row">
            <button type="button" className="button button-secondary button-sm" onClick={() => setEditing(row)}>{s.edit}</button>
            {row.kind === 'Completion' && <button type="button" className="button button-ghost button-sm" disabled={!row.ready} onClick={() => setTesting(row)}>{s.test}</button>}
          </div>
        ) }
      ]} />

      <AiHarnessPanel />

      <h2 className="section-title">{s.ledger}</h2>
      <p className="muted small">{s.ledgerIntro}</p>
      <TablePagination {...callsPage.props} />
      <DataTable caption={s.ledger} rows={calls.items} rowKey={(row) => row.id} loading={calls.loading} emptyMessage={s.ledgerEmpty} onRowClick={(row) => setViewing(row)} columns={[
        { key: 'time', label: c.time, render: (row) => formatDateTime(row.createdUtc) },
        { key: 'role', label: c.role, render: (row) => row.role || row.purpose },
        { key: 'model', label: c.model, render: (row) => <code className="mono">{row.model || ''}</code> },
        { key: 'position', label: c.position, className: 'num', render: (row) => (row.executorPosition == null ? '' : '#' + row.executorPosition) },
        { key: 'outcome', label: c.outcome, render: (row) => <Badge tone={row.outcome === 'Ok' ? 'success' : 'danger'} title={row.escalationReason || row.errorCode || undefined}>{row.outcome}</Badge> },
        { key: 'tokens', label: c.tokens, className: 'num', render: (row) => (row.totalTokens ?? (row.promptTokens + row.completionTokens)).toLocaleString() },
        { key: 'firstToken', label: c.firstToken, className: 'num', render: (row) => (row.timeToFirstTokenMs == null ? '' : row.timeToFirstTokenMs + ' ms') },
        { key: 'latency', label: c.latency, className: 'num', render: (row) => row.latencyMs + ' ms' },
        { key: 'cost', label: c.cost, className: 'num', render: (row) => usd(row.costUsd) },
        { key: 'job', label: c.job, render: (row) => (row.jobKey ? <code className="mono small">{row.jobKey}</code> : '') },
        { key: 'details', label: <span className="visually-hidden">{s.details}</span>, render: (row) => <button type="button" className="button button-ghost button-sm" aria-label={s.detailsFor(row.id)} onClick={() => setViewing(row)}>{s.details}</button> }
      ]} />

      {editing && <RoleEditor role={editing} endpoints={endpoints} onClose={() => setEditing(null)} onSaved={() => { setEditing(null); load(); }} />}
      {testing && <RoleTester role={testing} onClose={() => setTesting(null)} />}
      {viewing && <AiCallDetail call={viewing} onClose={() => setViewing(null)} />}
    </section>
  );
}

export default AiRolesView;
