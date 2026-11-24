import { useCallback, useEffect, useRef, useState } from 'react';
import strings from '../../../strings.js';
import { useAdminAuth } from '../../../context/AdminAuthContext.jsx';
import { useProperty } from '../../../context/PropertyContext.jsx';
import { collectEnumeration, audienceChoices, composerAudiences } from '@trelliskit/shared';
import { useToast } from '../../../context/ToastContext.jsx';
import useDebounce from '../../../hooks/useDebounce.js';
import { errorMessage, getFieldErrors } from '../../../utils/api.js';
import PageHeader from '../../../components/PageHeader.jsx';
import DataTable from '../../../components/DataTable.jsx';
import ActionMenu from '../../../components/ActionMenu.jsx';
import ConfirmModal from '../../../components/ConfirmModal.jsx';
import JsonViewerModal from '../../../components/JsonViewerModal.jsx';
import Modal from '../../../components/Modal.jsx';
import { Badge } from '../../../components/Badges.jsx';
import { ErrorBanner } from '../../../components/States.jsx';
import EmbedOriginsEditor from './EmbedOriginsEditor.jsx';
import AssistantToolsPanel from './AssistantToolsPanel.jsx';
import { ToolCallTable } from './AiConversationsView.jsx';
import { PURPOSES, assistantBody, assistantForm, toolLimitProblems, usePagedList, usd, widgetSnippet } from './aiAdminUtils.js';

const s = strings.ai.admin.assistants;
const TABS = ['general', 'prompt', 'models', 'knowledge', 'tools', 'limits', 'embed', 'test'];

function Field({ id, label, hint, error, children }) {
  return (
    <div className="field">
      <label className="label-text" htmlFor={id}>{label}</label>
      {children}
      {hint && <span className="muted small">{hint}</span>}
      {error && <span className="field-error small ai-error" role="alert">{error}</span>}
    </div>
  );
}

function useEndpoints(client) {
  const [endpoints, setEndpoints] = useState([]);
  useEffect(() => {
    const controller = new AbortController();
    client.adminEnumerateEndpoints({ maxResults: 100, ordering: 'NameAscending' }, controller.signal)
      .then((page) => setEndpoints((page && page.objects) || []), () => setEndpoints([]));
    return () => controller.abort();
  }, [client]);
  return endpoints;
}

function KnowledgePanel({ assistant }) {
  const { client } = useAdminAuth();
  const toast = useToast();
  const [status, setStatus] = useState(null);
  const [query, setQuery] = useState('');
  const [preview, setPreview] = useState(null);
  const [error, setError] = useState('');
  const load = useCallback(() => {
    client.getAssistantKnowledgeStatus(assistant.id).then(setStatus, () => setStatus(null));
  }, [client, assistant.id]);
  useEffect(() => { load(); }, [load]);
  const reindex = async () => {
    try {
      await client.reindexAssistantKnowledge(assistant.id);
      toast.success(s.reindexStarted);
      load();
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
    }
  };
  const runPreview = async (e) => {
    e.preventDefault();
    setError('');
    try {
      setPreview(await collectEnumeration((page, signal) => client.previewAssistantKnowledge(assistant.id, page, signal), { filters: { query: query.trim() } }));
    } catch (err) {
      setError(errorMessage(err, strings.common.errorGeneric));
    }
  };
  return (
    <section className="detail-block">
      {status && (
        <p>
          {s.indexStatus}: <Badge tone={status.status === 'Failed' ? 'danger' : status.status === 'Idle' ? 'success' : 'neutral'}>{s.indexStates[status.status] || status.status}</Badge>{' '}
          <span className="muted small">{s.indexCounts(status.itemCount || 0, status.chunkCount || 0)}</span>
          {status.lastError && <span className="ai-error small"> {status.lastError}</span>}
        </p>
      )}
      <button type="button" className="button button-secondary button-sm" onClick={reindex}>{s.reindex}</button>
      <form className="ai-form-grid" onSubmit={runPreview}>
        <input aria-label={s.previewQuery} placeholder={s.previewQuery} value={query} onChange={(e) => setQuery(e.target.value)} />
        <button type="submit" className="button button-secondary button-sm" disabled={!query.trim()}>{s.preview}</button>
      </form>
      <ErrorBanner message={error} />
      {preview && (preview.objects.length === 0 ? <p className="muted">{s.previewEmpty}</p> : (
        <ol className="ai-telemetry">
          {preview.objects.map((item) => <li key={item.targetId}><strong>{item.title}</strong> <span className="muted">({item.score})</span><br />{item.excerpt}</li>)}
        </ol>
      ))}
    </section>
  );
}

function TestConsole({ assistant, form }) {
  const { client } = useAdminAuth();
  const [input, setInput] = useState('');
  const [turns, setTurns] = useState([]);
  const [debug, setDebug] = useState(null);
  const [telemetry, setTelemetry] = useState(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const abort = useRef(null);
  useEffect(() => () => abort.current && abort.current.abort(), []);
  const send = async (e) => {
    e.preventDefault();
    const content = input.trim();
    if (!content || busy) return;
    const history = turns.concat([{ role: 'user', content }]);
    setTurns(history.concat([{ role: 'assistant', content: '' }]));
    setInput('');
    setBusy(true);
    setError('');
    setDebug(null);
    setTelemetry(null);
    const controller = new AbortController();
    abort.current = controller;
    let text = '';
    try {
      const events = client.testAssistant(assistant.id, { messages: history, useDraft: assistantBody(form) }, { signal: controller.signal });
      for await (const item of events) {
        if (item.event === 'reply.delta') {
          text += (item.data && item.data.text) || '';
          setTurns(history.concat([{ role: 'assistant', content: text }]));
        } else if (item.event === 'debug') {
          const data = item.data || {};
          if (data.toolCall) {
            const call = data.toolCall;
            setDebug((d) => {
              const calls = ((d && d.toolCalls) || []).filter((c) => c.toolCallId !== call.toolCallId);
              return { ...(d || {}), toolCalls: calls.concat([call]) };
            });
          } else {
            setDebug((d) => ({ ...(d || {}), ...data }));
          }
        } else if (item.event === 'reply.completed' || item.event === 'reply.blocked') {
          const reply = item.data && item.data.reply;
          if (reply) {
            setTurns(history.concat([{ role: 'assistant', content: reply.content || text }]));
            setTelemetry({ model: reply.model, usage: reply.usage, latencyMs: reply.latencyMs, timeToFirstTokenMs: reply.timeToFirstTokenMs, finishReason: reply.finishReason, cost: reply.telemetry && reply.telemetry.costUsd });
          }
        } else if (item.event === 'error') {
          setError((item.data && (item.data.message || item.data.code)) || strings.common.errorGeneric);
        }
      }
    } catch (err) {
      if (!controller.signal.aborted) setError(errorMessage(err, strings.common.errorGeneric));
    } finally {
      setBusy(false);
    }
  };
  return (
    <section className="detail-block">
      <p className="muted small">{s.testHint}</p>
      <ol className="ai-transcript" aria-live="polite">
        {turns.map((t, i) => <li key={i} className={'ai-message ' + (t.role === 'user' ? 'ai-message-user' : 'ai-message-reply')}><p className="ai-user-text">{t.content}</p></li>)}
      </ol>
      <ErrorBanner message={error} />
      <form className="ai-composer" onSubmit={send}>
        <textarea aria-label={strings.ai.composerLabel} rows={2} value={input} onChange={(e) => setInput(e.target.value)} />
        <div className="ai-composer-row">
          <button type="button" className="button button-secondary button-sm" onClick={() => { setTurns([]); setDebug(null); setTelemetry(null); }} disabled={busy}>{strings.ai.newConversation}</button>
          {busy ? <button type="button" className="button button-secondary button-sm" onClick={() => abort.current && abort.current.abort()}>{strings.ai.stop}</button>
            : <button type="submit" className="button button-primary button-sm" disabled={!input.trim()}>{strings.ai.send}</button>}
        </div>
      </form>
      {debug && (
        <details className="ai-telemetry" open>
          <summary>{s.testDebug}</summary>
          {debug.systemPrompt && <><h4>{s.testSystemPrompt}</h4><pre className="ai-code-block">{debug.systemPrompt}</pre></>}
          {debug.knowledge && debug.knowledge.length > 0 && <><h4>{s.testKnowledge}</h4><ol>{debug.knowledge.map((k) => <li key={k.targetId}>{k.title} ({k.score})</li>)}</ol></>}
          {debug.toolCalls && debug.toolCalls.length > 0 && <><h4>{s.testToolCalls}</h4><ToolCallTable calls={debug.toolCalls.map((c) => ({ ...c, id: c.toolCallId, resultSummary: c.output }))} /></>}
          {debug.error && <p className="ai-error">{debug.error}</p>}
        </details>
      )}
      {telemetry && (
        <div className="ai-telemetry">
          <h4>{s.testTelemetry}</h4>
          <p>
            {telemetry.model && telemetry.model.model}{telemetry.model && telemetry.model.usedFallback ? ' (fallback)' : ''}
            {telemetry.usage && ' · ' + strings.ai.admin.conversations.tokens(telemetry.usage.promptTokens, telemetry.usage.completionTokens)}
            {' · ' + strings.ai.admin.conversations.timing(telemetry.timeToFirstTokenMs, telemetry.latencyMs)}
            {telemetry.cost != null && ' · ' + usd(telemetry.cost)}
          </p>
        </div>
      )}
    </section>
  );
}

/** Create and edit an assistant, with tabs for each part of the configuration (AI contract section 14.4). */
export function AssistantDialog({ assistant, onClose, onSaved }) {
  const { client } = useAdminAuth();
  const endpoints = useEndpoints(client);
  const [tab, setTab] = useState('general');
  const { property } = useProperty();
  const audiencePicker = composerAudiences(property, { isSystemAdmin: true, feature: 'assistants' });
  const [form, setForm] = useState(() => assistantForm(assistant, audiencePicker.defaultAudience));
  const audiences = audienceChoices(audiencePicker.options, assistant && assistant.audience);
  const [error, setError] = useState('');
  const [fieldErrors, setFieldErrors] = useState({});
  const [busy, setBusy] = useState(false);
  const set = (key) => (e) => {
    const value = e.target.type === 'checkbox' ? e.target.checked : e.target.value;
    setForm((f) => ({ ...f, [key]: value }));
  };
  const setSource = (i, key, value) => setForm((f) => ({ ...f, sources: f.sources.map((x, j) => (j === i ? { ...x, [key]: value } : x)) }));
  const tabs = TABS.filter((t) => (t !== 'test' && t !== 'embed') || assistant);
  const submit = async (e) => {
    e.preventDefault();
    if (!form.name.trim() || !form.models.chat) {
      setError(strings.common.errorGeneric);
      return;
    }
    const limitProblems = toolLimitProblems(form);
    if (Object.keys(limitProblems).length) {
      setFieldErrors(Object.fromEntries(Object.keys(limitProblems).map((k) => [k, s.tools.range])));
      setError(s.tools.checkLimits);
      setTab('tools');
      return;
    }
    setBusy(true);
    setError('');
    setFieldErrors({});
    try {
      const body = assistantBody(form);
      await (assistant ? client.adminUpdateAssistant(assistant.id, body) : client.adminCreateAssistant(body));
      onSaved(assistant ? s.updated : s.created);
    } catch (err) {
      const fields = getFieldErrors(err);
      const texts = Object.fromEntries(Object.entries(fields).map(([k, v]) => [k, Array.isArray(v) ? v.join(' ') : String(v)]));
      setFieldErrors(texts);
      setError([errorMessage(err, strings.common.errorGeneric), ...Object.entries(texts).map(([k, v]) => k + ': ' + v)].join(' '));
      if (Object.keys(texts).some((k) => k.startsWith('tools.'))) setTab('tools');
      setBusy(false);
    }
  };
  const origin = typeof window !== 'undefined' ? window.location.origin : '';
  return (
    <Modal open size="lg" variant="drawer" onClose={busy ? () => {} : onClose} title={assistant ? s.editTitle(assistant.name) : s.createTitle}
      footer={(
        <>
          <button type="button" className="button button-secondary" onClick={onClose} disabled={busy}>{strings.common.cancel}</button>
          <button type="submit" form="assistant-form" className="button button-primary" disabled={busy}>{s.save}</button>
        </>
      )}
    >
      <div className="ai-tabs" role="tablist">
        {tabs.map((t) => <button key={t} type="button" role="tab" aria-selected={tab === t} className="button button-secondary button-sm" onClick={() => setTab(t)}>{s.tabs[t]}</button>)}
      </div>
      <form id="assistant-form" className="stack-form" onSubmit={submit} noValidate>
        <ErrorBanner message={error} />
        {tab === 'general' && (
          <>
            <div className="ai-form-grid">
              <Field id="as-name" label={s.name}><input id="as-name" value={form.name} onChange={set('name')} maxLength={100} required /></Field>
              <Field id="as-title" label={s.chatTitle}><input id="as-title" value={form.title} onChange={set('title')} maxLength={100} /></Field>
              <Field id="as-emoji" label={s.emoji}><input id="as-emoji" value={form.emoji} onChange={set('emoji')} maxLength={16} /></Field>
              <Field id="as-aud" label={s.audience}>
                <select id="as-aud" value={form.audience} onChange={set('audience')}>{audiences.map((k) => <option key={k} value={k}>{s.audiences[k] || k}</option>)}</select>
              </Field>
            </div>
            <Field id="as-desc" label={s.description}><input id="as-desc" value={form.description} onChange={set('description')} maxLength={1000} /></Field>
            <Field id="as-disc" label={s.disclaimer}><input id="as-disc" value={form.disclaimer} onChange={set('disclaimer')} maxLength={500} /></Field>
            <Field id="as-start" label={s.starterPrompts}><textarea id="as-start" rows={4} value={form.starterPrompts} onChange={set('starterPrompts')} /></Field>
            <label className="checkbox-inline"><input type="checkbox" checked={form.enabled} onChange={set('enabled')} /> {s.enabled}</label>
          </>
        )}
        {tab === 'prompt' && (
          <>
            <Field id="as-sys" label={s.systemPrompt} hint={s.promptTokens}><textarea id="as-sys" rows={14} value={form.systemPrompt} onChange={set('systemPrompt')} /></Field>
            <div className="ai-form-grid">
              <Field id="as-temp" label={strings.ai.admin.endpoints.temperature}><input id="as-temp" type="number" min="0" max="2" step="0.1" value={form.temperature} onChange={set('temperature')} /></Field>
              <Field id="as-topp" label={strings.ai.admin.endpoints.topP}><input id="as-topp" type="number" min="0" max="1" step="0.05" value={form.topP} onChange={set('topP')} /></Field>
            </div>
          </>
        )}
        {tab === 'models' && (
          <div className="ai-form-grid">
            {PURPOSES.map(([purpose, kind]) => (
              <Field key={purpose} id={'as-m-' + purpose} label={s.purposes[purpose]} hint={s.purposeHints[purpose]} error={fieldErrors['models.' + purpose]}>
                <select id={'as-m-' + purpose} value={form.models[purpose]} onChange={(e) => setForm((f) => ({ ...f, models: { ...f.models, [purpose]: e.target.value } }))}>
                  <option value="">{s.none}</option>
                  {endpoints.filter((x) => x.kind === kind).map((x) => <option key={x.id} value={x.id}>{x.name + ' (' + x.model + ')'}</option>)}
                </select>
              </Field>
            ))}
          </div>
        )}
        {tab === 'knowledge' && (
          <>
            <label className="checkbox-inline"><input type="checkbox" checked={form.knowledgeEnabled} onChange={set('knowledgeEnabled')} /> {s.knowledgeEnabled}</label>
            <div className="ai-form-grid">
              <Field id="as-mode" label={s.knowledgeMode}>
                <select id="as-mode" value={form.knowledgeMode} onChange={set('knowledgeMode')}>{Object.entries(s.modes).map(([k, v]) => <option key={k} value={k}>{v}</option>)}</select>
              </Field>
              <Field id="as-ctx" label={s.maxContextItems}><input id="as-ctx" type="number" min="1" max="20" value={form.maxContextItems} onChange={set('maxContextItems')} /></Field>
            </div>
            <label className="checkbox-inline"><input type="checkbox" checked={form.citations} onChange={set('citations')} /> {s.citations}</label>
            <fieldset className="field">
              <legend className="label-text">{s.sources}</legend>
              {form.sources.map((src, i) => (
                <div key={i} className="ai-form-grid">
                  <input aria-label={s.sourceLabels} placeholder={s.sourceLabels} value={src.labels} onChange={(e) => setSource(i, 'labels', e.target.value)} />
                  <select aria-label={s.sourceMatch} value={src.labelMatch} onChange={(e) => setSource(i, 'labelMatch', e.target.value)}><option value="All">All</option><option value="Any">Any</option></select>
                  <input aria-label={s.sourceMaxItems} type="number" min="1" max="2000" value={src.maxItems} onChange={(e) => setSource(i, 'maxItems', e.target.value)} />
                  <button type="button" className="button button-secondary button-sm" onClick={() => setForm((f) => ({ ...f, sources: f.sources.filter((_, j) => j !== i) }))}>{s.removeSource}</button>
                </div>
              ))}
              <button type="button" className="button button-secondary button-sm" onClick={() => setForm((f) => ({ ...f, sources: f.sources.concat([{ labels: '', labelMatch: 'All', maxItems: '200' }]) }))}>{s.addSource}</button>
            </fieldset>
            {assistant && <KnowledgePanel assistant={assistant} />}
          </>
        )}
        {tab === 'limits' && (
          <>
            <div className="ai-form-grid">
              <Field id="as-lm" label={s.messagesPerMemberPerDay}><input id="as-lm" type="number" min="0" value={form.messagesPerMemberPerDay} onChange={set('messagesPerMemberPerDay')} /></Field>
              <Field id="as-lv" label={s.messagesPerVisitorPerDay}><input id="as-lv" type="number" min="0" value={form.messagesPerVisitorPerDay} onChange={set('messagesPerVisitorPerDay')} /></Field>
              <Field id="as-lc" label={s.maxMessagesPerConversation}><input id="as-lc" type="number" min="2" value={form.maxMessagesPerConversation} onChange={set('maxMessagesPerConversation')} /></Field>
              <Field id="as-lt" label={s.maxTokensPerReply}><input id="as-lt" type="number" min="16" value={form.maxTokensPerReply} onChange={set('maxTokensPerReply')} /></Field>
              <Field id="as-lch" label={s.maxMessageCharacters}><input id="as-lch" type="number" min="100" value={form.maxMessageCharacters} onChange={set('maxMessageCharacters')} /></Field>
              <Field id="as-mod" label={s.moderationMode}>
                <select id="as-mod" value={form.moderationMode} onChange={set('moderationMode')}>{Object.entries(s.moderationModes).map(([k, v]) => <option key={k} value={k}>{v}</option>)}</select>
              </Field>
              <Field id="as-budget" label={s.budgetMonthly}><input id="as-budget" type="number" min="0" step="0.01" value={form.budgetMonthly} onChange={set('budgetMonthly')} /></Field>
            </div>
            <label className="checkbox-inline"><input type="checkbox" checked={form.budgetHardStop} onChange={set('budgetHardStop')} /> {s.budgetHardStop}</label>
            <label className="checkbox-inline"><input type="checkbox" checked={form.streaming} onChange={set('streaming')} /> {s.streaming}</label>
            <label className="checkbox-inline"><input type="checkbox" checked={form.showReasoning} onChange={set('showReasoning')} /> {s.showReasoning}</label>
          </>
        )}
        {tab === 'embed' && assistant && (
          <>
            <label className="checkbox-inline"><input type="checkbox" checked={form.embedEnabled} onChange={set('embedEnabled')} disabled={form.audience !== 'Public'} /> {s.embedEnabled}</label>
            <Field id="as-elabel" label={s.embedLabel}><input id="as-elabel" value={form.embedLabel} onChange={set('embedLabel')} maxLength={40} /></Field>
            <Field id="as-epos" label={s.embedPosition}>
              <select id="as-epos" value={form.embedPosition} onChange={set('embedPosition')}><option value="BottomRight">BottomRight</option><option value="BottomLeft">BottomLeft</option></select>
            </Field>
            <EmbedOriginsEditor value={form.embedOrigins} onChange={(text) => setForm((f) => ({ ...f, embedOrigins: text }))} />
            {form.embedEnabled && <Field id="as-snip" label={s.embedSnippet}><textarea id="as-snip" rows={3} readOnly value={widgetSnippet(origin, { id: assistant.id, embed: { launcherLabel: form.embedLabel, position: form.embedPosition } })} onFocus={(e) => e.target.select()} /></Field>}
          </>
        )}
      </form>
      {tab === 'tools' && <AssistantToolsPanel form={form} setForm={setForm} fieldErrors={fieldErrors} />}
      {tab === 'test' && assistant && <TestConsole assistant={assistant} form={form} />}
    </Modal>
  );
}

/** Admin, AI, Assistants (AI contract section 14.4). */
function AiAssistantsView() {
  const { client } = useAdminAuth();
  const toast = useToast();
  const [search, setSearch] = useState('');
  const [dialog, setDialog] = useState(null);
  const debounced = useDebounce(search, 300);
  const fetcher = useCallback((query, signal) => client.adminEnumerateAssistants({ ...query, search: debounced || undefined, ordering: 'NameAscending' }, signal), [client, debounced]);
  const list = usePagedList(fetcher);
  const act = async (fn, message) => {
    try {
      await fn();
      toast.success(message);
      list.reload();
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
    }
  };
  const columns = [
    { key: 'name', label: s.columns.name, render: (row) => <strong>{(row.avatar && row.avatar.emoji ? row.avatar.emoji + ' ' : '') + row.name}</strong> },
    { key: 'audience', label: s.columns.audience, render: (row) => s.audiences[row.audience] || row.audience },
    { key: 'knowledge', label: s.columns.knowledge, render: (row) => (row.knowledge && row.knowledge.enabled ? s.modes[row.knowledge.mode] : s.none) },
    { key: 'conversations', label: s.columns.conversations, className: 'num', render: (row) => (row.stats ? row.stats.conversations30d : 0) },
    { key: 'budget', label: s.columns.budget, className: 'num', render: (row) => (row.budget ? usd(row.budget.monthToDateUsd) + (row.budget.monthlyUsd != null ? ' / ' + usd(row.budget.monthlyUsd) : '') : '-') },
    { key: 'enabled', label: s.columns.enabled, render: (row) => <Badge tone={row.enabled ? 'success' : 'neutral'}>{row.enabled ? strings.common.yes : strings.common.no}</Badge> },
    {
      key: 'actions', className: 'cell-actions', label: <span className="visually-hidden">{s.columns.actions}</span>,
      render: (row) => <ActionMenu label={strings.admin.common.rowActions(row.name)} items={[
        { label: strings.admin.common.edit, icon: 'edit', onSelect: () => setDialog({ kind: 'edit', assistant: row }) },
        { label: s.duplicate, icon: 'copy', onSelect: () => act(() => client.duplicateAssistant(row.id), s.duplicated) },
        { label: strings.admin.common.viewJson, icon: 'code', onSelect: () => setDialog({ kind: 'json', assistant: row }) },
        { label: strings.common.delete, icon: 'trash', danger: true, onSelect: () => setDialog({ kind: 'delete', assistant: row }) }
      ]} />
    }
  ];
  return (
    <div className="admin-page">
      <PageHeader title={s.title} summary={s.summary} actions={<button type="button" className="button button-primary button-sm" onClick={() => setDialog({ kind: 'create' })}>{s.add}</button>} />
      <section className="panel">
        <input type="search" aria-label={s.search} placeholder={s.search} value={search} onChange={(e) => setSearch(e.target.value)} />
      </section>
      <DataTable caption={s.title} columns={columns} rows={list.items} rowKey={(row) => row.id} loading={list.loading} error={list.error ? errorMessage(list.error, '') : null} onRetry={list.reload} emptyMessage={s.empty} onRowClick={(row) => setDialog({ kind: 'edit', assistant: row })} />
      {list.token && <button type="button" className="button button-secondary button-sm" onClick={list.more}>{strings.ai.loadMore}</button>}
      {dialog && (dialog.kind === 'create' || dialog.kind === 'edit') && <AssistantDialog assistant={dialog.assistant || null} onClose={() => setDialog(null)} onSaved={(m) => { toast.success(m); setDialog(null); list.reload(); }} />}
      <JsonViewerModal open={Boolean(dialog && dialog.kind === 'json')} onClose={() => setDialog(null)} subtitle={dialog && dialog.assistant && dialog.assistant.id} value={dialog && dialog.assistant} />
      <ConfirmModal open={Boolean(dialog && dialog.kind === 'delete')} title={dialog && dialog.assistant ? s.deleteTitle(dialog.assistant.name) : ''} message={s.deleteBody} confirmLabel={strings.common.delete}
        onCancel={() => setDialog(null)} onConfirm={() => { const a = dialog.assistant; setDialog(null); act(() => client.adminDeleteAssistant(a.id), s.deleted); }} />
    </div>
  );
}

export default AiAssistantsView;
