import { useCallback, useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { formatAddress, formatEventShort, formatReminderOffset, isLockedItem, isPastEvent, joinUrlOf, lockedRequirement } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import Icon from '../../components/Icon.jsx';
import AvatarImage from '../../components/Avatar.jsx';
import Modal from '../../components/Modal.jsx';
import { Badge } from '../../components/Badges.jsx';
import { Banner, EmptyState, ErrorBanner, Loading } from '../../components/States.jsx';
import MarkdownBody from '../../components/feed/MarkdownBody.jsx';
import ReactionBar from '../../components/feed/ReactionBar.jsx';
import CommentThread from '../../components/feed/CommentThread.jsx';
import ReportDialog from '../../components/feed/ReportDialog.jsx';
import { AccessPromptCard } from '../../components/AccessPrompt.jsx';
import { PublicPageLink } from '../../components/PublicPageFields.jsx';
import { CategoryChip, EventRow, EventTime, RsvpControl } from '../../components/events/EventParts.jsx';
import useCursorList from '../../hooks/useCursorList.js';
import { authorName, errorText, mediaUrl, memberPath } from '../../utils/engagement.js';
import { appPaths, downloadText, phase4Errors } from '../../utils/phase4.js';
import { CheckInPanel } from '../ranks/CheckIn.jsx';

const t = strings.events;

function Avatar({ user, size }) {
  return <AvatarImage src={user && !user.isDeleted ? mediaUrl(user.avatarUrl) : null} name={authorName(user)} size={size} />;
}

function AttendeesDialog({ client, event, onClose }) {
  const fetchPage = useCallback((cursor, signal) => client.enumerateAttendees(event.id, { continuationToken: cursor, maxResults: 50 }, signal), [client, event.id]);
  const list = useCursorList(fetchPage);
  return (
    <Modal open size="md" onClose={onClose} title={t.attendees}>
      {list.loading && !list.loaded && <Loading />}
      <ErrorBanner message={list.error ? errorText(list.error, phase4Errors()) : ''} onRetry={list.refresh} />
      <ul className="history-list">
        {list.items.map((a, i) => (
          <li key={(a.user && a.user.id) || i} className="history-item">
            <Link to={memberPath(a.user.id)} className="member-cell"><Avatar user={a.user} size={28} /><span>{authorName(a.user)}</span></Link>
            {a.guestCount > 0 && <span className="muted small"> +{a.guestCount}</span>}
          </li>
        ))}
      </ul>
      {list.loaded && !list.items.length && <p className="muted">{t.attendeesEmpty}</p>}
      {list.loaded && !list.done && <button type="button" className="button button-secondary button-sm" onClick={list.loadMore}>{t.loadMore}</button>}
    </Modal>
  );
}

function Occurrences({ client, event }) {
  const fetcher = useCallback((signal) => client.enumerateEventOccurrences(event.id, { maxResults: 12, filters: { when: 'Upcoming' } }, signal), [client, event.id]);
  const query = useApiQuery(fetcher);
  const others = ((query.data && query.data.objects) || []).filter((e) => e.id !== event.id);
  return (
    <section className="panel" aria-labelledby="event-occurrences">
      <h2 id="event-occurrences" className="section-title">{t.occurrencesTitle}</h2>
      {query.loading && <Loading inline />}
      {query.data && !others.length && <p className="muted small">{t.occurrencesEmpty}</p>}
      <div className="p4-list">{others.map((e) => <EventRow key={e.id} event={e} />)}</div>
    </section>
  );
}

/**
 * /app/events/:id (sections 2.5, 4.3 to 4.5): time in the event's zone (and the viewer's), location
 * or online details with the join link when revealed, the RSVP control with capacity, waitlist
 * position, and guests, attendees, add to calendar (this date or the whole series), the
 * recurrence summary and other dates, reactions, comments, and report. Locked stubs prompt.
 */
function EventDetailView() {
  const { id } = useParams();
  const [searchParams] = useSearchParams();
  const focusId = searchParams.get('comment');
  const { client, me } = useAuth();
  const toast = useToast();
  const fetcher = useCallback((signal) => client.getEvent(id, signal), [client, id]);
  const query = useApiQuery(fetcher);
  const [override, setOverride] = useState(null);
  const [dialog, setDialog] = useState(null);
  const base = query.data;
  const event = base && override && override.id === base.id ? { ...base, ...override } : base;
  useDocumentMeta(strings.member.metaTitle(event ? event.title : t.title));
  const back = <Link to={appPaths.events} className="back-link small"><Icon name="chevronLeft" size={14} /><span>{t.back}</span></Link>;

  if (query.error && query.error.status === 404) return <div className="member-view">{back}<EmptyState icon="calendar" title={t.notFound}>{t.notFoundBody}</EmptyState></div>;
  if (!event) return <div className="member-view">{back}{query.loading && <Loading />}<ErrorBanner message={query.error ? errorText(query.error, phase4Errors(), t.loadError) : ''} onRetry={query.reload} /></div>;

  const locked = isLockedItem(event);
  const viewer = event.viewer || {};
  const canceled = event.status === 'Canceled';
  const loc = event.location || {};
  const join = joinUrlOf(event);
  const target = event.commentTarget || { type: 'Event', id: event.id };
  const ics = async (scope) => {
    try {
      const text = await client.downloadEventIcs(event.id, { scope });
      downloadText(text, (event.slug || event.id) + (scope === 'Series' ? '-series' : '') + '.ics', 'text/calendar');
      toast.success(t.icsSaved);
    } catch (err) {
      toast.error(errorText(err, phase4Errors()));
    }
  };

  return (
    <div className="member-view event-view">
      {back}
      <header className="page-header">
        <div className="page-header-text">
          <h1 style={canceled ? { textDecoration: 'line-through' } : undefined}>{event.title}</h1>
          {event.summary && <p className="muted">{event.summary}</p>}
          <span className="badge-row">
            <CategoryChip event={event} />
            <Badge tone="neutral">{t.formats[event.format] || event.format}</Badge>
            {canceled && <Badge tone="danger">{t.canceled}</Badge>}
            {event.recurrence && event.recurrence.isException && <Badge tone="info">{t.exception}</Badge>}
          </span>
          <PublicPageLink url={event.publicUrl} />
        </div>
      </header>
      {canceled && <Banner tone="danger" icon="alert">{t.canceledBanner(event.cancelReason)}</Banner>}
      {!canceled && isPastEvent(event) && <Banner tone="info">{t.past}</Banner>}

      <div className="p4-detail">
        <div className="p4-section">
          <section className="panel">
            <EventTime event={event} />
            {event.recurrence && <p className="small"><Icon name="refresh" size={12} /> {t.repeats}: {event.recurrence.summary}</p>}
            {event.format !== 'Online' && (
              <p>
                <Icon name="pin" size={14} />{' '}
                {loc.venue ? <Link to={appPaths.venue(loc.venue.slug || loc.venue.id)}>{loc.venue.name}</Link> : loc.name}
                {loc.address && <span className="muted"> {formatAddress(loc.address, loc.addressVisibility)}</span>}
              </p>
            )}
            {event.format !== 'InPerson' && !locked && (
              <p>
                <Icon name="link" size={14} /> {(event.online && event.online.platformLabel) || t.online}{' '}
                {join ? <a href={join} target="_blank" rel="noopener noreferrer" className="button button-primary button-sm">{t.join}</a>
                  : <span className="muted small">{event.online && event.online.joinUrlAvailableUtc ? t.joinAvailable(formatEventShort({ ...event, startUtc: event.online.joinUrlAvailableUtc })) : t.joinHidden}</span>}
              </p>
            )}
            {event.externalUrl && /^https:\/\//i.test(event.externalUrl) && <p><a href={event.externalUrl} target="_blank" rel="noopener noreferrer">{t.externalLink}</a></p>}
          </section>
          {locked ? <AccessPromptCard required={lockedRequirement(event)} feature="events" title={t.locked} body={t.lockedBody} /> : (
            event.description && <section className="panel"><MarkdownBody body={event.description} allowHeadings mentions={event.mentions} /></section>
          )}
          {!locked && (event.hosts || []).length > 0 && (
            <section className="panel" aria-labelledby="event-hosts">
              <h2 id="event-hosts" className="section-title">{t.hosts}</h2>
              <div className="badge-row">{event.hosts.map((h) => <Link key={h.id} to={memberPath(h.id)} className="member-cell"><Avatar user={h} size={28} /><span>{authorName(h)}</span></Link>)}</div>
            </section>
          )}
          {!locked && (event.instructors || []).length > 0 && (
            <p className="small">{t.instructors}: {event.instructors.map((i, n) => <span key={i.id}>{n > 0 ? ', ' : ''}<Link to={appPaths.instructor(i.slug || i.id)}>{i.displayName}</Link></span>)}</p>
          )}
        </div>
        <aside className="p4-detail-side">
          <section className="panel" aria-labelledby="event-rsvp">
            <h2 id="event-rsvp" className="section-title">{t.rsvpTitle}</h2>
            <p className="small">
              {event.capacity ? t.capacity(event.goingCount || 0, event.capacity) : t.goingCount(event.goingCount || 0)}
              {event.interestedCount > 0 && <> · {t.interestedCount(event.interestedCount)}</>}
            </p>
            {event.capacity && event.spotsLeft !== null && <p className="small">{event.spotsLeft > 0 ? t.spotsLeft(event.spotsLeft) : <Badge tone="warning">{t.full}</Badge>}{event.waitlistCount > 0 && <> · {t.waitlistCount(event.waitlistCount)}</>}</p>}
            {locked ? <p className="muted small">{t.lockedBody}</p> : <RsvpControl event={event} client={client} me={me} onChange={(updated) => setOverride(updated)} />}
          </section>
          {!locked && !canceled && <CheckInPanel targetType="Event" targetId={event.id} title={event.title} />}
          {!locked && viewer.canSeeAttendees && event.attendeePreview && (
            <section className="panel" aria-labelledby="event-attendees">
              <h2 id="event-attendees" className="section-title">{t.attendees}</h2>
              {(event.attendeePreview.objects || []).length === 0 ? <p className="muted small">{t.attendeesEmpty}</p> : (
                <div className="badge-row">{event.attendeePreview.objects.map((u) => <Avatar key={u.id} user={u} size={28} />)}</div>
              )}
              {event.attendeePreview.totalRecords > 0 && <button type="button" className="link-button small" onClick={() => setDialog('attendees')}>{t.attendeesAll}</button>}
            </section>
          )}
          {!locked && me && (
            <section className="panel" aria-labelledby="event-calendar">
              <h2 id="event-calendar" className="section-title">{t.addToCalendar}</h2>
              <div className="form-actions-start">
                <button type="button" className="button button-secondary button-sm" onClick={() => ics()}><Icon name="download" size={14} /><span>{t.downloadIcs}</span></button>
                {event.recurrence && <button type="button" className="button button-secondary button-sm" onClick={() => ics('Series')}><Icon name="download" size={14} /><span>{t.downloadSeriesIcs}</span></button>}
              </div>
              {(event.reminderOffsetsMinutes || []).length > 0 && <p className="muted small">{t.reminderOffsets(event.reminderOffsetsMinutes.map(formatReminderOffset).join(', '))}</p>}
            </section>
          )}
          {me && viewer.canReport !== false && <button type="button" className="link-button small" onClick={() => setDialog('report')}><Icon name="flag" size={12} /> {t.report}</button>}
        </aside>
      </div>
      {event.recurrence && !locked && <Occurrences client={client} event={event} />}
      {!locked && (
        <section className="p4-section" aria-labelledby={event.commentsEnabled === false ? 'event-comments' : 'comments-title'}>
          <footer className="post-foot">
            <ReactionBar target={target} value={event} canReact={Boolean(me) && viewer.canReact !== false} onChange={(fields) => setOverride({ ...event, ...fields })} />
          </footer>
          {event.commentsEnabled === false ? <><h2 id="event-comments">{t.comments}</h2><Banner tone="info">{t.commentsOff}</Banner></> : (
            <>
              {canceled && <Banner tone="info">{t.commentsCanceled}</Banner>}
              <CommentThread key={event.id + ':' + (focusId || '')} post={event} focusId={focusId} canComment={Boolean(me) && !canceled && viewer.canComment !== false} viewAllPath={appPaths.event(event.id)} accessFeature="events" />
            </>
          )}
        </section>
      )}
      {dialog === 'attendees' && <AttendeesDialog client={client} event={event} onClose={() => setDialog(null)} />}
      {dialog === 'report' && <ReportDialog target={{ type: 'Event', id: event.id }} onClose={() => setDialog(null)} />}
    </div>
  );
}

export default EventDetailView;
