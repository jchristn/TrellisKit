import { loadDailyMetrics } from '../../../utils/metricCollections.js';
import { useCallback, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { EventFormats, EventStatuses, eventCategories, formatEventShort, moveItem } from '@trelliskit/shared';
import strings from '../../../strings.js';
import { useAdminAuth } from '../../../context/AdminAuthContext.jsx';
import { useProperty } from '../../../context/PropertyContext.jsx';
import { useToast } from '../../../context/ToastContext.jsx';
import useApiQuery from '../../../hooks/useApiQuery.js';
import PageHeader from '../../../components/PageHeader.jsx';
import Icon from '../../../components/Icon.jsx';
import Modal from '../../../components/Modal.jsx';
import CopyableId from '../../../components/CopyableId.jsx';
import KpiCard from '../../../components/KpiCard.jsx';
import DailySeriesChart from '../../../components/usage/DailySeriesChart.jsx';
import { Badge } from '../../../components/Badges.jsx';
import { ErrorBanner, Loading } from '../../../components/States.jsx';
import { AdminListSection, Field, TabBar, Toggle, useAdminList, useTab } from '../../../components/admin/AdminParts.jsx';
import { authorName, errorText } from '../../../utils/engagement.js';
import { formatNumber, formatPercent } from '../../../utils/format.js';
import { phase4Errors, useBoundedList } from '../../../utils/phase4.js';
import TableColumns from '../../../components/TableColumns.jsx';

const s = strings.admin.eventsAdmin;
const c = strings.admin.common;
const ev = strings.events;
const ADMIN_TABS = Object.freeze(['events', 'proposals', 'metrics', 'categories']);
const STAFF_TABS = Object.freeze(['events', 'proposals', 'metrics']);
const EVENT_FILTERS = Object.freeze(['search', 'status', 'when', 'category', 'format', 'hostUserId', 'fromUtc', 'toUtc']);
const PROPOSAL_FILTERS = Object.freeze(['search', 'status']);

function statusTone(status) {
  return { Published: 'success', Draft: 'neutral', Canceled: 'danger', Proposed: 'info', Rejected: 'danger' }[status] || 'neutral';
}

function EventsTab() {
  const { client } = useAdminAuth();
  const { property } = useProperty();
  const navigate = useNavigate();
  const fetch = useCallback((q, signal) => client.adminEnumerateEvents(q, signal), [client]);
  const admin = useAdminList({
    storageKey: 'tk.admin.events', keys: EVENT_FILTERS, prefix: 'e.', fetch,
    transform: ({ status, category, format, when, ...rest }) => ({ ...rest, when: when || 'All', ...(status ? { status: [status] } : {}), ...(category ? { category: [category] } : {}), ...(format ? { format: [format] } : {}) })
  });
  const open = (row) => navigate('/admin/events/' + encodeURIComponent(row.id));
  const columns = [
    { key: 'title', label: s.columns.title, render: (row) => <span className="cell-stack"><strong>{row.title}</strong>{row.recurrence && <span className="muted small"><Icon name="refresh" size={12} /> {row.recurrence.summary}</span>}</span> },
    { key: 'start', label: s.columns.start, className: 'nowrap', render: (row) => formatEventShort(row) },
    { key: 'status', label: s.columns.status, render: (row) => <Badge tone={statusTone(row.status)}>{s.statuses[row.status] || row.status}</Badge> },
    { key: 'format', label: s.columns.format, render: (row) => ev.formats[row.format] || row.format },
    { key: 'category', label: s.columns.category, render: (row) => (row.category ? row.category.label : '-') },
    { key: 'going', label: s.columns.going, className: 'num', render: (row) => formatNumber(row.goingCount || 0) + (row.capacity ? ' / ' + formatNumber(row.capacity) : '') },
    { key: 'waitlist', label: s.columns.waitlist, className: 'num', render: (row) => formatNumber(row.waitlistCount || 0) },
    { key: 'reports', label: s.columns.reports, className: 'num', render: (row) => (row.openReportCount ? <Badge tone="warning">{row.openReportCount}</Badge> : '0') },
    { key: 'id', label: s.columns.id, className: 'cell-id', render: (row) => <CopyableId value={row.id} label={s.columns.id} /> }
  ];
  const filterFields = [
    { key: 'search', label: c.search, grow: true },
    { key: 'status', label: s.columns.status, type: 'select', options: EventStatuses.filter((x) => x !== 'Withdrawn').map((x) => ({ value: x, label: s.statuses[x] || x })) },
    { key: 'when', label: ev.whenLabel, type: 'select', options: Object.keys(ev.when).map((x) => ({ value: x, label: ev.when[x] })) },
    { key: 'category', label: s.columns.category, type: 'select', options: eventCategories(property).map((x) => ({ value: x.key, label: x.label })) },
    { key: 'format', label: s.columns.format, type: 'select', options: EventFormats.map((x) => ({ value: x, label: ev.formats[x] })) },
    { key: 'hostUserId', label: s.host, mono: true },
    { key: 'fromUtc', label: s.createdFrom, type: 'datetime' },
    { key: 'toUtc', label: s.createdTo, type: 'datetime' }
  ];
  return (
    <>
      <div className="form-actions-start"><button type="button" className="button button-primary button-sm" onClick={() => navigate('/admin/events/new')}><Icon name="plus" size={14} /><span>{s.create}</span></button></div>
      <AdminListSection admin={admin} filterFields={filterFields} client={client} targetType="Event" columns={columns} caption={s.eventsTitle} empty={s.empty} emptyFiltered={s.emptyFiltered} onRowClick={open} />
    </>
  );
}

function ReviewDialog({ proposal, decision, onClose, onDone }) {
  const { client } = useAdminAuth();
  const [note, setNote] = useState('');
  const [publish, setPublish] = useState(true);
  const [notify, setNotify] = useState(false);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const submit = async () => {
    if (decision === 'reject' && note.trim().length < 3) {
      setError(s.reviewNoteRequired);
      return;
    }
    setBusy(true);
    try {
      if (decision === 'approve') await client.adminApproveEvent(proposal.id, { reviewNote: note.trim() || undefined, publish, notifyMembers: publish && notify });
      else await client.adminRejectEvent(proposal.id, { reviewNote: note.trim() });
      onDone();
    } catch (err) {
      setError(errorText(err, phase4Errors()));
      setBusy(false);
    }
  };
  return (
    <Modal open size="sm" onClose={busy ? () => {} : onClose} title={decision === 'approve' ? s.approveTitle(proposal.title) : s.rejectTitle(proposal.title)}
      footer={<><button type="button" className="button button-secondary" onClick={onClose} disabled={busy}>{strings.common.cancel}</button><button type="button" className={'button ' + (decision === 'approve' ? 'button-primary' : 'button-danger')} onClick={submit} disabled={busy}>{decision === 'approve' ? s.approve : s.reject}</button></>}>
      <div className="stack-form">
        <ErrorBanner message={error} />
        <p className="small">{proposal.proposedBy ? s.proposedBy(authorName(proposal.proposedBy)) : ''} {formatEventShort(proposal)}</p>
        {proposal.proposalNote && <p className="small">{s.proposalNote}: {proposal.proposalNote}</p>}
        {decision === 'approve' && <Toggle checked={publish} onChange={setPublish} label={s.publishNow} />}
        {decision === 'approve' && publish && <Toggle checked={notify} onChange={setNotify} label={s.notifyMembers} />}
        <Field id="review-note" label={s.reviewNote} hint={decision === 'reject' ? s.reviewNoteHintReject : s.reviewNoteHint}>
          <textarea id="review-note" rows={3} maxLength={1000} value={note} onChange={(e) => setNote(e.target.value)} disabled={busy} />
        </Field>
      </div>
    </Modal>
  );
}

function ProposalsTab() {
  const { client } = useAdminAuth();
  const toast = useToast();
  const navigate = useNavigate();
  const [review, setReview] = useState(null);
  const fetch = useCallback((q, signal) => client.adminEnumerateEventProposals(q, signal), [client]);
  const admin = useAdminList({ storageKey: 'tk.admin.eventProposals', keys: PROPOSAL_FILTERS, prefix: 'p.', fetch, transform: ({ status, ...rest }) => ({ ...rest, ...(status ? { status: [status] } : {}) }) });
  const columns = [
    { key: 'title', label: s.columns.title, render: (row) => <strong>{row.title}</strong> },
    { key: 'by', label: s.columns.proposedBy, render: (row) => authorName(row.proposedBy) },
    { key: 'start', label: s.columns.start, render: (row) => formatEventShort(row) },
    { key: 'status', label: s.columns.status, render: (row) => <Badge tone={statusTone(row.status)}>{ev.proposalStatus[row.status] || row.status}</Badge> },
    { key: 'actions', label: <span className="visually-hidden">{s.columns.actions}</span>, className: 'cell-actions', render: (row) => (row.status === 'Proposed' ? (
      <span className="badge-row">
        <button type="button" className="button button-primary button-sm" onClick={() => setReview({ proposal: row, decision: 'approve' })}>{s.approve}</button>
        <button type="button" className="button button-secondary button-sm" onClick={() => setReview({ proposal: row, decision: 'reject' })}>{s.reject}</button>
      </span>
    ) : null) }
  ];
  const filterFields = [
    { key: 'search', label: c.search, grow: true },
    { key: 'status', label: s.columns.status, type: 'select', options: ['Proposed', 'Published', 'Draft', 'Rejected', 'Withdrawn'].map((x) => ({ value: x, label: ev.proposalStatus[x] || x })) }
  ];
  return (
    <>
      <p className="muted small">{s.proposalsIntro}</p>
      <AdminListSection admin={admin} filterFields={filterFields} client={client} targetType="Event" columns={columns} caption={s.proposalsTitle} empty={s.proposalsEmpty} emptyFiltered={s.emptyFiltered} onRowClick={(row) => navigate('/admin/events/' + encodeURIComponent(row.id))} />
      {review && <ReviewDialog proposal={review.proposal} decision={review.decision} onClose={() => setReview(null)} onDone={() => { toast.success(review.decision === 'approve' ? s.approved : s.rejected); setReview(null); admin.list.reload(); }} />}
    </>
  );
}

function MetricsTab() {
  const { client } = useAdminAuth();
  const [days, setDays] = useState(30);
  const fetcher = useCallback((signal) => loadDailyMetrics((q, sig) => client.adminEventMetrics(q, sig), { days: days }, signal), [client, days]);
  const query = useApiQuery(fetcher);
  const m = query.data;
  const series = useMemo(() => (m ? [{ key: 'events', label: s.metrics.events, points: m.eventsByDay }, { key: 'rsvps', label: s.metrics.rsvps, points: m.rsvpsByDay }] : []), [m]);
  const sum = (list) => (list || []).reduce((total, x) => total + (Number(x.count || x.value) || 0), 0);
  return (
    <section className="stack">
      <label className="inline-field"><span className="label-text">{s.days}</span>
        <select className="select-compact" value={days} onChange={(e) => setDays(Number(e.target.value))}>{[7, 30, 90, 365].map((d) => <option key={d} value={d}>{s.daysValue(d)}</option>)}</select>
      </label>
      {query.loading && <Loading inline />}
      <ErrorBanner message={query.error ? errorText(query.error, phase4Errors()) : ''} onRetry={query.reload} />
      {m && (
        <>
          <div className="kpi-grid">
            <KpiCard label={s.metrics.events} value={formatNumber(sum(m.eventsByDay))} />
            <KpiCard label={s.metrics.rsvps} value={formatNumber(sum(m.rsvpsByDay))} />
            <KpiCard label={s.metrics.checkInRate} value={formatPercent(m.checkInRate || 0)} />
            <KpiCard label={s.metrics.noShowRate} value={formatPercent(m.noShowRate || 0)} />
          </div>
          <DailySeriesChart title={s.metrics.chartTitle} series={series} />
          <TableColumns id="tk.admin.events.EventsAdminView"><table className="data-table">
            <caption>{s.metrics.byCategory}</caption>
            <thead><tr><th scope="col">{s.columns.category}</th><th scope="col" className="num">{s.metrics.events}</th><th scope="col" className="num">{s.metrics.rsvps}</th></tr></thead>
            <tbody>{((m.byCategory && m.byCategory.objects) || []).map((x, i) => <tr key={x.key || i}><td>{x.label || x.key}</td><td className="num">{formatNumber(x.events || 0)}</td><td className="num">{formatNumber(x.rsvps || 0)}</td></tr>)}</tbody>
          </table></TableColumns>
        </>
      )}
    </section>
  );
}

function CategoriesTab() {
  const { client } = useAdminAuth();
  const toast = useToast();
  const fetchPage = useCallback((q, signal) => client.adminEnumerateEventCategories(q, signal), [client]);
  const list = useBoundedList(fetchPage, { pageSize: 100, maxPages: 2 });
  const [form, setForm] = useState({ key: '', label: '', color: '#1F5FBF' });
  const [order, setOrder] = useState(null);
  const [editing, setEditing] = useState(null);
  const rows = order || list.items;
  const run = async (fn, message) => {
    try {
      await fn();
      toast.success(message);
      setOrder(null);
      setEditing(null);
      list.reload();
      return true;
    } catch (err) {
      toast.error(errorText(err, phase4Errors()));
      return false;
    }
  };
  return (
    <section className="panel stack">
      <p className="muted small">{s.categoriesNote}</p>
      <ErrorBanner message={list.error ? errorText(list.error, phase4Errors()) : ''} onRetry={list.reload} />
      <ul className="history-list">
        {rows.map((cat, i) => (
          <li key={cat.id} className="history-item badge-row">
            <span className="p4-chip-dot" style={cat.color ? { background: cat.color } : undefined} />
            {editing && editing.id === cat.id ? (
              <>
                <input className="input-compact" aria-label={s.fields.label} value={editing.label} onChange={(e) => setEditing({ ...editing, label: e.target.value })} maxLength={80} />
                <input type="color" aria-label={s.fields.color} value={editing.color || '#888888'} onChange={(e) => setEditing({ ...editing, color: e.target.value })} />
                <button type="button" className="button button-primary button-sm" onClick={() => run(() => client.adminUpdateEventCategory(cat.id, { label: editing.label.trim(), color: editing.color }), c.saved)}>{strings.common.save}</button>
              </>
            ) : <><strong>{cat.label}</strong> <span className="muted mono small">{cat.key}</span></>}
            {cat.isRetired && <Badge tone="neutral">{s.retired}</Badge>}
            <button type="button" className="icon-button" aria-label={s.moveUp(cat.label)} disabled={i === 0} onClick={() => setOrder(moveItem(rows, i, i - 1))}><Icon name="chevronUp" size={14} /></button>
            <button type="button" className="icon-button" aria-label={s.moveDown(cat.label)} disabled={i === rows.length - 1} onClick={() => setOrder(moveItem(rows, i, i + 1))}><Icon name="chevronDown" size={14} /></button>
            {!editing && <button type="button" className="link-button small" onClick={() => setEditing({ id: cat.id, label: cat.label, color: cat.color })}>{c.edit}</button>}
            {!cat.isRetired && <button type="button" className="link-button small" onClick={() => run(() => client.adminDeleteEventCategory(cat.id), s.categoryRetired)}>{s.retire}</button>}
          </li>
        ))}
      </ul>
      {order && <div><button type="button" className="button button-primary button-sm" onClick={() => run(() => client.adminOrderEventCategories(order.map((x) => x.id)), s.orderSaved)}>{s.saveOrder}</button></div>}
      <form className="inline-form" onSubmit={async (e) => { e.preventDefault(); if (form.key.trim() && form.label.trim() && await run(() => client.adminCreateEventCategory({ key: form.key.trim(), label: form.label.trim(), color: form.color }), s.categoryAdded)) setForm({ key: '', label: '', color: '#1F5FBF' }); }}>
        <input className="input-compact" aria-label={s.fields.key} placeholder={s.fields.key} value={form.key} onChange={(e) => setForm({ ...form, key: e.target.value })} maxLength={64} />
        <input className="input-compact" aria-label={s.fields.label} placeholder={s.fields.label} value={form.label} onChange={(e) => setForm({ ...form, label: e.target.value })} maxLength={80} />
        <input type="color" aria-label={s.fields.color} value={form.color} onChange={(e) => setForm({ ...form, color: e.target.value })} />
        <button type="submit" className="button button-secondary button-sm">{s.addCategory}</button>
      </form>
    </section>
  );
}

/**
 * /admin/events (docs/PHASE4_CONTRACT.md section 4.7, resolved decisions 10 and 11): events with
 * status, time, category, format, host, search, label, and tag filters (rows open the editor), the
 * member proposals queue with approve and reject, and metrics for event staff (SystemAdmin or
 * EventManager); categories for SystemAdmin only.
 */
function EventsAdminView() {
  const { isSystemAdmin } = useAdminAuth();
  const tabs = isSystemAdmin ? ADMIN_TABS : STAFF_TABS;
  const [tab, select] = useTab(tabs);
  return (
    <div className="view">
      <PageHeader title={strings.admin.routes.events.label} summary={strings.admin.routes.events.summary} />
      <TabBar tabs={tabs} labels={s.tabs} current={tab} onSelect={select} idPrefix="evt" label={s.tabsLabel} />
      <div role="tabpanel" id={'evt-panel-' + tab} aria-labelledby={'evt-tab-' + tab} className="tab-panel">
        {tab === 'events' && <EventsTab />}
        {tab === 'proposals' && <ProposalsTab />}
        {tab === 'metrics' && <MetricsTab />}
        {tab === 'categories' && <CategoriesTab />}
      </div>
    </div>
  );
}

export default EventsAdminView;
