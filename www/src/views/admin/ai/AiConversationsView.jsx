import { useCallback, useEffect, useState } from 'react';
import strings from '../../../strings.js';
import { useAdminAuth } from '../../../context/AdminAuthContext.jsx';
import useDebounce from '../../../hooks/useDebounce.js';
import { errorMessage } from '../../../utils/api.js';
import { formatDateTime } from '../../../utils/format.js';
import PageHeader from '../../../components/PageHeader.jsx';
import DataTable from '../../../components/DataTable.jsx';
import Modal from '../../../components/Modal.jsx';
import { Badge } from '../../../components/Badges.jsx';
import { ErrorBanner, Loading } from '../../../components/States.jsx';
import { usePagedList } from './aiAdminUtils.js';

const s = strings.ai.admin.conversations;

function ownerName(owner) {
  if (!owner) return '-';
  if (owner.type === 'Visitor') return s.visitor + ' ' + (owner.visitorId || '');
  return (owner.member && owner.member.displayName) || owner.userId || '-';
}

function when(value) {
  return value ? formatDateTime(value) : '-';
}

function prettyArguments(json) {
  if (!json) return '';
  try {
    return JSON.stringify(JSON.parse(json), null, 2);
  } catch {
    return String(json);
  }
}

function toolTone(status) {
  if (status === 'Succeeded') return 'success';
  if (status === 'Running') return 'neutral';
  return status === 'Rejected' ? 'warning' : 'danger';
}

/**
 * Tool calls of a reply for administrators: tool, round, status, arguments, result count, duration,
 * and the stored result summary (or the truncated output in the test console) with any error.
 * @param {{ calls: { id?: string, toolName: string, label?: string, round?: number, status: string, argumentsJson?: string,
 *   resultCount?: number, durationMs?: number, resultSummary?: string, resultCharacters?: number, error?: string|null }[] }} props
 */
export function ToolCallTable({ calls }) {
  if (!calls || calls.length === 0) return null;
  return (
    <table className="ai-tool-debug" data-testid="ai-tool-calls">
      <caption className="visually-hidden">{s.toolCalls}</caption>
      <thead>
        <tr><th>{s.toolColumns.tool}</th><th>{s.toolColumns.status}</th><th>{s.toolColumns.arguments}</th><th>{s.toolColumns.results}</th><th>{s.toolColumns.ms}</th><th>{s.toolColumns.summary}</th></tr>
      </thead>
      <tbody>
        {calls.map((c, i) => (
          <tr key={c.id || i}>
            <td><code className="mono">{c.toolName}</code>{c.round != null && <div className="muted small">{s.toolRound(c.round)}</div>}</td>
            <td><Badge tone={toolTone(c.status)}>{s.toolStatuses[c.status] || c.status}</Badge></td>
            <td>{c.argumentsJson ? <pre className="mono">{prettyArguments(c.argumentsJson)}</pre> : '-'}</td>
            <td className="num">{c.resultCount ?? '-'}</td>
            <td className="num">{c.durationMs ?? '-'}</td>
            <td>
              {c.resultSummary ? <pre>{c.resultSummary}</pre> : '-'}
              {c.resultCharacters != null && <div className="muted small">{s.toolCharacters(c.resultCharacters)}</div>}
              {c.error && <div className="ai-error small">{c.error}</div>}
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

/** One message with its full telemetry: model, tokens, timing, cost, moderation, stages, and retrieved items. */
export function MessageTelemetry({ message }) {
  const t = message.telemetry;
  return (
    <div className="ai-telemetry">
      <p>
        {message.model && <>{message.model.endpointName || message.model.endpointId} <code className="mono">{message.model.model}</code>{message.model.usedFallback && <> <Badge tone="warning">{s.fallback}</Badge></>} · </>}
        {message.usage && s.tokens(message.usage.promptTokens, message.usage.completionTokens) + ' · '}
        {s.timing(message.timeToFirstTokenMs, message.latencyMs)}
        {t && ' · ' + s.cost(Number(t.costUsd || 0).toFixed(6))}
        {message.finishReason && ' · ' + message.finishReason}
        {message.errorCode && <> · <span className="ai-error">{message.errorCode}</span></>}
      </p>
      {t && t.moderationVerdict && <p>{s.moderation}: {t.moderationVerdict}{t.moderationCategory ? ' (' + t.moderationCategory + ')' : ''}</p>}
      {t && t.retrievalQuery && <p>{s.retrievalQuery}: {t.retrievalQuery}</p>}
      {t && t.traceId && <p>{s.trace} <code className="mono">{t.traceId}</code></p>}
      {message.toolCalls && message.toolCalls.objects && message.toolCalls.objects.length > 0 && (
        <><h4>{s.toolCalls}</h4><ToolCallTable calls={message.toolCalls.objects} /></>
      )}
      {t && t.stages && t.stages.length > 0 && (
        <table>
          <caption className="visually-hidden">{s.stages}</caption>
          <thead><tr><th>{s.stages}</th><th>{s.stageColumns.model}</th><th>{s.stageColumns.ms}</th><th>{s.stageColumns.tokens}</th><th>{s.stageColumns.ok}</th></tr></thead>
          <tbody>
            {t.stages.map((st, i) => (
              <tr key={i}><td>{st.stage}</td><td>{st.model || '-'}</td><td>{st.durationMs}</td><td>{st.promptTokens == null ? '-' : st.promptTokens + ' / ' + (st.completionTokens ?? 0)}</td><td>{st.success ? strings.common.yes : (st.detail || strings.common.no)}</td></tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}

function ConversationDialog({ conversation, onClose }) {
  const { client } = useAdminAuth();
  const [state, setState] = useState({ loading: true, error: null, detail: null, messages: [] });
  useEffect(() => {
    const controller = new AbortController();
    Promise.all([
      client.adminGetConversation(conversation.id, controller.signal),
      client.adminEnumerateConversationMessages(conversation.id, { maxResults: 200 }, controller.signal)
    ]).then(
      ([detail, page]) => setState({ loading: false, error: null, detail, messages: (page && page.objects) || [] }),
      (error) => { if (error.name !== 'AbortError') setState({ loading: false, error, detail: null, messages: [] }); }
    );
    return () => controller.abort();
  }, [client, conversation.id]);
  return (
    <Modal open size="lg" variant="drawer" onClose={onClose} title={conversation.title || strings.ai.untitled} subtitle={conversation.id}>
      <p className="muted small">{s.auditedNote}</p>
      {state.loading && <Loading />}
      <ErrorBanner message={state.error ? errorMessage(state.error, strings.common.errorGeneric) : ''} />
      {state.detail && <p>{state.detail.assistant.title} · {ownerName(state.detail.owner)} · {state.detail.surface}</p>}
      <ol className="ai-transcript">
        {state.messages.map((m) => (
          <li key={m.id} className={'ai-message ' + (m.role === 'User' ? 'ai-message-user' : 'ai-message-reply')}>
            <p className="ai-user-text">{m.content || (m.status !== 'Completed' ? '(' + m.status + ')' : '')}</p>
            {m.feedback && <p className="small">{m.feedback.rating === 'Up' ? strings.ai.feedbackUp : strings.ai.feedbackDown}{m.feedback.note ? ': ' + m.feedback.note : ''}</p>}
            {m.role !== 'User' && <MessageTelemetry message={m} />}
            <span className="muted small">{when(m.createdUtc)}</span>
          </li>
        ))}
      </ol>
    </Modal>
  );
}

/** Admin, AI, Conversations: browse every conversation; opening one is audited (AI contract section 14.4). */
function AiConversationsView() {
  const { client } = useAdminAuth();
  const [search, setSearch] = useState('');
  const [assistantId, setAssistantId] = useState('');
  const [ownerType, setOwnerType] = useState('');
  const [userId, setUserId] = useState('');
  const [assistants, setAssistants] = useState([]);
  const [open, setOpen] = useState(null);
  const debounced = useDebounce(search, 300);
  const debouncedUser = useDebounce(userId, 300);
  useEffect(() => {
    const controller = new AbortController();
    client.adminEnumerateAssistants({ maxResults: 100, ordering: 'NameAscending' }, controller.signal).then((page) => setAssistants((page && page.objects) || []), () => setAssistants([]));
    return () => controller.abort();
  }, [client]);
  const fetcher = useCallback((query, signal) => client.adminEnumerateConversations({
    ...query,
    search: debounced || undefined,
    filters: { assistantId: assistantId || undefined, ownerType: ownerType || undefined, userId: debouncedUser.trim() || undefined }
  }, signal), [client, debounced, assistantId, ownerType, debouncedUser]);
  const list = usePagedList(fetcher);
  const columns = [
    { key: 'title', label: s.columns.title, render: (row) => <><strong>{row.title || strings.ai.untitled}</strong>{row.lastMessagePreview && <div className="muted small">{row.lastMessagePreview}</div>}</> },
    { key: 'assistant', label: s.columns.assistant, render: (row) => row.assistant.title },
    { key: 'owner', label: s.columns.owner, render: (row) => ownerName(row.owner) },
    { key: 'messages', label: s.columns.messages, className: 'num', render: (row) => row.messageCount },
    { key: 'surface', label: s.columns.surface, render: (row) => row.surface },
    { key: 'last', label: s.columns.last, render: (row) => when(row.lastMessageUtc) }
  ];
  return (
    <div className="admin-page">
      <PageHeader title={s.title} summary={s.summary} />
      <section className="panel ai-form-grid">
        <input type="search" aria-label={s.search} placeholder={s.search} value={search} onChange={(e) => setSearch(e.target.value)} />
        <select aria-label={s.assistant} value={assistantId} onChange={(e) => setAssistantId(e.target.value)}>
          <option value="">{s.allAssistants}</option>
          {assistants.map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}
        </select>
        <select aria-label={s.owner} value={ownerType} onChange={(e) => setOwnerType(e.target.value)}>
          {Object.entries(s.owners).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
        </select>
        <input aria-label={s.member} placeholder={s.member} value={userId} onChange={(e) => setUserId(e.target.value)} />
      </section>
      <DataTable caption={s.title} columns={columns} rows={list.items} rowKey={(row) => row.id} loading={list.loading} error={list.error ? errorMessage(list.error, '') : null} onRetry={list.reload} emptyMessage={s.empty} onRowClick={(row) => setOpen(row)} />
      {list.token && <button type="button" className="button button-secondary button-sm" onClick={list.more}>{strings.ai.loadMore}</button>}
      {open && <ConversationDialog conversation={open} onClose={() => setOpen(null)} />}
    </div>
  );
}

export default AiConversationsView;
