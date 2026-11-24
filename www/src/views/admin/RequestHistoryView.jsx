import { useCallback, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useLocalStorage from '../../hooks/useLocalStorage.js';
import { errorMessage } from '../../utils/api.js';
import {
  DEFAULT_PAGE_SIZE, HTTP_METHODS, PAGE_SIZES, buildRequestHistoryQuery, buildSummaryQuery, cleanFilters, describeFilters,
  filtersFromSearchParams, filtersToSearchParams, hasFilters, normalizePage
} from '../../utils/query.js';
import { rangeToParams } from '../../utils/time.js';
import { formatBytes, formatPreciseDateTime, formatDuration, formatNumber, formatPercent, formatShortDateTime } from '../../utils/format.js';
import PageHeader from '../../components/PageHeader.jsx';
import KpiCard from '../../components/KpiCard.jsx';
import ActivityChart from '../../components/ActivityChart.jsx';
import RangeSelector from '../../components/RangeSelector.jsx';
import AdminFilterPanel from '../../components/AdminFilterPanel.jsx';
import { readMeta, writeMeta } from '../../hooks/useEnumerationUrlState.js';
import DataTable from '../../components/DataTable.jsx';
import TablePagination from '../../components/TablePagination.jsx';
import ActionMenu from '../../components/ActionMenu.jsx';
import ConfirmModal from '../../components/ConfirmModal.jsx';
import JsonViewerModal from '../../components/JsonViewerModal.jsx';
import RequestDetailsModal from '../../components/RequestDetailsModal.jsx';
import CopyableId from '../../components/CopyableId.jsx';
import Icon from '../../components/Icon.jsx';
import { MethodBadge, StatusBadge } from '../../components/Badges.jsx';
import { ErrorBanner, Loading } from '../../components/States.jsx';

const r = strings.admin.requests;
const col = r.columns;
const DEFAULT_HIDDEN = ['requestBytes', 'responseBytes', 'id'];

/**
 * Request history: KPI strip, activity chart, backend filters, server-paged table, request
 * inspector, and single, selected, and filter-based deletion. Filters and paging live in the
 * URL so links (for example from a Home chart bucket) open a filtered view.
 */
function RequestHistoryView() {
  const { api, client } = useAdminAuth();
  const toast = useToast();
  const [searchParams, setSearchParams] = useSearchParams();
  const [storedPageSize, setStoredPageSize] = useLocalStorage('tk.admin.requests.pageSize', DEFAULT_PAGE_SIZE);
  const [hidden, setHidden] = useLocalStorage('tk.admin.requests.hiddenColumns', DEFAULT_HIDDEN);
  const [rangeId, setStoredRange] = useLocalStorage('tk.admin.requests.range', 'day');
  const [now, setNow] = useState(() => new Date());

  // Keyed on the serialized filters so paging does not re-run the summary query.
  const filterKey = filtersToSearchParams(filtersFromSearchParams(searchParams)).toString();
  const filters = useMemo(() => filtersFromSearchParams(new URLSearchParams(filterKey)), [filterKey]);
  const pageNumber = Math.max(1, parseInt(searchParams.get('page') || '1', 10) || 1);
  const sizeParam = parseInt(searchParams.get('pageSize') || '', 10);
  const pageSize = PAGE_SIZES.includes(sizeParam) ? sizeParam : (PAGE_SIZES.includes(storedPageSize) ? storedPageSize : DEFAULT_PAGE_SIZE);

  const metaText = JSON.stringify(readMeta(searchParams));
  const meta = useMemo(() => JSON.parse(metaText), [metaText]);
  const listQuery = useMemo(() => buildRequestHistoryQuery(filters, { pageNumber, pageSize, meta }), [filters, pageNumber, pageSize, meta]);
  const listFetcher = useCallback((signal) => api.getRequestHistory(listQuery, signal), [api, listQuery]);
  const list = useApiQuery(listFetcher);
  const page = list.data ? normalizePage(list.data, pageSize) : null;

  const chartParams = useMemo(() => rangeToParams(rangeId, now), [rangeId, now]);
  const summaryQuery = useMemo(() => buildSummaryQuery(filters, chartParams), [filters, chartParams]);
  const summaryFetcher = useCallback((signal) => api.getRequestHistorySummary(summaryQuery, signal), [api, summaryQuery]);
  const summary = useApiQuery(summaryFetcher);
  const s = summary.data;

  const [selection, setSelection] = useState({ key: '', ids: [] });
  const selectionKey = filterKey + '|' + pageNumber + '|' + pageSize;
  const selected = selection.key === selectionKey ? selection.ids : [];
  const setSelected = (ids) => setSelection({ key: selectionKey, ids });

  const [detailId, setDetailId] = useState(null);
  const [jsonRow, setJsonRow] = useState(null);
  const [confirm, setConfirm] = useState(null);
  const [busy, setBusy] = useState(false);

  const update = (nextFilters, nextPage = 1, nextSize = pageSize, nextMeta = meta) => {
    const extra = { page: nextPage > 1 ? nextPage : '', pageSize: nextSize !== DEFAULT_PAGE_SIZE ? nextSize : '' };
    setSearchParams(writeMeta(filtersToSearchParams(nextFilters, extra), { ...nextMeta, search: '', ordering: '' }));
  };
  // One filter panel (form, label, and tag filters) over the URL state, with one Apply.
  const filterTable = {
    filters,
    meta,
    filterKey: filterKey + '|' + metaText,
    applyAll: (nextFilters, nextMeta) => update(nextFilters, 1, pageSize, nextMeta || meta),
    clearAll: () => update({}, 1, pageSize, { labels: [], labelMatch: meta.labelMatch, tags: {} })
  };
  const filterFields = [
    { key: 'method', label: r.method, type: 'select', options: HTTP_METHODS.map((x) => ({ value: x, label: x })) },
    { key: 'statusCode', label: r.statusCode, placeholder: r.statusPlaceholder },
    { key: 'pathContains', label: r.pathContains, placeholder: r.pathPlaceholder, grow: true, mono: true },
    ...Object.entries(strings.chartKit.requestFilters).map(([key, label]) => ({ key, label, mono: true })),
    { key: 'fromUtc', label: r.fromUtc, type: 'datetime' },
    { key: 'toUtc', label: r.toUtc, type: 'datetime' }
  ];
  const validateFilters = (values) => {
    const status = values.statusCode || '';
    if (status && !(/^\d{3}$/.test(status) && Number(status) >= 100 && Number(status) <= 599)) return r.statusInvalid;
    if (values.statusClass && !/^[1-5]xx$/.test(values.statusClass)) return strings.chartKit.statusClassInvalid;
    if (values.fromUtc && values.toUtc && new Date(values.fromUtc) >= new Date(values.toUtc)) return r.rangeInvalid;
    return '';
  };

  const applyFilters = (next) => update(next, 1);
  const goToPage = (n) => update(filters, n);
  const changePageSize = (size) => {
    setStoredPageSize(size);
    update(filters, 1, size);
  };
  const setRangeId = (id) => {
    setStoredRange(id);
    setNow(new Date());
  };
  const refreshAll = () => {
    list.reload();
    setNow(new Date());
  };
  const onBucketClick = (bucket) => applyFilters({ ...filters, fromUtc: bucket.bucketStartUtc, toUtc: bucket.bucketEndUtc });

  const runConfirm = async () => {
    if (!confirm) return;
    setBusy(true);
    try {
      if (confirm.kind === 'one') {
        await api.deleteRequestHistoryEntry(confirm.id);
        toast.success(r.deletedOne);
        if (detailId === confirm.id) setDetailId(null);
      } else if (confirm.kind === 'selected') {
        let count = 0;
        for (const id of confirm.ids) {
          await api.deleteRequestHistoryEntry(id);
          count += 1;
        }
        toast.success(r.deletedCount(count));
      } else {
        const result = await api.deleteRequestHistoryBulk(cleanFilters(filters));
        toast.success(r.deletedCount(result && typeof result.deletedCount === 'number' ? result.deletedCount : 0));
      }
      setSelected([]);
      setConfirm(null);
      if (confirm.kind !== 'one' && pageNumber > 1) update(filters, 1);
      refreshAll();
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
    } finally {
      setBusy(false);
    }
  };

  const rows = page ? page.items : [];
  const allSelected = rows.length > 0 && rows.every((row) => selected.includes(row.id));
  const toggleRow = (id) => setSelected(selected.includes(id) ? selected.filter((x) => x !== id) : [...selected, id]);
  const toggleAll = () => setSelected(allSelected ? [] : rows.map((row) => row.id));

  const columns = [
    {
      key: 'select', pinned: true, label: <input type="checkbox" checked={allSelected} onChange={toggleAll} aria-label={r.selectAll} disabled={rows.length === 0} />,
      selectorLabel: col.select, className: 'cell-check',
      render: (row) => <input type="checkbox" checked={selected.includes(row.id)} onChange={() => toggleRow(row.id)} aria-label={r.selectRow(row.id)} />
    },
    { key: 'time', label: col.time, pinned: true, className: 'nowrap', render: (row) => <span title={row.createdUtc + ' (' + formatPreciseDateTime(row.createdUtc) + ')'}>{formatShortDateTime(row.createdUtc)}</span> },
    { key: 'method', label: col.method, pinned: true, render: (row) => <MethodBadge method={row.method} /> },
    { key: 'path', label: col.path, pinned: true, className: 'cell-path', render: (row) => <code className="mono truncate" title={row.url || row.path}>{row.path}</code> },
    { key: 'status', label: col.status, pinned: true, render: (row) => <StatusBadge code={row.statusCode} /> },
    { key: 'duration', label: col.duration, className: 'num nowrap', render: (row) => formatDuration(row.durationMs) },
    { key: 'principal', label: col.principal, className: 'hide-narrow', render: (row) => row.principalName || <span className="muted">{r.anonymous}</span> },
    { key: 'sourceIp', label: col.sourceIp, render: (row) => <code className="mono nowrap">{row.sourceIp}</code> },
    { key: 'requestBytes', label: col.requestBytes, className: 'num nowrap', render: (row) => formatBytes(row.requestBodyBytes) },
    { key: 'responseBytes', label: col.responseBytes, className: 'num nowrap', render: (row) => formatBytes(row.responseBodyBytes) },
    { key: 'id', label: col.id, className: 'cell-id', render: (row) => <CopyableId value={row.id} label={col.id} /> },
    {
      key: 'actions', label: <span className="visually-hidden">{col.actions}</span>, selectorLabel: col.actions, pinned: true, className: 'cell-actions',
      render: (row) => (
        <ActionMenu
          label={r.actions.menu(row.id)}
          items={[
            { label: r.actions.view, icon: 'eye', onSelect: () => setDetailId(row.id) },
            { label: r.actions.viewJson, icon: 'code', onSelect: () => setJsonRow(row) },
            { label: r.actions.delete, icon: 'trash', danger: true, onSelect: () => setConfirm({ kind: 'one', id: row.id }) }
          ]}
        />
      )
    }
  ];
  const visibleColumns = columns.filter((c) => c.pinned || !hidden.includes(c.key));
  const selectorColumns = columns.map((c) => ({ key: c.key, label: c.selectorLabel || c.label, pinned: c.pinned }));

  const filtered = hasFilters(filters);
  const emptyMessage = filtered ? r.emptyNoMatch : r.emptyNoTraffic;
  const listError = list.error ? (page ? errorMessage(list.error, r.unavailable) : r.unavailable + ' ' + errorMessage(list.error, '')) : null;
  const total = page ? page.totalCount : 0;

  let confirmTitle = '';
  let confirmMessage = '';
  if (confirm && confirm.kind === 'one') {
    confirmTitle = r.deleteOneTitle;
    confirmMessage = r.deleteOneBody(confirm.id);
  } else if (confirm && confirm.kind === 'selected') {
    confirmTitle = r.deleteSelectedTitle;
    confirmMessage = r.deleteSelectedBody(confirm.ids.length);
  } else if (confirm) {
    confirmTitle = r.deleteMatchingTitle;
    confirmMessage = filtered ? r.deleteMatchingBody(formatNumber(total), describeFilters(filters)) : r.deleteAllBody(formatNumber(total));
  }

  return (
    <div className="view">
      <PageHeader title={strings.admin.routes.requests.label} summary={strings.admin.routes.requests.summary} />

      <div className="kpi-grid">
        <KpiCard label={r.kpiRetained} value={s ? formatNumber(s.totalCount) : '-'} hint={strings.admin.chart.ranges[rangeId]} />
        <KpiCard label={r.kpiFailures} value={s ? formatNumber(s.totalFailure) : '-'} tone={s && s.totalFailure > 0 ? 'danger' : undefined} />
        <KpiCard label={r.kpiSuccessRate} value={s ? (s.totalCount > 0 ? formatPercent(s.totalSuccess / s.totalCount) : '0.0%') : '-'} />
        <KpiCard label={r.kpiAvg} value={s ? formatDuration(s.averageDurationMs) : '-'} />
        <KpiCard label={r.kpiMatching} value={page ? formatNumber(page.totalCount) : '-'} />
      </div>

      <section className="panel" aria-label={strings.admin.home.activityTitle}>
        <div className="panel-header">
          <h2>{strings.admin.home.activityTitle}</h2>
          <div className="panel-actions">
            <RangeSelector value={rangeId} onChange={setRangeId} disabled={summary.loading} />
            <button type="button" className="icon-button" onClick={() => setNow(new Date())} disabled={summary.loading} aria-label={strings.common.refresh} title={strings.common.refresh}>
              <Icon name="refresh" className={summary.loading ? 'spin' : ''} />
            </button>
          </div>
        </div>
        {summary.error && <ErrorBanner message={strings.admin.home.summaryError + ' ' + errorMessage(summary.error, '')} onRetry={summary.reload} />}
        {summary.loading && !s ? <Loading /> : <ActivityChart summary={s} rangeId={rangeId} fromUtc={chartParams.fromUtc} onBucketClick={onBucketClick} />}
      </section>

      <section className="panel" aria-label={r.filtersTitle}>
        <AdminFilterPanel fields={filterFields} table={filterTable} client={client} targetType="RequestHistory" validate={validateFilters} disabled={busy} />
      </section>

      <section className="table-section">
        <div className="table-toolbar">
          <div className="toolbar-left">
            {selected.length > 0 ? (
              <div className="bulk-bar" role="region" aria-label={r.selectedCount(selected.length)}>
                <span>{r.selectedCount(selected.length)}</span>
                <button type="button" className="button button-danger button-sm" onClick={() => setConfirm({ kind: 'selected', ids: selected })}>
                  <Icon name="trash" size={14} /><span>{r.deleteSelected(selected.length)}</span>
                </button>
                <button type="button" className="button button-secondary button-sm" onClick={() => setSelected([])}>{r.clearSelection}</button>
              </div>
            ) : (
              <span className="muted small">{r.sortNote}</span>
            )}
          </div>
          <div className="toolbar-right">
            <button type="button" className="button button-danger-outline button-sm" onClick={() => setConfirm({ kind: 'matching' })} disabled={!page || total === 0}>
              <Icon name="trash" size={14} /><span>{r.deleteMatching}</span>
            </button>
          </div>
        </div>
        <TablePagination
          pageNumber={pageNumber}
          pageSize={pageSize}
          totalCount={total}
          totalPages={page ? page.totalPages : 1}
          onPageChange={goToPage}
          onPageSizeChange={changePageSize}
          onRefresh={refreshAll}
          loading={list.loading}
        />
        <DataTable
          caption={strings.admin.routes.requests.label}
          columns={visibleColumns}
          columnChoices={selectorColumns}
          columnsHidden={hidden}
          onColumnsChange={setHidden}
          rows={rows}
          rowKey={(row) => row.id}
          loading={list.loading}
          error={listError}
          onRetry={list.reload}
          emptyMessage={emptyMessage}
          onRowClick={(row) => setDetailId(row.id)}
          rowClassName={(row) => (row.statusCode >= 400 ? 'row-failed' : '')}
        />
      </section>

      {detailId && <RequestDetailsModal id={detailId} onClose={() => setDetailId(null)} />}
      <JsonViewerModal open={Boolean(jsonRow)} onClose={() => setJsonRow(null)} subtitle={jsonRow && jsonRow.id} value={jsonRow} />
      <ConfirmModal
        open={Boolean(confirm)}
        title={confirmTitle}
        message={confirmMessage}
        confirmLabel={strings.common.delete}
        busy={busy}
        onConfirm={runConfirm}
        onCancel={() => setConfirm(null)}
      />
    </div>
  );
}

export default RequestHistoryView;
