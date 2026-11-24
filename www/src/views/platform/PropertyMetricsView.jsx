import { useCallback, useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { ENUMERATION_MAX_RESULTS_ADMIN, PropertyMetricsOrderings, PropertyStatus } from '@trelliskit/shared';
import strings from '../../strings.js';
import { usePlatformAuth } from '../../context/PlatformAuthContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useTableState from '../../hooks/useTableState.js';
import { errorMessage } from '../../utils/api.js';
import { normalizePage } from '../../utils/query.js';
import { ADMIN_PAGE_SIZES } from '../../utils/adminQuery.js';
import { PROPERTY_METRICS_TABLE_FILTERS, buildPropertyMetricsQuery, platformMetadataSource } from '../../utils/platformQuery.js';
import { formatBytes, formatNumber } from '../../utils/format.js';
import PageHeader from '../../components/PageHeader.jsx';
import KpiCard from '../../components/KpiCard.jsx';
import AdminFilterPanel from '../../components/AdminFilterPanel.jsx';
import DataTable from '../../components/DataTable.jsx';
import TablePagination from '../../components/TablePagination.jsx';
import ActionMenu from '../../components/ActionMenu.jsx';
import JsonViewerModal from '../../components/JsonViewerModal.jsx';
import { ErrorBanner } from '../../components/States.jsx';
import { MetadataCell, PropertyStatusBadge } from './PropertiesView.jsx';

const t = strings.platform.metrics;
const col = t.columns;
const c = strings.admin.common;
const route = strings.platform.routes.metrics;

/** Totals of a page of metrics rows (members, active, requests, errors, storage). */
export function metricsTotals(rows) {
  const totals = { properties: 0, members: 0, activeMembers: 0, requests24h: 0, errors24h: 0, storageBytes: 0 };
  for (const row of rows || []) {
    totals.properties += 1;
    totals.members += Number(row.members) || 0;
    totals.activeMembers += Number(row.activeMembers) || 0;
    totals.requests24h += Number(row.requests24h) || 0;
    totals.errors24h += Number(row.errors24h) || 0;
    totals.storageBytes += Number(row.storageBytes) || 0;
  }
  return totals;
}

const detailPath = (row) => '/platform/properties/' + encodeURIComponent(row.propertyId);

/**
 * Property metrics: KPI totals over the matching properties, and one row per property with
 * members, active members (30 days), requests and server errors (24 hours), and storage. Status,
 * search, ordering, label, and tag filters; bounded by deployment.maxProperties.
 */
function PropertyMetricsView() {
  const { call, client } = usePlatformAuth();
  const navigate = useNavigate();
  const metadataSource = useMemo(() => platformMetadataSource(client), [client]);
  const table = useTableState({ storageKey: 'tk.platform.metrics', filterKeys: PROPERTY_METRICS_TABLE_FILTERS, defaultSort: '' });
  const [jsonRow, setJsonRow] = useState(null);

  const query = useMemo(() => buildPropertyMetricsQuery(table.filters, { pageNumber: table.pageNumber, pageSize: table.pageSize, meta: table.meta }), [table.filters, table.pageNumber, table.pageSize, table.meta]);
  const fetcher = useCallback((signal) => call((x) => x.listPropertyMetrics(query, signal)), [call, query]);
  const list = useApiQuery(fetcher);
  const page = list.data ? normalizePage(list.data, table.pageSize) : null;

  // KPI totals cover every matching property (one bounded call), not only the visible page.
  const totalsQuery = useMemo(() => buildPropertyMetricsQuery(table.filters, { pageNumber: 1, pageSize: ENUMERATION_MAX_RESULTS_ADMIN, meta: table.meta }), [table.filters, table.meta]);
  const totalsFetcher = useCallback((signal) => call((x) => x.listPropertyMetrics(totalsQuery, signal)), [call, totalsQuery]);
  const totalsResult = useApiQuery(totalsFetcher);
  const totalsPage = totalsResult.data ? normalizePage(totalsResult.data, ENUMERATION_MAX_RESULTS_ADMIN) : null;
  const totals = totalsPage ? metricsTotals(totalsPage.items) : null;
  const partial = totalsPage && totalsPage.totalCount > totalsPage.items.length;

  const refresh = () => {
    list.reload();
    totalsResult.reload();
  };

  const columns = [
    {
      key: 'name', label: col.name, render: (row) => (
        <span className="member-cell-text">
          <Link to={detailPath(row)} onClick={(e) => e.stopPropagation()}>{row.name}</Link>
          {row.slug && <code className="mono small">{row.slug}</code>}
        </span>
      )
    },
    { key: 'status', label: col.status, render: (row) => <PropertyStatusBadge record={{ ...row, id: row.propertyId }} /> },
    { key: 'members', label: col.members, className: 'num', render: (row) => formatNumber(row.members) },
    { key: 'active', label: col.active, className: 'num', render: (row) => formatNumber(row.activeMembers) },
    { key: 'requests', label: col.requests, className: 'num', render: (row) => formatNumber(row.requests24h) },
    {
      key: 'errors', label: col.errors, className: 'num', render: (row) => (row.errors24h > 0
        ? <span className="tone-danger" title={t.kpiErrorsHint}>{formatNumber(row.errors24h)}</span>
        : formatNumber(row.errors24h))
    },
    { key: 'storage', label: col.storage, className: 'num nowrap', render: (row) => formatBytes(row.storageBytes) },
    { key: 'metadata', defaultHidden: true, label: col.metadata, render: (row) => <MetadataCell labels={row.labels} tags={row.tags} /> },
    {
      key: 'actions', className: 'cell-actions', label: <span className="visually-hidden">{col.actions}</span>,
      render: (row) => (
        <ActionMenu
          label={c.rowActions(row.name)}
          items={[
            { label: t.open, icon: 'external', onSelect: () => navigate(detailPath(row)) },
            { label: t.viewRequests, icon: 'activity', onSelect: () => navigate('/platform/requests?propertyId=' + encodeURIComponent(row.propertyId)) },
            { label: c.viewJson, icon: 'code', onSelect: () => setJsonRow(row) }
          ]}
        />
      )
    }
  ];

  const filterFields = [
    { key: 'search', label: t.search, placeholder: t.searchPlaceholder },
    { key: 'status', label: t.status, type: 'select', options: Object.values(PropertyStatus).map((s) => ({ value: s, label: strings.platform.status[s] })) },
    { key: 'ordering', label: t.ordering, type: 'select', options: PropertyMetricsOrderings.map((o) => ({ value: o, label: strings.platform.orderings[o] })) }
  ];

  return (
    <div className="view">
      <PageHeader title={route.label} summary={route.summary} />
      <div className="kpi-grid">
        <KpiCard label={t.kpiProperties} value={totals ? formatNumber(totals.properties) : '-'} />
        <KpiCard label={t.kpiMembers} value={totals ? formatNumber(totals.members) : '-'} />
        <KpiCard label={t.kpiActive} value={totals ? formatNumber(totals.activeMembers) : '-'} hint={t.kpiActiveHint} />
        <KpiCard label={t.kpiRequests} value={totals ? formatNumber(totals.requests24h) : '-'} />
        <KpiCard label={t.kpiErrors} value={totals ? formatNumber(totals.errors24h) : '-'} tone={totals && totals.errors24h > 0 ? 'danger' : undefined} hint={t.kpiErrorsHint} />
        <KpiCard label={t.kpiStorage} value={totals ? formatBytes(totals.storageBytes) : '-'} />
      </div>
      {partial && <p className="muted small">{t.kpiPartial(totalsPage.items.length)}</p>}
      {totalsResult.error && <ErrorBanner message={c.unavailable(route.label) + ' ' + errorMessage(totalsResult.error, '')} onRetry={totalsResult.reload} />}
      <section className="panel">
        <AdminFilterPanel fields={filterFields} table={table} client={metadataSource} targetType="Property" />
      </section>
      <section className="table-section">
        <TablePagination
          pageNumber={table.pageNumber}
          pageSize={table.pageSize}
          totalCount={page ? page.totalCount : 0}
          totalPages={page ? page.totalPages : 1}
          onPageChange={table.setPage}
          onPageSizeChange={table.setPageSize}
          onRefresh={refresh}
          loading={list.loading}
          pageSizes={ADMIN_PAGE_SIZES}
        />
        <DataTable
          caption={route.label}
          columns={columns}
          rows={page ? page.items : []}
          rowKey={(row) => row.propertyId}
          loading={list.loading}
          error={list.error ? c.unavailable(route.label) + ' ' + errorMessage(list.error, '') : null}
          onRetry={list.reload}
          emptyMessage={table.hasFilters ? t.emptyFiltered : t.empty}
          onRowClick={(row) => navigate(detailPath(row))}
          rowClassName={(row) => (row.errors24h > 0 ? 'row-failed' : '')}
        />
      </section>
      <JsonViewerModal open={Boolean(jsonRow)} onClose={() => setJsonRow(null)} subtitle={jsonRow && jsonRow.propertyId} value={jsonRow} />
    </div>
  );
}

export default PropertyMetricsView;
