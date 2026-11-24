import { useCallback, useMemo } from 'react';
import { useSearchParams } from 'react-router-dom';
import strings from '../../strings.js';
import useApiQuery from '../../hooks/useApiQuery.js';
import useTableState from '../../hooks/useTableState.js';
import { ADMIN_PAGE_SIZES, pagedQuery } from '../../utils/adminQuery.js';
import { normalizePage } from '../../utils/query.js';
import { errorText } from '../../utils/engagement.js';
import AdminFilterPanel from '../AdminFilterPanel.jsx';
import DataTable from '../DataTable.jsx';
import TablePagination from '../TablePagination.jsx';

const c = strings.admin.common;

/** URL-backed tabs (`?tab=`); the first tab is the default and is not written to the URL. */
export function useTab(tabs) {
  const [searchParams, setSearchParams] = useSearchParams();
  const current = tabs.includes(searchParams.get('tab')) ? searchParams.get('tab') : tabs[0];
  const select = useCallback((id) => setSearchParams(id === tabs[0] ? {} : { tab: id }), [setSearchParams, tabs]);
  return [current, select];
}

/** A tab bar (role tablist) for admin pages. */
export function TabBar({ tabs, labels, current, onSelect, idPrefix, label }) {
  return (
    <div className="segmented tab-bar" role="tablist" aria-label={label}>
      {tabs.map((id) => (
        <button key={id} type="button" role="tab" id={idPrefix + '-tab-' + id} aria-selected={current === id} aria-controls={idPrefix + '-panel-' + id} className={'segment' + (current === id ? ' active' : '')} onClick={() => onSelect(id)}>{labels[id] || id}</button>
      ))}
    </div>
  );
}

/**
 * A server-paged admin enumeration: URL-backed filters, label and tag filters, sort, and page (via
 * `useTableState` with a `prefix` per table), turned into an EnumerationQuery for `fetch(query, signal)`.
 * @param {{ storageKey: string, keys: readonly string[], defaultSort?: string, prefix?: string, fetch: (query: object, signal: AbortSignal) => Promise<any>, transform?: (filters: object) => object, extra?: object }} options
 */
export function useAdminList({ storageKey, keys, defaultSort = '', prefix = '', fetch, transform, extra = null }) {
  const table = useTableState({ storageKey, filterKeys: keys, defaultSort, prefix });
  const extraKey = JSON.stringify(extra || {});
  const query = useMemo(() => {
    const q = pagedQuery(table.filters, keys, { pageNumber: table.pageNumber, pageSize: table.pageSize, sort: table.sort, meta: table.meta }, transform);
    return { ...q, filters: { ...(q.filters || {}), ...JSON.parse(extraKey) } };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [table.filters, table.pageNumber, table.pageSize, table.sort, table.meta, extraKey]);
  const fetcher = useCallback((signal) => fetch(query, signal), [fetch, query]);
  const list = useApiQuery(fetcher);
  const page = list.data ? normalizePage(list.data, table.pageSize) : null;
  return { table, list, page, rows: page ? page.items : [], query };
}

/**
 * The standard admin list block: the filter panel, pagination, and the data table.
 * @param {{ admin: ReturnType<typeof useAdminList>, filterFields: object[], client: object, targetType?: string, columns: object[], caption: string, empty: string, emptyFiltered: string, onRowClick?: Function, toolbar?: React.ReactNode }} props
 */
export function AdminListSection({ admin, filterFields, client, targetType, columns, caption, empty, emptyFiltered, onRowClick, toolbar = null }) {
  const { table, list, page, rows } = admin;
  return (
    <>
      <section className="panel">
        <AdminFilterPanel fields={filterFields} table={table} client={client} targetType={targetType} />
      </section>
      <section className="table-section">
        {toolbar && <div className="table-toolbar">{toolbar}</div>}
        <TablePagination pageNumber={table.pageNumber} pageSize={table.pageSize} totalCount={page ? page.totalCount : 0} totalPages={page ? page.totalPages : 1} onPageChange={table.setPage} onPageSizeChange={table.setPageSize} onRefresh={list.reload} loading={list.loading} pageSizes={ADMIN_PAGE_SIZES} />
        <DataTable
          caption={caption}
          columns={columns}
          rows={rows}
          rowKey={(row) => row.id}
          loading={list.loading}
          error={list.error ? c.unavailable(caption) + ' ' + errorText(list.error, {}, '') : null}
          onRetry={list.reload}
          emptyMessage={table.hasFilters ? emptyFiltered : empty}
          onRowClick={onRowClick}
          sort={table.sort}
          onSortChange={table.setSort}
        />
      </section>
    </>
  );
}

/** A labeled form field with an optional hint and error. */
export function Field({ id, label, hint, error, children }) {
  return (
    <div className="field">
      <label className="label-text" htmlFor={id}>{label}</label>
      {children}
      {hint && <span className="muted small">{hint}</span>}
      {error && <span className="field-error" role="alert">{error}</span>}
    </div>
  );
}

/** A toggle switch. */
export function Toggle({ checked, onChange, label, disabled = false }) {
  return (
    <label className="toggle">
      <input type="checkbox" checked={Boolean(checked)} onChange={(e) => onChange(e.target.checked)} disabled={disabled} />
      <span className="toggle-track"><span className="toggle-thumb" /></span>
      <span>{label}</span>
    </label>
  );
}
