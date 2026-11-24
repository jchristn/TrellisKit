import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, NavLink, useParams, useSearchParams } from 'react-router-dom';
import { ConversationFolder, canUseMessaging, formatRelativeTime, formatUnreadBadge, headlineParts } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import { useConversationList, useRealtime } from '../../context/RealtimeContext.jsx';
import useCursorList from '../../hooks/useCursorList.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import useEnumerationUrlState, { activeMetaCount } from '../../hooks/useEnumerationUrlState.js';
import MetadataFilterBar from '../../components/MetadataFilterBar.jsx';
import Avatar from '../../components/Avatar.jsx';
import Icon from '../../components/Icon.jsx';
import { AccessPromptCard } from '../../components/AccessPrompt.jsx';
import { EmptyState, ErrorBanner, Loading } from '../../components/States.jsx';
import { authorName, mediaUrl } from '../../utils/engagement.js';
import { formatDateTime } from '../../utils/format.js';
import { conversationPath, otherMemberOf, previewText } from '../../utils/messaging.js';
import ConversationPane from './ConversationPane.jsx';

const t = strings.messaging;
const PAGE_SIZE = 30;
const SEARCH_PAGE_SIZE = 20;

function FolderTabs({ folder, unread }) {
  const tabs = [
    { id: ConversationFolder.Inbox, to: '/app/messages', count: unread.conversations },
    { id: ConversationFolder.Requests, to: '/app/messages/requests', count: unread.requests },
    { id: ConversationFolder.Archived, to: '/app/messages?folder=Archived', count: null }
  ];
  return (
    <nav className="segmented messages-tabs" aria-label={t.foldersLabel}>
      {tabs.map((tab) => {
        const badge = formatUnreadBadge(tab.count);
        return (
          <NavLink key={tab.id} to={tab.to} end className={() => 'segment' + (folder === tab.id ? ' active' : '')} aria-current={folder === tab.id ? 'page' : undefined}>
            <span>{t.folders[tab.id]}</span>
            {badge && <span className="count-badge" aria-label={t.folderUnread(tab.count)}>{badge}</span>}
          </NavLink>
        );
      })}
    </nav>
  );
}

function ConversationRow({ conversation, meId, active }) {
  const other = otherMemberOf(conversation, meId);
  const name = authorName(other);
  const unread = conversation.unreadCount || 0;
  const last = conversation.lastMessage;
  const time = (last && last.createdUtc) || conversation.updatedUtc;
  return (
    <li className={'conversation-row' + (unread ? ' unread' : '') + (active ? ' active' : '')}>
      <Link to={conversationPath(conversation.id)} className="conversation-link" aria-current={active ? 'page' : undefined}>
        <Avatar src={other && !other.isDeleted ? mediaUrl(other.avatarUrl) : null} name={name} size={40} />
        <span className="conversation-text">
          <span className="conversation-name">
            {name}
            {conversation.isMuted && <Icon name="mute" size={12} />}
          </span>
          <span className="conversation-preview muted small">{previewText(last, meId)}</span>
        </span>
        <span className="conversation-side">
          {time && <time className="muted small" dateTime={time} title={formatDateTime(time)}>{formatRelativeTime(time)}</time>}
          {unread > 0 && <span className="count-badge" aria-label={t.unreadBadge(unread)}>{formatUnreadBadge(unread)}</span>}
        </span>
      </Link>
    </li>
  );
}

function SearchResults({ query, meta, onClose }) {
  const { client } = useAuth();
  const fetchPage = useCallback((cursor, signal) => client.messaging.searchMessages({ search: query, labels: meta.labels, labelMatch: meta.labelMatch, tags: meta.tags, maxResults: SEARCH_PAGE_SIZE, continuationToken: cursor }, signal), [client, query, meta.labels, meta.labelMatch, meta.tags]);
  const list = useCursorList(fetchPage);
  return (
    <section className="message-search-results" aria-label={t.searchResults(query)}>
      <div className="row-between">
        <h2 className="small">{t.searchResults(query)}</h2>
        <button type="button" className="link-button small" onClick={onClose}>{t.searchClear}</button>
      </div>
      <ErrorBanner message={list.error ? t.searchError : ''} onRetry={list.refresh} />
      {!list.loaded && list.loading && <Loading inline />}
      {list.loaded && !list.items.length && !list.error && <p className="muted small">{t.searchEmpty}</p>}
      <ul className="conversation-list">
        {list.items.map((hit) => {
          const name = authorName(hit.otherMember);
          const message = hit.message || {};
          return (
            <li key={message.id || hit.conversationId} className="conversation-row">
              <Link to={conversationPath(hit.conversationId, message.seq)} className="conversation-link" aria-label={t.searchResultLabel(name)}>
                <Avatar src={hit.otherMember ? mediaUrl(hit.otherMember.avatarUrl) : null} name={name} size={32} />
                <span className="conversation-text">
                  <span className="conversation-name">{name}</span>
                  <span className="conversation-preview small">{hit.headline ? headlineParts(hit.headline).map((part, i) => (part.match ? <mark key={i}>{part.text}</mark> : <span key={i}>{part.text}</span>)) : message.body}</span>
                </span>
                {message.createdUtc && <time className="muted small" dateTime={message.createdUtc}>{formatRelativeTime(message.createdUtc)}</time>}
              </Link>
            </li>
          );
        })}
      </ul>
      {list.loaded && !list.done && !list.loading && <button type="button" className="button button-secondary button-sm" onClick={list.loadMore}>{t.loadMore}</button>}
    </section>
  );
}

function MessageSearchForm({ value, onSearch }) {
  const [text, setText] = useState(value);
  const [error, setError] = useState('');
  const [last, setLast] = useState(value);
  if (last !== value) {
    setLast(value);
    setText(value);
  }
  const submit = (e) => {
    e.preventDefault();
    const q = text.trim();
    if (q.length < 2) {
      setError(t.searchTooShort);
      return;
    }
    setError('');
    onSearch(q.slice(0, 200));
  };
  return (
    <form className="message-search" role="search" onSubmit={submit}>
      <label className="visually-hidden" htmlFor="message-search-input">{t.messageSearch}</label>
      <input id="message-search-input" className="input-compact" type="search" value={text} maxLength={200} placeholder={t.messageSearch} onChange={(e) => setText(e.target.value)} />
      <button type="submit" className="button button-secondary button-sm"><Icon name="search" size={14} /><span>{t.messageSearchButton}</span></button>
      {error && <span className="field-error small" role="alert">{error}</span>}
    </form>
  );
}

/**
 * /app/messages, /app/messages/requests, and /app/messages/:id: the conversation list (Inbox,
 * Requests, Archived; name search; label and tag filters on the caller's own conversation
 * metadata; unread badges; load more) beside the open conversation, plus message search across
 * the caller's conversations (results open the conversation at the message).
 * @param {{ requests?: boolean }} props
 */
function MessagesView({ requests = false }) {
  const { client, me } = useAuth();
  const { property } = useProperty();
  const { id } = useParams();
  const [searchParams, setSearchParams] = useSearchParams();
  const { listStore } = useRealtime();
  const snapshot = useConversationList();
  useDocumentMeta(t.meta, t.summary);
  const folder = requests ? ConversationFolder.Requests : (searchParams.get('folder') === ConversationFolder.Archived ? ConversationFolder.Archived : ConversationFolder.Inbox);
  const [meta, setMeta, metaKey] = useEnumerationUrlState();
  const messageQuery = searchParams.get('q') || '';
  const access = canUseMessaging(property, me, 'read');
  const filtered = activeMetaCount(meta) > 0;

  const fetchPage = useCallback((cursor, signal) => {
    const parsed = JSON.parse(metaKey);
    return client.messaging.enumerateConversations({ search: parsed.search, labels: parsed.labels, labelMatch: parsed.labelMatch, tags: parsed.tags, maxResults: PAGE_SIZE, continuationToken: cursor, filters: { folder } }, signal);
  }, [client, folder, metaKey]);
  const list = useCursorList(fetchPage, { enabled: access.allowed });

  useEffect(() => {
    if (listStore && list.items.length) listStore.mergePage(list.items);
  }, [listStore, list.items]);

  const conversations = useMemo(() => {
    const live = new Map(snapshot.conversations.map((c) => [c.id, c]));
    if (filtered || !listStore) return list.items.map((c) => live.get(c.id) || c).filter((c) => (c.folder || ConversationFolder.Inbox) === folder);
    return snapshot.conversations.filter((c) => (c.folder || ConversationFolder.Inbox) === folder);
  }, [filtered, folder, list.items, listStore, snapshot.conversations]);

  const setQuery = (q) => setSearchParams((prev) => {
    const next = new URLSearchParams(prev);
    if (q) next.set('q', q);
    else next.delete('q');
    return next;
  });

  if (!access.allowed) {
    return (
      <div className="member-view">
        {access.requires ? <AccessPromptCard required={access.requires} feature="messaging" /> : <EmptyState icon="message" title={t.notFound} />}
      </div>
    );
  }

  const unread = snapshot.unread || {};
  return (
    <div className={'member-view messages-view' + (id ? ' has-conversation' : '')}>
      <div className="messages-layout">
        <aside className="messages-list-pane" aria-label={t.title}>
          <header className="page-header">
            <div className="page-header-text">
              <h1>{t.title}</h1>
            </div>
            <Link to="/app/settings/messaging" className="icon-button" aria-label={t.settings.link} title={t.settings.link}><Icon name="gear" size={16} /></Link>
          </header>
          <FolderTabs folder={folder} unread={unread} />
          <MessageSearchForm value={messageQuery} onSearch={setQuery} />
          {messageQuery ? (
            <SearchResults query={messageQuery} meta={meta} onClose={() => setQuery('')} />
          ) : (
            <>
              <MetadataFilterBar value={meta} onChange={setMeta} client={client} targetType="Conversation" showSearch searchLabel={t.nameSearch} />
              <ErrorBanner message={list.error ? t.listError : ''} onRetry={list.refresh} />
              {!list.loaded && list.loading && <Loading inline />}
              {list.loaded && !conversations.length && !list.loading && !list.error && <p className="muted small conversation-empty">{filtered ? t.emptyFiltered : t.empty[folder]}</p>}
              {conversations.length > 0 && (
                <ul className="conversation-list" aria-label={t.folders[folder]}>
                  {conversations.map((c) => <ConversationRow key={c.id} conversation={c} meId={me && me.id} active={c.id === id} />)}
                </ul>
              )}
              {list.loaded && !list.done && !list.loading && <button type="button" className="button button-secondary button-sm" onClick={list.loadMore}>{t.loadMore}</button>}
            </>
          )}
        </aside>
        <div className="messages-thread-pane">
          {id ? <ConversationPane key={id} conversationId={id} /> : (
            <EmptyState icon="message" title={t.selectConversation} action={null}>{t.selectConversationBody}</EmptyState>
          )}
        </div>
      </div>
    </div>
  );
}

export default MessagesView;
