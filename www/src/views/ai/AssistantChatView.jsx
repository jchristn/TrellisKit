import { useCallback, useState } from 'react';
import { Link, Navigate, useNavigate, useParams } from 'react-router-dom';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useDebounce from '../../hooks/useDebounce.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import ConfirmModal from '../../components/ConfirmModal.jsx';
import { Loading, ErrorBanner } from '../../components/States.jsx';
import { formatShortDate } from '../../utils/format.js';
import ChatPanel, { chatErrorText } from './ChatPanel.jsx';

const a = strings.ai;

function ConversationSidebar({ client, assistantId, activeId, version, onOpen, onNew, onChanged }) {
  const [search, setSearch] = useState('');
  const [archived, setArchived] = useState(false);
  const [deleting, setDeleting] = useState(null);
  const debounced = useDebounce(search, 300);
  const fetcher = useCallback((signal) => client.enumerateAssistantConversations(assistantId, { search: debounced || undefined, maxResults: 50, filters: { archived: archived ? 'true' : 'false' } }, signal),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [client, assistantId, debounced, archived, version]);
  const list = useApiQuery(fetcher);
  const rename = async (conversation) => {
    const title = window.prompt(a.renamePrompt, conversation.title || '');
    if (!title || !title.trim()) return;
    await client.updateConversation(conversation.id, { title: title.trim() }).catch(() => {});
    list.reload();
  };
  const toggleArchive = async (conversation) => {
    await client.updateConversation(conversation.id, { isArchived: !conversation.isArchived }).catch(() => {});
    list.reload();
  };
  const remove = async () => {
    await client.deleteConversation(deleting.id).catch(() => {});
    setDeleting(null);
    onChanged(deleting.id);
    list.reload();
  };
  const items = (list.data && list.data.objects) || [];
  return (
    <aside className="ai-sidebar" aria-label={a.conversations}>
      <button type="button" className="button button-primary button-sm" onClick={onNew}>{a.newConversation}</button>
      <input type="search" className="ai-sidebar-search" placeholder={a.searchConversations} aria-label={a.searchConversations} value={search} onChange={(e) => setSearch(e.target.value)} />
      <label className="checkbox-inline small"><input type="checkbox" checked={archived} onChange={(e) => setArchived(e.target.checked)} /> {a.showArchived}</label>
      {list.loading && !list.data && <Loading inline />}
      {list.data && items.length === 0 && <p className="muted small">{a.noConversations}</p>}
      <ul className="ai-conversation-list">
        {items.map((c) => (
          <li key={c.id} className={c.id === activeId ? 'active' : ''}>
            <button type="button" className="ai-conversation-link" onClick={() => onOpen(c)}>
              <span>{c.title || a.untitled}</span>
              <span className="muted small">{formatShortDate(c.lastMessageUtc || c.createdUtc)}</span>
            </button>
            <span className="ai-conversation-actions">
              <button type="button" className="link-button small" onClick={() => rename(c)}>{a.rename}</button>
              <button type="button" className="link-button small" onClick={() => toggleArchive(c)}>{c.isArchived ? a.unarchive : a.archive}</button>
              <button type="button" className="link-button small danger" onClick={() => setDeleting(c)}>{a.deleteConversation}</button>
            </span>
          </li>
        ))}
      </ul>
      <ConfirmModal open={Boolean(deleting)} title={a.deleteConfirmTitle} message={a.deleteConfirmBody} confirmLabel={strings.common.delete} onCancel={() => setDeleting(null)} onConfirm={remove} />
    </aside>
  );
}

/** /app/assistants/:id and /app/assistants/:id/c/:conversationId: the member chat page. */
function MemberAssistantChatView() {
  const { id, conversationId } = useParams();
  const navigate = useNavigate();
  const { client } = useAuth();
  const [version, setVersion] = useState(0);
  const fetcher = useCallback((signal) => client.getAssistant(id, signal), [client, id]);
  const assistant = useApiQuery(fetcher);
  useDocumentMeta(strings.member.metaTitle(assistant.data ? assistant.data.title : a.assistantsTitle), strings.member.metaDescription);
  if (assistant.loading && !assistant.data) return <Loading />;
  if (assistant.error) return <div className="member-view"><ErrorBanner message={chatErrorText(assistant.error)} /><Link to="/app/assistants">{a.assistantsTitle}</Link></div>;
  const summary = assistant.data;
  return (
    <div className="member-view ai-chat-view">
      <header className="ai-chat-header">
        <span className="ai-avatar" aria-hidden="true">{(summary.avatar && summary.avatar.emoji) || summary.title.slice(0, 1)}</span>
        <div>
          <h1>{summary.title} <span className="badge badge-info">{a.aiBadge}</span></h1>
          {summary.description && <p className="muted small">{summary.description}</p>}
        </div>
      </header>
      <div className="ai-chat-layout">
        <ConversationSidebar
          client={client}
          assistantId={summary.id}
          activeId={conversationId || null}
          version={version}
          onOpen={(c) => navigate('/app/assistants/' + summary.id + '/c/' + c.id)}
          onNew={() => navigate('/app/assistants/' + summary.id)}
          onChanged={(removedId) => { if (removedId === conversationId) navigate('/app/assistants/' + summary.id); }}
        />
        <ChatPanel
          client={client}
          assistant={summary}
          conversationId={conversationId || null}
          allowReport
          onConversation={(c) => { setVersion((v) => v + 1); navigate('/app/assistants/' + summary.id + '/c/' + c.id, { replace: true }); }}
        />
      </div>
    </div>
  );
}

/** Signed-out visitors (anonymous browsing) chat on the public assistant page with a visitor session. */
function AssistantChatView() {
  const { me } = useAuth();
  const { id } = useParams();
  if (!me) return <Navigate to={'/assistants/' + encodeURIComponent(id)} replace />;
  return <MemberAssistantChatView />;
}

export default AssistantChatView;
