import { useCallback, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { TicketStatus, canUseSupport, formatRelativeTime, normalizeEnumerationResult } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useCursorList from '../../hooks/useCursorList.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import useEnumerationUrlState, { activeMetaCount } from '../../hooks/useEnumerationUrlState.js';
import MetadataFilterBar from '../../components/MetadataFilterBar.jsx';
import MetadataEditor from '../../components/MetadataEditor.jsx';
import Icon from '../../components/Icon.jsx';
import { Badge } from '../../components/Badges.jsx';
import { EmptyState, ErrorBanner, Loading } from '../../components/States.jsx';
import AttachmentList from '../../components/feed/AttachmentList.jsx';
import { useMediaUploads } from '../../components/feed/useMediaUploads.js';
import { errorText } from '../../utils/engagement.js';
import { formatDateTime } from '../../utils/format.js';
import { supportEmailOf } from '../../utils/messaging.js';

const s = strings.support;
const f = s.form;
const PAGE_SIZE = 25;
const STATUSES = Object.values(TicketStatus);
const TITLE_MIN = 3;
const TITLE_MAX = 140;
const BODY_MIN = 10;
const BODY_MAX = 10000;
const MAX_ATTACHMENTS = 5;

/** Badge tone of a ticket status. */
export function ticketStatusTone(status) {
  if (status === TicketStatus.WaitingOnMember) return 'warning';
  if (status === TicketStatus.Resolved) return 'success';
  if (status === TicketStatus.Closed) return 'neutral';
  return 'info';
}

/** The route of a member ticket. */
export function ticketPath(id) {
  return '/app/support/tickets/' + encodeURIComponent(id);
}

/** Client checks before `POST /tickets` (contract 6.1). Returns `{ field: message }`. */
export function validateTicket({ type, title, body }) {
  const problems = {};
  if (!type) problems.type = f.errors.type;
  const tt = String(title || '').trim();
  if (tt.length < TITLE_MIN || tt.length > TITLE_MAX) problems.title = f.errors.title;
  const bb = String(body || '').trim();
  if (bb.length < BODY_MIN) problems.body = f.errors.body;
  else if (bb.length > BODY_MAX) problems.body = f.errors.bodyLong;
  return problems;
}

/** Shown in place of the ticket screens when `features.support` is off: the property's support email. */
export function SupportEmailCard() {
  const { property, fill } = useProperty();
  const email = supportEmailOf(property);
  return (
    <section className="panel support-email" aria-labelledby="support-email-title">
      <h2 id="support-email-title">{s.offTitle}</h2>
      {email ? <p>{s.offBody(email).split(email)[0]}<a href={'mailto:' + email}>{email}</a>{s.offBody(email).split(email)[1]}</p> : <p>{fill(s.offNoEmail)}</p>}
    </section>
  );
}

function SupportShell({ children, title = s.title, summary = s.summary, actions = null }) {
  return (
    <div className="member-view support-view">
      <header className="page-header">
        <div className="page-header-text">
          <h1>{title}</h1>
          <p className="page-summary">{summary}</p>
        </div>
        {actions && <div className="page-header-actions">{actions}</div>}
      </header>
      {children}
    </div>
  );
}

/**
 * /app/support: the member's support requests (status chips, search, label and tag filters, load
 * more) and the entry to a new request. Support is an account feature: every signed-in account
 * reaches it in every access model, including from the paywall (contract 0.2). When
 * `features.support` is off the page shows the property's support email instead.
 */
function SupportView() {
  const { client, me } = useAuth();
  const { property } = useProperty();
  useDocumentMeta(s.meta, s.summary);
  const [searchParams, setSearchParams] = useSearchParams();
  const status = STATUSES.includes(searchParams.get('status')) ? searchParams.get('status') : '';
  const [meta, setMeta, metaKey] = useEnumerationUrlState();
  const allowed = canUseSupport(property, me).allowed;
  const fetchPage = useCallback((cursor, signal) => {
    const parsed = JSON.parse(metaKey);
    return client.enumerateTickets({ search: parsed.search, labels: parsed.labels, labelMatch: parsed.labelMatch, tags: parsed.tags, maxResults: PAGE_SIZE, continuationToken: cursor, filters: status ? { status } : {} }, signal);
  }, [client, status, metaKey]);
  const list = useCursorList(fetchPage, { enabled: allowed });

  if (!allowed) return <SupportShell><SupportEmailCard /></SupportShell>;

  const setStatus = (value) => setSearchParams((prev) => {
    const next = new URLSearchParams(prev);
    if (value) next.set('status', value);
    else next.delete('status');
    return next;
  });
  const filtered = Boolean(status) || activeMetaCount(meta) > 0;
  return (
    <SupportShell actions={<Link to="/app/support/new" className="button button-primary"><Icon name="plus" size={14} /><span>{s.newTicket}</span></Link>}>
      <div className="chip-row" role="group" aria-label={s.statusFilter}>
        <button type="button" className={'chip-button' + (!status ? ' active' : '')} aria-pressed={!status} onClick={() => setStatus('')}>{s.allStatuses}</button>
        {STATUSES.map((value) => (
          <button key={value} type="button" className={'chip-button' + (status === value ? ' active' : '')} aria-pressed={status === value} onClick={() => setStatus(value)}>{s.statuses[value]}</button>
        ))}
      </div>
      <MetadataFilterBar value={meta} onChange={setMeta} client={client} targetType="Ticket" showSearch searchLabel={s.search} />
      <ErrorBanner message={list.error ? s.loadError : ''} onRetry={list.refresh} />
      {!list.loaded && list.loading && <Loading />}
      {list.loaded && !list.items.length && !list.loading && !list.error && (
        <EmptyState icon="help" title={filtered ? s.emptyFiltered : s.empty} action={filtered ? null : <Link to="/app/support/new" className="button button-secondary">{s.newTicket}</Link>} />
      )}
      {list.items.length > 0 && (
        <ul className="ticket-list panel" aria-label={s.listLabel}>
          {list.items.map((ticket) => (
            <li key={ticket.id} className="ticket-row">
              <Link to={ticketPath(ticket.id)} className="ticket-link">
                <span className="ticket-title">{ticket.title}</span>
                <span className="muted small">{s.number(ticket.number)} · {(ticket.type && ticket.type.label) || ''}</span>
              </Link>
              <span className="ticket-side">
                <Badge tone={ticketStatusTone(ticket.status)}>{s.statuses[ticket.status] || ticket.status}</Badge>
                {ticket.lastActivityUtc && <time className="muted small" dateTime={ticket.lastActivityUtc} title={formatDateTime(ticket.lastActivityUtc)}>{s.lastActivity(formatRelativeTime(ticket.lastActivityUtc))}</time>}
              </span>
            </li>
          ))}
        </ul>
      )}
      {list.loaded && list.loading && list.items.length > 0 && <Loading inline />}
      {list.loaded && !list.done && !list.loading && <button type="button" className="button button-secondary" onClick={list.loadMore}>{s.loadMore}</button>}
    </SupportShell>
  );
}

/**
 * /app/support/new: a new request with the configured ticket types (description and help text
 * shown for the chosen type), subject, description, screenshots, optional diagnostics (off by
 * default on the website, contract 6.1), and the caller's own labels and tags.
 */
export function NewTicketView() {
  const { client, me } = useAuth();
  const { property } = useProperty();
  const toast = useToast();
  const navigate = useNavigate();
  useDocumentMeta(f.meta);
  const allowed = canUseSupport(property, me).allowed;
  const typesFetcher = useCallback((signal) => client.enumerateTicketTypes({ maxResults: 100 }, signal), [client]);
  const types = useApiQuery(typesFetcher, { enabled: allowed });
  const typeList = types.data ? normalizeEnumerationResult(types.data).objects : [];
  const [form, setForm] = useState({ type: '', title: '', body: '', includeDiagnostics: false, labels: [], tags: {} });
  const [problems, setProblems] = useState({});
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const uploads = useMediaUploads({ client, maxCount: MAX_ATTACHMENTS, maxVideos: 0, allowMedia: true, allowVideo: false, onError: (msg) => toast.error(msg), purpose: 'Ticket' });

  if (!allowed) return <SupportShell title={f.title}><SupportEmailCard /></SupportShell>;

  const set = (key) => (e) => setForm((v) => ({ ...v, [key]: e && e.target ? (e.target.type === 'checkbox' ? e.target.checked : e.target.value) : e }));
  const selected = typeList.find((x) => x.key === form.type) || null;

  const submit = async (e) => {
    e.preventDefault();
    const found = validateTicket(form);
    setProblems(found);
    setError('');
    if (Object.keys(found).length || uploads.busy) return;
    setBusy(true);
    try {
      const ticket = await client.createTicket({ type: form.type, title: form.title.trim(), body: form.body.trim(), mediaIds: uploads.mediaIds, includeDiagnostics: form.includeDiagnostics, labels: form.labels, tags: form.tags });
      toast.success(f.created(ticket.number));
      navigate(ticketPath(ticket.id));
    } catch (err) {
      const fields = (err && err.body && err.body.fieldErrors) || {};
      setProblems(Object.fromEntries(Object.keys(fields).map((k) => [k, f.errors[k] || fields[k]])));
      setError(errorText(err, f.errors));
    } finally {
      setBusy(false);
    }
  };

  return (
    <SupportShell title={f.title} summary={s.summary}>
      <Link to="/app/support" className="back-link small"><Icon name="chevronLeft" size={14} /><span>{s.ticket.back}</span></Link>
      <form className="panel form-stack" onSubmit={submit} noValidate aria-label={f.title}>
        <ErrorBanner message={error} onDismiss={() => setError('')} />
        <ErrorBanner message={types.error ? f.typesError : ''} onRetry={types.reload} />
        <div className="field">
          <label className="label-text" htmlFor="ticket-type">{f.type}</label>
          <select id="ticket-type" className="select" value={form.type} onChange={set('type')} aria-invalid={Boolean(problems.type)}>
            <option value="">{f.typePlaceholder}</option>
            {typeList.map((tt) => <option key={tt.key} value={tt.key}>{tt.label || tt.key}</option>)}
          </select>
          {problems.type && <span className="field-error">{problems.type}</span>}
          {selected && selected.description && <span className="muted small">{selected.description}</span>}
        </div>
        {selected && selected.helpText && <div className="support-help small" role="note">{selected.helpText}</div>}
        <div className="field">
          <label className="label-text" htmlFor="ticket-title">{f.subject}</label>
          <input id="ticket-title" className="input" value={form.title} maxLength={TITLE_MAX} onChange={set('title')} aria-invalid={Boolean(problems.title)} />
          {problems.title && <span className="field-error">{problems.title}</span>}
        </div>
        <div className="field">
          <label className="label-text" htmlFor="ticket-body">{f.body}</label>
          <textarea id="ticket-body" className="textarea" rows={7} value={form.body} maxLength={BODY_MAX} onChange={set('body')} aria-invalid={Boolean(problems.body)} aria-describedby="ticket-body-hint" />
          <span id="ticket-body-hint" className="muted small">{f.bodyHint}</span>
          {problems.body && <span className="field-error">{problems.body}</span>}
        </div>
        <AttachmentList uploads={uploads} allowMedia allowVideo={false} maxCount={MAX_ATTACHMENTS} label={f.attachments} disabled={busy} />
        <label className="toggle-row">
          <input type="checkbox" checked={form.includeDiagnostics} onChange={set('includeDiagnostics')} />
          <span><span className="radio-label">{f.diagnostics}</span><span className="muted small">{f.diagnosticsBody}</span></span>
        </label>
        <MetadataEditor value={{ labels: form.labels, tags: form.tags }} onChange={(next) => setForm((v) => ({ ...v, labels: next.labels, tags: next.tags }))} client={client} targetType="Ticket" collapsible />
        <div className="form-actions">
          <button type="submit" className="button button-primary" disabled={busy || uploads.busy}>{busy ? f.submitting : f.submit}</button>
          <Link to="/app/support" className="button button-secondary">{f.cancel}</Link>
        </div>
      </form>
    </SupportShell>
  );
}

export default SupportView;
