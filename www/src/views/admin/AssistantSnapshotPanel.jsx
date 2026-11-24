import { useState } from 'react';
import { Link } from 'react-router-dom';
import { normalizeEnumerationResult } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { errorMessage } from '../../utils/api.js';
import { authorName } from '../../utils/engagement.js';
import { formatDateTime } from '../../utils/format.js';
import { Badge } from '../../components/Badges.jsx';
import { ErrorBanner } from '../../components/States.jsx';
import AssistantMarkdown from '../ai/AssistantMarkdown.jsx';

const s = strings.admin.moderation.snapshot;

/** Page size for loading more snapshot messages. */
export const SNAPSHOT_PAGE_SIZE = 50;

/**
 * The moderation snapshot of a reported assistant reply (AI contract section 12.3; SystemAdmin):
 * assistant, conversation owner, why it was kept, the model, citations, and the retained messages
 * oldest first with the reported one marked. The first page comes with the queue detail; more pages
 * come from `POST /admin/moderation/queue/AssistantMessage/{id}/snapshot/enumerate`.
 * @param {{ messageId: string, snapshot: object }} props
 */
function AssistantSnapshotPanel({ messageId, snapshot }) {
  const { api } = useAdminAuth();
  const first = normalizeEnumerationResult(snapshot.messages || {});
  const [messages, setMessages] = useState(first.objects);
  const [token, setToken] = useState(first.continuationToken || null);
  const [done, setDone] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);
  const total = first.totalRecords || messages.length;

  const loadMore = async () => {
    setLoading(true);
    setError(null);
    try {
      // The detail carries a bounded preview; the enumeration pages from the start with continuation tokens.
      const query = token ? { maxResults: SNAPSHOT_PAGE_SIZE, continuationToken: token } : { maxResults: SNAPSHOT_PAGE_SIZE };
      const page = normalizeEnumerationResult(await api.enumerateAssistantSnapshot(messageId, query));
      setMessages((current) => {
        const seen = new Set(current.map((m) => m.id));
        return [...current, ...page.objects.filter((m) => !seen.has(m.id))];
      });
      setToken(page.continuationToken || null);
      setDone(!page.continuationToken);
    } catch (err) {
      setError(err);
    } finally {
      setLoading(false);
    }
  };

  const assistant = snapshot.assistant || {};
  const model = snapshot.model || {};
  const citations = snapshot.citations || [];
  return (
    <section className="detail-block" aria-labelledby="mod-snapshot-title" data-testid="assistant-snapshot">
      <h3 id="mod-snapshot-title">{s.title}</h3>
      <p className="muted small">{s.summary}</p>
      <dl className="kv-grid kv-grid-wide">
        <div className="kv-item"><dt>{s.assistant}</dt><dd>{assistant.deleted ? s.assistantDeleted(assistant.name || assistant.id) : (assistant.name || assistant.id)}</dd></div>
        <div className="kv-item"><dt>{s.owner}</dt><dd>{snapshot.owner ? authorName(snapshot.owner) : '-'}</dd></div>
        <div className="kv-item"><dt>{s.reason}</dt><dd>{s.reasons[snapshot.reason] || snapshot.reason}</dd></div>
        <div className="kv-item"><dt>{s.model}</dt><dd>{[model.endpointName, model.model].filter(Boolean).join(' / ') || '-'}{model.usedFallback ? ' (' + s.fallback + ')' : ''}</dd></div>
        <div className="kv-item"><dt>{s.conversation}</dt><dd>{snapshot.conversationDeleted ? s.conversationDeleted : s.conversationKept}</dd></div>
        <div className="kv-item"><dt>{s.expires}</dt><dd>{snapshot.expiresUtc ? formatDateTime(snapshot.expiresUtc) : '-'}</dd></div>
      </dl>
      {citations.length > 0 && (
        <div>
          <strong className="small">{s.citations}</strong>
          <ul className="history-list">
            {citations.map((c) => <li key={c.index} className="small">[{c.index}] {c.title} <span className="muted mono">{c.targetType} {c.targetId}</span></li>)}
          </ul>
        </div>
      )}
      <h4>{s.messagesTitle(total)}</h4>
      <ol className="ai-transcript ai-snapshot" aria-label={s.title}>
        {messages.map((m) => (
          <li key={m.id || m.position} className={'ai-message ' + (m.role === 'Assistant' ? 'ai-message-reply' : 'ai-message-user') + (m.isReported ? ' ai-message-reported' : '')} data-testid={'snapshot-message-' + m.position}>
            <span className="badge-row">
              <Badge tone="neutral">{s.roles[m.role] || m.role}</Badge>
              {m.isReported && <Badge tone="danger">{s.reported}</Badge>}
              {m.status && m.status !== 'Completed' && <Badge tone="warning">{m.status}</Badge>}
              <span className="muted small">{formatDateTime(m.createdUtc)}</span>
            </span>
            {m.role === 'Assistant' ? <AssistantMarkdown text={m.content} /> : <p className="ai-user-text">{m.content}</p>}
          </li>
        ))}
      </ol>
      <ErrorBanner message={error ? s.loadError + ' ' + errorMessage(error, '') : ''} onRetry={loadMore} />
      {!done && messages.length < total && (
        <button type="button" className="button button-secondary button-sm" onClick={loadMore} disabled={loading}>{s.loadMore}</button>
      )}
      <p className="muted small">{s.assistantNote} <Link to="/admin/ai-assistants">{s.openAssistant}</Link></p>
    </section>
  );
}

export default AssistantSnapshotPanel;
