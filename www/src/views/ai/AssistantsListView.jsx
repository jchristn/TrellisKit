import { useCallback, useState } from 'react';
import { Link } from 'react-router-dom';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useDebounce from '../../hooks/useDebounce.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import { Loading, ErrorBanner, EmptyState } from '../../components/States.jsx';
import { chatErrorText } from './ChatPanel.jsx';
import useVisitorClient from './useVisitorClient.js';

const a = strings.ai;

/**
 * /app/assistants: the assistants the member may use, as cards, with search. Signed-out visitors
 * (anonymous browsing) list the Public assistants with a visitor session and chat on the public
 * assistant page.
 */
function AssistantsListView() {
  const { client: memberClient, me } = useAuth();
  const visitor = useVisitorClient({ enabled: !me });
  const client = me ? memberClient : visitor.client;
  const { fill } = useProperty();
  const [search, setSearch] = useState('');
  const debounced = useDebounce(search, 300);
  useDocumentMeta(strings.member.metaTitle(a.assistantsTitle), strings.member.metaDescription);
  const fetcher = useCallback((signal) => (client ? client.enumerateAssistants({ search: debounced || undefined, maxResults: 50 }, signal) : new Promise(() => {})), [client, debounced]);
  const list = useApiQuery(fetcher);
  const items = (list.data && list.data.objects) || [];
  return (
    <div className="member-view ai-list-view">
      <header className="member-hero">
        <h1>{a.assistantsTitle}</h1>
        <p className="muted">{fill(a.assistantsSummary)}</p>
      </header>
      <label className="field">
        <span className="visually-hidden">{a.searchAssistants}</span>
        <input type="search" placeholder={a.searchAssistants} value={search} onChange={(e) => setSearch(e.target.value)} />
      </label>
      {list.loading && !list.data && <Loading />}
      {visitor.error && !me && <ErrorBanner message={chatErrorText(visitor.error)} />}
      {list.error && <ErrorBanner message={chatErrorText(list.error)} onRetry={list.reload} />}
      {list.data && items.length === 0 && <EmptyState icon="message" title={a.noAssistants} />}
      <ul className="ai-assistant-grid">
        {items.map((assistant) => (
          <li key={assistant.id}>
            <Link to={(me ? '/app/assistants/' : '/assistants/') + assistant.id} className="ai-assistant-card" aria-label={a.openAssistant(assistant.title)}>
              <span className="ai-avatar" aria-hidden="true">{(assistant.avatar && assistant.avatar.emoji) || assistant.title.slice(0, 1)}</span>
              <span className="ai-assistant-card-text">
                <strong>{assistant.title}</strong>
                {assistant.description && <span className="muted small">{assistant.description}</span>}
              </span>
              <span className="badge badge-info">{a.aiBadge}</span>
            </Link>
          </li>
        ))}
      </ul>
    </div>
  );
}

export default AssistantsListView;
