import { useCallback, useMemo, useState } from 'react';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useLocalStorage from '../../hooks/useLocalStorage.js';
import useTableState from '../../hooks/useTableState.js';
import { errorMessage } from '../../utils/api.js';
import { normalizePage } from '../../utils/query.js';
import { ADMIN_PAGE_SIZES, AUDIT_ACTIONS, AUDIT_DEFAULT_SORT, AUDIT_FILTERS, buildAuditQuery } from '../../utils/adminQuery.js';
import { formatPreciseDateTime, formatShortDateTime } from '../../utils/format.js';
import PageHeader from '../../components/PageHeader.jsx';
import AdminFilterPanel from '../../components/AdminFilterPanel.jsx';
import DataTable from '../../components/DataTable.jsx';
import TablePagination from '../../components/TablePagination.jsx';
import ActionMenu from '../../components/ActionMenu.jsx';
import JsonViewerModal from '../../components/JsonViewerModal.jsx';
import CopyableId from '../../components/CopyableId.jsx';

const a = strings.admin.audit;
const c = strings.admin.common;
const col = a.columns;
const DEFAULT_HIDDEN = ['sourceIp', 'id'];

/** Audit Log: filterable, server-sorted record of administrative actions with JSON detail. */
function AuditLogView() {
  const { api } = useAdminAuth();
  const adminClient = useAdminAuth().client;
  const table = useTableState({ storageKey: 'tk.admin.audit', filterKeys: AUDIT_FILTERS, defaultSort: AUDIT_DEFAULT_SORT });
  const [hidden, setHidden] = useLocalStorage('tk.admin.audit.hiddenColumns', DEFAULT_HIDDEN);
  const [jsonRow, setJsonRow] = useState(null);

  const query = useMemo(() => buildAuditQuery(table.filters, { pageNumber: table.pageNumber, pageSize: table.pageSize, sort: table.sort, meta: table.meta }), [table.filters, table.pageNumber, table.pageSize, table.sort, table.meta]);
  const fetcher = useCallback((signal) => api.listAudit(query, signal), [api, query]);
  const list = useApiQuery(fetcher);
  const page = list.data ? normalizePage(list.data, table.pageSize) : null;

  const columns = [
    { key: 'time', label: col.time, pinned: true, sortField: 'createdUtc', sortDefault: 'desc', className: 'nowrap', render: (row) => <span title={row.createdUtc + ' (' + formatPreciseDateTime(row.createdUtc) + ')'}>{formatShortDateTime(row.createdUtc)}</span> },
    { key: 'actor', label: col.actor, render: (row) => (row.actorName || row.actorUserId ? <span className="member-cell-text"><span>{row.actorName || '-'}</span>{row.actorUserId && <code className="mono small">{row.actorUserId}</code>}</span> : <span className="muted">{a.system}</span>) },
    { key: 'action', label: col.action, pinned: true, render: (row) => <code className="mono">{row.action}</code> },
    { key: 'target', label: col.target, render: (row) => <span className="member-cell-text"><span>{row.targetType}</span>{row.targetId && <code className="mono small">{row.targetId}</code>}</span> },
    { key: 'summary', label: col.summary, className: 'cell-wrap', render: (row) => row.summary },
    { key: 'sourceIp', label: col.sourceIp, render: (row) => <code className="mono nowrap">{row.sourceIp}</code> },
    { key: 'id', label: col.id, className: 'cell-id', render: (row) => <CopyableId value={row.id} label={col.id} /> },
    {
      key: 'actions', pinned: true, selectorLabel: col.actions, className: 'cell-actions', label: <span className="visually-hidden">{col.actions}</span>,
      render: (row) => <ActionMenu label={c.rowActions(row.id)} items={[{ label: c.viewJson, icon: 'code', onSelect: () => setJsonRow(row) }]} />
    }
  ];

  const filterFields = [
    { key: 'action', label: a.action, placeholder: a.actionPlaceholder, mono: true, list: 'audit-actions' },
    { key: 'actorUserId', label: a.actor, placeholder: a.actorPlaceholder, mono: true },
    { key: 'targetType', label: a.targetType, placeholder: a.targetTypePlaceholder },
    { key: 'targetId', label: a.targetId, mono: true },
    { key: 'fromUtc', label: a.from, type: 'datetime' },
    { key: 'toUtc', label: a.to, type: 'datetime' }
  ];
  const validate = (v) => (v.fromUtc && v.toUtc && new Date(v.fromUtc) >= new Date(v.toUtc) ? a.rangeInvalid : '');

  return (
    <div className="view">
      <PageHeader title={strings.admin.routes.audit.label} summary={strings.admin.routes.audit.summary} />
      <section className="panel">
        <datalist id="audit-actions">{AUDIT_ACTIONS.map((x) => <option key={x} value={x} />)}</datalist>
        <AdminFilterPanel fields={filterFields} table={table} client={adminClient} targetType="AuditRecord" validate={validate} />
      </section>
      <section className="table-section">
        <div className="table-toolbar">
          <div className="toolbar-left" />
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
          caption={strings.admin.routes.audit.label}
          columns={columns.filter((x) => x.pinned || !hidden.includes(x.key))}
          columnChoices={columns.map((x) => ({ key: x.key, label: x.selectorLabel || x.label, pinned: x.pinned }))}
          columnsHidden={hidden}
          onColumnsChange={setHidden}
          rows={page ? page.items : []}
          rowKey={(row) => row.id}
          loading={list.loading}
          error={list.error ? c.unavailable(strings.admin.routes.audit.label) + ' ' + errorMessage(list.error, '') : null}
          onRetry={list.reload}
          emptyMessage={table.hasFilters ? a.emptyFiltered : a.empty}
          onRowClick={(row) => setJsonRow(row)}
          sort={table.sort}
          onSortChange={table.setSort}
        />
      </section>
      <JsonViewerModal open={Boolean(jsonRow)} onClose={() => setJsonRow(null)} subtitle={jsonRow && (jsonRow.action + ' ' + jsonRow.id)} value={jsonRow} />
    </div>
  );
}

export default AuditLogView;
