import { formatRelativeTime } from '@trelliskit/shared';
import { useCallback, useEffect, useRef, useState } from 'react';
import { ScrollView, View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { describeContentError } from '../../content/errors';
import { useCursorList } from '../../hooks/useCursorList';
import { composerError, useUploads } from '../../media/useUploads';
import { useMessaging } from '../../messaging/MessagingProvider';
import strings from '../../strings';
import { TicketLimits } from '../../support/support';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import Card from '../Card';
import ErrorState from '../ErrorState';
import Loading from '../Loading';
import Notice from '../Notice';
import TextField from '../TextField';
import AttachButtons from '../feed/AttachButtons';
import MarkdownBody from '../feed/MarkdownBody';
import MediaGallery from '../feed/MediaGallery';
import UploadTray from '../feed/UploadTray';
import { statusText } from './TicketRow';

const t = strings.support;

/** Member-facing text for a support error. */
export function describeTicketError(error) {
  return (error && error.code && t.errors[error.code]) || describeContentError(error);
}

function TicketMessage({ message }) {
  const theme = useTheme();
  const staff = message.authorRole === 'Staff';
  const author = staff ? t.staffName(message.author && message.author.displayName) : t.you;
  return (
    <View style={{ gap: theme.spacing.xs, padding: theme.spacing.md, borderRadius: theme.radii.md, backgroundColor: staff ? theme.colors.surface : 'transparent', borderWidth: staff ? 0 : 1, borderColor: theme.colors.border }} testID={'ticket-message-' + message.id}>
      <View style={{ flexDirection: 'row', gap: theme.spacing.sm }}>
        <AppText variant="label" style={{ flex: 1 }}>
          {author}
        </AppText>
        {message.createdUtc ? (
          <AppText variant="caption" color="textMuted">
            {formatRelativeTime(message.createdUtc)}
          </AppText>
        ) : null}
      </View>
      <MarkdownBody body={message.body || ''} />
      {message.attachments && message.attachments.length ? <MediaGallery attachments={message.attachments} compact /> : null}
    </View>
  );
}

/**
 * A support ticket (contract section 6.1): number, type, status, the request, the reply timeline
 * (staff replies show as the property's support team with the agent's name), a reply composer
 * with screenshots, and Close or Reopen from `viewer`. A `ticket.updated` socket hint reloads it.
 * @param {{ ticketId: string, onTicketChange?: (ticket: object) => void }} props
 */
export default function TicketDetail({ ticketId, onTicketChange, testID = 'ticket-detail' }) {
  const theme = useTheme();
  const { api, user } = useAuth();
  const { realtime } = useMessaging();
  const [ticket, setTicket] = useState(null);
  const [status, setStatus] = useState('loading');
  const [error, setError] = useState(null);
  const [reply, setReply] = useState('');
  const [busy, setBusy] = useState(false);
  const [actionError, setActionError] = useState(null);
  const [notice, setNotice] = useState(null);
  const uploads = useUploads({ api, purpose: 'Ticket' });
  const fetchPage = useCallback((continuationToken) => api.enumerateTicketMessages(ticketId, { ordering: 'CreatedAscending', maxResults: 50, continuationToken }), [api, ticketId]);
  const messages = useCursorList(fetchPage, { key: ticketId });

  const changeRef = useRef(onTicketChange);
  useEffect(() => {
    changeRef.current = onTicketChange;
  });
  const change = useCallback((next) => {
    setTicket(next);
    if (changeRef.current && next) changeRef.current(next);
  }, []);

  const load = useCallback(async () => {
    setError(null);
    try {
      change(await api.getTicket(ticketId));
      setStatus('ready');
    } catch (e) {
      setError(e);
      setStatus(e && e.status === 404 ? 'gone' : 'error');
    }
  }, [api, ticketId, change]);

  useEffect(() => {
    // load sets state after its request resolves.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    load();
  }, [load]);

  const reloadMessages = messages.reload;
  useEffect(() => {
    if (!realtime) return undefined;
    return realtime.on('ticket.updated', (data) => {
      if (data && data.ticketId === ticketId) {
        load();
        reloadMessages();
      }
    });
  }, [realtime, ticketId, load, reloadMessages]);

  if (status === 'loading') return <Loading label={strings.common.loading} testID={testID + '-loading'} />;
  if (status !== 'ready' || !ticket) {
    return (
      <View style={{ padding: theme.spacing.lg }} testID={testID + '-error'}>
        <ErrorState title={status === 'gone' ? t.goneTitle : t.detailLoadFailedTitle} message={status === 'gone' ? null : describeTicketError(error)} onRetry={status === 'gone' ? undefined : load} />
      </View>
    );
  }

  const viewer = ticket.viewer || {};
  const closed = ticket.status === 'Closed';

  async function act(fn, done) {
    setActionError(null);
    setNotice(null);
    setBusy(true);
    try {
      const result = await fn();
      if (done) done(result);
    } catch (e) {
      setActionError(describeTicketError(e));
    } finally {
      setBusy(false);
    }
  }

  function sendReply() {
    const body = reply.trim();
    if (!body && !uploads.mediaIds.length) return setActionError(t.replyTooShort);
    if (uploads.busy || uploads.failed) return setActionError(strings.composer.waitForUploads);
    return act(
      () => api.replyToTicket(ticketId, { body, mediaIds: uploads.mediaIds }),
      (message) => {
        setReply('');
        uploads.clear();
        if (message && message.id) messages.setItems((current) => (current.some((i) => i.id === message.id) ? current : current.concat(message)));
        load();
      }
    );
  }

  const type = ticket.type ? ticket.type.label || ticket.type.key : '';
  return (
    <ScrollView contentContainerStyle={{ padding: theme.spacing.lg, gap: theme.spacing.lg }} keyboardShouldPersistTaps="handled" testID={testID}>
      <View style={{ gap: theme.spacing.xs }}>
        <AppText variant="caption" color="textMuted" testID={testID + '-number'}>
          {t.ticketNumber(ticket.number) + (type ? ' · ' + type : '')}
        </AppText>
        <AppText variant="title" accessibilityRole="header" testID={testID + '-title'}>
          {ticket.title}
        </AppText>
        <AppText variant="label" testID={testID + '-status'}>
          {t.statusFilter + ': ' + statusText(ticket.status)}
        </AppText>
        {ticket.device ? (
          <AppText variant="caption" color="textMuted" testID={testID + '-device'}>
            {t.diagnosticsAttached}
          </AppText>
        ) : null}
      </View>
      {/* The opening text is also the first message (with its attachments); show ticket.body only once the messages loaded empty. */}
      {ticket.body && messages.status === 'ready' && messages.items.length === 0 ? (
        <Card testID={testID + '-body'}>
          <AppText variant="label">{user ? user.displayName : t.you}</AppText>
          <MarkdownBody body={ticket.body} />
        </Card>
      ) : null}
      {messages.hasMore ? <Button label={t.messagesLoadMore} variant="ghost" onPress={messages.loadMore} disabled={messages.loadingMore} testID={testID + '-more'} /> : null}
      <View style={{ gap: theme.spacing.sm }} testID={testID + '-messages'}>
        {messages.items.map((m) => (
          <TicketMessage key={m.id} message={m} />
        ))}
      </View>
      <Notice tone="success" testID={testID + '-notice'}>
        {notice}
      </Notice>
      <Notice tone="error" testID={testID + '-error-notice'}>
        {composerError(actionError, uploads, strings.composer.waitForUploads)}
      </Notice>
      {viewer.canReply !== false && !closed ? (
        <View style={{ gap: theme.spacing.sm }}>
          <TextField label={t.replyField} value={reply} onChangeText={setReply} multiline maxLength={TicketLimits.bodyMax} testID={testID + '-reply'} />
          <UploadTray uploads={uploads} testID={testID + '-uploads'} />
          {viewer.canAttach !== false ? <AttachButtons permissions={{ media: true, video: false }} remaining={TicketLimits.attachments - uploads.items.length} max={TicketLimits.attachments} videoAllowed={false} onPicked={uploads.add} testID={testID + '-attach'} /> : null}
          <Button label={t.replySend} onPress={sendReply} disabled={busy} testID={testID + '-send'} />
        </View>
      ) : closed ? (
        <AppText color="textMuted" testID={testID + '-closed'}>
          {t.closedNotice}
        </AppText>
      ) : null}
      <View style={{ gap: theme.spacing.sm }}>
        {viewer.canClose && !closed ? <Button label={t.close} accessibilityHint={t.closeHint} variant="secondary" onPress={() => act(() => api.closeTicket(ticketId), (next) => { change(next); setNotice(t.closed); })} disabled={busy} testID={testID + '-close'} /> : null}
        {viewer.canReopen ? <Button label={t.reopen} accessibilityHint={t.reopenHint} variant="secondary" onPress={() => act(() => api.reopenTicket(ticketId), (next) => { change(next); setNotice(t.reopened); })} disabled={busy} testID={testID + '-reopen'} /> : null}
      </View>
    </ScrollView>
  );
}
