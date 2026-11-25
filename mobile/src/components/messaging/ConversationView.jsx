import { FlashList } from '@shopify/flash-list';
import { useEffect, useRef, useState } from 'react';
import { Keyboard, View } from 'react-native';
import KeyboardAvoider from '../KeyboardAvoider';
import { useAuth } from '../../auth/AuthProvider';
import { reactionSet } from '../../content/reactions';
import { cannotSendText, conversationName, describeMessagingError, lastOwnSeq, messageRows, otherMemberOf, receiptState } from '../../messaging/format';
import { useConversation } from '../../messaging/useConversation';
import { useProperty } from '../../property';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Avatar from '../Avatar';
import Button from '../Button';
import ErrorState from '../ErrorState';
import IconButton from '../IconButton';
import Loading from '../Loading';
import Notice from '../Notice';
import Sheet from '../Sheet';
import ActionMenu from '../feed/ActionMenu';
import ConfirmSheet from '../feed/ConfirmSheet';
import ReportSheet from '../feed/ReportSheet';
import { useBlock } from '../feed/useBlock';
import MessageBubble from './MessageBubble';
import MessageComposer from './MessageComposer';

const t = strings.messaging;

/** How many of the property's reactions the message menu offers directly. */
const MENU_REACTIONS = 4;

/** Within this distance (pixels) of the newest message, an arriving message scrolls into view. */
const NEWEST_THRESHOLD = 120;

/**
 * Key of the newest message (its server id, or the local id of an optimistic send).
 * @param {object[]} messages Oldest first.
 * @returns {string|null}
 */
export function newestMessageKey(messages) {
  const last = messages && messages.length ? messages[messages.length - 1] : null;
  return last ? last.clientMessageId || last.localId || last.id || null : null;
}

/**
 * The request banner (contract section 3.2): Accept, Decline, and Block and report for a pending
 * request to me; a waiting note for my own request.
 */
export function RequestBanner({ conversation, name, onAccept, onDecline, onBlockAndReport, busy, testID = 'request-banner' }) {
  const theme = useTheme();
  if (!conversation || !conversation.requestState) return null;
  if (conversation.requestState === 'AwaitingAcceptance') {
    return (
      <View style={{ padding: theme.spacing.md, borderRadius: theme.radii.md, backgroundColor: theme.colors.surface }} testID={testID + '-awaiting'}>
        <AppText color="textMuted">{t.awaitingBanner}</AppText>
      </View>
    );
  }
  const viewer = conversation.viewer || {};
  return (
    <View style={{ padding: theme.spacing.md, gap: theme.spacing.sm, borderRadius: theme.radii.md, borderWidth: 1, borderColor: theme.colors.border, backgroundColor: theme.colors.surface }} testID={testID}>
      <AppText variant="heading" accessibilityRole="header">
        {t.requestTitle(name)}
      </AppText>
      <AppText color="textMuted">{t.requestBody}</AppText>
      <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.sm }}>
        {viewer.canAccept !== false ? <Button label={t.accept} onPress={onAccept} disabled={busy} style={{ alignSelf: 'auto' }} testID={testID + '-accept'} /> : null}
        {viewer.canDecline !== false ? <Button label={t.decline} variant="secondary" onPress={onDecline} disabled={busy} style={{ alignSelf: 'auto' }} testID={testID + '-decline'} /> : null}
        {viewer.canBlock !== false ? <Button label={t.blockAndReport} variant="ghost" onPress={onBlockAndReport} disabled={busy} style={{ alignSelf: 'auto' }} testID={testID + '-block-report'} /> : null}
      </View>
    </View>
  );
}

/**
 * A conversation (phone screen and tablet detail pane): header with the other member and the
 * conversation menu (archive, mute, delete for me, report, block), the request banner, an
 * inverted FlashList of messages with day separators and "load earlier" at the top, the typing
 * indicator, receipts on my newest message, and a keyboard-aware composer (or the reason sending
 * is not possible). Long press a message for Reply, reactions, Edit (once), Delete for everyone,
 * and Report.
 * @param {object} props
 * @param {string} props.conversationId
 * @param {(conversation: object) => void} [props.onConversationChange]
 * @param {(conversationId: string, seq: number) => void} [props.onRead] The conversation was read up to `seq`.
 * @param {() => void} [props.onClosed] The conversation left this list (declined, deleted, blocked).
 * @param {number} [props.keyboardOffset] Height above the view (the navigation header).
 */
export default function ConversationView({ conversationId, onConversationChange, onRead, onClosed, keyboardOffset = 0, showHeader = true, testID = 'conversation' }) {
  const theme = useTheme();
  const property = useProperty();
  const { api, user } = useAuth();
  const meId = user ? user.id : null;
  const c = useConversation({ api, conversationId, user, onConversationChange, onRead });
  const blocker = useBlock(api);
  const [menuFor, setMenuFor] = useState(null);
  const [conversationMenu, setConversationMenu] = useState(false);
  const [replyTo, setReplyTo] = useState(null);
  const [editing, setEditing] = useState(null);
  const [confirmDelete, setConfirmDelete] = useState(null);
  const [confirmDeleteConversation, setConfirmDeleteConversation] = useState(false);
  const [original, setOriginal] = useState(null);
  const [report, setReport] = useState(null);
  const [notice, setNotice] = useState(null);
  const [busy, setBusy] = useState(false);
  const [actionError, setActionError] = useState(null);
  const reactions = reactionSet(property).items.slice(0, MENU_REACTIONS);
  const listRef = useRef(null);
  const nearNewest = useRef(true);
  const revealNewest = useRef(false);
  const newestKey = c.messages && c.messages.length ? newestMessageKey(c.messages) : null;

  // A sent message, or a new message while the newest one is in view, scrolls the inverted list to its start
  // (the bottom of the screen). FlashList otherwise keeps the visible content in place, which leaves a new message
  // below the visible area.
  useEffect(() => {
    if (!newestKey || !(revealNewest.current || nearNewest.current)) return undefined;
    revealNewest.current = false;
    const frame = requestAnimationFrame(() => {
      if (listRef.current && typeof listRef.current.scrollToOffset === 'function') listRef.current.scrollToOffset({ offset: 0, animated: true });
    });
    return () => cancelAnimationFrame(frame);
  }, [newestKey]);

  if (c.status === 'loading' && !c.conversation) return <Loading label={strings.common.loading} testID={testID + '-loading'} />;
  if (c.status === 'gone' || (c.status === 'error' && !c.conversation)) {
    return (
      <View style={{ padding: theme.spacing.lg }} testID={testID + '-error'}>
        <ErrorState title={c.status === 'gone' ? t.conversationGoneTitle : t.conversationLoadFailedTitle} message={c.status === 'gone' ? t.conversationGoneBody : describeMessagingError(c.error)} onRetry={c.status === 'gone' ? undefined : c.reload} />
      </View>
    );
  }

  const conversation = c.conversation;
  const other = otherMemberOf(conversation, meId);
  const name = conversationName(conversation, meId);
  const ownSeq = lastOwnSeq(c.messages, meId);
  const rows = messageRows(c.messages);
  const blockedText = cannotSendText(conversation);
  const pendingRequest = conversation.requestState === 'Pending';

  async function run(fn, done) {
    setActionError(null);
    setBusy(true);
    try {
      const result = await fn();
      if (done) done(result);
      return result;
    } catch (e) {
      setActionError(describeMessagingError(e));
      return null;
    } finally {
      setBusy(false);
    }
  }

  async function send(draft) {
    setReplyTo(null);
    revealNewest.current = true;
    Keyboard.dismiss();
    const result = await c.send(draft);
    if (!result.ok && result.error && !(result.error.status === 0 || result.error.status >= 500)) setActionError(describeMessagingError(result.error));
  }

  async function blockOther(thenReport) {
    if (!other) return;
    const result = await blocker.block(other);
    if (!result.ok) return;
    setNotice(strings.block.blocked(other.handle || other.displayName));
    if (thenReport) setReport({ targetType: 'Conversation', targetId: conversation.id, author: null, closeAfter: true });
    else if (onClosed) onClosed(conversation.id);
  }

  const messageMenu = (message) => {
    const viewer = message.viewer || {};
    const items = [];
    if (conversation.viewer && conversation.viewer.canSend !== false) items.push({ key: 'reply', label: t.reply, onPress: () => setReplyTo(message) });
    if (viewer.canReact !== false) {
      for (const r of reactions) items.push({ key: 'react-' + r.key, label: t.react(r.emoji ? r.emoji + ' ' + r.label : r.label), onPress: () => run(() => c.toggleReaction(message, r.key)) });
    }
    if (viewer.canEdit) items.push({ key: 'edit', label: t.edit, onPress: () => setEditing(message) });
    if (viewer.canDelete) items.push({ key: 'delete', label: t.delete, destructive: true, onPress: () => setConfirmDelete(message) });
    if (viewer.canReport) items.push({ key: 'report', label: t.report, destructive: true, onPress: () => setReport({ targetType: 'Message', targetId: message.id, author: message.author }) });
    return items;
  };

  const conversationItems = () => {
    const viewer = conversation.viewer || {};
    const items = [];
    if (!pendingRequest) {
      items.push(
        conversation.folder === 'Archived'
          ? { key: 'unarchive', label: t.unarchive, onPress: () => run(() => c.setFolder('Inbox')) }
          : { key: 'archive', label: t.archive, onPress: () => run(() => c.setFolder('Archived')) }
      );
    }
    items.push(conversation.isMuted ? { key: 'unmute', label: t.unmute, onPress: () => run(() => c.unmute()) } : { key: 'mute', label: t.mute, onPress: () => run(() => c.mute()) });
    if (viewer.canReport !== false) items.push({ key: 'report', label: t.reportConversation, destructive: true, onPress: () => setReport({ targetType: 'Conversation', targetId: conversation.id, author: other }) });
    if (viewer.canBlock !== false && other && !other.isStaff) items.push({ key: 'block', label: t.block, destructive: true, onPress: () => blockOther(false) });
    items.push({ key: 'delete', label: t.deleteConversation, destructive: true, onPress: () => setConfirmDeleteConversation(true) });
    return items;
  };

  const renderRow = ({ item }) => {
    if (item.type === 'day') {
      return (
        <AppText variant="caption" color="textMuted" style={{ textAlign: 'center', paddingVertical: theme.spacing.sm }} accessibilityRole="header" testID={'day-' + item.label}>
          {item.label}
        </AppText>
      );
    }
    const m = item.message;
    const mine = Boolean(m.author && meId && m.author.id === meId);
    const receipt = mine && (m.pending || (typeof m.seq === 'number' && m.seq === ownSeq)) ? receiptState(m, conversation) : null;
    return (
      <MessageBubble
        message={m}
        mine={mine}
        authorName={mine ? strings.support.you : name}
        receipt={receipt}
        attachmentsHidden={pendingRequest && !mine}
        onLongPress={() => setMenuFor(m)}
        onShowOriginal={() => setOriginal(m)}
        onRetry={() => c.retry(m.clientMessageId)}
        onDiscard={() => c.discard(m.clientMessageId)}
        onReactionsChange={(fields) => c.store.upsertMessage({ ...(c.store.getMessage(m.id) || m), ...fields })}
      />
    );
  };

  return (
    <KeyboardAvoider style={{ flex: 1 }} iosOffset={keyboardOffset} testID={testID}>
      {showHeader ? (
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.spacing.md, paddingHorizontal: theme.spacing.lg, paddingVertical: theme.spacing.sm, borderBottomWidth: 1, borderBottomColor: theme.colors.border }}>
          <Avatar uri={other && other.avatarUrl ? api.url(other.avatarUrl) : null} token={api.accessToken} name={name} size={36} />
          <View style={{ flex: 1 }}>
            <AppText variant="heading" accessibilityRole="header" testID={testID + '-name'}>
              {name}
            </AppText>
            {conversation.isMuted ? (
              <AppText variant="caption" color="textMuted" testID={testID + '-muted'}>
                {t.muted}
              </AppText>
            ) : null}
          </View>
          <IconButton icon="ellipsis-horizontal" label={t.conversationMenu} onPress={() => setConversationMenu(true)} testID={testID + '-menu'} />
        </View>
      ) : null}
      <View style={{ paddingHorizontal: theme.spacing.lg, gap: theme.spacing.sm }}>
        <RequestBanner
          conversation={conversation}
          name={name}
          busy={busy || blocker.busy}
          onAccept={() => run(() => c.accept(), () => setNotice(t.accepted))}
          onDecline={() =>
            run(
              () => c.decline(),
              () => {
                setNotice(t.declined);
                if (onClosed) onClosed(conversation.id);
              }
            )
          }
          onBlockAndReport={() => blockOther(true)}
        />
        <Notice tone="success" testID={testID + '-notice'}>
          {notice}
        </Notice>
        <Notice tone="error" testID={testID + '-action-error'}>
          {actionError || blocker.error}
        </Notice>
      </View>
      <View style={{ flex: 1 }}>
        <FlashList
          ref={listRef}
          testID={testID + '-messages'}
          inverted
          onScroll={(event) => {
            const y = event && event.nativeEvent && event.nativeEvent.contentOffset ? event.nativeEvent.contentOffset.y : 0;
            nearNewest.current = y <= NEWEST_THRESHOLD;
          }}
          scrollEventThrottle={64}
          data={rows}
          keyExtractor={(row) => row.key}
          getItemType={(row) => row.type}
          renderItem={renderRow}
          contentContainerStyle={{ paddingHorizontal: theme.spacing.lg, paddingVertical: theme.spacing.sm }}
          onEndReached={c.hasOlder ? c.loadOlder : undefined}
          onEndReachedThreshold={0.3}
          ListFooterComponent={c.hasOlder && rows.length ? <Button label={t.loadOlder} variant="ghost" onPress={c.loadOlder} disabled={c.olderBusy} testID={testID + '-load-older'} /> : null}
        />
      </View>
      {c.typing ? (
        <AppText variant="caption" color="textMuted" style={{ paddingHorizontal: theme.spacing.lg }} accessibilityLiveRegion="polite" testID={testID + '-typing'}>
          {t.typing(name)}
        </AppText>
      ) : null}
      <View style={{ paddingHorizontal: theme.spacing.lg, paddingBottom: theme.spacing.md }}>
        {blockedText ? (
          <AppText color="textMuted" style={{ paddingVertical: theme.spacing.md }} testID={testID + '-cannot-send'}>
            {blockedText}
          </AppText>
        ) : (
          <MessageComposer
            conversation={conversation}
            replyTo={replyTo}
            editing={editing}
            onSend={send}
            onTyping={c.typingChanged}
            onEdit={async (message, body) => {
              await c.edit(message, body);
              setEditing(null);
            }}
            onCancel={() => {
              setReplyTo(null);
              setEditing(null);
            }}
          />
        )}
      </View>

      <ActionMenu visible={Boolean(menuFor)} items={menuFor ? messageMenu(menuFor) : []} onClose={() => setMenuFor(null)} testID={testID + '-message-menu'} />
      <ActionMenu visible={conversationMenu} items={conversationMenu ? conversationItems() : []} onClose={() => setConversationMenu(false)} testID={testID + '-conversation-menu'} />
      <ConfirmSheet
        visible={Boolean(confirmDelete)}
        title={t.deleteTitle}
        body={t.deleteBody}
        confirmLabel={t.deleteConfirm}
        busy={busy}
        error={actionError}
        onClose={() => setConfirmDelete(null)}
        onConfirm={() => run(() => c.remove(confirmDelete), () => setConfirmDelete(null))}
        testID={testID + '-delete-confirm'}
      />
      <ConfirmSheet
        visible={confirmDeleteConversation}
        title={t.deleteConversationTitle}
        body={t.deleteConversationBody}
        confirmLabel={t.deleteConfirm}
        busy={busy}
        error={actionError}
        onClose={() => setConfirmDeleteConversation(false)}
        onConfirm={() =>
          run(
            () => c.deleteConversation(),
            () => {
              setConfirmDeleteConversation(false);
              if (onClosed) onClosed(conversation.id);
            }
          )
        }
        testID={testID + '-delete-conversation-confirm'}
      />
      <Sheet visible={Boolean(original)} title={t.originalTitle} onClose={() => setOriginal(null)} testID={testID + '-original'}>
        <AppText color="textMuted">{t.originalBody}</AppText>
        <AppText testID={testID + '-original-body'}>{original ? original.originalBody : ''}</AppText>
      </Sheet>
      {report ? (
        <ReportSheet
          target={{ targetType: report.targetType, targetId: report.targetId }}
          author={report.author}
          onClose={() => {
            const closeAfter = report.closeAfter;
            setReport(null);
            if (closeAfter && onClosed) onClosed(conversation.id);
          }}
          onBlocked={() => {
            if (onClosed) onClosed(conversation.id);
          }}
        />
      ) : null}
    </KeyboardAvoider>
  );
}
