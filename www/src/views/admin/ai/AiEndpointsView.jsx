import { useCallback, useState } from 'react';
import strings from '../../../strings.js';
import { useAdminAuth } from '../../../context/AdminAuthContext.jsx';
import { useToast } from '../../../context/ToastContext.jsx';
import useDebounce from '../../../hooks/useDebounce.js';
import { errorMessage } from '../../../utils/api.js';
import PageHeader from '../../../components/PageHeader.jsx';
import DataTable from '../../../components/DataTable.jsx';
import ActionMenu from '../../../components/ActionMenu.jsx';
import ConfirmModal from '../../../components/ConfirmModal.jsx';
import JsonViewerModal from '../../../components/JsonViewerModal.jsx';
import Modal from '../../../components/Modal.jsx';
import { Badge } from '../../../components/Badges.jsx';
import { ErrorBanner } from '../../../components/States.jsx';
import { API_KINDS, authTypesFor, authUsesSecret, endpointBody, endpointForm, healthModesFor, hostChangedWithSecrets, providerFieldsFor, providerSettingsProblems, REASONING_EFFORTS, sendsCredentialsInClear, TOOL_CALLING_MODES, usePagedList } from './aiAdminUtils.js';

const s = strings.ai.admin.endpoints;

function Field({ id, label, hint, children }) {
  return (
    <div className="field">
      <label className="label-text" htmlFor={id}>{label}</label>
      {children}
      {hint && <span className="muted small">{hint}</span>}
    </div>
  );
}

/** Create and edit form for a model endpoint (AI contract sections 1.4 and 2.3). */
export function EndpointDialog({ endpoint, onClose, onSaved }) {
  const { client } = useAdminAuth();
  const [form, setForm] = useState(() => endpointForm(endpoint));
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [models, setModels] = useState([]);
  const set = (key) => (e) => {
    const value = e && e.target ? (e.target.type === 'checkbox' ? e.target.checked : e.target.value) : e;
    setForm((f) => ({ ...f, [key]: value }));
  };
  const kinds = API_KINDS[form.apiType] || ['Completion'];
  const authTypes = authTypesFor(form.apiType);
  const provider = providerFieldsFor(form.apiType, form.authType);
  const [providerProblems, setProviderProblems] = useState({});
  const loadModels = async () => {
    if (!endpoint) return;
    try {
      const page = await client.enumerateEndpointModels(endpoint.id, { maxResults: 200 });
      setModels(((page && page.objects) || []).map((m) => m.name));
    } catch {
      setModels([]);
    }
  };
  const submit = async (e) => {
    e.preventDefault();
    if (!form.name.trim() || !form.model.trim()) {
      setError(strings.common.errorGeneric);
      return;
    }
    const found = providerSettingsProblems(form);
    setProviderProblems(found);
    if (Object.keys(found).length) {
      setError(s.providerInvalid);
      return;
    }
    if (endpoint && hostChangedWithSecrets(endpoint, form) && !window.confirm(s.hostChangeConfirm)) return;
    setBusy(true);
    setError('');
    try {
      const body = endpointBody(form, { isCreate: !endpoint });
      await (endpoint ? client.adminUpdateEndpoint(endpoint.id, body) : client.adminCreateEndpoint(body));
      onSaved(endpoint ? s.updated : s.created);
    } catch (err) {
      setError(errorMessage(err, strings.common.errorGeneric));
      setBusy(false);
    }
  };
  const secretSet = endpoint && form.hasAuthValue;
  return (
    <Modal open size="lg" variant="drawer" onClose={busy ? () => {} : onClose} title={endpoint ? s.editTitle(endpoint.name) : s.createTitle}
      footer={(
        <>
          <button type="button" className="button button-secondary" onClick={onClose} disabled={busy}>{strings.common.cancel}</button>
          <button type="submit" form="endpoint-form" className="button button-primary" disabled={busy}>{s.save}</button>
        </>
      )}
    >
      <form id="endpoint-form" className="stack-form" onSubmit={submit} noValidate>
        <ErrorBanner message={error} />
        <div className="ai-form-grid">
          <Field id="ep-name" label={s.name}><input id="ep-name" value={form.name} onChange={set('name')} maxLength={100} required /></Field>
          <Field id="ep-api" label={s.apiType}>
            <select id="ep-api" value={form.apiType} onChange={(e) => setForm((f) => ({ ...f, apiType: e.target.value, kind: (API_KINDS[e.target.value] || ['Completion']).includes(f.kind) ? f.kind : API_KINDS[e.target.value][0], authType: authTypesFor(e.target.value).includes(f.authType) ? f.authType : authTypesFor(e.target.value)[0] }))} disabled={Boolean(endpoint)}>
              {Object.keys(API_KINDS).map((k) => <option key={k} value={k}>{k}</option>)}
            </select>
          </Field>
          <Field id="ep-kind" label={s.kind}>
            <select id="ep-kind" value={form.kind} onChange={set('kind')}>{kinds.map((k) => <option key={k} value={k}>{s.kinds[k]}</option>)}</select>
          </Field>
          <Field id="ep-model" label={s.model} hint={s.modelHint}>
            <input id="ep-model" value={form.model} onChange={set('model')} onFocus={loadModels} list="ep-model-list" maxLength={200} required />
            <datalist id="ep-model-list">{models.map((m) => <option key={m} value={m} />)}</datalist>
          </Field>
        </div>
        <Field id="ep-desc" label={s.description}><input id="ep-desc" value={form.description} onChange={set('description')} maxLength={1000} /></Field>
        <Field id="ep-url" label={s.endpointUrl} hint={s.endpointUrlHint}><input id="ep-url" type="url" value={form.endpointUrl} onChange={set('endpointUrl')} placeholder="http://ollama.internal:11434" /></Field>
        {sendsCredentialsInClear(form) && <p className="ai-warning" role="status">{s.plainHttpWarning}</p>}
        <div className="ai-form-grid">
          <Field id="ep-auth" label={s.authType}>
            <select id="ep-auth" value={form.authType} onChange={set('authType')}>{authTypes.map((t) => <option key={t} value={t}>{s.authTypes[t]}</option>)}</select>
          </Field>
          {form.authType === 'Header' && <Field id="ep-hname" label={s.authHeaderName}><input id="ep-hname" value={form.authHeaderName} onChange={set('authHeaderName')} placeholder="X-API-Key" /></Field>}
          {form.authType === 'QueryParameter' && <Field id="ep-qname" label={s.authQueryParameter}><input id="ep-qname" value={form.authQueryParameter} onChange={set('authQueryParameter')} placeholder="key" /></Field>}
          {form.authType === 'Basic' && <Field id="ep-user" label={s.authUsername}><input id="ep-user" value={form.authUsername} onChange={set('authUsername')} autoComplete="off" /></Field>}
        </div>
        {provider.used.length > 0 && (
          <fieldset className="field" data-testid="ep-provider-settings">
            <legend className="label-text">{s.providerSettings}</legend>
            {s.providerHints[form.apiType] && <p className="muted small">{s.providerHints[form.apiType]}</p>}
            <div className="ai-form-grid">
              {provider.used.map((field) => (
                <Field key={field} id={'ep-ps-' + field} label={s.providerFields[field] + (provider.required.includes(field) ? '' : ' ' + s.optional)} hint={providerProblems[field] ? s.providerProblems[providerProblems[field]] : s.providerFieldHints[field]}>
                  <input id={'ep-ps-' + field} value={form[field]} onChange={set(field)} maxLength={200} required={provider.required.includes(field)} aria-invalid={Boolean(providerProblems[field])} autoComplete="off" />
                </Field>
              ))}
            </div>
          </fieldset>
        )}
        {authUsesSecret(form.authType) && (
          <div className="field">
            <span className="label-text">{s.authValue} <Badge tone={secretSet ? 'success' : 'neutral'}>{secretSet ? s.secretSet : s.secretNotSet}</Badge></span>
            {endpoint && (
              <div className="badge-row" role="radiogroup" aria-label={s.authValue}>
                {['keep', 'replace', 'clear'].map((mode) => (
                  <label key={mode} className="checkbox-inline"><input type="radio" name="secret-mode" checked={form.secretMode === mode} onChange={() => setForm((f) => ({ ...f, secretMode: mode }))} /> {mode === 'keep' ? s.keepSecret : mode === 'replace' ? s.replaceSecret : s.clearSecret}</label>
                ))}
              </div>
            )}
            {(!endpoint || form.secretMode === 'replace') && (form.apiType === 'VertexAI' && form.authType === 'ApiKey'
              ? <textarea rows={4} autoComplete="off" spellCheck={false} aria-label={s.authValue} value={form.authValue} onChange={set('authValue')} />
              : <input type="password" autoComplete="new-password" aria-label={s.authValue} value={form.authValue} onChange={set('authValue')} />)}
            {s.authValueHints[form.apiType + ':' + form.authType] && <span className="muted small">{s.authValueHints[form.apiType + ':' + form.authType]}</span>}
          </div>
        )}
        <fieldset className="field">
          <legend className="label-text">{s.extraHeaders}</legend>
          {form.extraHeaders.map((h, i) => (
            <div key={i} className="ai-form-grid">
              <input aria-label={s.headerName} placeholder={s.headerName} value={h.name} onChange={(e) => setForm((f) => ({ ...f, extraHeaders: f.extraHeaders.map((x, j) => (j === i ? { ...x, name: e.target.value } : x)) }))} />
              <input type="password" aria-label={s.headerValue} placeholder={h.hasValue ? s.secretSet : s.headerValue} value={h.value} onChange={(e) => setForm((f) => ({ ...f, extraHeaders: f.extraHeaders.map((x, j) => (j === i ? { ...x, value: e.target.value } : x)) }))} />
              <button type="button" className="button button-secondary button-sm" onClick={() => setForm((f) => ({ ...f, extraHeaders: f.extraHeaders.filter((_, j) => j !== i) }))}>{s.removeHeader}</button>
            </div>
          ))}
          <button type="button" className="button button-secondary button-sm" onClick={() => setForm((f) => ({ ...f, extraHeaders: f.extraHeaders.concat([{ name: '', value: '', hasValue: false }]) }))}>{s.addHeader}</button>
        </fieldset>
        <h3>{s.parameters}</h3>
        <div className="ai-form-grid">
          <Field id="ep-temp" label={s.temperature}><input id="ep-temp" type="number" min="0" max="2" step="0.1" value={form.temperature} onChange={set('temperature')} /></Field>
          <Field id="ep-topp" label={s.topP}><input id="ep-topp" type="number" min="0" max="1" step="0.05" value={form.topP} onChange={set('topP')} /></Field>
          <Field id="ep-max" label={s.maxTokens}><input id="ep-max" type="number" min="1" value={form.maxTokens} onChange={set('maxTokens')} /></Field>
          <Field id="ep-ctx" label={s.contextWindow}><input id="ep-ctx" type="number" min="512" value={form.contextWindow} onChange={set('contextWindow')} /></Field>
          <Field id="ep-timeout" label={s.timeoutMs}><input id="ep-timeout" type="number" min="1000" max="600000" value={form.timeoutMs} onChange={set('timeoutMs')} /></Field>
          <Field id="ep-ttft" label={s.firstResponseTimeoutMs}><input id="ep-ttft" type="number" min="500" value={form.firstResponseTimeoutMs} onChange={set('firstResponseTimeoutMs')} /></Field>
          {form.kind === 'Completion' && (
            <Field id="ep-tools" label={s.toolCalling} hint={s.toolCallingHints[form.toolCalling]}>
              <select id="ep-tools" value={form.toolCalling} onChange={set('toolCalling')}>{TOOL_CALLING_MODES.map((m) => <option key={m} value={m}>{s.toolCallingModes[m]}</option>)}</select>
            </Field>
          )}
          {form.kind === 'Completion' && (
            <Field id="ep-effort" label={s.reasoningEffort} hint={s.reasoningEffortHint}>
              <select id="ep-effort" value={form.reasoningEffort} onChange={set('reasoningEffort')}>
                <option value="">{s.reasoningEffortNone}</option>
                {REASONING_EFFORTS.map((x) => <option key={x} value={x}>{x}</option>)}
              </select>
            </Field>
          )}
          {form.kind === 'Embedding' && <Field id="ep-dims" label={s.dimensions}><input id="ep-dims" type="number" min="1" max="8192" value={form.dimensions} onChange={set('dimensions')} /></Field>}
        </div>
        <label className="checkbox-inline"><input type="checkbox" checked={form.streaming} onChange={set('streaming')} /> {s.streaming}</label>
        <h3>{s.limits}</h3>
        <div className="ai-form-grid">
          <Field id="ep-conc" label={s.maxConcurrentRequests}><input id="ep-conc" type="number" min="1" max="256" value={form.maxConcurrentRequests} onChange={set('maxConcurrentRequests')} /></Field>
          <Field id="ep-queue" label={s.maxQueueDepth}><input id="ep-queue" type="number" min="0" max="10000" value={form.maxQueueDepth} onChange={set('maxQueueDepth')} /></Field>
          <Field id="ep-retries" label={s.maxRetries}><input id="ep-retries" type="number" min="0" max="5" value={form.maxRetries} onChange={set('maxRetries')} /></Field>
        </div>
        <h3>{s.healthCheck}</h3>
        <label className="checkbox-inline"><input type="checkbox" checked={form.healthEnabled} onChange={set('healthEnabled')} /> {s.healthEnabled}</label>
        <div className="ai-form-grid">
          <Field id="ep-health-mode" label={s.healthMode} hint={s.healthModeHints[healthModesFor(form.kind).includes(form.healthMode) ? form.healthMode : 'Connectivity']}>
            <select id="ep-health-mode" value={healthModesFor(form.kind).includes(form.healthMode) ? form.healthMode : 'Connectivity'} onChange={set('healthMode')}>
              {healthModesFor(form.kind).map((m) => <option key={m} value={m}>{s.healthModes[m]}</option>)}
            </select>
          </Field>
          <Field id="ep-interval" label={s.intervalMs}><input id="ep-interval" type="number" min="10000" max="3600000" value={form.intervalMs} onChange={set('intervalMs')} /></Field>
          <Field id="ep-health-timeout" label={s.healthTimeoutMs}><input id="ep-health-timeout" type="number" min="1000" max="120000" value={form.healthTimeoutMs} onChange={set('healthTimeoutMs')} /></Field>
          <Field id="ep-healthy" label={s.healthyThreshold}><input id="ep-healthy" type="number" min="1" max="20" value={form.healthyThreshold} onChange={set('healthyThreshold')} /></Field>
          <Field id="ep-unhealthy" label={s.unhealthyThreshold}><input id="ep-unhealthy" type="number" min="1" max="20" value={form.unhealthyThreshold} onChange={set('unhealthyThreshold')} /></Field>
        </div>
        <h3>{s.pricing}</h3>
        <p className="muted small">{s.pricingHint}</p>
        <div className="ai-form-grid">
          <Field id="ep-in" label={s.inputPrice}><input id="ep-in" type="number" min="0" step="0.01" value={form.inputPrice} onChange={set('inputPrice')} /></Field>
          <Field id="ep-out" label={s.outputPrice}><input id="ep-out" type="number" min="0" step="0.01" value={form.outputPrice} onChange={set('outputPrice')} /></Field>
          <Field id="ep-cache-read" label={s.cacheReadPrice}><input id="ep-cache-read" type="number" min="0" step="0.01" value={form.cacheReadPrice} onChange={set('cacheReadPrice')} /></Field>
          <Field id="ep-cache-write" label={s.cacheWritePrice}><input id="ep-cache-write" type="number" min="0" step="0.01" value={form.cacheWritePrice} onChange={set('cacheWritePrice')} /></Field>
        </div>
        <label className="checkbox-inline"><input type="checkbox" checked={form.enabled} onChange={set('enabled')} /> {s.enabled}</label>
      </form>
    </Modal>
  );
}

function TestDialog({ endpoint, onClose }) {
  const { client } = useAdminAuth();
  const [prompt, setPrompt] = useState('Say hello in five words.');
  const [maxTokens, setMaxTokens] = useState('1024');
  const [result, setResult] = useState(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const run = async () => {
    setBusy(true);
    setError('');
    try {
      const cap = Math.min(8192, Math.max(1, parseInt(maxTokens, 10) || 1024));
      setResult(await client.testEndpoint(endpoint.id, endpoint.kind === 'Completion' ? { prompt, maxTokens: cap } : {}));
    } catch (err) {
      setError(errorMessage(err, strings.common.errorGeneric));
    } finally {
      setBusy(false);
    }
  };
  return (
    <Modal open size="md" onClose={onClose} title={s.testTitle(endpoint.name)}
      footer={<button type="button" className="button button-primary" onClick={run} disabled={busy}>{s.testRun}</button>}>
      <ErrorBanner message={error} />
      {endpoint.kind === 'Completion' && <Field id="test-prompt" label={s.testPrompt}><textarea id="test-prompt" rows={2} value={prompt} onChange={(e) => setPrompt(e.target.value)} /></Field>}
      {endpoint.kind === 'Completion' && <Field id="test-max-tokens" label={s.testMaxTokens} hint={s.testMaxTokensHint}><input id="test-max-tokens" type="number" min={1} max={8192} value={maxTokens} onChange={(e) => setMaxTokens(e.target.value)} /></Field>}
      {result && (
        <div aria-live="polite">
          <p><Badge tone={result.success ? (result.hint ? 'warning' : 'success') : 'danger'}>{result.success ? s.testOk : s.testFailed}</Badge> {s.testLatency(result.latencyMs)}{result.timeToFirstTokenMs != null && ' · ' + s.testTtft(result.timeToFirstTokenMs)}{result.finishReason && ' · ' + s.testFinish(result.finishReason)}</p>
          {result.usage && <p className="muted small">{s.testTokens(result.usage)}</p>}
          {result.success && result.hint && s.testHints[result.hint] && <p className="ai-warning" role="status">{s.testHints[result.hint]}</p>}
          {result.output ? <pre className="ai-code-block">{result.output}</pre> : (result.success && endpoint.kind === 'Completion' && <p className="muted small">{s.testEmptyOutput}</p>)}
          {result.reasoning && <details><summary>{s.testReasoning}</summary><pre className="ai-code-block">{result.reasoning}</pre></details>}
          {result.embedding && <pre className="ai-code-block">{result.embedding.dimensions + ': ' + result.embedding.preview.join(', ')}</pre>}
          {result.error && <p className="ai-error">{result.error}</p>}
        </div>
      )}
    </Modal>
  );
}

function RevealDialog({ endpoint, onClose }) {
  const { client } = useAdminAuth();
  const [field, setField] = useState(endpoint.hasAuthValue ? 'authValue' : 'extraHeader:' + ((endpoint.extraHeaders || [])[0] || {}).name);
  const [reason, setReason] = useState('');
  const [value, setValue] = useState(null);
  const [error, setError] = useState('');
  const reveal = async () => {
    setError('');
    try {
      const header = field.startsWith('extraHeader:') ? field.slice(12) : undefined;
      const result = await client.revealEndpointSecret(endpoint.id, { field: header ? 'extraHeader' : 'authValue', headerName: header, reason });
      setValue(result.value);
    } catch (err) {
      setError(errorMessage(err, strings.common.errorGeneric));
    }
  };
  return (
    <Modal open size="sm" onClose={onClose} title={s.revealTitle}
      footer={<button type="button" className="button button-primary" onClick={reveal} disabled={reason.trim().length < 10}>{s.revealSubmit}</button>}>
      <p className="muted small">{s.revealBody}</p>
      <ErrorBanner message={error} />
      <Field id="rv-field" label={s.revealField}>
        <select id="rv-field" value={field} onChange={(e) => setField(e.target.value)}>
          {endpoint.hasAuthValue && <option value="authValue">{s.revealAuth}</option>}
          {(endpoint.extraHeaders || []).filter((h) => h.hasValue).map((h) => <option key={h.name} value={'extraHeader:' + h.name}>{s.revealHeader(h.name)}</option>)}
        </select>
      </Field>
      <Field id="rv-reason" label={s.revealReason}><input id="rv-reason" value={reason} onChange={(e) => setReason(e.target.value)} maxLength={500} /></Field>
      {value !== null && <Field id="rv-value" label={s.revealed}><input id="rv-value" readOnly value={value} onFocus={(e) => e.target.select()} /></Field>}
    </Modal>
  );
}

/** Admin, AI, Model endpoints (AI contract section 14.4). */
function AiEndpointsView() {
  const { client } = useAdminAuth();
  const toast = useToast();
  const [search, setSearch] = useState('');
  const [kind, setKind] = useState('');
  const [health, setHealth] = useState('');
  const [dialog, setDialog] = useState(null);
  const debounced = useDebounce(search, 300);
  const fetcher = useCallback((query, signal) => client.adminEnumerateEndpoints({ ...query, search: debounced || undefined, ordering: 'NameAscending', filters: { kind: kind || undefined, healthStatus: health || undefined } }, signal), [client, debounced, kind, health]);
  const list = usePagedList(fetcher);
  const act = async (fn, message) => {
    try {
      const result = await fn();
      toast.success(typeof message === 'function' ? message(result) : message);
      list.reload();
    } catch (err) {
      toast.error(err && err.code === 'EndpointInUse' ? s.inUse : errorMessage(err, strings.common.errorGeneric));
    }
  };
  const columns = [
    { key: 'name', label: s.columns.name, render: (row) => <strong>{row.name}</strong> },
    { key: 'kind', label: s.columns.kind, render: (row) => s.kinds[row.kind] || row.kind },
    { key: 'apiType', defaultHidden: true, label: s.columns.apiType, render: (row) => row.apiType },
    { key: 'model', label: s.columns.model, render: (row) => <code className="mono">{row.model}</code> },
    { key: 'toolCalling', label: s.columns.toolCalling, render: (row) => (row.kind === 'Completion' ? s.toolCallingModes[(row.parameters && row.parameters.toolCalling) || 'Native'] || row.parameters.toolCalling : '-') },
    { key: 'health', label: s.columns.health, render: (row) => <span className="ai-health" data-status={row.health.status}>{s.health[row.health.status] || row.health.status}</span> },
    { key: 'uptime', label: s.columns.uptime, className: 'num', render: (row) => (row.health.uptimePercentage == null ? '-' : row.health.uptimePercentage + '%') },
    { key: 'latency', label: s.columns.latency, className: 'num', render: (row) => (row.health.lastLatencyMs == null ? '-' : row.health.lastLatencyMs + ' ms') },
    { key: 'auth', defaultHidden: true, label: s.columns.auth, render: (row) => s.authTypes[row.authType] || row.authType },
    { key: 'enabled', label: s.columns.enabled, render: (row) => <Badge tone={row.enabled ? 'success' : 'neutral'}>{row.enabled ? s.enabledBadge : s.disabledBadge}</Badge> },
    {
      key: 'actions', className: 'cell-actions', label: <span className="visually-hidden">{s.columns.actions}</span>,
      render: (row) => <ActionMenu label={strings.admin.common.rowActions(row.name)} items={[
        { label: strings.admin.common.edit, icon: 'edit', onSelect: () => setDialog({ kind: 'edit', endpoint: row }) },
        { label: s.test, icon: 'play', onSelect: () => setDialog({ kind: 'test', endpoint: row }) },
        { label: s.checkHealth, icon: 'activity', onSelect: () => act(() => client.checkEndpointHealth(row.id), (r) => s.checked(r && r.success)) },
        { label: s.duplicate, icon: 'copy', onSelect: () => act(() => client.duplicateEndpoint(row.id, {}), s.duplicated) },
        { label: row.enabled ? s.disable : s.enable, icon: 'power', onSelect: () => act(() => client.adminUpdateEndpoint(row.id, { enabled: !row.enabled }), s.updated) },
        ...(row.hasAuthValue || (row.extraHeaders || []).some((h) => h.hasValue) ? [{ label: s.reveal, icon: 'eye', onSelect: () => setDialog({ kind: 'reveal', endpoint: row }) }] : []),
        { label: strings.admin.common.viewJson, icon: 'code', onSelect: () => setDialog({ kind: 'json', endpoint: row }) },
        { label: strings.common.delete, icon: 'trash', danger: true, onSelect: () => setDialog({ kind: 'delete', endpoint: row }) }
      ]} />
    }
  ];
  return (
    <div className="admin-page">
      <PageHeader title={s.title} summary={s.summary} actions={(
        <>
          <button type="button" className="button button-secondary button-sm" onClick={() => act(() => client.reencryptEndpointSecrets(), (r) => s.reencrypted(r ? r.endpoints : 0))}>{s.reencrypt}</button>
          <button type="button" className="button button-primary button-sm" onClick={() => setDialog({ kind: 'create' })}>{s.add}</button>
        </>
      )} />
      <section className="panel ai-form-grid">
        <input type="search" aria-label={s.search} placeholder={s.search} value={search} onChange={(e) => setSearch(e.target.value)} />
        <select aria-label={s.columns.kind} value={kind} onChange={(e) => setKind(e.target.value)}><option value="">{s.columns.kind}</option>{Object.entries(s.kinds).map(([k, v]) => <option key={k} value={k}>{v}</option>)}</select>
        <select aria-label={s.columns.health} value={health} onChange={(e) => setHealth(e.target.value)}><option value="">{s.columns.health}</option>{Object.entries(s.health).map(([k, v]) => <option key={k} value={k}>{v}</option>)}</select>
      </section>
      <DataTable caption={s.title} columns={columns} rows={list.items} rowKey={(row) => row.id} loading={list.loading} error={list.error ? errorMessage(list.error, '') : null} onRetry={list.reload} emptyMessage={s.empty} onRowClick={(row) => setDialog({ kind: 'edit', endpoint: row })} />
      {list.token && <button type="button" className="button button-secondary button-sm" onClick={list.more}>{strings.ai.loadMore}</button>}
      {dialog && (dialog.kind === 'create' || dialog.kind === 'edit') && <EndpointDialog endpoint={dialog.endpoint || null} onClose={() => setDialog(null)} onSaved={(m) => { toast.success(m); setDialog(null); list.reload(); }} />}
      {dialog && dialog.kind === 'test' && <TestDialog endpoint={dialog.endpoint} onClose={() => setDialog(null)} />}
      {dialog && dialog.kind === 'reveal' && <RevealDialog endpoint={dialog.endpoint} onClose={() => setDialog(null)} />}
      <JsonViewerModal open={Boolean(dialog && dialog.kind === 'json')} onClose={() => setDialog(null)} subtitle={dialog && dialog.endpoint && dialog.endpoint.id} value={dialog && dialog.endpoint} />
      <ConfirmModal open={Boolean(dialog && dialog.kind === 'delete')} title={dialog && dialog.endpoint ? s.deleteTitle(dialog.endpoint.name) : ''} message={s.deleteBody} confirmLabel={strings.common.delete}
        onCancel={() => setDialog(null)} onConfirm={() => { const e = dialog.endpoint; setDialog(null); act(() => client.adminDeleteEndpoint(e.id), s.deleted); }} />
    </div>
  );
}

export default AiEndpointsView;
