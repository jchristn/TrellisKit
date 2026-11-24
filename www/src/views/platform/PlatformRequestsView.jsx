import { useCallback, useMemo, useState } from 'react';
import { PlatformRequestScope } from '@trelliskit/shared';
import strings from '../../strings.js';
import { usePlatformAuth } from '../../context/PlatformAuthContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useTableState from '../../hooks/useTableState.js';
import { errorMessage } from '../../utils/api.js';
import { HTTP_METHODS, normalizePage } from '../../utils/query.js';
import { ADMIN_PAGE_SIZES } from '../../utils/adminQuery.js';
import { PLATFORM_REQUEST_TABLE_FILTERS, buildPlatformRequestHistoryQuery } from '../../utils/platformQuery.js';
import { formatBytes, formatPreciseDateTime, formatDuration, formatShortDateTime } from '../../utils/format.js';
import PageHeader from '../../components/PageHeader.jsx';
import AdminFilterPanel from '../../components/AdminFilterPanel.jsx';
import DataTable from '../../components/DataTable.jsx';
import TablePagination from '../../components/TablePagination.jsx';
import ActionMenu from '../../components/ActionMenu.jsx';
import JsonViewerModal from '../../components/JsonViewerModal.jsx';
import Modal from '../../components/Modal.jsx';
import CodeBlock from '../../components/CodeBlock.jsx';
import CopyableId from '../../components/CopyableId.jsx';
import { MethodBadge, StatusBadge } from '../../components/Badges.jsx';
import { EmptyState, ErrorBanner } from '../../components/States.jsx';

const r = strings.platform.requests;
const col = r.columns;
const det = strings.admin.details;
const route = strings.platform.routes.requests;
const MAX_SCOPE_PROPERTIES = 100;

/** Validation of the status code filter (an HTTP status, 100 to 599). */
export function validateRequestFilters(values) {
  const status = values.statusCode || '';
  if (status && !(/^\d{3}$/.test(status) && Number(status) >= 100 && Number(status) <= 599)) return r.statusInvalid;
  return '';
}

/**
 * Inspector for one platform request history entry. The platform route returns list items only
 * (no headers or bodies, and there is no platform detail route), so this shows the entry's
 * metadata and says where the full entry can be read.
 */
export function PlatformRequestInspector({ entry, onClose }) {
  const has = (k) => entry && entry[k] !== undefined && entry[k] !== null;
  const rows = entry ? [
    [det.fields.method, <MethodBadge key="m" method={entry.method} />],
    [det.fields.path, <code key="p" className="mono break">{entry.path}</code>],
    [det.fields.url, <code key="u" className="mono break">{entry.url}</code>],
    [det.fields.statusCode, <StatusBadge key="s" code={entry.statusCode} />],
    [det.fields.duration, formatDuration(entry.durationMs)],
    [det.fields.createdUtc, <span key="c" title={entry.createdUtc}>{formatPreciseDateTime(entry.createdUtc)}</span>],
    [det.fields.completedUtc, entry.completedUtc ? <span key="cc" title={entry.completedUtc}>{formatPreciseDateTime(entry.completedUtc)}</span> : strings.common.none],
    [det.fields.sourceIp, <code key="ip" className="mono">{entry.sourceIp || '-'}</code>],
    [det.fields.principal, entry.principalName || r.anonymous],
    [det.fields.userId, entry.userId ? <CopyableId key="uid" value={entry.userId} label={det.fields.userId} /> : strings.common.none],
    [det.fields.requestBytes, formatBytes(entry.requestBodyBytes)],
    [det.fields.responseBytes, formatBytes(entry.responseBodyBytes)]
  ] : [];
  const hasBodies = has('requestHeaders') || has('responseHeaders') || has('requestBody') || has('responseBody');
  return (
    <Modal open={Boolean(entry)} onClose={onClose} size="xl" title={r.inspectorTitle} subtitle={entry ? <CopyableId value={entry.id} label={det.fields.id} /> : null}>
      {entry && (
        <div className="detail-sections">
          <section className="detail-section">
            <div className="detail-section-header"><h3>{det.metadata}</h3></div>
            <div className="detail-section-body">
              <dl className="kv-grid">
                {rows.map(([label, value]) => <div key={label} className="kv-item"><dt>{label}</dt><dd>{value}</dd></div>)}
              </dl>
            </div>
          </section>
          <section className="detail-section">
            <div className="detail-section-header"><h3>{r.bodiesTitle}</h3></div>
            <div className="detail-section-body">
              {hasBodies ? (
                <>
                  {has('requestHeaders') && <><h4>{det.requestHeaders}</h4><CodeBlock value={JSON.stringify(entry.requestHeaders, null, 2)} label={det.requestHeaders} /></>}
                  {has('requestBody') && <><h4>{det.requestBody}</h4><CodeBlock value={String(entry.requestBody)} label={det.requestBody} /></>}
                  {has('responseHeaders') && <><h4>{det.responseHeaders}</h4><CodeBlock value={JSON.stringify(entry.responseHeaders, null, 2)} label={det.responseHeaders} /></>}
                  {has('responseBody') && <><h4>{det.responseBody}</h4><CodeBlock value={String(entry.responseBody)} label={det.responseBody} /></>}
                </>
              ) : (
                <EmptyState icon="info" title={det.noBody}>{r.bodiesAbsent}</EmptyState>
              )}
            </div>
          </section>
          <section className="detail-section">
            <div className="detail-section-header"><h3>{r.rawJson}</h3></div>
            <div className="detail-section-body"><CodeBlock value={JSON.stringify(entry, null, 2)} label={r.rawJson} maxHeight={400} /></div>
          </section>
        </div>
      )}
    </Modal>
  );
}

function ScopePicker({ value, onChange }) {
  const { call } = usePlatformAuth();
  const fetcher = useCallback((signal) => call((x) => x.listProperties({ maxResults: MAX_SCOPE_PROPERTIES, ordering: 'NameAscending' }, signal)), [call]);
  const list = useApiQuery(fetcher);
  const items = list.data ? normalizePage(list.data, MAX_SCOPE_PROPERTIES).items.filter((x) => x.status !== 'Deleted') : [];
  const known = !value || value === PlatformRequestScope.Platform || value === PlatformRequestScope.Unresolved || items.some((x) => x.id === value);
  return (
    <div className="stack-form">
      <label className="field field-compact" htmlFor="request-scope">
        <span className="label-text">{r.scope}</span>
        <select id="request-scope" className="select-compact" value={value || PlatformRequestScope.Platform} onChange={(e) => onChange(e.target.value)}>
          <option value={PlatformRequestScope.Platform}>{r.scopePlatform}</option>
          <option value={PlatformRequestScope.Unresolved}>{r.scopeUnresolved}</option>
          <optgroup label={r.scopePropertiesGroup}>
            {!known && <option value={value}>{value}</option>}
            {items.map((x) => <option key={x.id} value={x.id}>{x.name + ' (' + (x.slug || x.id) + ')'}</option>)}
          </optgroup>
        </select>
      </label>
      <span className="muted small">{r.scopeHint}</span>
      {list.error && <ErrorBanner message={r.scopeLoadError + ' ' + errorMessage(list.error, '')} onRetry={list.reload} />}
    </div>
  );
}

/**
 * Platform request history: one realm per view (the platform, unresolved hosts, or a property),
 * with method, status, path, user, search, label, and tag filters, server paging, and an inspector.
 */
function PlatformRequestsView() {
  const { call } = usePlatformAuth();
  const table = useTableState({ storageKey: 'tk.platform.requests', filterKeys: PLATFORM_REQUEST_TABLE_FILTERS, defaultSort: '' });
  const [inspect, setInspect] = useState(null);
  const [jsonRow, setJsonRow] = useState(null);
  const scope = table.filters.propertyId || PlatformRequestScope.Platform;

  const query = useMemo(() => buildPlatformRequestHistoryQuery(table.filters, { pageNumber: table.pageNumber, pageSize: table.pageSize, meta: table.meta }), [table.filters, table.pageNumber, table.pageSize, table.meta]);
  const fetcher = useCallback((signal) => call((x) => x.listPlatformRequestHistory(query, signal)), [call, query]);
  const list = useApiQuery(fetcher);
  const page = list.data ? normalizePage(list.data, table.pageSize) : null;

  // The scope is chosen outside the filter form; keep it when the form applies or clears.
  const keepScope = (filters) => ({ ...filters, propertyId: scope === PlatformRequestScope.Platform ? '' : scope });
  const filterTable = {
    ...table,
    applyAll: (filters, meta) => table.applyAll(keepScope(filters), meta),
    clearAll: () => table.applyAll(keepScope({}), { labels: [], labelMatch: table.meta.labelMatch, tags: {} })
  };
  const setScope = (next) => table.applyAll({ ...table.filters, propertyId: next === PlatformRequestScope.Platform ? '' : next }, table.meta);

  const filterFields = [
    { key: 'search', label: r.search, placeholder: r.searchPlaceholder },
    { key: 'method', label: r.method, type: 'select', options: HTTP_METHODS.map((x) => ({ value: x, label: x })) },
    { key: 'statusCode', label: r.statusCode, placeholder: r.statusPlaceholder },
    { key: 'pathContains', label: r.pathContains, placeholder: r.pathPlaceholder, grow: true, mono: true },
    { key: 'userId', label: r.userId, placeholder: r.userIdPlaceholder, mono: true }
  ];

  const columns = [
    { key: 'time', label: col.time, className: 'nowrap', render: (row) => <span title={row.createdUtc + ' (' + formatPreciseDateTime(row.createdUtc) + ')'}>{formatShortDateTime(row.createdUtc)}</span> },
    { key: 'method', label: col.method, render: (row) => <MethodBadge method={row.method} /> },
    { key: 'path', label: col.path, className: 'cell-path', render: (row) => <code className="mono truncate" title={row.url || row.path}>{row.path}</code> },
    { key: 'status', label: col.status, render: (row) => <StatusBadge code={row.statusCode} /> },
    { key: 'duration', label: col.duration, className: 'num nowrap', render: (row) => formatDuration(row.durationMs) },
    { key: 'principal', label: col.principal, className: 'hide-narrow', render: (row) => row.principalName || <span className="muted">{r.anonymous}</span> },
    { key: 'sourceIp', defaultHidden: true, label: col.sourceIp, render: (row) => <code className="mono nowrap">{row.sourceIp}</code> },
    { key: 'id', label: col.id, className: 'cell-id', render: (row) => <CopyableId value={row.id} label={col.id} /> },
    {
      key: 'actions', label: <span className="visually-hidden">{col.actions}</span>, className: 'cell-actions',
      render: (row) => (
        <ActionMenu
          label={r.menu(row.id)}
          items={[
            { label: r.view, icon: 'eye', onSelect: () => setInspect(row) },
            { label: r.viewJson, icon: 'code', onSelect: () => setJsonRow(row) }
          ]}
        />
      )
    }
  ];

  const filtered = ['search', 'method', 'statusCode', 'pathContains', 'userId'].some((k) => Boolean(table.filters[k])) || table.meta.labels.length > 0 || Object.keys(table.meta.tags).length > 0;

  return (
    <div className="view">
      <PageHeader title={route.label} summary={route.summary} />
      <section className="panel" aria-label={r.filtersTitle}>
        <ScopePicker value={scope} onChange={setScope} />
        <AdminFilterPanel fields={filterFields} table={filterTable} client={null} targetType="RequestHistory" validate={validateRequestFilters} label={r.filtersTitle} />
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
          error={list.error ? strings.admin.common.unavailable(route.label) + ' ' + errorMessage(list.error, '') : null}
          onRetry={list.reload}
          emptyMessage={filtered ? r.emptyFiltered : r.empty}
          onRowClick={(row) => setInspect(row)}
          rowClassName={(row) => (row.statusCode >= 400 ? 'row-failed' : '')}
        />
      </section>
      <PlatformRequestInspector entry={inspect} onClose={() => setInspect(null)} />
      <JsonViewerModal open={Boolean(jsonRow)} onClose={() => setJsonRow(null)} subtitle={jsonRow && jsonRow.id} value={jsonRow} />
    </div>
  );
}

export default PlatformRequestsView;
