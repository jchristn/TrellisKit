import { useCallback, useMemo, useState } from 'react';
import strings from '../../strings.js';
import { usePlatformAuth } from '../../context/PlatformAuthContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useTableState from '../../hooks/useTableState.js';
import { errorMessage } from '../../utils/api.js';
import { normalizePage } from '../../utils/query.js';
import { ADMIN_PAGE_SIZES } from '../../utils/adminQuery.js';
import { PLATFORM_AUDIT_TABLE_FILTERS, buildPlatformAuditQuery } from '../../utils/platformQuery.js';
import { formatPreciseDateTime, formatShortDateTime } from '../../utils/format.js';
import PageHeader from '../../components/PageHeader.jsx';
import AdminFilterPanel from '../../components/AdminFilterPanel.jsx';
import DataTable from '../../components/DataTable.jsx';
import TablePagination from '../../components/TablePagination.jsx';
import ActionMenu from '../../components/ActionMenu.jsx';
import JsonViewerModal from '../../components/JsonViewerModal.jsx';
import CopyableId from '../../components/CopyableId.jsx';

const a = strings.platform.audit;
const col = a.columns;
const c = strings.admin.common;
const route = strings.platform.routes.audit;

export const PLATFORM_AUDIT_ACTIONS = Object.freeze([
  'platform.login', 'platform.admin.bootstrap', 'platform.admin.invite', 'platform.admin.accept', 'platform.admin.remove',
  'platform.property.create', 'platform.property.update', 'platform.property.enable', 'platform.property.disable',
  'platform.property.delete', 'platform.property.restore', 'platform.property.purge',
  'platform.property.admin.grant', 'platform.property.admin.revoke', 'platform.delegate', 'platform.settings.update', 'platform.server.restart'
]);

/**
 * Platform audit (POST /platform/audit/enumerate). Without a property filter it reads the
 * platform realm's audit; `filters.propertyId` reads one property's audit trail. Embedded in a
 * property's detail page, the property is fixed and the table state uses its own URL prefix.
 */
function PlatformAuditView({ propertyId = '', embedded = false }) {
  const { call } = usePlatformAuth();
  const table = useTableState({ storageKey: 'tk.platform.audit', filterKeys: PLATFORM_AUDIT_TABLE_FILTERS, defaultSort: '', prefix: embedded ? 'a.' : '' });
  const [jsonRow, setJsonRow] = useState(null);
  const filters = useMemo(() => (propertyId ? { ...table.filters, propertyId } : table.filters), [table.filters, propertyId]);
  const query = useMemo(() => buildPlatformAuditQuery(filters, { pageNumber: table.pageNumber, pageSize: table.pageSize, meta: table.meta }), [filters, table.pageNumber, table.pageSize, table.meta]);
  const fetcher = useCallback((signal) => call((client) => client.listPlatformAudit(query, signal)), [call, query]);
  const list = useApiQuery(fetcher);
  const page = list.data ? normalizePage(list.data, table.pageSize) : null;

  const columns = [
    { key: 'time', label: col.time, className: 'nowrap', render: (row) => <span title={row.createdUtc + ' (' + formatPreciseDateTime(row.createdUtc) + ')'}>{formatShortDateTime(row.createdUtc)}</span> },
    { key: 'actor', label: col.actor, render: (row) => <span className="member-cell-text"><span>{row.actorName || '-'}</span>{row.actorUserId && <code className="mono small">{row.actorUserId}</code>}</span> },
    { key: 'action', label: col.action, render: (row) => <code className="mono">{row.action}</code> },
    { key: 'target', label: col.target, render: (row) => <span className="member-cell-text"><span>{row.targetType}</span>{row.targetId && <code className="mono small">{row.targetId}</code>}</span> },
    { key: 'summary', label: col.summary, className: 'cell-wrap', render: (row) => row.summary },
    { key: 'id', label: col.id, className: 'cell-id', render: (row) => <CopyableId value={row.id} label={col.id} /> },
    { key: 'actions', className: 'cell-actions', label: <span className="visually-hidden">{col.actions}</span>, render: (row) => <ActionMenu label={c.rowActions(row.id)} items={[{ label: c.viewJson, icon: 'code', onSelect: () => setJsonRow(row) }]} /> }
  ];

  const filterFields = [
    { key: 'search', label: a.search, placeholder: a.searchPlaceholder },
    ...(propertyId ? [] : [{ key: 'propertyId', label: a.propertyId, placeholder: a.propertyIdPlaceholder, mono: true }]),
    { key: 'action', label: a.action, mono: true, list: 'platform-audit-actions' },
    { key: 'actorUserId', label: a.actor, mono: true },
    { key: 'targetType', label: a.targetType },
    { key: 'targetId', label: a.targetId, mono: true },
    { key: 'fromUtc', label: strings.admin.audit.from, type: 'datetime' },
    { key: 'toUtc', label: strings.admin.audit.to, type: 'datetime' }
  ];

  const body = (
    <>
      <section className="panel">
        <datalist id="platform-audit-actions">{PLATFORM_AUDIT_ACTIONS.map((x) => <option key={x} value={x} />)}</datalist>
        <AdminFilterPanel fields={filterFields} table={table} client={null} targetType="AuditRecord" />
        {!propertyId && <p className="muted small">{a.propertyIdHint}</p>}
      </section>
      <section className="table-section">
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
          caption={route.label}
          columns={columns}
          rows={page ? page.items : []}
          rowKey={(row) => row.id}
          loading={list.loading}
          error={list.error ? c.unavailable(route.label) + ' ' + errorMessage(list.error, '') : null}
          onRetry={list.reload}
          emptyMessage={table.hasFilters ? a.emptyFiltered : a.empty}
          onRowClick={(row) => setJsonRow(row)}
        />
      </section>
      <JsonViewerModal open={Boolean(jsonRow)} onClose={() => setJsonRow(null)} subtitle={jsonRow && (jsonRow.action + ' ' + jsonRow.id)} value={jsonRow} />
    </>
  );

  if (embedded) return body;
  return (
    <div className="view">
      <PageHeader title={route.label} summary={route.summary} />
      {body}
    </div>
  );
}

export default PlatformAuditView;
