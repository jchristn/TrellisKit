import { useCallback, useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useLocalStorage from '../../hooks/useLocalStorage.js';
import useTableState from '../../hooks/useTableState.js';
import { errorMessage } from '../../utils/api.js';
import { normalizePage } from '../../utils/query.js';
import { ADMIN_PAGE_SIZES, SUBSCRIPTION_DEFAULT_SORT, SUBSCRIPTION_FILTERS, buildSubscriptionsQuery } from '../../utils/adminQuery.js';
import { optionsFrom, subscriptionTone } from '../../utils/labels.js';
import { formatDateTime, formatShortDate, formatShortDateTime } from '../../utils/format.js';
import PageHeader from '../../components/PageHeader.jsx';
import AdminFilterPanel from '../../components/AdminFilterPanel.jsx';
import MetadataPanel from '../../components/MetadataPanel.jsx';
import DataTable from '../../components/DataTable.jsx';
import TablePagination from '../../components/TablePagination.jsx';
import ActionMenu from '../../components/ActionMenu.jsx';
import Modal from '../../components/Modal.jsx';
import JsonViewerModal from '../../components/JsonViewerModal.jsx';
import CopyableId from '../../components/CopyableId.jsx';
import Icon from '../../components/Icon.jsx';
import { Badge } from '../../components/Badges.jsx';
import { ErrorBanner, Loading } from '../../components/States.jsx';
import { previewOf } from './MemberDetailDrawer.jsx';
import TableColumns from '../../components/TableColumns.jsx';

const s = strings.admin.subscriptions;
const c = strings.admin.common;
const en = strings.enums;
const col = s.columns;
const DEFAULT_HIDDEN = ['environment', 'created', 'id'];

function parsePayload(raw) {
  if (typeof raw !== 'string') return raw;
  try {
    return JSON.parse(raw);
  } catch {
    return raw;
  }
}

function SubscriptionDrawer({ id, onClose }) {
  const { api, client } = useAdminAuth();
  const fetcher = useCallback((signal) => api.getSubscription(id, signal), [api, id]);
  const query = useApiQuery(fetcher);
  const [payload, setPayload] = useState(null);
  const [jsonOpen, setJsonOpen] = useState(false);
  const sub = query.data;
  // `events` is a bounded preview (the newest 5) with the total and the full enumeration's path.
  const eventsPreview = previewOf(sub && sub.events);
  const events = eventsPreview.objects;

  return (
    <Modal
      open
      variant="drawer"
      size="lg"
      onClose={onClose}
      title={sub ? (sub.memberDisplayName || sub.memberEmail || s.detailTitle) : s.detailTitle}
      subtitle={<CopyableId value={id} label={s.fields.id} />}
      headerExtra={sub && <button type="button" className="button button-secondary button-sm" onClick={() => setJsonOpen(true)}><Icon name="code" size={14} /><span>{c.viewJson}</span></button>}
    >
      {query.loading && !sub && <Loading />}
      {query.error && <ErrorBanner message={s.loadError + ' ' + errorMessage(query.error, '')} onRetry={query.reload} />}
      {sub && (
        <div className="detail-stack">
          <MetadataPanel client={client} targetType="Subscription" targetId={sub.id || id} isStaff initial={sub.labels ? sub : null} />
          <section className="detail-block">
            <dl className="kv-grid">
              <div className="kv-item"><dt>{s.fields.member}</dt><dd>{sub.memberDisplayName || '-'} {sub.userId && <Link className="small" to={'/admin/members?member=' + encodeURIComponent(sub.userId)}>{s.openMember}</Link>}</dd></div>
              <div className="kv-item"><dt>{s.fields.memberEmail}</dt><dd>{sub.memberEmail || '-'}</dd></div>
              <div className="kv-item"><dt>{s.fields.store}</dt><dd>{en.store[sub.store] || sub.store}</dd></div>
              <div className="kv-item"><dt>{s.fields.status}</dt><dd><Badge tone={subscriptionTone(sub.status)}>{en.subscriptionStatus[sub.status] || sub.status}</Badge></dd></div>
              <div className="kv-item"><dt>{s.fields.productId}</dt><dd className="mono">{sub.productId}</dd></div>
              <div className="kv-item"><dt>{s.fields.periodStart}</dt><dd title={sub.currentPeriodStartUtc}>{formatDateTime(sub.currentPeriodStartUtc)}</dd></div>
              <div className="kv-item"><dt>{s.fields.periodEnd}</dt><dd title={sub.currentPeriodEndUtc}>{formatDateTime(sub.currentPeriodEndUtc)}</dd></div>
              <div className="kv-item"><dt>{s.fields.autoRenew}</dt><dd>{sub.autoRenew ? c.yes : c.no}</dd></div>
              <div className="kv-item"><dt>{s.fields.environment}</dt><dd>{sub.environment || '-'}</dd></div>
              <div className="kv-item"><dt>{s.fields.storeSubscriptionId}</dt><dd>{sub.storeSubscriptionId ? <CopyableId value={sub.storeSubscriptionId} label={s.fields.storeSubscriptionId} /> : '-'}</dd></div>
              <div className="kv-item"><dt>{s.fields.userId}</dt><dd>{sub.userId ? <CopyableId value={sub.userId} label={s.fields.userId} /> : '-'}</dd></div>
              <div className="kv-item"><dt>{s.fields.created}</dt><dd title={sub.createdUtc}>{formatDateTime(sub.createdUtc)}</dd></div>
            </dl>
          </section>
          <section className="detail-block" aria-labelledby="sub-events-title">
            <h3 id="sub-events-title">{s.eventsTitle}</h3>
            {eventsPreview.totalRecords > events.length && <p className="muted small">{strings.admin.members.previewShowing(events.length, eventsPreview.totalRecords)}</p>}
            {events.length === 0 ? <p className="muted small">{s.eventsEmpty}</p> : (
              <div className="table-scroll">
                <TableColumns id="tk.admin.SubscriptionsView"><table className="data-table">
                  <caption className="visually-hidden">{s.eventsTitle}</caption>
                  <thead>
                    <tr>
                      {['received', 'source', 'type', 'outcome', 'processed', 'payload'].map((k) => <th key={k} scope="col">{s.eventColumns[k]}</th>)}
                    </tr>
                  </thead>
                  <tbody>
                    {events.map((event) => (
                      <tr key={event.id}>
                        <td className="nowrap" title={event.receivedUtc}>{formatShortDateTime(event.receivedUtc)}</td>
                        <td>{event.source}</td>
                        <td><code className="mono">{event.type}</code></td>
                        <td>{event.outcome}</td>
                        <td className="nowrap" title={event.processedUtc || ''}>{event.processedUtc ? formatShortDateTime(event.processedUtc) : '-'}</td>
                        <td>
                          <button type="button" className="button button-secondary button-sm" onClick={() => setPayload(event)} disabled={event.rawPayload === null || event.rawPayload === undefined}>
                            {s.viewPayload}
                          </button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table></TableColumns>
              </div>
            )}
          </section>
        </div>
      )}
      <JsonViewerModal open={Boolean(payload)} onClose={() => setPayload(null)} title={s.payloadTitle} subtitle={payload && (payload.type + ' ' + payload.id)} value={payload ? parsePayload(payload.rawPayload) : null} />
      <JsonViewerModal open={jsonOpen} onClose={() => setJsonOpen(false)} subtitle={id} value={sub} />
    </Modal>
  );
}

/** Subscriptions across Stripe, Apple, and Google: filters, sorting, and a detail drawer with the event log. */
function SubscriptionsView() {
  const { api } = useAdminAuth();
  const [searchParams, setSearchParams] = useSearchParams();
  const adminClient = useAdminAuth().client;
  const table = useTableState({ storageKey: 'tk.admin.subscriptions', filterKeys: SUBSCRIPTION_FILTERS, defaultSort: SUBSCRIPTION_DEFAULT_SORT });
  const [hidden, setHidden] = useLocalStorage('tk.admin.subscriptions.hiddenColumns', DEFAULT_HIDDEN);
  const [jsonRow, setJsonRow] = useState(null);
  const detailId = searchParams.get('subscription');

  const query = useMemo(() => buildSubscriptionsQuery(table.filters, { pageNumber: table.pageNumber, pageSize: table.pageSize, sort: table.sort, meta: table.meta }), [table.filters, table.pageNumber, table.pageSize, table.sort, table.meta]);
  const fetcher = useCallback((signal) => api.listSubscriptions(query, signal), [api, query]);
  const list = useApiQuery(fetcher);
  const page = list.data ? normalizePage(list.data, table.pageSize) : null;

  const openDetail = (id) => {
    const next = new URLSearchParams(searchParams);
    if (id) next.set('subscription', id);
    else next.delete('subscription');
    setSearchParams(next);
  };

  const columns = [
    { key: 'member', label: col.member, pinned: true, render: (row) => <span className="member-cell-text"><strong>{row.memberDisplayName || '-'}</strong><span className="muted small">{row.memberEmail}</span></span> },
    { key: 'store', label: col.store, render: (row) => en.store[row.store] || row.store },
    { key: 'product', label: col.product, render: (row) => <code className="mono">{row.productId}</code> },
    { key: 'status', label: col.status, pinned: true, render: (row) => <Badge tone={subscriptionTone(row.status)}>{en.subscriptionStatus[row.status] || row.status}</Badge> },
    { key: 'periodEnd', label: col.periodEnd, sortField: 'currentPeriodEndUtc', sortDefault: 'desc', className: 'nowrap', render: (row) => <span title={formatDateTime(row.currentPeriodEndUtc)}>{formatShortDate(row.currentPeriodEndUtc)}</span> },
    { key: 'autoRenew', label: col.autoRenew, render: (row) => (row.autoRenew ? c.yes : c.no) },
    { key: 'environment', label: col.environment, render: (row) => row.environment || '-' },
    { key: 'lastEvent', label: col.lastEvent, sortField: 'lastEventUtc', sortDefault: 'desc', className: 'nowrap', render: (row) => (row.lastEventUtc ? <span title={formatDateTime(row.lastEventUtc)}>{formatShortDateTime(row.lastEventUtc)}</span> : '-') },
    { key: 'created', label: col.created, sortField: 'createdUtc', sortDefault: 'desc', className: 'nowrap', render: (row) => formatShortDate(row.createdUtc) },
    { key: 'id', label: col.id, className: 'cell-id', render: (row) => <CopyableId value={row.id} label={col.id} /> },
    {
      key: 'actions', pinned: true, selectorLabel: col.actions, className: 'cell-actions', label: <span className="visually-hidden">{col.actions}</span>,
      render: (row) => (
        <ActionMenu
          label={c.rowActions(row.id)}
          items={[
            { label: c.view, icon: 'eye', onSelect: () => openDetail(row.id) },
            { label: c.viewJson, icon: 'code', onSelect: () => setJsonRow(row) }
          ]}
        />
      )
    }
  ];

  const filterFields = [
    { key: 'search', label: s.searchLabel, placeholder: s.searchPlaceholder, grow: true },
    { key: 'store', label: s.store, type: 'select', options: optionsFrom(en.store) },
    { key: 'status', label: s.status, type: 'select', options: optionsFrom(en.subscriptionStatus) }
  ];

  return (
    <div className="view">
      <PageHeader title={strings.admin.routes.subscriptions.label} summary={strings.admin.routes.subscriptions.summary} />
      <section className="panel">
        <AdminFilterPanel fields={filterFields} table={table} client={adminClient} targetType="Subscription" />
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
          caption={strings.admin.routes.subscriptions.label}
          columns={columns.filter((x) => x.pinned || !hidden.includes(x.key))}
          columnChoices={columns.map((x) => ({ key: x.key, label: x.selectorLabel || x.label, pinned: x.pinned }))}
          columnsHidden={hidden}
          onColumnsChange={setHidden}
          rows={page ? page.items : []}
          rowKey={(row) => row.id}
          loading={list.loading}
          error={list.error ? c.unavailable(strings.admin.routes.subscriptions.label) + ' ' + errorMessage(list.error, '') : null}
          onRetry={list.reload}
          emptyMessage={table.hasFilters ? s.emptyFiltered : s.empty}
          onRowClick={(row) => openDetail(row.id)}
          sort={table.sort}
          onSortChange={table.setSort}
        />
      </section>
      {detailId && <SubscriptionDrawer key={detailId} id={detailId} onClose={() => openDetail(null)} />}
      <JsonViewerModal open={Boolean(jsonRow)} onClose={() => setJsonRow(null)} subtitle={jsonRow && jsonRow.id} value={jsonRow} />
    </div>
  );
}

export default SubscriptionsView;
