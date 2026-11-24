import { useCallback, useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { PropertyOrderings, PropertyStatus, propertyDeletionState } from '@trelliskit/shared';
import strings from '../../strings.js';
import { usePlatformAuth } from '../../context/PlatformAuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useTableState from '../../hooks/useTableState.js';
import { errorMessage } from '../../utils/api.js';
import { normalizePage } from '../../utils/query.js';
import { ADMIN_PAGE_SIZES } from '../../utils/adminQuery.js';
import { PROPERTY_TABLE_FILTERS, buildPropertiesQuery, platformMetadataSource, startDelegation } from '../../utils/platformQuery.js';
import { formatShortDateTime } from '../../utils/format.js';
import PageHeader from '../../components/PageHeader.jsx';
import AdminFilterPanel from '../../components/AdminFilterPanel.jsx';
import DataTable from '../../components/DataTable.jsx';
import TablePagination from '../../components/TablePagination.jsx';
import ActionMenu from '../../components/ActionMenu.jsx';
import JsonViewerModal from '../../components/JsonViewerModal.jsx';
import CopyableId from '../../components/CopyableId.jsx';
import Icon from '../../components/Icon.jsx';
import { Badge } from '../../components/Badges.jsx';

const p = strings.platform.properties;
const col = p.columns;
const c = strings.admin.common;
const route = strings.platform.routes.properties;
const MAX_PROPERTIES = 25;

/** Badge tone for a property status. */
export function statusTone(status) {
  if (status === PropertyStatus.Enabled) return 'success';
  if (status === PropertyStatus.Deleting) return 'danger';
  if (status === PropertyStatus.Disabled) return 'warning';
  return 'neutral';
}

export function PropertyStatusBadge({ record }) {
  const del = propertyDeletionState(record);
  return (
    <span className="badge-row">
      <Badge tone={statusTone(record.status)}>{strings.platform.status[record.status] || record.status}</Badge>
      {del.deleting && !del.purging && <span className="muted small">{p.deletingIn(del.daysLeft)}</span>}
    </span>
  );
}

export function MetadataCell({ labels = [], tags = {} }) {
  const entries = Object.entries(tags || {});
  if (!labels.length && !entries.length) return <span className="muted">-</span>;
  return (
    <span className="badge-row">
      {labels.map((l) => <Badge key={'l:' + l}>{l}</Badge>)}
      {entries.map(([k, v]) => <Badge key={'t:' + k} tone="info">{k + '=' + v}</Badge>)}
    </span>
  );
}

/** Properties: bounded enumeration with search, status, host, ordering, label, and tag filters. */
function PropertiesView() {
  const { call, client } = usePlatformAuth();
  const toast = useToast();
  const navigate = useNavigate();
  const metadataSource = useMemo(() => platformMetadataSource(client), [client]);
  const table = useTableState({ storageKey: 'tk.platform.properties', filterKeys: PROPERTY_TABLE_FILTERS, defaultSort: '' });
  const [jsonRow, setJsonRow] = useState(null);

  const query = useMemo(() => buildPropertiesQuery(table.filters, { pageNumber: table.pageNumber, pageSize: table.pageSize, meta: table.meta }), [table.filters, table.pageNumber, table.pageSize, table.meta]);
  const fetcher = useCallback((signal) => call((client) => client.listProperties(query, signal)), [call, query]);
  const list = useApiQuery(fetcher);
  const page = list.data ? normalizePage(list.data, table.pageSize) : null;

  const act = async (row) => {
    try {
      await startDelegation(call, row);
    } catch (err) {
      toast.error(err && err.status === 403 ? strings.platform.detail.actBreakGlass : errorMessage(err, strings.common.errorGeneric));
    }
  };

  const columns = [
    {
      key: 'name', label: col.name, render: (row) => (
        <span className="member-cell-text">
          <Link to={'/platform/properties/' + encodeURIComponent(row.id)} onClick={(e) => e.stopPropagation()}>{row.name}</Link>
          {row.slug && <code className="mono small">{row.slug}</code>}
        </span>
      )
    },
    { key: 'status', label: col.status, render: (row) => <PropertyStatusBadge record={row} /> },
    {
      key: 'domains', label: col.domains, className: 'cell-wrap', render: (row) => {
        const domains = row.domains || [];
        if (!domains.length) return <span className="muted">-</span>;
        return (
          <span className="member-cell-text">
            {domains.slice(0, 2).map((d) => <code key={d.id || d.host} className="mono small">{d.host}</code>)}
            {domains.length > 2 && <span className="muted small">{p.moreDomains(domains.length - 2)}</span>}
          </span>
        );
      }
    },
    { key: 'apps', label: col.apps, className: 'num', render: (row) => (row.apps || []).length },
    { key: 'members', label: col.members, className: 'num', render: (row) => row.memberCount },
    { key: 'metadata', label: col.metadata, render: (row) => <MetadataCell labels={row.labels} tags={row.tags} /> },
    { key: 'updated', label: col.updated, className: 'nowrap', render: (row) => formatShortDateTime(row.updatedUtc) },
    { key: 'id', label: col.id, className: 'cell-id', render: (row) => <CopyableId value={row.id} label={col.id} /> },
    {
      key: 'actions', className: 'cell-actions', label: <span className="visually-hidden">{col.actions}</span>,
      render: (row) => (
        <ActionMenu
          label={c.rowActions(row.name)}
          items={[
            { label: p.open, icon: 'external', onSelect: () => navigate('/platform/properties/' + encodeURIComponent(row.id)) },
            ...(row.status === PropertyStatus.Enabled ? [{ label: p.actInApp, icon: 'shield', onSelect: () => act(row) }] : []),
            { label: c.viewJson, icon: 'code', onSelect: () => setJsonRow(row) }
          ]}
        />
      )
    }
  ];

  const filterFields = [
    { key: 'search', label: p.search, placeholder: p.searchPlaceholder },
    { key: 'status', label: p.status, type: 'select', options: Object.values(PropertyStatus).map((s) => ({ value: s, label: strings.platform.status[s] })) },
    { key: 'host', label: p.host, placeholder: p.hostPlaceholder, mono: true },
    { key: 'ordering', label: p.ordering, type: 'select', options: PropertyOrderings.map((o) => ({ value: o, label: strings.platform.orderings[o] })) }
  ];

  return (
    <div className="view">
      <PageHeader
        title={route.label}
        summary={route.summary}
        actions={<Link to="/platform/properties/new" className="button button-primary button-sm"><Icon name="plus" size={14} /><span>{p.newProperty}</span></Link>}
      >
        <p className="muted small">{p.limitNote(MAX_PROPERTIES)}</p>
      </PageHeader>
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
          emptyMessage={table.hasFilters ? p.emptyFiltered : p.empty}
          onRowClick={(row) => navigate('/platform/properties/' + encodeURIComponent(row.id))}
        />
      </section>
      <JsonViewerModal open={Boolean(jsonRow)} onClose={() => setJsonRow(null)} subtitle={jsonRow && jsonRow.id} value={jsonRow} />
    </div>
  );
}

export default PropertiesView;
