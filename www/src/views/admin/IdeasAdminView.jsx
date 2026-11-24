import { useCallback, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { ENUMERATION_MAX_RESULTS_ADMIN, formatRelativeTime, normalizeEnumerationResult } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useTableState from '../../hooks/useTableState.js';
import { errorMessage } from '../../utils/api.js';
import { normalizePage } from '../../utils/query.js';
import { ADMIN_PAGE_SIZES, pagedQuery } from '../../utils/adminQuery.js';
import { formatDateTime } from '../../utils/format.js';
import PageHeader from '../../components/PageHeader.jsx';
import AdminFilterPanel from '../../components/AdminFilterPanel.jsx';
import DataTable from '../../components/DataTable.jsx';
import TablePagination from '../../components/TablePagination.jsx';
import Modal from '../../components/Modal.jsx';
import ConfirmModal from '../../components/ConfirmModal.jsx';
import Icon from '../../components/Icon.jsx';
import { Badge } from '../../components/Badges.jsx';
import { ErrorBanner, Loading } from '../../components/States.jsx';
import MarkdownBody from '../../components/feed/MarkdownBody.jsx';
import { IdeaStatusBadge } from '../member/IdeasView.jsx';
import EntityPicker, { pickerSearches } from '../../components/admin/EntityPicker.jsx';
import TableColumns from '../../components/TableColumns.jsx';

const s = strings.ideasAdmin;
const d = s.detail;
const c = strings.admin.common;

export const IDEA_FILTERS = Object.freeze(['search', 'status', 'merged', 'hidden']);
export const IDEA_DEFAULT_SORT = '-updatedUtc';

/** The admin ideas enumeration body (POST /admin/ideas/enumerate). */
export function buildIdeasQuery(filters, paging) {
  return pagedQuery(filters, IDEA_FILTERS, paging, (rest) => {
    const out = { ...rest };
    if (out.status) out.status = [out.status];
    for (const key of ['merged', 'hidden']) if (out[key]) out[key] = true;
    return out;
  });
}

function useStatuses() {
  const { client } = useAdminAuth();
  const fetcher = useCallback((signal) => client.adminEnumerateIdeaStatuses({ maxResults: ENUMERATION_MAX_RESULTS_ADMIN }, signal), [client]);
  const query = useApiQuery(fetcher);
  return { ...query, items: query.data ? normalizeEnumerationResult(query.data).objects : [] };
}

function IdeaDrawer({ idea, statuses, onClose, onChanged }) {
  const { client, isSystemAdmin } = useAdminAuth();
  const ideaSearch = useMemo(() => pickerSearches(client, { isSystemAdmin }).ideas, [client, isSystemAdmin]);
  const toast = useToast();
  const [current, setCurrent] = useState(idea);
  const [status, setStatus] = useState(idea.status ? idea.status.key : '');
  const [note, setNote] = useState(idea.statusNote || '');
  const [notify, setNotify] = useState(true);
  const [intoPick, setIntoPick] = useState([]);
  const into = intoPick[0] ? intoPick[0].id : '';
  const [problem, setProblem] = useState('');
  const [confirmDelete, setConfirmDelete] = useState(false);

  const run = async (fn, message) => {
    try {
      const result = await fn();
      if (result && result.id) setCurrent(result);
      toast.success(message);
      onChanged();
      return true;
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
      return false;
    }
  };
  const saveStatus = (e) => {
    e.preventDefault();
    if (note.length > 1000) { setProblem(d.errors.note); return; }
    setProblem('');
    run(() => client.adminSetIdeaStatus(current.id, { status, statusNote: note.trim(), notifyVoters: notify }), d.statusSaved);
  };
  const merge = (e) => {
    e.preventDefault();
    const target = into.trim();
    if (!target || target === current.id) { setProblem(d.errors.into); return; }
    setProblem('');
    run(() => client.adminMergeIdea(current.id, target), d.merged).then((ok) => { if (ok) setIntoPick([]); });
  };
  const remove = async () => {
    const ok = await run(() => client.adminDeleteIdea(current.id), d.deleted);
    setConfirmDelete(false);
    if (ok) onClose();
  };

  return (
    <Modal open variant="drawer" size="lg" onClose={onClose} title={current.title} subtitle={<code className="mono">{current.id}</code>}>
      <div className="detail-stack">
        <section className="detail-block">
          <div className="badge-row"><IdeaStatusBadge status={current.status} /><span className="muted small">{d.votes(current.voteCount || 0)}</span></div>
          <MarkdownBody body={current.body || ''} allowHeadings={false} />
          <a href={'/app/ideas/' + encodeURIComponent(current.id)} target="_blank" rel="noreferrer" className="small">{d.open}</a>
        </section>
        {problem && <p className="field-error" role="alert">{problem}</p>}
        <form className="detail-block form-stack" onSubmit={saveStatus} aria-labelledby="idea-status-title">
          <h3 id="idea-status-title">{d.statusTitle}</h3>
          <div className="field">
            <label className="label-text" htmlFor="idea-admin-status">{d.status}</label>
            <select id="idea-admin-status" value={status} onChange={(e) => setStatus(e.target.value)}>
              {statuses.filter((st) => !st.isRetired || st.key === status).map((st) => <option key={st.key} value={st.key}>{st.label || st.key}</option>)}
            </select>
          </div>
          <div className="field">
            <label className="label-text" htmlFor="idea-admin-note">{d.statusNote}</label>
            <textarea id="idea-admin-note" className="textarea" rows={3} value={note} maxLength={1000} onChange={(e) => setNote(e.target.value)} />
          </div>
          <label className="checkbox-row"><input type="checkbox" checked={notify} onChange={(e) => setNotify(e.target.checked)} /><span>{d.notifyVoters}</span></label>
          <div className="form-actions"><button type="submit" className="button button-primary button-sm" disabled={!status}>{d.setStatus}</button></div>
        </form>
        <section className="detail-block" aria-labelledby="idea-merge-title">
          <h3 id="idea-merge-title">{d.mergeTitle}</h3>
          {current.mergedInto ? (
            <div className="row-actions">
              <span>{d.mergedInto(current.mergedInto.title || current.mergedInto.id)}</span>
              <button type="button" className="button button-secondary button-sm" onClick={() => run(() => client.adminUnmergeIdea(current.id), d.unmerged)}>{d.unmerge}</button>
            </div>
          ) : (
            <form className="inline-form" onSubmit={merge}>
              <EntityPicker id="idea-merge-into" label={d.mergeInto} value={intoPick} onChange={(items) => setIntoPick(items.filter((x) => x.id !== current.id))} search={ideaSearch} placeholder={strings.supportAdmin.detail.ideaPlaceholder} />
              <button type="submit" className="button button-secondary button-sm">{d.merge}</button>
            </form>
          )}
        </section>
        <div className="row-actions"><button type="button" className="button button-danger-outline button-sm" onClick={() => setConfirmDelete(true)}>{d.delete}</button></div>
      </div>
      <ConfirmModal open={confirmDelete} title={d.deleteTitle} message={d.deleteBody} confirmLabel={d.delete} onConfirm={remove} onCancel={() => setConfirmDelete(false)} />
    </Modal>
  );
}

function CreateIdeaDialog({ onClose, onCreated }) {
  const { client } = useAdminAuth();
  const toast = useToast();
  const [title, setTitle] = useState('');
  const [body, setBody] = useState('');
  const [busy, setBusy] = useState(false);
  const submit = async (e) => {
    e.preventDefault();
    if (title.trim().length < 3) return;
    setBusy(true);
    try {
      await client.adminCreateIdea({ title: title.trim(), body: body.trim() });
      toast.success(s.created);
      onCreated();
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
      setBusy(false);
    }
  };
  return (
    <Modal open onClose={onClose} title={s.createTitle}>
      <form className="form-stack" onSubmit={submit}>
        <div className="field"><label className="label-text" htmlFor="idea-new-title">{s.name}</label><input id="idea-new-title" value={title} maxLength={140} onChange={(e) => setTitle(e.target.value)} /></div>
        <div className="field"><label className="label-text" htmlFor="idea-new-body">{s.body}</label><textarea id="idea-new-body" className="textarea" rows={5} value={body} onChange={(e) => setBody(e.target.value)} /></div>
        <div className="form-actions">
          <button type="submit" className="button button-primary" disabled={busy || title.trim().length < 3}>{strings.common.save}</button>
          <button type="button" className="button button-secondary" onClick={onClose}>{strings.common.cancel}</button>
        </div>
      </form>
    </Modal>
  );
}

function IdeasTab({ statuses }) {
  const { client } = useAdminAuth();
  const table = useTableState({ storageKey: 'tk.admin.ideas', filterKeys: IDEA_FILTERS, defaultSort: IDEA_DEFAULT_SORT });
  const query = useMemo(() => buildIdeasQuery(table.filters, { pageNumber: table.pageNumber, pageSize: table.pageSize, sort: table.sort, meta: table.meta }), [table.filters, table.pageNumber, table.pageSize, table.sort, table.meta]);
  const fetcher = useCallback((signal) => client.adminEnumerateIdeas(query, signal), [client, query]);
  const list = useApiQuery(fetcher);
  const page = list.data ? normalizePage(list.data, table.pageSize) : null;
  const rows = page ? page.items : [];
  const [open, setOpen] = useState(null);
  const [creating, setCreating] = useState(false);
  const columns = [
    { key: 'title', label: s.columns.title, pinned: true, render: (row) => <span className="cell-stack"><span>{row.title}</span><span className="muted small mono">{row.id}</span></span> },
    { key: 'status', label: s.columns.status, render: (row) => <IdeaStatusBadge status={row.status} /> },
    { key: 'votes', label: s.columns.votes, className: 'num', sortField: 'voteCount', sortDefault: 'desc', render: (row) => row.voteCount || 0 },
    { key: 'comments', label: s.columns.comments, className: 'num', render: (row) => row.commentCount || 0 },
    { key: 'merged', label: s.columns.merged, render: (row) => (row.mergedInto ? <Badge tone="info">{row.mergedInto.title || row.mergedInto.id}</Badge> : <span className="muted">-</span>) },
    { key: 'updated', label: s.columns.updated, sortField: 'updatedUtc', sortDefault: 'desc', className: 'nowrap', render: (row) => <span title={formatDateTime(row.updatedUtc)}>{formatRelativeTime(row.updatedUtc)}</span> }
  ];
  const filterFields = [
    { key: 'search', label: s.filters.search },
    { key: 'status', label: s.filters.status, type: 'select', options: statuses.map((st) => ({ value: st.key, label: st.label || st.key })) },
    { key: 'merged', label: s.filters.merged, type: 'checkbox' },
    { key: 'hidden', label: s.filters.hidden, type: 'checkbox' }
  ];
  return (
    <>
      <section className="panel">
        <div className="row-between">
          <AdminFilterPanel fields={filterFields} table={table} client={client} targetType="Idea" />
          <button type="button" className="button button-primary button-sm" onClick={() => setCreating(true)}><Icon name="plus" size={14} /><span>{s.create}</span></button>
        </div>
      </section>
      <section className="table-section">
        <TablePagination pageNumber={table.pageNumber} pageSize={table.pageSize} totalCount={page ? page.totalCount : 0} totalPages={page ? page.totalPages : 1} onPageChange={table.setPage} onPageSizeChange={table.setPageSize} onRefresh={list.reload} loading={list.loading} pageSizes={ADMIN_PAGE_SIZES} />
        <DataTable caption={s.tabs.ideas} columns={columns} rows={rows} rowKey={(row) => row.id} loading={list.loading} error={list.error ? c.unavailable(s.tabs.ideas) + ' ' + errorMessage(list.error, '') : null} onRetry={list.reload} emptyMessage={table.hasFilters ? s.emptyFiltered : s.empty} onRowClick={setOpen} sort={table.sort} onSortChange={table.setSort} />
      </section>
      {open && <IdeaDrawer key={open.id} idea={open} statuses={statuses} onClose={() => setOpen(null)} onChanged={list.reload} />}
      {creating && <CreateIdeaDialog onClose={() => setCreating(false)} onCreated={() => { setCreating(false); list.reload(); }} />}
    </>
  );
}

function StatusesTab({ query }) {
  const { client } = useAdminAuth();
  const toast = useToast();
  const t = s.statuses;
  const [order, setOrder] = useState(null);
  const [form, setForm] = useState({ key: '', label: '', color: '#607D8B', isClosed: false, isInitial: false });
  const rows = order ? order.map((id) => query.items.find((x) => x.id === id)).filter(Boolean) : query.items;
  const run = async (fn, message) => {
    try {
      await fn();
      toast.success(message);
      query.reload();
      return true;
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
      return false;
    }
  };
  const move = (index, delta) => {
    const ids = rows.map((x) => x.id);
    const target = index + delta;
    if (target < 0 || target >= ids.length) return;
    [ids[index], ids[target]] = [ids[target], ids[index]];
    setOrder(ids);
  };
  const add = async (e) => {
    e.preventDefault();
    if (!form.key.trim() || !form.label.trim()) return;
    if (await run(() => client.adminCreateIdeaStatus({ ...form, key: form.key.trim(), label: form.label.trim() }), t.saved)) setForm({ key: '', label: '', color: '#607D8B', isClosed: false, isInitial: false });
  };
  return (
    <section className="panel">
      {query.loading && !query.data && <Loading />}
      <ErrorBanner message={query.error ? errorMessage(query.error, '') : ''} onRetry={query.reload} />
      {order && <div className="row-actions"><button type="button" className="button button-secondary button-sm" onClick={() => run(() => client.adminOrderIdeaStatuses(rows.map((x) => x.id)), t.orderSaved).then(() => setOrder(null))}>{t.saveOrder}</button></div>}
      <div className="table-scroll">
        <TableColumns id="tk.admin.IdeasAdminView"><table className="data-table">
          <caption className="visually-hidden">{s.tabs.statuses}</caption>
          <thead><tr>{['order', 'key', 'label', 'closed', 'state', 'actions'].map((k) => <th key={k} scope="col">{t.columns[k]}</th>)}</tr></thead>
          <tbody>
            {rows.map((st, i) => (
              <tr key={st.id}>
                <td className="nowrap">
                  <button type="button" className="icon-button" onClick={() => move(i, -1)} disabled={i === 0} aria-label={t.moveUp(st.label)} title={t.moveUp(st.label)}><Icon name="chevronUp" size={14} /></button>
                  <button type="button" className="icon-button" onClick={() => move(i, 1)} disabled={i === rows.length - 1} aria-label={t.moveDown(st.label)} title={t.moveDown(st.label)}><Icon name="chevronDown" size={14} /></button>
                </td>
                <td className="mono">{st.key}</td>
                <td><IdeaStatusBadge status={st} /></td>
                <td>{st.isClosed ? c.yes : c.no}</td>
                <td><Badge tone={st.isRetired ? 'neutral' : 'success'}>{st.isRetired ? t.retiredLabel : t.active}</Badge></td>
                <td>{!st.isRetired && <button type="button" className="button button-danger-outline button-sm" onClick={() => run(() => client.adminDeleteIdeaStatus(st.id), t.retired)}>{t.retire}</button>}</td>
              </tr>
            ))}
          </tbody>
        </table></TableColumns>
      </div>
      <form className="form-grid" onSubmit={add} aria-label={t.add}>
        <div className="field"><label className="label-text" htmlFor="ids-key">{t.key}</label><input id="ids-key" className="mono" value={form.key} onChange={(e) => setForm((v) => ({ ...v, key: e.target.value }))} /></div>
        <div className="field"><label className="label-text" htmlFor="ids-label">{t.label}</label><input id="ids-label" value={form.label} maxLength={60} onChange={(e) => setForm((v) => ({ ...v, label: e.target.value }))} /></div>
        <div className="field"><label className="label-text" htmlFor="ids-color">{t.color}</label><input id="ids-color" type="color" value={form.color} onChange={(e) => setForm((v) => ({ ...v, color: e.target.value }))} /></div>
        <label className="checkbox-row"><input type="checkbox" checked={form.isClosed} onChange={(e) => setForm((v) => ({ ...v, isClosed: e.target.checked }))} /><span>{t.isClosed}</span></label>
        <label className="checkbox-row"><input type="checkbox" checked={form.isInitial} onChange={(e) => setForm((v) => ({ ...v, isInitial: e.target.checked }))} /><span>{t.isInitial}</span></label>
        <div className="form-actions"><button type="submit" className="button button-primary button-sm">{t.add}</button></div>
      </form>
    </section>
  );
}

/**
 * /admin/ideas (SystemAdmin, SupportAgent): the ideas table with filters and label and tag
 * filters, the idea drawer (status with a note and the notify-voters switch, merge and unmerge,
 * delete), staff-created ideas, and the status list (SystemAdmin: add, retire, order).
 */
function IdeasAdminView() {
  const { isSystemAdmin } = useAdminAuth();
  const statuses = useStatuses();
  const [searchParams, setSearchParams] = useSearchParams();
  const tabs = ['ideas', ...(isSystemAdmin ? ['statuses'] : [])];
  const tab = tabs.includes(searchParams.get('tab')) ? searchParams.get('tab') : 'ideas';
  return (
    <div className="view">
      <PageHeader title={strings.admin.routes.ideas.label} summary={strings.admin.routes.ideas.summary} />
      {tabs.length > 1 && (
        <div className="segmented tab-bar" role="tablist" aria-label={s.tabsLabel}>
          {tabs.map((id) => <button key={id} type="button" role="tab" aria-selected={tab === id} className={'segment' + (tab === id ? ' active' : '')} onClick={() => setSearchParams(id === 'ideas' ? {} : { tab: id })}>{s.tabs[id]}</button>)}
        </div>
      )}
      <div role="tabpanel" className="tab-panel">
        {tab === 'ideas' ? <IdeasTab statuses={statuses.items} /> : <StatusesTab query={statuses} />}
      </div>
    </div>
  );
}

export default IdeasAdminView;
