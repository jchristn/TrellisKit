import { useCallback, useMemo, useState } from 'react';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useTableState from '../../hooks/useTableState.js';
import { errorMessage } from '../../utils/api.js';
import { normalizePage } from '../../utils/query.js';
import { ADMIN_PAGE_SIZES, MOD_ACTION_DEFAULT_SORT, MOD_ACTION_FILTERS, buildModerationActionsQuery } from '../../utils/adminQuery.js';
import { optionsFrom } from '../../utils/labels.js';
import { formatDateTime, formatShortDateTime } from '../../utils/format.js';
import { authorName } from '../../utils/engagement.js';
import AdminFilterPanel from '../../components/AdminFilterPanel.jsx';
import DataTable from '../../components/DataTable.jsx';
import TablePagination from '../../components/TablePagination.jsx';
import ActionMenu from '../../components/ActionMenu.jsx';
import JsonViewerModal from '../../components/JsonViewerModal.jsx';
import { Badge } from '../../components/Badges.jsx';

const s = strings.admin.moderation;
const c = strings.admin.common;
const col = s.actionColumns;

/** Moderation action history (contract section 10.2), filterable by moderator, target, action, and time. */
function ModerationActionsTab() {
  const { api, client } = useAdminAuth();
  const table = useTableState({ storageKey: 'tk.admin.modactions', filterKeys: MOD_ACTION_FILTERS, defaultSort: MOD_ACTION_DEFAULT_SORT, prefix: 'a.' });
  const [jsonRow, setJsonRow] = useState(null);
  const query = useMemo(() => buildModerationActionsQuery(table.filters, { pageNumber: table.pageNumber, pageSize: table.pageSize, sort: table.sort, meta: table.meta }), [table.filters, table.pageNumber, table.pageSize, table.sort, table.meta]);
  const fetcher = useCallback((signal) => api.listModerationActions(query, signal), [api, query]);
  const list = useApiQuery(fetcher);
  const page = list.data ? normalizePage(list.data, table.pageSize) : null;
  const columns = [
    { key: 'time', label: col.time, sortField: 'createdUtc', sortDefault: 'desc', className: 'nowrap', render: (row) => <span title={formatDateTime(row.createdUtc)}>{formatShortDateTime(row.createdUtc)}</span> },
    { key: 'moderator', label: col.moderator, render: (row) => (row.moderator ? authorName(row.moderator) : <span className="muted">{s.system}</span>) },
    { key: 'action', label: col.action, render: (row) => <Badge tone={row.action === 'Remove' ? 'danger' : row.action === 'Hide' || row.action === 'AutoHide' ? 'warning' : 'neutral'}>{s.actionOptions[row.action] || row.action}</Badge> },
    { key: 'target', label: col.target, render: (row) => <span className="cell-stack"><span>{s.targetTypes[row.targetType] || row.targetType}</span><code className="mono small">{row.targetId}</code></span> },
    { key: 'author', label: col.author, render: (row) => authorName(row.targetAuthor) },
    { key: 'reason', label: col.reason, className: 'cell-wrap', render: (row) => row.reason || '-' },
    { key: 'notified', label: col.notified, render: (row) => (row.notifiedAuthor ? c.yes : c.no) },
    { key: 'actions', className: 'cell-actions', label: <span className="visually-hidden">{strings.admin.members.columns.actions}</span>, render: (row) => <ActionMenu label={c.rowActions(row.id)} items={[{ label: c.viewJson, icon: 'code', onSelect: () => setJsonRow(row) }]} /> }
  ];
  const fields = [
    { key: 'moderatorUserId', label: s.moderatorFilter, placeholder: 'usr_...', mono: true },
    { key: 'targetType', label: s.targetType, type: 'select', options: optionsFrom(s.targetTypes, ['FeedPost', 'Comment']) },
    { key: 'action', label: s.actionFilter, type: 'select', options: optionsFrom(s.actionOptions) },
    { key: 'fromUtc', label: strings.admin.audit.from, type: 'datetime' },
    { key: 'toUtc', label: strings.admin.audit.to, type: 'datetime' }
  ];
  return (
    <>
      <section className="panel">
        <AdminFilterPanel fields={fields} table={table} client={client} targetType="ModerationAction" />
      </section>
      <section className="table-section">
        <TablePagination pageNumber={table.pageNumber} pageSize={table.pageSize} totalCount={page ? page.totalCount : 0} totalPages={page ? page.totalPages : 1} onPageChange={table.setPage} onPageSizeChange={table.setPageSize} onRefresh={list.reload} loading={list.loading} pageSizes={ADMIN_PAGE_SIZES} />
        <DataTable caption={s.tabs.actions} columns={columns} rows={page ? page.items : []} rowKey={(row) => row.id} loading={list.loading} error={list.error ? c.unavailable(s.tabs.actions) + ' ' + errorMessage(list.error, '') : null} onRetry={list.reload} emptyMessage={s.actionsListEmpty} onRowClick={(row) => setJsonRow(row)} sort={table.sort} onSortChange={table.setSort} />
      </section>
      <JsonViewerModal open={Boolean(jsonRow)} onClose={() => setJsonRow(null)} subtitle={jsonRow && jsonRow.id} value={jsonRow} />
    </>
  );
}

export default ModerationActionsTab;
