import { useCallback, useEffect, useMemo, useState } from 'react';
import { REMOTE_STRING_FILTER_KEYS, invalidStringTokens } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useTableState from '../../hooks/useTableState.js';
import { errorMessage, getFieldErrors } from '../../utils/api.js';
import { normalizePage } from '../../utils/query.js';
import { ADMIN_PAGE_SIZES } from '../../utils/adminQuery.js';
import { optionsFrom } from '../../utils/labels.js';
import { formatDateTime, formatShortDate } from '../../utils/format.js';
import PageHeader from '../../components/PageHeader.jsx';
import AdminFilterPanel from '../../components/AdminFilterPanel.jsx';
import DataTable from '../../components/DataTable.jsx';
import TablePagination from '../../components/TablePagination.jsx';
import Modal from '../../components/Modal.jsx';
import MetadataEditor from '../../components/MetadataEditor.jsx';
import { Badge } from '../../components/Badges.jsx';
import { ErrorBanner, Loading } from '../../components/States.jsx';
import ContentPublishPanel from './ContentPublishPanel.jsx';
import { contentQuery } from './appContentAdmin.js';
import '../../components/appContent.css';
import TableColumns from '../../components/TableColumns.jsx';

const u = strings.admin.liveUpdates;
const ac = strings.appContent.admin;
const st = ac.strings;
const FILTERS = Object.freeze(['search', ...REMOTE_STRING_FILTER_KEYS]);

/** POST /admin/app/strings/enumerate query; `overridden` and `hasDraftChanges` are booleans. */
export function buildStringsQuery(filters, paging) {
  return contentQuery(filters, FILTERS, paging, { ordering: 'KeyAscending', booleans: ['overridden', 'hasDraftChanges'] });
}

/** Status badges of a catalog row. */
export function stringBadges(row) {
  const out = [];
  if (row.overridden) out.push({ tone: 'success', label: st.overridden });
  if (row.pendingDelete) out.push({ tone: 'danger', label: st.pendingDelete });
  else if (row.hasDraftChanges) out.push({ tone: 'warning', label: st.draft });
  if (!out.length) out.push({ tone: 'neutral', label: st.builtIn });
  return out;
}

/** The editor's problem with an override value, or null. */
export function stringValueProblem(value, entry) {
  if (!String(value || '').length) return st.valueRequired;
  const bad = invalidStringTokens(value, entry.tokens || []);
  if (bad.length) return st.invalidTokens(bad.map((t) => '{' + t + '}'), (entry.tokens || []).map((t) => '{' + t + '}'));
  if (entry.maxLength && value.length > entry.maxLength) return st.tooLong(entry.maxLength);
  return null;
}

function StringHistory({ client, stringKey, history, onUse }) {
  const [all, setAll] = useState(null);
  const [error, setError] = useState('');
  const rows = all || (history && history.objects) || [];
  const total = history ? history.totalRecords || rows.length : 0;
  const loadAll = async () => {
    try {
      const page = await client.enumerateRemoteStringHistory(stringKey, { maxResults: 100 });
      setAll(normalizePage(page).items);
    } catch (err) {
      setError(errorMessage(err, strings.common.errorGeneric));
    }
  };
  return (
    <section aria-labelledby="string-history-title">
      <h3 id="string-history-title">{ac.history}</h3>
      {error && <ErrorBanner message={error} />}
      {rows.length === 0 ? <p className="muted">{ac.noHistory}</p> : (
        <TableColumns id="tk.admin.StringsView"><table className="table">
          <thead><tr><th>{u.flags.version}</th><th>{st.value}</th><th>{ac.when}</th><th><span className="visually-hidden">{u.columns.actions}</span></th></tr></thead>
          <tbody>
            {rows.map((h, i) => (
              <tr key={(h.publishedInVersion || '') + ':' + i}>
                <td>{h.publishedInVersion ?? '-'}</td>
                <td>{h.value === null || h.value === undefined ? <span className="muted">{st.builtIn}</span> : h.value}</td>
                <td>{formatDateTime(h.updatedUtc)}</td>
                <td>{h.value !== null && h.value !== undefined && <button type="button" className="button button-secondary button-sm" onClick={() => onUse(h.value)}>{st.useValue}</button>}</td>
              </tr>
            ))}
          </tbody>
        </table></TableColumns>
      )}
      {!all && total > rows.length && <button type="button" className="button button-secondary button-sm" onClick={loadAll}>{ac.showAllHistory(total)}</button>}
    </section>
  );
}

function StringEditor({ stringKey, client, onClose, onSaved }) {
  const { fill } = useProperty();
  const fetcher = useCallback((signal) => client.getRemoteString(stringKey, signal), [client, stringKey]);
  const detail = useApiQuery(fetcher);
  const [form, setForm] = useState(null);
  const [errors, setErrors] = useState({});
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const entry = detail.data ? detail.data.string : null;

  useEffect(() => {
    if (entry && !form) setForm({ value: entry.value ?? entry.defaultValue, notes: entry.notes || '', meta: { labels: entry.labels || [], tags: entry.tags || {} }, metaProblems: [] });
  }, [entry, form]);

  const problem = entry && form ? stringValueProblem(form.value, entry) : null;
  const save = async (e) => {
    e.preventDefault();
    if (problem || (form.metaProblems && form.metaProblems.length)) return;
    setBusy(true);
    setError('');
    try {
      await client.putRemoteString(stringKey, { value: form.value, notes: form.notes.trim() || undefined, labels: form.meta.labels, tags: form.meta.tags });
      onSaved(st.saved);
    } catch (err) {
      setErrors(getFieldErrors(err));
      setError(errorMessage(err, strings.common.errorGeneric));
    } finally {
      setBusy(false);
    }
  };
  const remove = async () => {
    setBusy(true);
    setError('');
    try {
      await client.deleteRemoteString(stringKey);
      onSaved(entry.overridden ? st.removalDrafted : st.draftRemoved);
    } catch (err) {
      setError(errorMessage(err, strings.common.errorGeneric));
    } finally {
      setBusy(false);
    }
  };
  const canDelete = entry && entry.id && !entry.pendingDelete;

  return (
    <Modal
      open
      size="lg"
      onClose={onClose}
      title={st.editTitle(stringKey)}
      footer={(
        <>
          {canDelete && <button type="button" className="button button-danger" onClick={remove} disabled={busy}>{entry.overridden ? st.removeOverride : st.discardDraft}</button>}
          <button type="button" className="button button-secondary" onClick={onClose} disabled={busy}>{strings.common.cancel}</button>
          <button type="submit" form="string-form" className="button button-primary" disabled={busy || !form || Boolean(problem)}>{ac.saveDraft}</button>
        </>
      )}
    >
      {detail.error && <ErrorBanner message={errorMessage(detail.error, strings.common.errorGeneric)} onRetry={detail.reload} />}
      {!form ? (!detail.error && <Loading />) : (
        <form id="string-form" onSubmit={save} noValidate>
          {error && <ErrorBanner message={error} />}
          {entry.pendingDelete && <p className="muted">{st.pendingDeleteHelp}</p>}
          <div className="tk-string-editor">
            <div className="field">
              <span className="label-text">{st.defaultValue}</span>
              <div className="tk-string-default" data-testid="string-default">{entry.defaultValue}</div>
              <span className="field-hint">{entry.tokens && entry.tokens.length ? st.tokens(entry.tokens.map((t) => '{' + t + '}')) : st.noTokens}</span>
            </div>
            <div className="field">
              <label className="label-text" htmlFor="string-value">{st.override}</label>
              <textarea id="string-value" rows={4} value={form.value} onChange={(e) => setForm((f) => ({ ...f, value: e.target.value }))} maxLength={entry.maxLength || undefined} aria-invalid={Boolean(problem || errors.value)} aria-describedby="string-value-hint" />
              <span id="string-value-hint" className="field-hint">{st.length(form.value.length, entry.maxLength)}</span>
              {(problem || errors.value) && <span className="field-error" role="alert">{problem || errors.value}</span>}
            </div>
          </div>
          <div className="field">
            <span className="label-text">{st.preview}</span>
            <div className="tk-string-preview" data-testid="string-preview" aria-live="polite">{fill(form.value)}</div>
          </div>
          <div className="field">
            <label className="label-text" htmlFor="string-notes">{st.notes}</label>
            <input id="string-notes" value={form.notes} onChange={(e) => setForm((f) => ({ ...f, notes: e.target.value }))} maxLength={500} />
          </div>
          <MetadataEditor value={form.meta} onChange={(meta, problems) => setForm((f) => ({ ...f, meta, metaProblems: problems }))} isStaff client={client} targetType="RemoteString" collapsible />
          <StringHistory client={client} stringKey={stringKey} history={detail.data.history} onUse={(value) => setForm((f) => ({ ...f, value }))} />
        </form>
      )}
    </Modal>
  );
}

/**
 * Remote strings (docs/LIVE_UPDATES_CONTRACT.md section 14.3): the engine string catalog joined with
 * overrides, with search, client, overridden, draft, label, and tag filters; a side-by-side editor
 * (built-in text and override) with token validation and a live preview, notes, override removal,
 * and history; and the pending content drafts with Publish.
 */
function StringsView() {
  const { client } = useAdminAuth();
  const toast = useToast();
  const table = useTableState({ storageKey: 'tk.admin.strings', filterKeys: FILTERS, defaultSort: '' });
  const [editing, setEditing] = useState(null);
  const query = useMemo(() => buildStringsQuery(table.filters, { pageNumber: table.pageNumber, pageSize: table.pageSize, meta: table.meta }), [table.filters, table.pageNumber, table.pageSize, table.meta]);
  const fetcher = useCallback((signal) => client.enumerateRemoteStrings(query, signal), [client, query]);
  const list = useApiQuery(fetcher);
  const draftsFetcher = useCallback((signal) => client.getContentDrafts({ maxResults: 1 }, signal), [client]);
  const drafts = useApiQuery(draftsFetcher);
  const page = list.data ? normalizePage(list.data, table.pageSize) : null;
  const route = strings.appContent.routes.strings;

  const columns = [
    { key: 'key', label: st.key, render: (row) => <button type="button" className="link-button mono" onClick={() => setEditing(row.key)}>{row.key}</button> },
    { key: 'client', label: st.client, render: (row) => u.flags.clientNames[row.client] || row.client },
    { key: 'default', label: st.defaultValue, render: (row) => <span className="truncate">{row.defaultValue}</span> },
    { key: 'value', label: st.override, render: (row) => (row.value === null || row.value === undefined ? <span className="muted">-</span> : <span className="truncate">{row.value}</span>) },
    { key: 'state', label: u.flags.state, render: (row) => <span className="nowrap">{stringBadges(row).map((b) => <Badge key={b.label} tone={b.tone}>{b.label}</Badge>)}</span> },
    { key: 'updated', label: u.flags.updated, render: (row) => (row.updatedUtc ? formatShortDate(row.updatedUtc) : '') },
    { key: 'actions', className: 'cell-actions', label: <span className="visually-hidden">{u.columns.actions}</span>, render: (row) => <button type="button" className="button button-secondary button-sm" aria-label={st.editTitle(row.key)} onClick={() => setEditing(row.key)}>{strings.admin.common.edit}</button> }
  ];
  const yesNo = [{ value: 'true', label: u.yes }, { value: 'false', label: u.no }];
  const filterFields = [
    { key: 'search', label: st.searchLabel, placeholder: st.searchPlaceholder, grow: true },
    { key: 'client', label: st.client, type: 'select', options: optionsFrom(st.clients) },
    { key: 'overridden', label: st.overridden, type: 'select', options: yesNo },
    { key: 'hasDraftChanges', label: u.flags.draft, type: 'select', options: yesNo }
  ];

  return (
    <div className="view">
      <PageHeader title={route.label} summary={route.summary} />
      <ContentPublishPanel client={client} drafts={drafts} onChanged={list.reload} />
      <section className="panel">
        <AdminFilterPanel fields={filterFields} table={table} client={client} targetType="RemoteString" />
      </section>
      <section className="table-section">
        <TablePagination pageNumber={table.pageNumber} pageSize={table.pageSize} totalCount={page ? page.totalCount : 0} totalPages={page ? page.totalPages : 1} onPageChange={table.setPage} onPageSizeChange={table.setPageSize} onRefresh={list.reload} loading={list.loading} pageSizes={ADMIN_PAGE_SIZES} />
        <DataTable caption={route.label} columns={columns} rows={page ? page.items : []} rowKey={(row) => row.key} loading={list.loading} error={list.error ? errorMessage(list.error, '') : null} onRetry={list.reload} emptyMessage={table.hasFilters ? st.emptyFiltered : st.empty} />
      </section>
      {editing && (
        <StringEditor
          key={editing}
          stringKey={editing}
          client={client}
          onClose={() => setEditing(null)}
          onSaved={(message) => { setEditing(null); toast.success(message); list.reload(); drafts.reload(); }}
        />
      )}
    </div>
  );
}

export default StringsView;
