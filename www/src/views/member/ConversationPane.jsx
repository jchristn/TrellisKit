import { Fragment, useCallback, useEffect, useLayoutEffect, useRef, useState, useSyncExternalStore } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { ConversationFolder, MessageState, RequestState, canUseMessaging, createMessageStore } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import { useConversationList, useRealtime, useRealtimeEvent } from '../../context/RealtimeContext.jsx';
import { useAccessPrompt } from '../../components/AccessPrompt.jsx';
import Avatar from '../../components/Avatar.jsx';
import Icon from '../../components/Icon.jsx';
import ActionMenu from '../../components/ActionMenu.jsx';
import ConfirmModal from '../../components/ConfirmModal.jsx';
import { Banner, EmptyState, ErrorBanner, Loading } from '../../components/States.jsx';
import ReactionBar from '../../components/feed/ReactionBar.jsx';
import MediaGallery from '../../components/feed/MediaGallery.jsx';
import LinkPreviewCard from '../../components/feed/LinkPreviewCard.jsx';
import AttachmentList from '../../components/feed/AttachmentList.jsx';
import ReportDialog from '../../components/feed/ReportDialog.jsx';
import BlockDialog from '../../components/feed/BlockDialog.jsx';
import { useMediaUploads } from '../../components/feed/useMediaUploads.js';
import { useLinkPreview } from '../../components/feed/useLinkPreview.js';
import { authorName, errorText, mediaUrl, memberPath } from '../../utils/engagement.js';
import { formatDateTime, formatTime } from '../../utils/format.js';
import { cannotSendText, hasUrl, isPendingRequest, otherMemberOf, receiptOf, withDaySeparators } from '../../utils/messaging.js';

const t = strings.messaging;
const MAX_ATTACHMENTS = 4;
const URL_PATTERN = /(https?:\/\/[^\s<]+)/gi;

/** Plain message text with URLs turned into links (direct messages have no Markdown, contract 2.2). */
export function LinkifiedText({ text }) {
  const parts = String(text || '').split(URL_PATTERN);
  return (
    <p className="message-text preserve-lines">
      {parts.map((part, i) => (i % 2 === 1
        ? <a key={i} href={part} target="_blank" rel="noopener noreferrer nofollow">{part}</a>
        : <Fragment key={i}>{part}</Fragment>))}
    </p>
  );
}

function ReplyQuote({ replyTo, onJump }) {
  if (!replyTo) return null;
  const name = replyTo.authorDisplayName || '';
  return (
    <button type="button" className="message-quote" onClick={() => onJump(replyTo.seq)} aria-label={t.jumpToReply(name)}>
      <span className="message-quote-author small">{name}</span>
      <span className="message-quote-text small muted">{replyTo.isDeleted ? t.quoteDeleted : replyTo.excerpt}</span>
    </button>
  );
}

function MessageItem({ message, mine, conversation, highlighted, isLastMine, onReply, onEdit, onDelete, onReport, onJump, onRetry, onDiscard, onReactions, canSend }) {
  const [showOriginal, setShowOriginal] = useState(false);
  const viewer = message.viewer || {};
  const time = message.createdUtc;
  const visible = !message.state || message.state === MessageState.Visible;
  const receipt = mine ? receiptOf(message, conversation) : null;
  const failed = message.pending && message.sendState === 'Failed';
  const items = [];
  if (visible && !message.pending && canSend) items.push({ label: t.reply, icon: 'reply', onSelect: () => onReply(message) });
  if (visible && !message.pending && viewer.canEdit) items.push({ label: t.edit, icon: 'edit', onSelect: () => onEdit(message) });
  if (visible && !message.pending && viewer.canDelete) items.push({ label: t.delete, icon: 'trash', danger: true, onSelect: () => onDelete(message) });
  if (visible && !message.pending && viewer.canReport) items.push({ label: t.report, icon: 'flag', onSelect: () => onReport(message) });
  const hiddenAttachments = isPendingRequest(conversation) && !mine && message.attachmentsHidden === true;
  return (
    <li className={'message' + (mine ? ' message-mine' : ' message-theirs') + (highlighted ? ' message-highlight' : '') + (message.pending ? ' message-pending' : '')} data-seq={message.seq ?? undefined} data-testid="message">
      <div className="message-bubble">
        {!visible ? (
          <p className="message-tombstone muted"><Icon name="ban" size={12} /> {message.state === MessageState.Removed ? t.removed : t.deleted}</p>
        ) : (
          <>
            <ReplyQuote replyTo={message.replyTo} onJump={onJump} />
            {message.body && <LinkifiedText text={message.body} />}
            {Array.isArray(message.attachments) && message.attachments.length > 0 && <MediaGallery items={message.attachments} compact />}
            {hiddenAttachments && <p className="muted small"><Icon name="image" size={12} /> {t.attachmentsHidden}</p>}
            {message.linkPreview && <LinkPreviewCard preview={message.linkPreview} />}
            {message.isEdited && (
              <div className="message-edited small">
                <button type="button" className="link-button small" onClick={() => setShowOriginal((v) => !v)} aria-expanded={showOriginal} title={showOriginal ? t.hideOriginal : t.showOriginal}>
                  {t.edited}
                </button>
                {showOriginal && message.originalBody !== null && message.originalBody !== undefined && (
                  <blockquote className="message-original" aria-label={t.originalLabel}><span className="visually-hidden">{t.originalLabel}: </span>{message.originalBody}</blockquote>
                )}
              </div>
            )}
          </>
        )}
      </div>
      <div className="message-meta muted small">
        <time dateTime={time} title={formatDateTime(time)}>{formatTime(time)}</time>
        {receipt && (isLastMine || receipt === 'sending' || receipt === 'failed') && <span className={'message-receipt receipt-' + receipt}>{t.receipts[receipt]}</span>}
        {failed && (
          <>
            <button type="button" className="link-button small" onClick={() => onRetry(message)}>{t.retry}</button>
            <button type="button" className="link-button small" onClick={() => onDiscard(message)}>{t.discard}</button>
          </>
        )}
        {items.length > 0 && <ActionMenu label={t.messageActions(formatDateTime(time))} items={items} />}
      </div>
      {visible && !message.pending && message.id && (message.reactionTotal > 0 || viewer.canReact) && (
        <div className="message-reactions">
          <ReactionBar target={{ type: 'Message', id: message.id }} value={message} canReact={Boolean(viewer.canReact)} onChange={(fields) => onReactions(message, fields)} />
        </div>
      )}
    </li>
  );
}

function EditDialog({ message, onCancel, onSave }) {
  const [body, setBody] = useState(message.body || '');
  const [busy, setBusy] = useState(false);
  const submit = async (e) => {
    e.preventDefault();
    setBusy(true);
    try { await onSave(body); } finally { setBusy(false); }
  };
  return (
    <form className="message-edit panel" onSubmit={submit} aria-label={t.editLabel}>
      <label className="label-text" htmlFor="message-edit-body">{t.editLabel}</label>
      <textarea id="message-edit-body" className="textarea" rows={3} value={body} maxLength={4000} onChange={(e) => setBody(e.target.value)} />
      <div className="form-actions">
        <button type="submit" className="button button-primary button-sm" disabled={busy || !body.trim()}>{t.saveEdit}</button>
        <button type="button" className="button button-secondary button-sm" onClick={onCancel}>{t.cancelEdit}</button>
      </div>
    </form>
  );
}

function Composer({ conversationId, otherName, onSend, onTyping, replyTo, onCancelReply, allowMedia, allowVideo, disabled }) {
  const { client } = useAuth();
  const { fill } = useProperty();
  const toast = useToast();
  const [body, setBody] = useState('');
  const [showPreview, setShowPreview] = useState(true);
  const uploads = useMediaUploads({ client, maxCount: MAX_ATTACHMENTS, maxVideos: 1, allowMedia, allowVideo, isAdmin: false, onError: (msg) => toast.error(msg), purpose: 'Message', scopeId: conversationId });
  const preview = useLinkPreview(client, body, showPreview && hasUrl(body));
  const inputRef = useRef(null);

  useEffect(() => {
    if (replyTo && inputRef.current) inputRef.current.focus();
  }, [replyTo]);

  const submit = (e) => {
    if (e) e.preventDefault();
    const text = body.trim();
    if (!text && !uploads.mediaIds.length) {
      toast.error(t.errors.MessageEmpty);
      return;
    }
    if (uploads.busy) return;
    const attachments = uploads.items.map((item) => item.media).filter(Boolean);
    onSend({ body: text, mediaIds: uploads.mediaIds, replyToMessageId: replyTo ? replyTo.id : null, showLinkPreview: showPreview, attachments });
    setBody('');
    setShowPreview(true);
    uploads.reset();
  };

  const onKeyDown = (e) => {
    if (e.key === 'Enter' && !e.shiftKey && !e.nativeEvent.isComposing) {
      e.preventDefault();
      submit();
    }
  };

  return (
    <form className="message-composer" onSubmit={submit} aria-label={t.composerLabel(otherName)}>
      {replyTo && (
        <div className="composer-reply small">
          <span>{t.replyingTo(authorName(replyTo.author))}: <span className="muted">{String(replyTo.body || '').slice(0, 120)}</span></span>
          <button type="button" className="icon-button" onClick={onCancelReply} aria-label={t.cancelReply} title={t.cancelReply}><Icon name="close" size={12} /></button>
        </div>
      )}
      {hasUrl(body) && showPreview && preview && <LinkPreviewCard preview={preview} onRemove={() => setShowPreview(false)} />}
      {hasUrl(body) && showPreview && <p className="muted small composer-preview-note">{fill(t.linkPreviewNote)}</p>}
      <div className="composer-row">
        <label className="visually-hidden" htmlFor={'composer-' + conversationId}>{t.composerLabel(otherName)}</label>
        <textarea
          id={'composer-' + conversationId}
          ref={inputRef}
          className="textarea message-input"
          rows={2}
          maxLength={4000}
          value={body}
          placeholder={t.composerPlaceholder}
          disabled={disabled}
          onKeyDown={onKeyDown}
          onChange={(e) => { setBody(e.target.value); if (e.target.value) onTyping(); }}
        />
        <button type="submit" className="button button-primary" disabled={disabled || uploads.busy}>{t.send}</button>
      </div>
      {hasUrl(body) && (
        <label className="checkbox-row small">
          <input type="checkbox" checked={showPreview} onChange={(e) => setShowPreview(e.target.checked)} />
          <span>{t.showPreview}</span>
        </label>
      )}
      {allowMedia && <AttachmentList uploads={uploads} allowMedia={allowMedia} allowVideo={allowVideo} maxCount={MAX_ATTACHMENTS} label={t.attach} disabled={disabled} showAlt={false} />}
    </form>
  );
}

/**
 * One conversation (contract sections 3 and 4): history newest at the bottom with date
 * separators, load older on scroll (and a button), jump to a message (`?around=<seq>`), read
 * receipts, the typing indicator, replies, reactions, attachments, link previews, one edit with
 * the original text, delete for everyone (tombstones), optimistic sends with retry, the request
 * banner, and conversation actions. Fed live by the session's RealtimeClient.
 * @param {{ conversationId: string }} props
 */
function ConversationPane({ conversationId }) {
  const { client, me } = useAuth();
  const { property } = useProperty();
  const toast = useToast();
  const accessPrompt = useAccessPrompt();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const around = searchParams.get('around');
  const { realtime, listStore, polling } = useRealtime();
  const listSnapshot = useConversationList();
  const api = client.messaging;
  const meId = me ? me.id : null;
  const [store] = useState(() => createMessageStore({ conversationId, api, currentUser: me ? { id: me.id, displayName: me.displayName, handle: me.handle, avatarUrl: me.avatarUrl } : null }));
  const snapshot = useSyncExternalStore(store.subscribe, store.getSnapshot, store.getSnapshot);
  const [fetched, setFetched] = useState(null);
  const [status, setStatus] = useState({ loading: true, error: null });
  const [loadingOlder, setLoadingOlder] = useState(false);
  const [replyTo, setReplyTo] = useState(null);
  const [editing, setEditing] = useState(null);
  const [deleting, setDeleting] = useState(null);
  const [dialog, setDialog] = useState(null);
  const [typing, setTyping] = useState(false);
  const [highlight, setHighlight] = useState(around ? Number(around) : null);
  const scrollRef = useRef(null);
  const preserveRef = useRef(null);
  const lastCountRef = useRef(0);
  const readSentRef = useRef(0);
  const typingTimer = useRef(null);

  const live = listSnapshot.conversations.find((c) => c.id === conversationId) || null;
  const conversation = live || fetched;
  const other = otherMemberOf(conversation, meId);
  const otherName = other ? authorName(other) : '';
  const viewer = (conversation && conversation.viewer) || {};
  const write = canUseMessaging(property, me, 'write');
  const canSend = Boolean(conversation) && viewer.canSend !== false && write.allowed && !isPendingRequest(conversation);

  const upsertConversation = useCallback((c) => {
    if (!c || !c.id) return;
    setFetched(c);
    if (listStore) listStore.upsertConversation(c);
  }, [listStore]);

  useEffect(() => {
    const controller = new AbortController();
    let active = true;
    (async () => {
      try {
        const c = await api.getConversation(conversationId, controller.signal);
        if (!active) return;
        upsertConversation(c);
        if (around) await store.loadAround(Number(around), controller.signal);
        else await store.loadLatest(controller.signal);
        if (active) setStatus({ loading: false, error: null });
      } catch (err) {
        if (active && !controller.signal.aborted) setStatus({ loading: false, error: err });
      }
    })();
    return () => { active = false; controller.abort(); };
  }, [api, conversationId, around, store, upsertConversation]);

  useEffect(() => {
    if (!realtime) return undefined;
    const detach = store.attachRealtime(realtime);
    realtime.setActiveConversation(conversationId);
    if (listStore) listStore.setActiveConversation(conversationId);
    return () => {
      detach();
      realtime.setActiveConversation(null);
      if (listStore) listStore.setActiveConversation(null);
    };
  }, [realtime, store, conversationId, listStore]);

  // Typing indicator: shown until `Stop`, a message from them, or `expiresInMs`.
  const onTypingEvent = useCallback((data) => {
    if (!data || data.conversationId !== conversationId || data.userId === meId) return;
    clearTimeout(typingTimer.current);
    if (data.state === 'Stop') { setTyping(false); return; }
    setTyping(true);
    typingTimer.current = setTimeout(() => setTyping(false), Number(data.expiresInMs) || 6000);
  }, [conversationId, meId]);
  useRealtimeEvent('typing', onTypingEvent);
  const onCreated = useCallback((data) => {
    if (data && data.conversationId === conversationId && data.message && data.message.author && data.message.author.id !== meId) {
      clearTimeout(typingTimer.current);
      setTyping(false);
    }
  }, [conversationId, meId]);
  useRealtimeEvent('message.created', onCreated);
  useEffect(() => () => clearTimeout(typingTimer.current), []);

  // Read receipts: mark read up to the newest message while the conversation is on screen.
  const newestSeq = snapshot.newestSeq;
  const myRead = conversation ? conversation.myLastReadSeq || 0 : 0;
  useEffect(() => {
    if (!conversation || !newestSeq || newestSeq <= myRead || readSentRef.current >= newestSeq) return;
    if (typeof document !== 'undefined' && document.visibilityState === 'hidden') return;
    readSentRef.current = newestSeq;
    if (realtime && realtime.isConnected) realtime.read(conversationId, newestSeq);
    else api.markConversationRead(conversationId, newestSeq).catch(() => {});
    if (listStore) listStore.markRead(conversationId, newestSeq);
    setFetched((c) => (c ? { ...c, myLastReadSeq: newestSeq, unreadCount: 0 } : c));
  }, [api, conversation, conversationId, listStore, myRead, newestSeq, realtime]);

  // Keep the scroll position when older messages load; follow new messages at the bottom.
  useLayoutEffect(() => {
    const el = scrollRef.current;
    if (!el) return;
    const count = snapshot.messages.length;
    if (preserveRef.current !== null) {
      el.scrollTop = el.scrollHeight - preserveRef.current;
      preserveRef.current = null;
    } else if (count > lastCountRef.current && !highlight) {
      el.scrollTop = el.scrollHeight;
    }
    lastCountRef.current = count;
  }, [snapshot.messages.length, highlight]);

  useEffect(() => {
    if (!highlight || status.loading) return;
    const el = scrollRef.current && scrollRef.current.querySelector('[data-seq="' + highlight + '"]');
    if (el && typeof el.scrollIntoView === 'function') el.scrollIntoView({ block: 'center' });
  }, [highlight, status.loading]);

  const loadOlder = useCallback(async () => {
    if (loadingOlder || snapshot.hasOlder === false) return;
    setLoadingOlder(true);
    const el = scrollRef.current;
    preserveRef.current = el ? el.scrollHeight - el.scrollTop : null;
    try {
      await store.loadOlder();
    } catch (err) {
      preserveRef.current = null;
      toast.error(errorText(err, t.errors, t.loadError));
    } finally {
      setLoadingOlder(false);
    }
  }, [loadingOlder, snapshot.hasOlder, store, toast]);

  const onScroll = (e) => {
    if (e.currentTarget.scrollTop < 60 && snapshot.hasOlder !== false && !status.loading) loadOlder();
  };

  const jumpTo = useCallback(async (seq) => {
    if (seq === null || seq === undefined) return;
    if (!snapshot.messages.some((m) => m.seq === seq)) {
      try { await store.loadAround(seq); } catch (err) { toast.error(errorText(err, t.errors, t.loadError)); return; }
    }
    setHighlight(seq);
  }, [snapshot.messages, store, toast]);

  const jumpLatest = async () => {
    try { await store.loadNewer(); } catch { /* keep what is shown */ }
    setHighlight(null);
    navigate('/app/messages/' + encodeURIComponent(conversationId), { replace: true });
    const el = scrollRef.current;
    if (el) el.scrollTop = el.scrollHeight;
  };

  const showError = (err) => {
    if (accessPrompt.handle(err)) return;
    toast.error(errorText(err, t.errors, t.errors.generic));
  };

  const send = async (draft) => {
    setReplyTo(null);
    setHighlight(null);
    const result = await store.sendMessage(draft);
    if (!result.ok && result.error) showError(result.error);
  };

  const run = async (fn, success) => {
    try {
      const result = await fn();
      if (success) toast.success(success);
      return result;
    } catch (err) {
      showError(err);
      return null;
    }
  };

  const saveEdit = async (body) => {
    const updated = await run(() => api.editMessage(editing.id, body), t.editSaved);
    if (updated) {
      store.upsertMessage(updated);
      setEditing(null);
    }
  };

  const confirmDelete = async () => {
    const tombstone = await run(() => api.deleteMessage(deleting.id));
    if (tombstone) store.upsertMessage(tombstone);
    setDeleting(null);
  };

  const accept = async () => { const c = await run(() => api.acceptRequest(conversationId), t.accepted); if (c) upsertConversation(c); };
  const decline = async () => {
    const c = await run(() => api.declineRequest(conversationId), t.declined);
    if (c) {
      if (listStore) listStore.remove(conversationId);
      navigate('/app/messages/requests');
    }
  };
  const setFolder = async (folder) => {
    const c = await run(() => api.setConversationFolder(conversationId, folder), folder === ConversationFolder.Archived ? t.archived : t.unarchived);
    if (c) upsertConversation(c);
  };
  const toggleMute = async () => {
    const c = await run(() => (conversation.isMuted ? api.unmuteConversation(conversationId) : api.muteConversation(conversationId)), conversation.isMuted ? t.unmutedDone : t.mutedDone);
    if (c) upsertConversation(c);
  };
  const deleteConversation = async () => {
    setDialog(null);
    const ok = await run(async () => { await api.deleteConversation(conversationId); return true; }, t.conversationDeleted);
    if (ok) {
      if (listStore) listStore.remove(conversationId);
      navigate('/app/messages');
    }
  };

  if (status.error && status.error.status === 404) {
    return <EmptyState icon="message" title={t.notFound} action={<Link to="/app/messages" className="button button-secondary">{t.back}</Link>} />;
  }

  const rows = withDaySeparators(snapshot.messages);
  let lastMineId = null;
  for (const m of snapshot.messages) if (!m.pending && m.author && m.author.id === meId) lastMineId = m.id;
  const menuItems = conversation ? [
    conversation.folder === ConversationFolder.Archived
      ? { label: t.unarchive, icon: 'list', onSelect: () => setFolder(ConversationFolder.Inbox) }
      : conversation.folder !== ConversationFolder.Requests && { label: t.archive, icon: 'list', onSelect: () => setFolder(ConversationFolder.Archived) },
    { label: conversation.isMuted ? t.unmute : t.mute, icon: 'mute', onSelect: toggleMute },
    viewer.canReport !== false && { label: t.reportConversation, icon: 'flag', onSelect: () => setDialog('reportConversation') },
    viewer.canBlock !== false && other && !other.isStaff && { label: t.block, icon: 'ban', onSelect: () => setDialog('block') },
    { label: t.deleteConversation, icon: 'trash', danger: true, onSelect: () => setDialog('delete') }
  ].filter(Boolean) : [];

  return (
    <section className="conversation" aria-label={t.conversationLabel(otherName)}>
      <header className="conversation-header">
        <Link to="/app/messages" className="icon-button conversation-back" aria-label={t.back} title={t.back}><Icon name="chevronLeft" size={16} /></Link>
        {other && <Avatar src={other.isDeleted ? null : mediaUrl(other.avatarUrl)} name={otherName} size={36} />}
        <div className="conversation-title">
          {other && !other.isDeleted ? <Link to={memberPath(other.id)}><h2>{otherName}</h2></Link> : <h2>{otherName}</h2>}
          {conversation && conversation.isMuted && <span className="muted small"><Icon name="mute" size={12} /> {t.muted}</span>}
        </div>
        {menuItems.length > 0 && <ActionMenu label={t.menu} items={menuItems} />}
      </header>
      {polling && <p className="conversation-polling muted small" role="status">{t.polling}</p>}
      <ErrorBanner message={status.error && status.error.status !== 404 ? t.loadError : ''} onRetry={() => window.location.reload()} />
      {conversation && isPendingRequest(conversation) && (
        <Banner tone="info" icon="message" actions={(
          <>
            {viewer.canAccept !== false && <button type="button" className="button button-primary button-sm" onClick={accept}>{t.accept}</button>}
            {viewer.canDecline !== false && <button type="button" className="button button-secondary button-sm" onClick={decline}>{t.decline}</button>}
            <button type="button" className="button button-danger-outline button-sm" onClick={() => setDialog('blockReport')}>{t.blockReport}</button>
          </>
        )}>
          <strong>{t.requestTitle(otherName)}</strong> {t.requestBody}
        </Banner>
      )}
      {conversation && conversation.requestState === RequestState.AwaitingAcceptance && (
        <Banner tone="info" icon="clock"><strong>{t.awaitingTitle}</strong> {t.awaitingBody}</Banner>
      )}
      <div className="conversation-scroll" ref={scrollRef} onScroll={onScroll}>
        {status.loading && <Loading />}
        {!status.loading && snapshot.hasOlder !== false && snapshot.messages.length > 0 && (
          <button type="button" className="button button-secondary button-sm conversation-older" onClick={loadOlder} disabled={loadingOlder}>{loadingOlder ? t.loadingOlder : t.loadOlder}</button>
        )}
        {!status.loading && snapshot.hasOlder === false && <p className="conversation-start muted small">{t.beginning}</p>}
        <ol className="message-list" aria-label={t.messagesLabel}>
          {rows.map((row) => (row.kind === 'day'
            ? <li key={row.key} className="message-day" role="separator"><span>{row.label}</span></li>
            : (
              <MessageItem
                key={row.key}
                message={row.message}
                mine={Boolean(row.message.author && row.message.author.id === meId)}
                conversation={conversation}
                highlighted={highlight !== null && row.message.seq === highlight}
                isLastMine={row.message.id === lastMineId}
                canSend={canSend}
                onReply={setReplyTo}
                onEdit={setEditing}
                onDelete={setDeleting}
                onReport={(m) => setDialog({ report: m })}
                onJump={jumpTo}
                onRetry={(m) => store.retrySend(m.clientMessageId).then((r) => { if (!r.ok && r.error) showError(r.error); })}
                onDiscard={(m) => store.discardPending(m.clientMessageId)}
                onReactions={(m, fields) => store.upsertMessage({ ...m, ...fields })}
              />
            )))}
        </ol>
        {typing && <p className="typing-indicator muted small" role="status">{t.typing(otherName)}</p>}
      </div>
      {around && <button type="button" className="button button-secondary button-sm conversation-latest" onClick={jumpLatest}>{t.jumpLatest}</button>}
      {editing && <EditDialog message={editing} onCancel={() => setEditing(null)} onSave={saveEdit} />}
      {conversation && (canSend ? (
        <Composer
          conversationId={conversationId}
          otherName={otherName}
          onSend={send}
          onTyping={() => { if (realtime) realtime.typing(conversationId, 'Start'); }}
          replyTo={replyTo}
          onCancelReply={() => setReplyTo(null)}
          allowMedia={viewer.canAttachMedia !== false}
          allowVideo={viewer.canAttachVideo !== false}
          disabled={false}
        />
      ) : (
        <p className="conversation-cannot-send muted" role="note">
          {isPendingRequest(conversation) ? cannotSendText('RequestPending') : !write.allowed && write.reason ? cannotSendText(write.reason) : cannotSendText(viewer.cannotSendReason)}
        </p>
      ))}
      <ConfirmModal open={Boolean(deleting)} title={t.deleteTitle} message={t.deleteBody} confirmLabel={t.deleteConfirm} onConfirm={confirmDelete} onCancel={() => setDeleting(null)} />
      <ConfirmModal open={dialog === 'delete'} title={t.deleteConversationTitle} message={t.deleteConversationBody} confirmLabel={t.deleteConfirm} onConfirm={deleteConversation} onCancel={() => setDialog(null)} />
      {dialog && dialog.report && <ReportDialog target={{ type: 'Message', id: dialog.report.id }} author={dialog.report.author} onClose={() => setDialog(null)} />}
      {dialog === 'reportConversation' && other && <ReportDialog target={{ type: 'Conversation', id: conversationId }} author={other} onClose={() => setDialog(null)} />}
      {dialog === 'block' && other && <BlockDialog user={other} onClose={() => setDialog(null)} onDone={() => { if (listStore) listStore.remove(conversationId); navigate('/app/messages'); }} />}
      {dialog === 'blockReport' && other && (
        <BlockDialog user={other} onClose={() => setDialog(null)} onDone={(_u, blocked) => setDialog(blocked ? 'reportAfterBlock' : null)} />
      )}
      {dialog === 'reportAfterBlock' && other && (
        <ReportDialog target={{ type: 'Conversation', id: conversationId }} author={null} onClose={() => { setDialog(null); if (listStore) listStore.remove(conversationId); navigate('/app/messages/requests'); }} />
      )}
    </section>
  );
}

export default ConversationPane;
