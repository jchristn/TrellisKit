import { useCallback, useEffect } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { formatRelativeTime } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import { useNotifications } from '../../context/NotificationsContext.jsx';
import useCursorList from '../../hooks/useCursorList.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import useEnumerationUrlState from '../../hooks/useEnumerationUrlState.js';
import MetadataFilterBar from '../../components/MetadataFilterBar.jsx';
import Avatar from '../../components/Avatar.jsx';
import Icon from '../../components/Icon.jsx';
import { EmptyState, ErrorBanner, Loading } from '../../components/States.jsx';
import { authorName, errorText, mediaUrl, webPathFor } from '../../utils/engagement.js';
import { formatDateTime } from '../../utils/format.js';

const n = strings.notifications;
const KNOWN_TYPES = ['PostComment', 'CommentReply', 'Mention', 'Reaction', 'NewFeedPost', 'ModerationAction', 'ModerationOverdue', 'MembershipExpiring',
  'ThreadReply', 'NewThreadInForum', 'AnswerAccepted', 'FeedbackReviewed', 'PollClosed', 'ThreadPromoted', 'ThreadMoved',
  'SubmissionDecision', 'SubmissionOverdue', 'InstructorLinked', 'EventReminder', 'EventUpdated', 'EventCanceled', 'EventWaitlistPromoted', 'NewEvent', 'NewLessonInSeries', 'EventProposalDecision',
  'DirectMessage', 'MessageRequest', 'TicketUpdate', 'TicketOpened', 'TicketMemberReplied', 'TicketOverdue', 'IdeaStatusChanged', 'Broadcast', 'VenueReview', 'VenueReviewReply', 'ContentFlagged', 'MetricAlert'];
const TYPE_ICONS = { PostComment: 'message', CommentReply: 'reply', Mention: 'user', Reaction: 'smile', NewFeedPost: 'feed', ModerationAction: 'shield', ModerationOverdue: 'clock', MembershipExpiring: 'card',
  ThreadReply: 'reply', NewThreadInForum: 'forum', AnswerAccepted: 'check', FeedbackReviewed: 'video', PollClosed: 'poll', ThreadPromoted: 'feed', ThreadMoved: 'forum',
  SubmissionDecision: 'pin', SubmissionOverdue: 'clock', InstructorLinked: 'pin', EventReminder: 'calendar', EventUpdated: 'calendar', EventCanceled: 'calendar', EventWaitlistPromoted: 'calendar', NewEvent: 'calendar', NewLessonInSeries: 'book', EventProposalDecision: 'calendar',
  DirectMessage: 'message', MessageRequest: 'message', TicketUpdate: 'help', TicketOpened: 'help', TicketMemberReplied: 'help', TicketOverdue: 'clock', IdeaStatusChanged: 'trending', Broadcast: 'megaphone', VenueReview: 'star', VenueReviewReply: 'reply', ContentFlagged: 'shield', MetricAlert: 'bell' };

/** Notification center deep link for a notification, or null for unknown types (contract section 0.3). */
export function notificationHref(item) {
  if (!item || !KNOWN_TYPES.includes(item.type)) return null;
  return webPathFor(item.path);
}

function NotificationRow({ item, onOpen, onToggleRead }) {
  const href = notificationHref(item);
  const actor = item.actor || (item.actors && item.actors[0]) || null;
  const content = (
    <>
      <span className="notification-avatar">
        {actor ? <Avatar src={actor.isDeleted ? null : mediaUrl(actor.avatarUrl)} name={authorName(actor)} size={36} /> : <span className="notification-icon"><Icon name={TYPE_ICONS[item.type] || 'bell'} size={18} /></span>}
      </span>
      <span className="notification-text">
        <span className="notification-title">{item.title || n.unknownType}</span>
        {item.body && <span className="notification-body muted small">{item.body}</span>}
        <time className="muted small" dateTime={item.updatedUtc || item.createdUtc} title={formatDateTime(item.updatedUtc || item.createdUtc)}>{formatRelativeTime(item.updatedUtc || item.createdUtc)}</time>
      </span>
      {!item.isRead && <span className="unread-dot" aria-label={n.unreadDot} role="img" />}
    </>
  );
  return (
    <li className={'notification' + (item.isRead ? '' : ' unread')}>
      {href
        ? <a href={href} className="notification-link" onClick={(e) => { e.preventDefault(); onOpen(item, href); }}>{content}</a>
        : <div className="notification-link" role="group">{content}</div>}
      <button type="button" className="icon-button notification-toggle" onClick={() => onToggleRead(item)} aria-label={(item.isRead ? n.markUnread : n.markRead) + ': ' + (item.title || '')} title={item.isRead ? n.markUnread : n.markRead}>
        <Icon name={item.isRead ? 'eyeOff' : 'check'} size={14} />
      </button>
    </li>
  );
}

/**
 * /app/notifications: the notification center with all or unread filters, cursor paging, mark
 * read and unread, mark all read, and deep links (opening marks the notification read first).
 */
function NotificationsView() {
  const { client } = useAuth();
  const toast = useToast();
  const navigate = useNavigate();
  const { refresh: refreshCount, setUnreadCount } = useNotifications();
  useDocumentMeta(n.meta, n.summary);
  // The All/Unread filter lives in the URL (`?show=unread`) so it survives reloads and can be shared.
  const [searchParams, setSearchParams] = useSearchParams();
  const unreadOnly = searchParams.get('show') === 'unread';
  const setUnreadOnly = (on) => setSearchParams((prev) => {
    const next = new URLSearchParams(prev);
    if (on) next.set('show', 'unread');
    else next.delete('show');
    return next;
  }, { replace: true });
  const [meta, setMeta] = useEnumerationUrlState();
  const fetchPage = useCallback((cursor, signal) => client.listNotifications({ ...meta, continuationToken: cursor, maxResults: 30, filters: { unreadOnly } }, signal), [client, unreadOnly, meta]);
  const list = useCursorList(fetchPage);

  useEffect(() => {
    if (list.extra && typeof list.extra.unreadCount === 'number') setUnreadCount(list.extra.unreadCount);
  }, [list.extra, setUnreadCount]);
  useEffect(() => { refreshCount(); }, [refreshCount]);

  const setRead = (ids, isRead) => list.update((items) => items.map((x) => (ids.includes(x.id) ? { ...x, isRead, readUtc: isRead ? new Date().toISOString() : null } : x)));

  const open = async (item, href) => {
    if (!item.isRead) {
      setRead([item.id], true);
      try {
        const body = await client.markNotificationsRead([item.id]);
        if (body && typeof body.unreadCount === 'number') setUnreadCount(body.unreadCount);
      } catch {
        // Navigation matters more than the read mark.
      }
    }
    navigate(href);
  };

  const toggleRead = async (item) => {
    const next = !item.isRead;
    setRead([item.id], next);
    try {
      const body = next ? await client.markNotificationsRead([item.id]) : await client.markNotificationsUnread([item.id]);
      if (body && typeof body.unreadCount === 'number') setUnreadCount(body.unreadCount);
    } catch (err) {
      setRead([item.id], item.isRead);
      toast.error(errorText(err));
    }
  };

  const readAll = async () => {
    const before = new Date().toISOString();
    try {
      const body = await client.markAllNotificationsRead(before);
      list.update((items) => items.map((x) => ({ ...x, isRead: true })));
      setUnreadCount(body && typeof body.unreadCount === 'number' ? body.unreadCount : 0);
      toast.success(n.markedAllRead);
      if (unreadOnly) list.refresh();
    } catch (err) {
      toast.error(errorText(err));
    }
  };

  return (
    <div className="member-view notifications-view">
      <header className="page-header">
        <div className="page-header-text">
          <h1>{n.title}</h1>
          <p className="page-summary">{n.summary}</p>
        </div>
        <div className="page-header-actions">
          <Link to="/app/settings/notifications" className="button button-secondary button-sm"><Icon name="gear" size={14} /><span>{n.settings}</span></Link>
        </div>
      </header>
      <div className="row-between">
        <div className="segmented" role="radiogroup" aria-label={n.filterLabel}>
          <button type="button" role="radio" aria-checked={!unreadOnly} className={'segment' + (!unreadOnly ? ' active' : '')} onClick={() => setUnreadOnly(false)}>{n.all}</button>
          <button type="button" role="radio" aria-checked={unreadOnly} className={'segment' + (unreadOnly ? ' active' : '')} onClick={() => setUnreadOnly(true)}>{n.unread}</button>
        </div>
        <button type="button" className="button button-secondary button-sm" onClick={readAll} disabled={!list.items.some((x) => !x.isRead)}><Icon name="check" size={14} /><span>{n.markAllRead}</span></button>
      </div>
      <MetadataFilterBar value={meta} onChange={setMeta} client={client} targetType="Notification" showSearch orderings={[{ value: '', label: strings.metadata.orderings.UpdatedDescending }, { value: 'CreatedAscending', label: strings.metadata.orderings.CreatedAscending }]} />
      <ErrorBanner message={list.error ? n.loadError + ' ' + errorText(list.error, {}, '') : ''} onRetry={list.refresh} />
      {!list.loaded && list.loading && <Loading />}
      {list.loaded && !list.items.length && !list.loading && !list.error && <EmptyState icon="bell" title={unreadOnly ? n.emptyUnread : n.empty} />}
      {list.items.length > 0 && (
        <ul className="notification-list">
          {list.items.map((item) => <NotificationRow key={item.id} item={item} onOpen={open} onToggleRead={toggleRead} />)}
        </ul>
      )}
      {list.loaded && list.loading && <Loading inline />}
      {list.loaded && !list.done && !list.loading && <button type="button" className="button button-secondary" onClick={list.loadMore}>{n.loadMore}</button>}
    </div>
  );
}

export default NotificationsView;
