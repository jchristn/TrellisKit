import { useCallback, useMemo } from 'react';
import { useSearchParams } from 'react-router-dom';
import { BroadcastStatus } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useTableState from '../../hooks/useTableState.js';
import { errorMessage } from '../../utils/api.js';
import { normalizePage } from '../../utils/query.js';
import { ADMIN_PAGE_SIZES, pagedQuery } from '../../utils/adminQuery.js';
import { formatDateTime, formatNumber, formatShortDateTime } from '../../utils/format.js';
import PageHeader from '../../components/PageHeader.jsx';
import AdminFilterPanel from '../../components/AdminFilterPanel.jsx';
import DataTable from '../../components/DataTable.jsx';
import TablePagination from '../../components/TablePagination.jsx';
import Icon from '../../components/Icon.jsx';
import { Badge } from '../../components/Badges.jsx';
import BroadcastEditor from './BroadcastEditor.jsx';

const s = strings.broadcastsAdmin;
const c = strings.admin.common;

export const BROADCAST_FILTERS = Object.freeze(['status', 'channel']);
export const BROADCAST_DEFAULT_SORT = '-createdUtc';

function statusTone(status) {
  if (status === BroadcastStatus.Sent) return 'success';
  if (status === BroadcastStatus.Sending || status === BroadcastStatus.Scheduled) return 'info';
  if (status === BroadcastStatus.Canceled) return 'neutral';
  return 'warning';
}

/**
 * /admin/broadcasts (SystemAdmin and Communicator, as-built note D): every broadcast with status
 * and channel filters, label and tag filters, and the editor drawer (`?broadcast=new` or an id).
 */
function BroadcastsView() {
  const { client } = useAdminAuth();
  const [searchParams, setSearchParams] = useSearchParams();
  const table = useTableState({ storageKey: 'tk.admin.broadcasts', filterKeys: BROADCAST_FILTERS, defaultSort: BROADCAST_DEFAULT_SORT });
  const query = useMemo(() => pagedQuery(table.filters, BROADCAST_FILTERS, { pageNumber: table.pageNumber, pageSize: table.pageSize, sort: table.sort, meta: table.meta }), [table.filters, table.pageNumber, table.pageSize, table.sort, table.meta]);
  const fetcher = useCallback((signal) => client.adminEnumerateBroadcasts(query, signal), [client, query]);
  const list = useApiQuery(fetcher);
  const page = list.data ? normalizePage(list.data, table.pageSize) : null;
  const rows = page ? page.items : [];
  const open = searchParams.get('broadcast');
  const setOpen = (id) => {
    const next = new URLSearchParams(searchParams);
    if (id) next.set('broadcast', id);
    else next.delete('broadcast');
    setSearchParams(next);
  };
  const columns = [
    { key: 'title', label: s.columns.title, pinned: true, render: (row) => <span className="cell-stack"><span>{row.title}</span><span className="muted small mono">{row.id}</span></span> },
    { key: 'status', label: s.columns.status, render: (row) => <Badge tone={statusTone(row.status)}>{s.statuses[row.status] || row.status}</Badge> },
    { key: 'channels', label: s.columns.channels, render: (row) => <span className="badge-row">{['inApp', 'push', 'email'].filter((ch) => row.channels && row.channels[ch]).map((ch) => <Badge key={ch} tone="neutral">{s.channels[ch]}</Badge>)}</span> },
    { key: 'category', label: s.columns.category, render: (row) => (row.category === 'Essential' ? <Badge tone="danger">{s.categories.Essential}</Badge> : s.categories.Optional) },
    { key: 'sendAt', label: s.columns.sendAt, className: 'nowrap', render: (row) => (row.sentUtc || row.sendAtUtc ? <span title={formatDateTime(row.sentUtc || row.sendAtUtc)}>{formatShortDateTime(row.sentUtc || row.sendAtUtc)}</span> : '-') },
    { key: 'targeted', label: s.columns.targeted, className: 'num', render: (row) => formatNumber((row.stats && row.stats.targeted) || 0) },
    { key: 'created', label: s.columns.created, sortField: 'createdUtc', sortDefault: 'desc', className: 'nowrap', render: (row) => <span title={formatDateTime(row.createdUtc)}>{formatShortDateTime(row.createdUtc)}</span> }
  ];
  const filterFields = [
    { key: 'status', label: s.filters.status, type: 'select', options: Object.values(BroadcastStatus).map((v) => ({ value: v, label: s.statuses[v] })) },
    { key: 'channel', label: s.filters.channel, type: 'select', options: ['inApp', 'push', 'email'].map((v) => ({ value: v, label: s.channels[v] })) }
  ];
  return (
    <div className="view">
      <PageHeader
        title={strings.admin.routes.broadcasts.label}
        summary={strings.admin.routes.broadcasts.summary}
        actions={<button type="button" className="button button-primary" onClick={() => setOpen('new')}><Icon name="plus" size={14} /><span>{s.newBroadcast}</span></button>}
      />
      <section className="panel">
        <AdminFilterPanel fields={filterFields} table={table} client={client} targetType="Broadcast" />
      </section>
      <section className="table-section">
        <TablePagination pageNumber={table.pageNumber} pageSize={table.pageSize} totalCount={page ? page.totalCount : 0} totalPages={page ? page.totalPages : 1} onPageChange={table.setPage} onPageSizeChange={table.setPageSize} onRefresh={list.reload} loading={list.loading} pageSizes={ADMIN_PAGE_SIZES} />
        <DataTable caption={strings.admin.routes.broadcasts.label} columns={columns} rows={rows} rowKey={(row) => row.id} loading={list.loading} error={list.error ? c.unavailable(strings.admin.routes.broadcasts.label) + ' ' + errorMessage(list.error, '') : null} onRetry={list.reload} emptyMessage={table.hasFilters ? s.emptyFiltered : s.empty} onRowClick={(row) => setOpen(row.id)} sort={table.sort} onSortChange={table.setSort} />
      </section>
      {open && <BroadcastEditor key={open} id={open === 'new' ? null : open} onClose={() => setOpen(null)} onChanged={list.reload} />}
    </div>
  );
}

export default BroadcastsView;
