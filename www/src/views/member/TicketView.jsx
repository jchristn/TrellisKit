import { useCallback, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { canUseSupport } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import { useRealtimeEvent } from '../../context/RealtimeContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useCursorList from '../../hooks/useCursorList.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import Icon from '../../components/Icon.jsx';
import { Badge } from '../../components/Badges.jsx';
import { Banner, EmptyState, ErrorBanner, Loading } from '../../components/States.jsx';
import MarkdownBody from '../../components/feed/MarkdownBody.jsx';
import MediaGallery from '../../components/feed/MediaGallery.jsx';
import AttachmentList from '../../components/feed/AttachmentList.jsx';
import { useMediaUploads } from '../../components/feed/useMediaUploads.js';
import { authorName, errorText } from '../../utils/engagement.js';
import { formatDateTime } from '../../utils/format.js';
import { SupportEmailCard, ticketStatusTone } from './SupportView.jsx';

const s = strings.support;
const k = s.ticket;
const MAX_ATTACHMENTS = 5;

function TimelineEntry({ author, staff, body, attachments, time }) {
  const name = staff ? k.staffName(author ? authorName(author) : '') : k.you;
  return (
    <li className={'ticket-entry' + (staff ? ' ticket-entry-staff' : ' ticket-entry-member')}>
      <div className="ticket-entry-head small"><strong>{name}</strong> <time className="muted" dateTime={time}>{formatDateTime(time)}</time></div>
      <MarkdownBody body={body || ''} allowHeadings={false} linkMentions={false} />
      {Array.isArray(attachments) && attachments.length > 0 && <MediaGallery items={attachments} compact />}
    </li>
  );
}

function ReplyForm({ ticketId, onReplied }) {
  const { client } = useAuth();
  const toast = useToast();
  const [body, setBody] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const uploads = useMediaUploads({ client, maxCount: MAX_ATTACHMENTS, maxVideos: 0, allowMedia: true, allowVideo: false, onError: (msg) => toast.error(msg), purpose: 'Ticket', scopeId: ticketId });
  const submit = async (e) => {
    e.preventDefault();
    if (!body.trim()) { setError(k.errors.empty); return; }
    setBusy(true);
    setError('');
    try {
      const message = await client.replyToTicket(ticketId, { body: body.trim(), mediaIds: uploads.mediaIds });
      setBody('');
      uploads.reset();
      toast.success(k.replied);
      onReplied(message);
    } catch (err) {
      setError(errorText(err, k.errors));
    } finally {
      setBusy(false);
    }
  };
  return (
    <form className="panel form-stack" onSubmit={submit} aria-label={k.replyLabel}>
      <ErrorBanner message={error} onDismiss={() => setError('')} />
      <label className="label-text" htmlFor="ticket-reply">{k.replyLabel}</label>
      <textarea id="ticket-reply" className="textarea" rows={4} maxLength={10000} value={body} placeholder={k.replyPlaceholder} onChange={(e) => setBody(e.target.value)} />
      <AttachmentList uploads={uploads} allowMedia allowVideo={false} maxCount={MAX_ATTACHMENTS} label={k.attachments} disabled={busy} />
      <div className="form-actions">
        <button type="submit" className="button button-primary" disabled={busy || uploads.busy}>{busy ? k.sending : k.send}</button>
      </div>
    </form>
  );
}

/**
 * /app/support/tickets/:id: one request with its timeline (the request, then staff and member
 * replies; internal notes never reach members), reply with screenshots, close and reopen from
 * `viewer`, and live refresh on `ticket.updated`.
 */
function TicketView() {
  const { id } = useParams();
  const { client, me } = useAuth();
  const { property } = useProperty();
  const toast = useToast();
  const allowed = canUseSupport(property, me).allowed;
  const fetcher = useCallback((signal) => client.getTicket(id, signal), [client, id]);
  const query = useApiQuery(fetcher, { enabled: allowed });
  const [override, setOverride] = useState(null);
  const ticket = override && query.data && override.id === query.data.id && override.updatedUtc >= (query.data.updatedUtc || '') ? override : query.data;
  useDocumentMeta(ticket ? s.number(ticket.number) + ' | {name}' : s.meta);
  const fetchPage = useCallback((cursor, signal) => client.enumerateTicketMessages(id, { ordering: 'CreatedAscending', maxResults: 50, continuationToken: cursor }, signal), [client, id]);
  const replies = useCursorList(fetchPage, { enabled: allowed });
  const [busy, setBusy] = useState(false);
  const { reload } = query;
  const { refresh } = replies;
  const onTicketEvent = useCallback((data) => {
    if (data && data.ticketId === id) {
      setOverride(null);
      reload();
      refresh();
    }
  }, [id, reload, refresh]);
  useRealtimeEvent('ticket.updated', onTicketEvent);

  if (!allowed) return <div className="member-view"><SupportEmailCard /></div>;
  if (query.error && query.error.status === 404) {
    return <div className="member-view"><EmptyState icon="help" title={k.notFound} action={<Link to="/app/support" className="button button-secondary">{k.back}</Link>} /></div>;
  }

  const act = async (fn, message) => {
    setBusy(true);
    try {
      const updated = await fn();
      setOverride({ ...updated, updatedUtc: updated.updatedUtc || new Date().toISOString() });
      toast.success(message);
    } catch (err) {
      toast.error(errorText(err, k.errors));
    } finally {
      setBusy(false);
    }
  };

  const viewer = (ticket && ticket.viewer) || {};
  const device = ticket && ticket.device;
  return (
    <div className="member-view support-view">
      <Link to="/app/support" className="back-link small"><Icon name="chevronLeft" size={14} /><span>{k.back}</span></Link>
      {query.loading && !ticket && <Loading />}
      <ErrorBanner message={query.error && query.error.status !== 404 ? k.loadError : ''} onRetry={query.reload} />
      {ticket && (
        <>
          <header className="page-header">
            <div className="page-header-text">
              <p className="muted small">{s.number(ticket.number)}</p>
              <h1>{ticket.title}</h1>
            </div>
            <div className="page-header-actions">
              {viewer.canClose && <button type="button" className="button button-secondary" onClick={() => act(() => client.closeTicket(id), k.closed)} disabled={busy}>{k.close}</button>}
              {viewer.canReopen && <button type="button" className="button button-secondary" onClick={() => act(() => client.reopenTicket(id), k.reopened)} disabled={busy}>{k.reopen}</button>}
            </div>
          </header>
          <dl className="kv-grid panel">
            <div className="kv-item"><dt>{k.status}</dt><dd><Badge tone={ticketStatusTone(ticket.status)}>{s.statuses[ticket.status] || ticket.status}</Badge></dd></div>
            <div className="kv-item"><dt>{k.type}</dt><dd>{(ticket.type && ticket.type.label) || ''}</dd></div>
            <div className="kv-item"><dt>{k.priority}</dt><dd>{s.priorities[ticket.priority] || ticket.priority}</dd></div>
            <div className="kv-item"><dt>{k.opened}</dt><dd>{formatDateTime(ticket.createdUtc)}</dd></div>
            {ticket.idea && ticket.idea.id && <div className="kv-item"><dt>{k.idea}</dt><dd><Link to={'/app/ideas/' + encodeURIComponent(ticket.idea.id)}>{ticket.idea.title || ticket.idea.id}</Link></dd></div>}
          </dl>
          {device && <p className="muted small"><Icon name="info" size={12} /> {k.device} {[device.client, device.clientVersion, device.platform, device.osVersion, device.deviceModel].filter(Boolean).join(', ')}</p>}
          <section aria-labelledby="ticket-timeline-title">
            <h2 id="ticket-timeline-title">{k.timeline}</h2>
            <ol className="ticket-timeline">
              {/* The opening text is the first reply (with its attachments); show ticket.body only when no reply has loaded. */}
              {replies.loaded && !replies.items.length && <TimelineEntry author={ticket.requester} staff={false} body={ticket.body} attachments={[]} time={ticket.createdUtc} />}
              {replies.items.map((m) => <TimelineEntry key={m.id} author={m.author} staff={m.authorRole === 'Staff'} body={m.body} attachments={m.attachments} time={m.createdUtc} />)}
            </ol>
            <ErrorBanner message={replies.error ? k.loadError : ''} onRetry={replies.refresh} />
            {replies.loaded && !replies.done && !replies.loading && <button type="button" className="button button-secondary button-sm" onClick={replies.loadMore}>{s.loadMore}</button>}
          </section>
          {viewer.canReply ? (
            <ReplyForm ticketId={id} onReplied={() => { setOverride(null); replies.refresh(); query.reload(); }} />
          ) : ticket.status === 'Closed' && <Banner tone="info" icon="info">{k.closedNote}</Banner>}
        </>
      )}
    </div>
  );
}

export default TicketView;
