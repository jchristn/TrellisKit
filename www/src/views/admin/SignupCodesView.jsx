import { useCallback, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useLocalStorage from '../../hooks/useLocalStorage.js';
import useTableState from '../../hooks/useTableState.js';
import { errorMessage } from '../../utils/api.js';
import { browser } from '../../utils/browser.js';
import { copyText } from '../../utils/clipboard.js';
import { normalizePage } from '../../utils/query.js';
import { qrFileName, qrPngDataUrl } from '../../utils/qr.js';
import { ADMIN_PAGE_SIZES, CODE_DEFAULT_SORT, CODE_FILTERS, buildCodesQuery, cleanQuery } from '../../utils/adminQuery.js';
import { codeStatusTone, optionsFrom } from '../../utils/labels.js';
import { formatDateTime, formatShortDate } from '../../utils/format.js';
import PageHeader from '../../components/PageHeader.jsx';
import AdminFilterPanel from '../../components/AdminFilterPanel.jsx';
import BulkMetadataDialog from '../../components/BulkMetadataDialog.jsx';
import BulkBar from '../../components/BulkBar.jsx';
import useRowSelection from '../../hooks/useRowSelection.jsx';
import DataTable from '../../components/DataTable.jsx';
import TablePagination from '../../components/TablePagination.jsx';
import ActionMenu from '../../components/ActionMenu.jsx';
import ConfirmModal from '../../components/ConfirmModal.jsx';
import JsonViewerModal from '../../components/JsonViewerModal.jsx';
import CopyableId from '../../components/CopyableId.jsx';
import CopyButton from '../../components/CopyButton.jsx';
import Icon from '../../components/Icon.jsx';
import { Badge } from '../../components/Badges.jsx';
import { SignupCodeBatchModal, SignupCodeFormModal } from './SignupCodeForms.jsx';
import { fetchCsvPages } from '../../utils/csv.js';
import useLivePurchases from '../../hooks/useLivePurchases.js';
import { Banner } from '../../components/States.jsx';

const k = strings.admin.codes;
const c = strings.admin.common;
const col = k.columns;
const DEFAULT_HIDDEN = ['tags', 'created', 'id'];

/** "37 of 50" or "12 (unlimited)". */
export function usesText(code) {
  const count = code.redemptionCount || 0;
  return code.maxRedemptions === null || code.maxRedemptions === undefined ? k.usesUnlimited(count) : k.uses(count, code.maxRedemptions);
}

/** Join link for a code; prefers the server's `joinUrl`. */
export function joinLink(code) {
  return code.joinUrl || (window.location.origin + '/join?code=' + encodeURIComponent(code.code));
}

/** Download a PNG QR code of the join link. */
export async function downloadQr(code) {
  const dataUrl = await qrPngDataUrl(joinLink(code));
  browser.downloadDataUrl(dataUrl, qrFileName(code.code));
}

/**
 * Sign-up codes: status, grant, uses, last redeemed; create single or batch codes; edit,
 * duplicate, deactivate; copy code and join link; QR download; CSV export.
 */
function SignupCodesView() {
  const { api } = useAdminAuth();
  // Open sells nothing: codes stay readable (history) but cannot be created or edited (404 NotAvailable).
  const access = useLivePurchases();
  const readOnly = !access.enabled;
  const toast = useToast();
  const navigate = useNavigate();
  const adminClient = useAdminAuth().client;
  const table = useTableState({ storageKey: 'tk.admin.codes', filterKeys: CODE_FILTERS, defaultSort: CODE_DEFAULT_SORT });
  const [hidden, setHidden] = useLocalStorage('tk.admin.codes.hiddenColumns', DEFAULT_HIDDEN);
  const [form, setForm] = useState(null);
  const [batchOpen, setBatchOpen] = useState(false);
  const [deactivating, setDeactivating] = useState(null);
  const [deleting, setDeleting] = useState(null);
  const [jsonRow, setJsonRow] = useState(null);
  const [busy, setBusy] = useState(false);

  const query = useMemo(() => buildCodesQuery(table.filters, { pageNumber: table.pageNumber, pageSize: table.pageSize, sort: table.sort, meta: table.meta }), [table.filters, table.pageNumber, table.pageSize, table.sort, table.meta]);
  const fetcher = useCallback((signal) => api.listSignupCodes(query, signal), [api, query]);
  const list = useApiQuery(fetcher);
  const page = list.data ? normalizePage(list.data, table.pageSize) : null;
  const selection = useRowSelection(page ? page.items : [], JSON.stringify(query));
  const [metaIds, setMetaIds] = useState(null);

  const copy = async (text) => {
    try {
      await copyText(text);
      toast.success(k.copied);
    } catch {
      toast.error(k.copyFailed);
    }
  };

  const qr = async (code) => {
    try {
      await downloadQr(code);
    } catch {
      toast.error(k.detail.qrError);
    }
  };

  const exportCsv = async () => {
    setBusy(true);
    try {
      const result = await fetchCsvPages(api.exportSignupCodes, cleanQuery(table.filters, CODE_FILTERS));
      browser.download(result.text, 'signup-codes.csv', 'text/csv');
      if (result.complete) toast.success(c.exported);
      else toast.error(c.exportTruncated);
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
    } finally {
      setBusy(false);
    }
  };

  const deactivate = async () => {
    setBusy(true);
    try {
      await api.deactivateSignupCode(deactivating.id);
      toast.success(k.deactivated);
      setDeactivating(null);
      list.reload();
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
    } finally {
      setBusy(false);
    }
  };

  const remove = async () => {
    setBusy(true);
    try {
      await api.deleteSignupCode(deleting.id);
      toast.success(k.deleted);
      setDeleting(null);
      list.reload();
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
    } finally {
      setBusy(false);
    }
  };

  const onSaved = (saved, mode) => {
    setForm(null);
    toast.success(mode === 'edit' ? k.form.updated : k.form.created(saved.code));
    list.reload();
  };

  const columns = [
    selection.column((row) => row.code),
    {
      key: 'code', label: col.code, pinned: true,
      render: (row) => <span className="copyable-id"><code className="mono nowrap code-strong">{row.code}</code><CopyButton value={row.code} label={col.code} /></span>
    },
    { key: 'name', label: col.name, sortField: 'name', render: (row) => row.name },
    { key: 'status', label: col.status, pinned: true, render: (row) => <Badge tone={codeStatusTone(row.status)}>{strings.enums.codeStatus[row.status] || row.status}</Badge> },
    { key: 'grant', label: col.grant, render: (row) => (row.grantType === 'Discount' ? <span><Badge tone="info">{k.discountBadge}</Badge> {row.grantSummary}</span> : (row.grantSummary || strings.enums.grantType[row.grantType])) },
    { key: 'uses', label: col.uses, sortField: 'redemptionCount', sortDefault: 'desc', className: 'nowrap', render: (row) => usesText(row) },
    { key: 'lastRedeemed', label: col.lastRedeemed, sortField: 'lastRedeemedUtc', sortDefault: 'desc', className: 'nowrap', render: (row) => (row.lastRedeemedUtc ? <span title={formatDateTime(row.lastRedeemedUtc)}>{formatShortDate(row.lastRedeemedUtc)}</span> : <span className="muted">{c.never}</span>) },
    { key: 'expires', label: col.expires, sortField: 'expiresUtc', className: 'nowrap', render: (row) => (row.expiresUtc ? <span title={formatDateTime(row.expiresUtc)}>{formatShortDate(row.expiresUtc)}</span> : '-') },
    { key: 'tags', label: col.tags, render: (row) => (row.tags || []).map((t) => <Badge key={t}>{t}</Badge>) },
    { key: 'created', label: col.created, sortField: 'createdUtc', sortDefault: 'desc', className: 'nowrap', render: (row) => formatShortDate(row.createdUtc) },
    { key: 'id', label: col.id, className: 'cell-id', render: (row) => <CopyableId value={row.id} label={col.id} /> },
    {
      key: 'actions', pinned: true, selectorLabel: col.actions, className: 'cell-actions', label: <span className="visually-hidden">{col.actions}</span>,
      render: (row) => (
        <ActionMenu
          label={c.rowActions(row.code)}
          items={[
            { label: k.actions.open, icon: 'eye', onSelect: () => navigate('/admin/signup-codes/' + encodeURIComponent(row.id)) },
            ...(readOnly ? [] : [
              { label: c.edit, icon: 'gear', onSelect: () => setForm({ mode: 'edit', code: row }) },
              { label: c.duplicate, icon: 'copy', onSelect: () => setForm({ mode: 'duplicate', code: row }) }
            ]),
            { label: k.actions.copyCode, icon: 'copy', onSelect: () => copy(row.code) },
            { label: k.actions.copyLink, icon: 'link', onSelect: () => copy(joinLink(row)) },
            { label: k.actions.qr, icon: 'qr', onSelect: () => qr(row) },
            { label: c.viewJson, icon: 'code', onSelect: () => setJsonRow(row) },
            ...(row.isActive ? [{ label: k.actions.deactivate, icon: 'ban', danger: true, onSelect: () => setDeactivating(row) }] : []),
            ...(!readOnly && !row.redemptionCount ? [{ label: k.actions.delete, icon: 'trash', danger: true, onSelect: () => setDeleting(row) }] : [])
          ]}
        />
      )
    }
  ];

  const filterFields = [
    { key: 'search', label: k.searchLabel, placeholder: k.searchPlaceholder, grow: true },
    { key: 'status', label: k.status, type: 'select', options: optionsFrom(strings.enums.codeStatus) },
    { key: 'tag', label: k.tag, placeholder: k.tagPlaceholder }
  ];

  return (
    <div className="view">
      <PageHeader
        title={strings.admin.routes['signup-codes'].label}
        summary={strings.admin.routes['signup-codes'].summary}
        actions={(
          <>
            <button type="button" className="button button-secondary button-sm" onClick={exportCsv} disabled={busy}>
              <Icon name="download" size={14} /><span>{busy ? c.exporting : c.exportCsv}</span>
            </button>
            {access.known && !readOnly && (
              <>
                <button type="button" className="button button-secondary button-sm" onClick={() => setBatchOpen(true)}>
                  <Icon name="plus" size={14} /><span>{k.createBatch}</span>
                </button>
                <button type="button" className="button button-primary button-sm" onClick={() => setForm({ mode: 'create' })}>
                  <Icon name="plus" size={14} /><span>{k.create}</span>
                </button>
              </>
            )}
          </>
        )}
      />
      {access.known && readOnly && <Banner tone="info" icon="info">{strings.admin.accessModel.readOnlyOpen}</Banner>}
      <section className="panel">
        <AdminFilterPanel fields={filterFields} table={table} client={adminClient} targetType="SignupCode" />
      </section>
      <section className="table-section">
        <div className="table-toolbar">
          <div className="toolbar-left"><BulkBar count={selection.selected.length} onClear={selection.clear} onMetadata={() => setMetaIds(selection.selected)} /></div>
        </div>
        <TablePagination
          pageNumber={table.pageNumber}
          pageSize={table.pageSize}
          totalCount={page ? page.totalCount : 0}
          totalPages={page ? page.totalPages : 1}
          onPageChange={table.setPage}
          onPageSizeChange={table.setPageSize}
          onRefresh={list.reload}
          loading={list.loading}
          pageSizes={ADMIN_PAGE_SIZES}
        />
        <DataTable
          caption={strings.admin.routes['signup-codes'].label}
          columns={columns.filter((x) => x.pinned || !hidden.includes(x.key))}
          columnChoices={columns.map((x) => ({ key: x.key, label: x.selectorLabel || x.label, pinned: x.pinned }))}
          columnsHidden={hidden}
          onColumnsChange={setHidden}
          rows={page ? page.items : []}
          rowKey={(row) => row.id}
          loading={list.loading}
          error={list.error ? c.unavailable(strings.admin.routes['signup-codes'].label) + ' ' + errorMessage(list.error, '') : null}
          onRetry={list.reload}
          emptyMessage={table.hasFilters ? k.emptyFiltered : k.empty}
          onRowClick={(row) => (readOnly ? navigate('/admin/signup-codes/' + encodeURIComponent(row.id)) : setForm({ mode: 'edit', code: row }))}
          sort={table.sort}
          onSortChange={table.setSort}
        />
      </section>

      {form && <SignupCodeFormModal mode={form.mode} code={form.code} onClose={() => setForm(null)} onSaved={onSaved} />}
      {batchOpen && <SignupCodeBatchModal onClose={() => setBatchOpen(false)} onCreated={() => list.reload()} />}
      <ConfirmModal
        open={Boolean(deactivating)}
        title={deactivating ? k.deactivateTitle(deactivating.code) : ''}
        message={k.deactivateBody}
        confirmLabel={k.actions.deactivate}
        busy={busy}
        onConfirm={deactivate}
        onCancel={() => setDeactivating(null)}
      />
      <ConfirmModal
        open={Boolean(deleting)}
        title={deleting ? k.deleteTitle(deleting.code) : ''}
        message={k.deleteBody}
        confirmLabel={k.actions.delete}
        busy={busy}
        onConfirm={remove}
        onCancel={() => setDeleting(null)}
      />
      {metaIds && <BulkMetadataDialog open targetType="SignupCode" ids={metaIds} onClose={() => setMetaIds(null)} onDone={() => { selection.clear(); list.reload(); }} />}
      <JsonViewerModal open={Boolean(jsonRow)} onClose={() => setJsonRow(null)} subtitle={jsonRow && jsonRow.code} value={jsonRow} />
    </div>
  );
}

export default SignupCodesView;
