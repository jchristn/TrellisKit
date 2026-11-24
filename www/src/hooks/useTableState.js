import { useMemo } from 'react';
import { useSearchParams } from 'react-router-dom';
import useLocalStorage from './useLocalStorage.js';
import { ADMIN_DEFAULT_PAGE_SIZE, ADMIN_PAGE_SIZES } from '../utils/adminQuery.js';
import { readMeta, writeMeta } from './useEnumerationUrlState.js';

/** localStorage key of the rows per page last chosen on any table. */
export const LAST_PAGE_SIZE_KEY = 'tk.table.lastPageSize';

/**
 * URL-backed state for a server-side table: filters, label and tag filters (`meta`), sort, page,
 * and page size. The rows per page chosen on a table is remembered in this browser for that table
 * (localStorage, `<storageKey>.pageSize`) and used on the next visit; a table with no choice yet
 * starts with the size last chosen on any table. Changing filters, metadata, sort, or page size
 * resets to page 1.
 *
 * @param {{ storageKey: string, filterKeys: readonly string[], defaultSort: string, prefix?: string }} options
 */
export function useTableState({ storageKey, filterKeys, defaultSort, prefix = '' }) {
  const [searchParams, setSearchParams] = useSearchParams();
  const [storedSize, setStoredSize] = useLocalStorage(storageKey + '.pageSize', null);
  // A table the viewer has not sized yet starts with the size they last chose on any table.
  const [lastSize, setLastSize] = useLocalStorage(LAST_PAGE_SIZE_KEY, null);
  const key = (name) => prefix + name;

  const filterText = filterKeys.map((k) => k + '=' + (searchParams.get(key(k)) || '')).join('&');
  const filters = useMemo(() => {
    const values = {};
    for (const pair of filterText.split('&')) {
      const index = pair.indexOf('=');
      values[pair.slice(0, index)] = pair.slice(index + 1);
    }
    return values;
  }, [filterText]);

  const metaText = JSON.stringify(readMeta(searchParams, prefix));
  const meta = useMemo(() => JSON.parse(metaText), [metaText]);

  const sort = searchParams.get(key('sort')) || defaultSort;
  const pageNumber = Math.max(1, parseInt(searchParams.get(key('page')) || '1', 10) || 1);
  const sizeParam = parseInt(searchParams.get(key('pageSize')) || '', 10);
  const remembered = ADMIN_PAGE_SIZES.includes(storedSize) ? storedSize : (ADMIN_PAGE_SIZES.includes(lastSize) ? lastSize : ADMIN_DEFAULT_PAGE_SIZE);
  const pageSize = ADMIN_PAGE_SIZES.includes(sizeParam) ? sizeParam : remembered;

  const write = (changes) => {
    const next = new URLSearchParams(searchParams);
    for (const [name, value] of Object.entries(changes)) {
      if (value === '' || value === null || value === undefined) next.delete(key(name));
      else next.set(key(name), String(value));
    }
    setSearchParams(next, { replace: false });
  };

  const setFilters = (values) => {
    const changes = { page: '' };
    for (const k of filterKeys) changes[k] = values[k] === true ? 'true' : (values[k] || '');
    write(changes);
  };

  // Form filters and label and tag filters in one URL write, so one Apply (or one chip removal)
  // changes both and the form filters (including a `search` filter key) are never dropped.
  const applyAll = (values, metaValue) => {
    const next = writeMeta(searchParams, { ...(metaValue || meta), search: '', ordering: '' }, { prefix, extra: { page: '' } });
    for (const k of filterKeys) {
      const v = values[k] === true ? 'true' : (values[k] || '');
      if (v) next.set(key(k), String(v));
      else next.delete(key(k));
    }
    setSearchParams(next, { replace: false });
  };

  const metaCount = meta.labels.length + Object.keys(meta.tags).length;
  const filterCount = filterKeys.filter((k) => Boolean(filters[k])).length;

  return {
    filters,
    filterKey: filterText + '|' + metaText,
    meta,
    setMeta: (value) => applyAll(filters, value),
    applyAll,
    clearAll: () => applyAll({}, { labels: [], labelMatch: meta.labelMatch, tags: {} }),
    activeCount: filterCount + metaCount,
    sort,
    pageNumber,
    pageSize,
    setFilters,
    clearFilters: () => setFilters({}),
    setSort: (value) => write({ sort: value === defaultSort ? '' : value, page: '' }),
    setPage: (n) => write({ page: n > 1 ? n : '' }),
    setPageSize: (size) => {
      setStoredSize(size);
      setLastSize(size);
      // The remembered size already applies without the URL; the URL keeps it only so a shared link shows the same page.
      write({ pageSize: size, page: '' });
    },
    hasFilters: filterKeys.some((k) => Boolean(filters[k])) || meta.labels.length > 0 || Object.keys(meta.tags).length > 0
  };
}

export default useTableState;
