import { loadDailyMetrics } from '../../utils/metricCollections.js';
import { useCallback, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { ENUMERATION_MAX_RESULTS_ADMIN, TicketMessageKind, TicketPriority, TicketStatus, formatRelativeTime, normalizeEnumerationResult } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useTableState from '../../hooks/useTableState.js';
import { errorMessage } from '../../utils/api.js';
import { normalizePage } from '../../utils/query.js';
import { ADMIN_PAGE_SIZES, pagedQuery } from '../../utils/adminQuery.js';
import { browser } from '../../utils/browser.js';
import { formatDateTime, formatNumber, formatShortDateTime } from '../../utils/format.js';
import { authorName, isOpeningTicketMessage } from '../../utils/engagement.js';
import PageHeader from '../../components/PageHeader.jsx';
import AdminFilterPanel from '../../components/AdminFilterPanel.jsx';
import DataTable from '../../components/DataTable.jsx';
import TablePagination from '../../components/TablePagination.jsx';
import KpiCard from '../../components/KpiCard.jsx';
import Modal from '../../components/Modal.jsx';
import ConfirmModal from '../../components/ConfirmModal.jsx';
import MetadataEditor from '../../components/MetadataEditor.jsx';
import Icon from '../../components/Icon.jsx';
import { Badge } from '../../components/Badges.jsx';
import { ErrorBanner, Loading } from '../../components/States.jsx';
import MarkdownBody from '../../components/feed/MarkdownBody.jsx';
import MediaGallery from '../../components/feed/MediaGallery.jsx';
import { ticketStatusTone } from '../member/SupportView.jsx';
import TicketTypesTab from './TicketTypesTab.jsx';
import EntityPicker, { memberItem, pickerSearches } from '../../components/admin/EntityPicker.jsx';

const s = strings.supportAdmin;
const d = s.detail;
const c = strings.admin.common;
const member = strings.support;

export const TICKET_FILTERS = Object.freeze(['search', 'status', 'type', 'priority', 'assigneeUserId', 'unassigned', 'overdue', 'hasIdea']);
export const TICKET_DEFAULT_SORT = '-lastActivityUtc';
const METRIC_DAYS = 30;

/** The admin ticket enumeration body from the table state (POST /admin/tickets/enumerate). */
export function buildTicketQuery(filters, paging) {
  return pagedQuery(filters, TICKET_FILTERS, paging, (rest) => {
    const out = { ...rest };
    for (const key of ['unassigned', 'overdue', 'hasIdea']) if (out[key]) out[key] = true;
    return out;
  });
}

function sumCounts(rows) {
  return (Array.isArray(rows) ? rows : []).reduce((total, row) => total + (Number(row && row.count) || 0), 0);
}

function priorityTone(priority) {
  if (priority === TicketPriority.Urgent) return 'danger';
  if (priority === TicketPriority.High) return 'warning';
  return 'neutral';
}

function MetricsTiles() {
  const { client } = useAdminAuth();
  const fetcher = useCallback((signal) => loadDailyMetrics((q, sig) => client.adminSupportMetrics(q, sig), { days: METRIC_DAYS }, signal), [client]);
  const query = useApiQuery(fetcher);
  const m = query.data;
  if (!m) return query.loading ? <Loading inline /> : null;
  return (
    <div className="kpi-grid" aria-label={strings.admin.routes.support.label}>
      <KpiCard label={s.metrics.opened(METRIC_DAYS)} value={formatNumber(sumCounts(m.openedByDay))} />
      <KpiCard label={s.metrics.resolved(METRIC_DAYS)} value={formatNumber(sumCounts(m.resolvedByDay))} tone="success" />
      <KpiCard label={s.metrics.firstResponse} value={s.metrics.hours(m.firstResponseMedianHours)} />
      <KpiCard label={s.metrics.overdue} value={formatNumber(m.overdueNow || 0)} tone={m.overdueNow ? 'danger' : undefined} />
    </div>
  );
}

function PublishIdeaDialog({ ticket, onClose, onDone }) {
  const { client } = useAdminAuth();
  const toast = useToast();
  const [title, setTitle] = useState(ticket.title || '');
  const [body, setBody] = useState(ticket.body || '');
  const [showRequester, setShowRequester] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const submit = async (e) => {
    e.preventDefault();
    setBusy(true);
    try {
      await client.adminPublishIdeaFromTicket(ticket.id, { title: title.trim(), body: body.trim(), showRequester });
      toast.success(d.published);
      onDone();
    } catch (err) {
      setError(errorMessage(err, c.saved));
      setBusy(false);
    }
  };
  return (
    <Modal open onClose={onClose} title={d.publishTitle}>
      <form className="form-stack" onSubmit={submit}>
        <ErrorBanner message={error} onDismiss={() => setError('')} />
        <div className="field">
          <label className="label-text" htmlFor="publish-title">{d.publishName}</label>
          <input id="publish-title" value={title} maxLength={140} onChange={(e) => setTitle(e.target.value)} />
        </div>
        <div className="field">
          <label className="label-text" htmlFor="publish-body">{d.publishBody}</label>
          <textarea id="publish-body" className="textarea" rows={5} value={body} onChange={(e) => setBody(e.target.value)} />
        </div>
        <label className="checkbox-row"><input type="checkbox" checked={showRequester} onChange={(e) => setShowRequester(e.target.checked)} /><span>{d.showRequester}</span></label>
        <div className="form-actions">
          <button type="submit" className="button button-primary" disabled={busy}>{d.publish}</button>
          <button type="button" className="button button-secondary" onClick={onClose}>{strings.common.cancel}</button>
        </div>
      </form>
    </Modal>
  );
}

function TimelineItem({ message }) {
  const note = message.kind === TicketMessageKind.Note;
  // System lines (status, priority, type, and assignment changes) have no author: show them as a timeline event.
  if (message.kind === TicketMessageKind.System) {
    return (
      <li className="ticket-entry ticket-entry-system muted small" data-kind={message.kind}>
        <Icon name="info" size={12} /> <span>{message.body}</span>{' '}
        <time dateTime={message.createdUtc}>{formatDateTime(message.createdUtc)}</time>
      </li>
    );
  }
  return (
    <li className={'ticket-entry' + (note ? ' ticket-note' : message.authorRole === 'Staff' ? ' ticket-entry-staff' : ' ticket-entry-member')} data-kind={message.kind}>
      <div className="ticket-entry-head small">
        {note && <Badge tone="warning"><Icon name="lock" size={10} /> {d.note}</Badge>}
        <strong>{authorName(message.author)}</strong>
        <span className="muted">{message.authorRole === 'Staff' ? d.staff : d.member}</span>
        <time className="muted" dateTime={message.createdUtc}>{formatDateTime(message.createdUtc)}</time>
      </div>
      <MarkdownBody body={message.body || ''} allowHeadings={false} linkMentions={false} />
      {Array.isArray(message.attachments) && message.attachments.length > 0 && <MediaGallery items={message.attachments} compact />}
    </li>
  );
}

function Composer({ ticketId, onSent }) {
  const { client } = useAdminAuth();
  const toast = useToast();
  const [kind, setKind] = useState(TicketMessageKind.Reply);
  const [body, setBody] = useState('');
  const [setStatus, setSetStatus] = useState('');
  const [busy, setBusy] = useState(false);
  const submit = async (e) => {
    e.preventDefault();
    if (!body.trim()) return;
    setBusy(true);
    try {
      await client.adminPostTicketMessage(ticketId, { kind, body: body.trim(), setStatus: kind === TicketMessageKind.Reply ? setStatus || undefined : undefined });
      setBody('');
      toast.success(d.sent);
      onSent();
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
    } finally {
      setBusy(false);
    }
  };
  return (
    <form className={'form-stack ticket-composer' + (kind === TicketMessageKind.Note ? ' ticket-composer-note' : '')} onSubmit={submit}>
      <div className="segmented" role="radiogroup" aria-label={d.composerKind}>
        {[TicketMessageKind.Reply, TicketMessageKind.Note].map((k) => (
          <button key={k} type="button" role="radio" aria-checked={kind === k} className={'segment' + (kind === k ? ' active' : '')} onClick={() => setKind(k)}>{d.composerKinds[k]}</button>
        ))}
      </div>
      {kind === TicketMessageKind.Note && <p className="muted small">{d.noteHint}</p>}
      <label className="label-text" htmlFor="ticket-admin-body">{d.composerBody}</label>
      <textarea id="ticket-admin-body" className="textarea" rows={4} value={body} onChange={(e) => setBody(e.target.value)} />
      {kind === TicketMessageKind.Reply && (
        <div className="field">
          <label className="label-text" htmlFor="ticket-admin-setstatus">{d.setStatus}</label>
          <select id="ticket-admin-setstatus" value={setStatus} onChange={(e) => setSetStatus(e.target.value)}>
            <option value="">{d.keepDefault}</option>
            {Object.values(TicketStatus).map((st) => <option key={st} value={st}>{s.statuses[st]}</option>)}
          </select>
        </div>
      )}
      <div className="form-actions"><button type="submit" className="button button-primary button-sm" disabled={busy || !body.trim()}>{d.send}</button></div>
    </form>
  );
}

/** Ticket drawer: requester, membership, device, workflow fields, timeline (notes distinct), reply or note, ideas, metadata, delete. */
/** Page size for the full ticket history (the ticket read embeds only the newest 20). */
export const TICKET_HISTORY_PAGE = 50;

/**
 * The timeline of a ticket drawer: the loaded history pages (oldest first, from
 * `POST /admin/tickets/{id}/messages/enumerate` with continuation) followed by the embedded newest
 * preview, without duplicates. `hidden` is how many messages are still between the two.
 * @param {{ objects: object[], totalRecords: number|null }|null} preview
 * @param {object[]} history
 */
export function mergeTicketTimeline(preview, history) {
  const tail = preview ? preview.objects || [] : [];
  const seen = new Set();
  const items = [];
  for (const m of [...(history || []), ...tail]) {
    if (!m || !m.id || seen.has(m.id)) continue;
    seen.add(m.id);
    items.push(m);
  }
  const total = preview && Number.isFinite(Number(preview.totalRecords)) ? Number(preview.totalRecords) : items.length;
  const firstTail = tail.length ? tail[0].id : null;
  const splitAt = history && history.length ? items.findIndex((m) => m.id === firstTail) : 0;
  return { items, hidden: Math.max(0, total - items.length), splitAt: splitAt < 0 ? items.length : splitAt };
}

function TicketDrawer({ id, types, onClose, onChanged }) {
  const { client, me, isSystemAdmin } = useAdminAuth();
  const toast = useToast();
  const searches = useMemo(() => pickerSearches(client, { isSystemAdmin }), [client, isSystemAdmin]);
  const fetcher = useCallback((signal) => client.adminGetTicket(id, signal), [client, id]);
  const query = useApiQuery(fetcher);
  const [assignee, setAssignee] = useState(null);
  const [meta, setMeta] = useState(null);
  const [dialog, setDialog] = useState(null);
  const [ideaPick, setIdeaPick] = useState([]);
  const ideaId = ideaPick[0] ? ideaPick[0].id : '';
  const [history, setHistory] = useState({ items: [], token: null, loading: false, error: false });
  const t = query.data;
  const loadEarlier = async () => {
    setHistory((h) => ({ ...h, loading: true, error: false }));
    try {
      const page = normalizeEnumerationResult(await client.adminEnumerateTicketMessages(id, { maxResults: TICKET_HISTORY_PAGE, continuationToken: history.token || undefined }));
      setHistory((h) => ({ items: [...h.items, ...page.objects], token: page.endOfResults ? null : page.continuationToken, loading: false, error: false }));
    } catch {
      setHistory((h) => ({ ...h, loading: false, error: true }));
    }
  };

  const update = async (body) => {
    try {
      await client.adminUpdateTicket(id, body);
      toast.success(d.updated);
      query.reload();
      onChanged();
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
    }
  };
  const linkIdea = async (e) => {
    e.preventDefault();
    if (!ideaId) return;
    try {
      await client.adminLinkIdea(id, ideaId);
      toast.success(d.linked);
      setIdeaPick([]);
      query.reload();
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
    }
  };
  const remove = async () => {
    try {
      await client.adminDeleteTicket(id);
      toast.success(d.deleted);
      onChanged();
      onClose();
    } catch (err) {
      setDialog(null);
      toast.error(errorMessage(err, strings.common.errorGeneric));
    }
  };

  const messages = t ? normalizeEnumerationResult(t.messages || {}) : null;
  const timeline = mergeTicketTimeline(messages, history.items);
  // The opening message is shown once, as the original request (with its attachments).
  const opening = timeline.items.find((m) => isOpeningTicketMessage(t, m)) || null;
  const assigneePick = assignee !== null ? assignee : (t && t.assignee ? [memberItem(t.assignee)].filter(Boolean) : []);
  const assigneeValue = assigneePick[0] ? assigneePick[0].id : '';
  const membership = t && t.membership;
  const device = t && t.device;
  return (
    <Modal open variant="drawer" size="lg" onClose={onClose} title={t ? d.title(t.number) : strings.common.loading} subtitle={t ? t.title : null}>
      {query.loading && !t && <Loading />}
      {query.error && <ErrorBanner message={errorMessage(query.error, '')} onRetry={query.reload} />}
      {t && (
        <div className="detail-stack">
          <section className="detail-block">
            <dl className="kv-grid kv-grid-wide">
              <div className="kv-item"><dt>{d.requester}</dt><dd>{t.requester ? (t.requester.isDeleted ? d.deletedMember : authorName(t.requester)) : d.deletedMember}{t.requester && t.requester.email ? ' (' + t.requester.email + ')' : ''}</dd></div>
              <div className="kv-item"><dt>{d.membership}</dt><dd>{membership ? (membership.isActive ? d.membershipActive([membership.source, membership.store].filter(Boolean).join(', ')) : d.membershipInactive) : '-'}</dd></div>
              <div className="kv-item"><dt>{d.device}</dt><dd>{device ? [device.client, device.clientVersion, device.platform, device.osVersion, device.deviceModel].filter(Boolean).join(', ') : d.noDevice}</dd></div>
            </dl>
            <div className="form-grid">
              <div className="field">
                <label className="label-text" htmlFor="tk-status">{d.status}</label>
                <select id="tk-status" value={t.status} onChange={(e) => update({ status: e.target.value })}>
                  {Object.values(TicketStatus).map((st) => <option key={st} value={st}>{s.statuses[st]}</option>)}
                </select>
              </div>
              <div className="field">
                <label className="label-text" htmlFor="tk-priority">{d.priority}</label>
                <select id="tk-priority" value={t.priority} onChange={(e) => update({ priority: e.target.value })}>
                  {Object.values(TicketPriority).map((p) => <option key={p} value={p}>{member.priorities[p]}</option>)}
                </select>
              </div>
              <div className="field">
                <label className="label-text" htmlFor="tk-type">{d.type}</label>
                <select id="tk-type" value={(t.type && t.type.key) || ''} onChange={(e) => update({ type: e.target.value })}>
                  {types.map((tt) => <option key={tt.key} value={tt.key}>{tt.label || tt.key}</option>)}
                  {t.type && !types.some((tt) => tt.key === t.type.key) && <option value={t.type.key}>{t.type.label || t.type.key}</option>}
                </select>
              </div>
              <div className="field">
                <EntityPicker id="tk-assignee" label={d.assignee} value={assigneePick} onChange={setAssignee} search={searches.members} placeholder={strings.admin.eventsAdmin.memberPlaceholder} />
                <div className="row-actions">
                  <button type="button" className="button button-secondary button-sm" onClick={() => update({ assigneeUserId: assigneeValue || null })}>{d.saveAssignee}</button>
                  {me && <button type="button" className="button button-secondary button-sm" onClick={() => { setAssignee([memberItem(me)].filter(Boolean)); update({ assigneeUserId: me.id }); }}>{d.assignMe}</button>}
                  {t.assignee && <button type="button" className="button button-secondary button-sm" onClick={() => { setAssignee([]); update({ assigneeUserId: null }); }}>{d.unassign}</button>}
                </div>
              </div>
            </div>
          </section>

          <section className="detail-block" aria-labelledby="tk-timeline-title">
            <h3 id="tk-timeline-title">{d.timeline}</h3>
            <ol className="ticket-timeline">
              <li className="ticket-entry ticket-entry-member">
                <div className="ticket-entry-head small"><strong>{d.original}</strong> <time className="muted" dateTime={t.createdUtc}>{formatDateTime(t.createdUtc)}</time></div>
                <MarkdownBody body={t.body || ''} allowHeadings={false} linkMentions={false} />
                {opening && Array.isArray(opening.attachments) && opening.attachments.length > 0 && <MediaGallery items={opening.attachments} compact />}
              </li>
              {timeline.items.slice(0, timeline.splitAt).filter((m) => m !== opening).map((m) => <TimelineItem key={m.id} message={m} />)}
              {timeline.hidden > 0 && (
                <li className="ticket-entry-more">
                  <span className="muted small">{d.olderHidden(timeline.hidden)}</span>{' '}
                  <button type="button" className="button button-secondary button-sm" onClick={loadEarlier} disabled={history.loading}>{history.loading ? strings.common.loading : d.loadEarlier}</button>
                  {history.error && <span className="field-error" role="alert"> {d.loadEarlierError}</span>}
                </li>
              )}
              {timeline.items.slice(timeline.splitAt).filter((m) => m !== opening).map((m) => <TimelineItem key={m.id} message={m} />)}
            </ol>
            <Composer ticketId={id} onSent={() => { query.reload(); onChanged(); }} />
          </section>

          <section className="detail-block" aria-labelledby="tk-idea-title">
            <h3 id="tk-idea-title">{d.ideaTitle}</h3>
            {t.idea ? <p>{d.linkedIdea(t.idea.title || t.idea.id)}</p> : (
              <>
                <button type="button" className="button button-secondary button-sm" onClick={() => setDialog('publish')}>{d.publish}</button>
                <form className="inline-form" onSubmit={linkIdea}>
                  <EntityPicker id="tk-link-idea" label={d.linkIdeaId} value={ideaPick} onChange={setIdeaPick} search={searches.ideas} placeholder={d.ideaPlaceholder} />
                  <button type="submit" className="button button-secondary button-sm">{d.linkIdea}</button>
                </form>
              </>
            )}
          </section>

          <section className="detail-block">
            <h3>{d.metadata}</h3>
            <MetadataEditor value={meta || { labels: t.labels || [], tags: t.tags || {} }} onChange={(next) => setMeta(next)} isStaff client={client} targetType="Ticket" />
            <button type="button" className="button button-secondary button-sm" disabled={!meta} onClick={() => update({ labels: meta.labels, tags: meta.tags })}>{d.saveMetadata}</button>
          </section>

          {t.status === TicketStatus.Closed && (
            <div className="row-actions"><button type="button" className="button button-danger-outline button-sm" onClick={() => setDialog('delete')}>{d.delete}</button></div>
          )}
        </div>
      )}
      {dialog === 'publish' && t && <PublishIdeaDialog ticket={t} onClose={() => setDialog(null)} onDone={() => { setDialog(null); query.reload(); onChanged(); }} />}
      <ConfirmModal open={dialog === 'delete'} title={d.deleteTitle} message={d.deleteBody} confirmLabel={d.delete} onConfirm={remove} onCancel={() => setDialog(null)} />
    </Modal>
  );
}

function InboxTab() {
  const { client, isSystemAdmin } = useAdminAuth();
  const toast = useToast();
  const [searchParams, setSearchParams] = useSearchParams();
  const table = useTableState({ storageKey: 'tk.admin.tickets', filterKeys: TICKET_FILTERS, defaultSort: TICKET_DEFAULT_SORT });
  const query = useMemo(() => buildTicketQuery(table.filters, { pageNumber: table.pageNumber, pageSize: table.pageSize, sort: table.sort, meta: table.meta }), [table.filters, table.pageNumber, table.pageSize, table.sort, table.meta]);
  const fetcher = useCallback((signal) => client.adminEnumerateTickets(query, signal), [client, query]);
  const list = useApiQuery(fetcher);
  // Ticket type administration is SystemAdmin only; support agents read the enabled types from the member route.
  const typesFetcher = useCallback((signal) => (isSystemAdmin
    ? client.adminEnumerateTicketTypes({ maxResults: ENUMERATION_MAX_RESULTS_ADMIN }, signal)
    : client.enumerateTicketTypes({ maxResults: ENUMERATION_MAX_RESULTS_ADMIN }, signal)), [client, isSystemAdmin]);
  const typesQuery = useApiQuery(typesFetcher);
  const types = typesQuery.data ? normalizeEnumerationResult(typesQuery.data).objects : [];
  const [exporting, setExporting] = useState(false);
  const page = list.data ? normalizePage(list.data, table.pageSize) : null;
  const rows = page ? page.items : [];
  const open = searchParams.get('ticket');
  const setOpen = (id) => {
    const next = new URLSearchParams(searchParams);
    if (id) next.set('ticket', id);
    else next.delete('ticket');
    setSearchParams(next);
  };

  const exportCsv = async () => {
    setExporting(true);
    try {
      const text = await client.adminExportTickets(query);
      browser.download(text || '', 'tickets.csv', 'text/csv');
      toast.success(s.exported);
    } catch (err) {
      toast.error(s.exportError + ' ' + errorMessage(err, ''));
    } finally {
      setExporting(false);
    }
  };

  const columns = [
    { key: 'number', label: s.columns.number, pinned: true, sortField: 'number', render: (row) => <span className="mono">#{row.number}</span> },
    {
      key: 'title', label: s.columns.title, render: (row) => (
        <span className="cell-stack"><span>{row.title}</span><span className="muted small">{row.requester ? authorName(row.requester) : d.deletedMember}</span></span>
      )
    },
    { key: 'type', label: s.columns.type, render: (row) => (row.type && row.type.label) || '' },
    { key: 'status', label: s.columns.status, render: (row) => <Badge tone={ticketStatusTone(row.status)}>{s.statuses[row.status] || row.status}</Badge> },
    { key: 'priority', label: s.columns.priority, sortField: 'priority', sortDefault: 'desc', render: (row) => <Badge tone={priorityTone(row.priority)}>{member.priorities[row.priority] || row.priority}</Badge> },
    { key: 'assignee', label: s.columns.assignee, render: (row) => (row.assignee ? authorName(row.assignee) : <span className="muted">{s.unassigned}</span>) },
    { key: 'activity', label: s.columns.activity, sortField: 'lastActivityUtc', sortDefault: 'desc', className: 'nowrap', render: (row) => <span title={formatDateTime(row.lastActivityUtc)}>{formatRelativeTime(row.lastActivityUtc)}</span> },
    { key: 'due', label: s.columns.due, className: 'nowrap', render: (row) => (row.isOverdue ? <Badge tone="danger"><Icon name="clock" size={12} /> {s.overdue}</Badge> : <span>{row.firstResponseUtc ? formatShortDateTime(row.firstResponseUtc) : row.firstResponseDueUtc ? formatShortDateTime(row.firstResponseDueUtc) : '-'}</span>) }
  ];
  const statusOptions = Object.values(TicketStatus).map((v) => ({ value: v, label: s.statuses[v] }));
  const filterFields = [
    { key: 'search', label: s.filters.search },
    { key: 'status', label: s.filters.status, type: 'select', options: statusOptions },
    { key: 'type', label: s.filters.type, type: 'select', options: types.map((tt) => ({ value: tt.key, label: tt.label || tt.key })) },
    { key: 'priority', label: s.filters.priority, type: 'select', options: Object.values(TicketPriority).map((v) => ({ value: v, label: member.priorities[v] })) },
    { key: 'assigneeUserId', label: s.filters.assignee, placeholder: 'usr_...', mono: true },
    { key: 'unassigned', label: s.filters.unassigned, type: 'checkbox' },
    { key: 'overdue', label: s.filters.overdue, type: 'checkbox' },
    { key: 'hasIdea', label: s.filters.hasIdea, type: 'checkbox' }
  ];

  return (
    <>
      <MetricsTiles />
      <section className="panel">
        <AdminFilterPanel fields={filterFields} table={table} client={client} targetType="Ticket" />
      </section>
      <section className="table-section">
        <div className="row-between">
          <TablePagination pageNumber={table.pageNumber} pageSize={table.pageSize} totalCount={page ? page.totalCount : 0} totalPages={page ? page.totalPages : 1} onPageChange={table.setPage} onPageSizeChange={table.setPageSize} onRefresh={list.reload} loading={list.loading} pageSizes={ADMIN_PAGE_SIZES} />
          <button type="button" className="button button-secondary button-sm" onClick={exportCsv} disabled={exporting}><Icon name="download" size={14} /><span>{exporting ? s.exporting : s.exportCsv}</span></button>
        </div>
        <DataTable
          caption={strings.admin.routes.support.label}
          columns={columns}
          rows={rows}
          rowKey={(row) => row.id}
          loading={list.loading}
          error={list.error ? c.unavailable(strings.admin.routes.support.label) + ' ' + errorMessage(list.error, '') : null}
          onRetry={list.reload}
          emptyMessage={table.hasFilters ? s.emptyFiltered : s.empty}
          onRowClick={(row) => setOpen(row.id)}
          rowClassName={(row) => (row.isOverdue ? 'row-overdue' : '')}
          sort={table.sort}
          onSortChange={table.setSort}
        />
      </section>
      {open && <TicketDrawer key={open} id={open} types={types} onClose={() => setOpen(null)} onChanged={list.reload} />}
    </>
  );
}

/**
 * /admin/support (SystemAdmin, SupportAgent): metrics tiles, the ticket inbox (filters, label and
 * tag filters, sort, CSV export) with the ticket drawer (assignment, priority, status, replies,
 * internal notes in a distinct style, publish to ideas and link an idea, metadata, delete closed
 * tickets), and the ticket types list for SystemAdmin.
 */
function SupportInboxView() {
  const { isSystemAdmin } = useAdminAuth();
  const [searchParams, setSearchParams] = useSearchParams();
  const tabs = ['inbox', ...(isSystemAdmin ? ['types'] : [])];
  const tab = tabs.includes(searchParams.get('tab')) ? searchParams.get('tab') : 'inbox';
  const select = (id) => setSearchParams(id === 'inbox' ? {} : { tab: id });
  return (
    <div className="view">
      <PageHeader title={strings.admin.routes.support.label} summary={strings.admin.routes.support.summary} />
      {tabs.length > 1 && (
        <div className="segmented tab-bar" role="tablist" aria-label={s.tabsLabel}>
          {tabs.map((id) => (
            <button key={id} type="button" role="tab" aria-selected={tab === id} className={'segment' + (tab === id ? ' active' : '')} onClick={() => select(id)}>{s.tabs[id]}</button>
          ))}
        </div>
      )}
      <div role="tabpanel" className="tab-panel">
        {tab === 'inbox' ? <InboxTab /> : <TicketTypesTab />}
      </div>
    </div>
  );
}

export default SupportInboxView;
