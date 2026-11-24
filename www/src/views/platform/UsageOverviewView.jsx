import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { PropertyStatus } from '@trelliskit/shared';
import strings from '../../strings.js';
import { usePlatformAuth } from '../../context/PlatformAuthContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useDebounce from '../../hooks/useDebounce.js';
import { errorMessage } from '../../utils/api.js';
import { normalizePage } from '../../utils/query.js';
import { ADMIN_PAGE_SIZES } from '../../utils/adminQuery.js';
import {
  RANGE_PERIOD, USAGE_PROPERTY_ORDERINGS, USAGE_ROW_COLUMNS, buildUsagePropertiesQuery, formatMeterValue,
  localDateOf, parseUsageOverviewState, parseUsageState, serializeUsageState, usageRowTotals, validateRange, writeUsageOverviewState
} from '../../utils/usage.js';
import { platformMetadataSource } from '../../utils/platformQuery.js';
import { readMeta, writeMeta } from '../../hooks/useEnumerationUrlState.js';
import MetadataFilterBar from '../../components/MetadataFilterBar.jsx';
import { enumerateUsageProperties } from '../../utils/usageApi.js';
import { formatShortDateTime } from '../../utils/format.js';
import PageHeader from '../../components/PageHeader.jsx';
import TablePagination from '../../components/TablePagination.jsx';
import Icon from '../../components/Icon.jsx';
import { UsagePeriodPicker } from '../../components/usage/UsageDashboard.jsx';
import { ErrorBanner, Loading } from '../../components/States.jsx';
import { PropertyStatusBadge } from './PropertiesView.jsx';
import '../../usage.css';
import TableColumns from '../../components/TableColumns.jsx';
import useLocalStorage from '../../hooks/useLocalStorage.js';

const t = strings.usage.overview;
const route = strings.platform.routes.usage;
const DEFAULT_PAGE_SIZE = 50;

function browserToday() {
  let zone = 'UTC';
  try {
    zone = Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
  } catch {
    zone = 'UTC';
  }
  return localDateOf(Date.now(), zone);
}

/** Link to a property's Usage tab, carrying the selected period. */
export function usageDetailPath(row, state) {
  const params = serializeUsageState({ ...state, bucket: '' });
  params.set('tab', 'usage');
  return '/platform/properties/' + encodeURIComponent(row.propertyId) + '?' + params.toString();
}

/**
 * Platform, Usage: every property's usage totals for one period, one column per meter, ordered
 * by any meter (computed orderings page with `skip`), with a search box, a status filter, label
 * and tag filters, and a totals row over the current page (docs/USAGE_METERING_CONTRACT.md section
 * 7.2). The whole state (period, range, search, status, labels, tags, ordering, page, and page
 * size) lives in the URL, so a view can be shared and survives a reload.
 */
function UsageOverviewView() {
  const { call, client } = usePlatformAuth();
  const [searchParams, setSearchParams] = useSearchParams();
  const state = parseUsageState(searchParams);
  const stateKey = serializeUsageState(state).toString();
  // Rows per page is remembered in this browser and used on the next visit.
  const [storedPageSize, setStoredPageSize] = useLocalStorage('tk.platform.usageOverview.pageSize', DEFAULT_PAGE_SIZE);
  const table = parseUsageOverviewState(searchParams, { pageSizes: ADMIN_PAGE_SIZES, defaultPageSize: ADMIN_PAGE_SIZES.includes(storedPageSize) ? storedPageSize : DEFAULT_PAGE_SIZE });
  const { status, ordering, pageNumber, pageSize } = table;
  const metaKey = JSON.stringify(readMeta(searchParams));
  const meta = useMemo(() => JSON.parse(metaKey), [metaKey]);
  const metadataSource = useMemo(() => platformMetadataSource(client), [client]);
  const [search, setSearch] = useState(table.search);
  const [lastUrlSearch, setLastUrlSearch] = useState(table.search);
  if (lastUrlSearch !== table.search) {
    setLastUrlSearch(table.search);
    setSearch(table.search);
  }
  const debouncedSearch = useDebounce(search, 300);
  const today = browserToday();

  const writeTable = useCallback((changes, { replace = false } = {}) => {
    setSearchParams((current) => writeUsageOverviewState(current, changes, { defaultPageSize: DEFAULT_PAGE_SIZE }), { replace });
  }, [setSearchParams]);
  // Only a settled edit is written (search equals its debounced value), so a URL change (back or
  // forward) is never overwritten by the previous text.
  useEffect(() => {
    if (search === debouncedSearch && debouncedSearch.trim() !== table.search.trim()) writeTable({ search: debouncedSearch.trim() }, { replace: true });
  }, [search, debouncedSearch, table.search, writeTable]);

  const valid = state.period !== RANGE_PERIOD || Object.keys(validateRange(state.from, state.to, '')).length === 0;
  const body = useMemo(
    () => buildUsagePropertiesQuery(parseUsageState(stateKey), { search: table.search, status, ordering, labels: meta.labels, labelMatch: meta.labelMatch, tags: meta.tags }, { pageNumber, pageSize }),
    [stateKey, table.search, status, ordering, pageNumber, pageSize, meta]
  );
  const fetcher = useCallback((signal) => enumerateUsageProperties(call, body, signal), [call, body]);
  const list = useApiQuery(fetcher, { enabled: valid });
  const page = list.data ? normalizePage(list.data, pageSize) : null;
  const rows = page ? page.items : [];
  const totals = usageRowTotals(rows);
  const filtered = Boolean(table.search || status || meta.labels.length || Object.keys(meta.tags).length);

  const changePeriod = (next) => {
    setSearchParams((current) => {
      const updated = serializeUsageState({ ...next, bucket: '' }, current);
      updated.delete('page');
      return updated;
    });
  };
  const sortBy = (value) => writeTable({ ordering: value });
  const changeMeta = (next) => {
    setSearchParams((current) => {
      const updated = writeMeta(current, { ...next, search: '', ordering: '' });
      updated.delete('page');
      return updated;
    });
  };

  const header = (col) => {
    const label = t.columns[col.key];
    if (!col.ordering) return <th key={col.key} scope="col" className="num">{label}</th>;
    const active = ordering === col.ordering;
    return (
      <th key={col.key} scope="col" className="num" aria-sort={active ? 'descending' : 'none'}>
        <button type="button" className="usage-sort-button" aria-pressed={active} onClick={() => sortBy(col.ordering)} title={t.sortBy(t.orderings[col.ordering])}>
          <span>{label}</span>
          <Icon name={active ? 'chevronDown' : 'sort'} size={12} />
        </button>
      </th>
    );
  };

  return (
    <div className="view">
      <PageHeader title={route.label} summary={route.summary} />
      <section className="panel usage-toolbar">
        <UsagePeriodPicker key={state.period === RANGE_PERIOD ? stateKey : 'preset'} state={state} onChange={changePeriod} today={today} disabled={list.loading} />
        <div className="usage-overview-filters">
          <label className="field">
            <span className="label-text">{t.search}</span>
            <input type="search" value={search} placeholder={t.searchPlaceholder} onChange={(e) => setSearch(e.target.value)} />
          </label>
          <label className="field">
            <span className="label-text">{t.status}</span>
            <select value={status} onChange={(e) => writeTable({ status: e.target.value })}>
              <option value="">{t.anyStatus}</option>
              {Object.values(PropertyStatus).map((s) => <option key={s} value={s}>{strings.platform.status[s]}</option>)}
            </select>
          </label>
          <label className="field">
            <span className="label-text">{t.ordering}</span>
            <select value={ordering} onChange={(e) => sortBy(e.target.value)}>
              {USAGE_PROPERTY_ORDERINGS.map((o) => <option key={o} value={o}>{t.orderings[o]}</option>)}
            </select>
          </label>
        </div>
        <MetadataFilterBar value={{ ...meta, search: '', ordering: '' }} onChange={changeMeta} client={metadataSource} targetType="Property" />
      </section>
      <section className="table-section">
        <TablePagination
          pageNumber={pageNumber}
          pageSize={pageSize}
          totalCount={page ? page.totalCount : 0}
          totalPages={page ? page.totalPages : 1}
          onPageChange={(n) => writeTable({ pageNumber: n })}
          onPageSizeChange={(size) => {
            setStoredPageSize(size);
            writeTable({ pageSize: size });
          }}
          onRefresh={list.reload}
          loading={list.loading}
          pageSizes={ADMIN_PAGE_SIZES}
        />
        <div className="table-frame">
          {list.error && <ErrorBanner message={strings.admin.common.unavailable(route.label) + ' ' + errorMessage(list.error, '')} onRetry={list.reload} />}
          {list.loading && !page && <Loading />}
          <div className="table-scroll">
            <TableColumns id="tk.platform.UsageOverviewView"><table className="data-table usage-overview-table">
              <caption className="visually-hidden">{t.caption}</caption>
              <thead>
                <tr>
                  <th scope="col" aria-sort={ordering === 'NameAscending' ? 'ascending' : 'none'}>
                    <button type="button" className="usage-sort-button" aria-pressed={ordering === 'NameAscending'} onClick={() => sortBy('NameAscending')} title={t.sortBy(t.orderings.NameAscending)}>
                      <span>{t.columns.name}</span>
                      <Icon name={ordering === 'NameAscending' ? 'chevronUp' : 'sort'} size={12} />
                    </button>
                  </th>
                  <th scope="col">{t.columns.status}</th>
                  {USAGE_ROW_COLUMNS.map(header)}
                  <th scope="col">{t.columns.dataThroughUtc}</th>
                </tr>
              </thead>
              <tbody>
                {page && rows.length === 0 && (
                  <tr><td colSpan={USAGE_ROW_COLUMNS.length + 3} className="muted">{filtered ? t.emptyFiltered : t.empty}</td></tr>
                )}
                {rows.map((row) => (
                  <tr key={row.propertyId}>
                    <th scope="row">
                      <span className="member-cell-text">
                        <Link to={usageDetailPath(row, state)}>{row.name || row.propertyId}</Link>
                        {row.slug && <code className="mono small">{row.slug}</code>}
                      </span>
                    </th>
                    <td><PropertyStatusBadge record={{ ...row, id: row.propertyId }} /></td>
                    {USAGE_ROW_COLUMNS.map((col) => <td key={col.key} className="num">{formatMeterValue(row[col.key], col.unit)}</td>)}
                    <td className="nowrap">{formatShortDateTime(row.dataThroughUtc) || '-'}</td>
                  </tr>
                ))}
              </tbody>
              {rows.length > 0 && (
                <tfoot>
                  <tr>
                    <th scope="row">{t.totalsRow}</th>
                    <td />
                    {USAGE_ROW_COLUMNS.map((col) => <td key={col.key} className="num">{formatMeterValue(totals[col.key], col.unit)}</td>)}
                    <td />
                  </tr>
                </tfoot>
              )}
            </table></TableColumns>
          </div>
        </div>
        {rows.length > 0 && <p className="muted small">{t.totalsNote}</p>}
      </section>
    </div>
  );
}

export default UsageOverviewView;
