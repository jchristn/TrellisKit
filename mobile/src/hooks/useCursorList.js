import { useCallback, useEffect, useRef, useState } from 'react';
import { mergePage, readPage } from '../content/paging';

/**
 * Infinite list over a cursor-paged route.
 *
 * - Loads the first page on mount (and when `key` changes).
 * - `refresh()` refetches the first page and replaces the list (pull to refresh).
 * - `loadMore()` fetches the next page once at a time and appends it, de-duplicated by id.
 * - `filter` drops items the client does not render (for example unknown kinds).
 *
 * @param {(cursor: string|null) => Promise<{ items: any[], nextCursor: string|null }>} fetchPage
 * @param {object} [options]
 * @param {string} [options.key] Reload when this changes.
 * @param {boolean} [options.enabled] Skip loading while false.
 * @param {(item: any) => boolean} [options.filter]
 * @param {(page: any) => void} [options.onPage] Receives every raw page (for extra fields).
 */
export function useCursorList(fetchPage, { key = '', enabled = true, filter, onPage } = {}) {
  const [items, setItems] = useState([]);
  const [status, setStatus] = useState(enabled ? 'loading' : 'idle');
  const [error, setError] = useState(null);
  const [refreshing, setRefreshing] = useState(false);
  const [loadingMore, setLoadingMore] = useState(false);
  const [moreError, setMoreError] = useState(null);
  const cursorRef = useRef(null);
  const busyRef = useRef(false);
  const generation = useRef(0);
  const fetchRef = useRef(fetchPage);
  const filterRef = useRef(filter);
  const onPageRef = useRef(onPage);
  const [hasMore, setHasMore] = useState(false);
  useEffect(() => {
    fetchRef.current = fetchPage;
    filterRef.current = filter;
    onPageRef.current = onPage;
  });

  const keep = (list) => (filterRef.current ? list.filter(filterRef.current) : list);

  const loadFirst = useCallback(async ({ pull = false } = {}) => {
    const gen = (generation.current += 1);
    busyRef.current = true;
    if (pull) setRefreshing(true);
    else setStatus('loading');
    setError(null);
    setMoreError(null);
    try {
      const raw = await fetchRef.current(null);
      if (gen !== generation.current) return;
      if (onPageRef.current) onPageRef.current(raw, { first: true });
      const page = readPage(raw);
      cursorRef.current = page.nextCursor;
      setHasMore(Boolean(page.nextCursor));
      setItems(mergePage([], keep(page.items)));
      setStatus('ready');
    } catch (e) {
      if (gen !== generation.current) return;
      setError(e);
      setStatus((s) => (pull && s === 'ready' ? 'ready' : 'error'));
    } finally {
      if (gen === generation.current) {
        busyRef.current = false;
        setRefreshing(false);
      }
    }
  }, []);

  useEffect(() => {
    if (!enabled) return;
    // loadFirst sets state only after its first await.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    loadFirst();
  }, [enabled, key, loadFirst]);

  const loadMore = useCallback(async () => {
    if (busyRef.current || !cursorRef.current) return;
    const gen = generation.current;
    busyRef.current = true;
    setLoadingMore(true);
    setMoreError(null);
    try {
      const raw = await fetchRef.current(cursorRef.current);
      if (gen !== generation.current) return;
      if (onPageRef.current) onPageRef.current(raw, { first: false });
      const page = readPage(raw);
      cursorRef.current = page.nextCursor;
      setHasMore(Boolean(page.nextCursor));
      setItems((current) => mergePage(current, keep(page.items)));
    } catch (e) {
      if (gen === generation.current) setMoreError(e);
    } finally {
      if (gen === generation.current) {
        busyRef.current = false;
        setLoadingMore(false);
      }
    }
  }, []);

  const refresh = useCallback(() => loadFirst({ pull: true }), [loadFirst]);
  const reload = useCallback(() => loadFirst(), [loadFirst]);

  /** Replace one item (by id) with fn(item); fn returning null removes it. */
  const updateItem = useCallback((id, fn) => {
    setItems((current) => {
      const next = [];
      for (const item of current) {
        if (item && item.id === id) {
          const replaced = fn(item);
          if (replaced) next.push(replaced);
        } else next.push(item);
      }
      return next;
    });
  }, []);

  const prependItem = useCallback((item) => setItems((current) => mergePage([item], current)), []);

  /** Merge an item into the list in place, or put it first when it is not there and `prepend` is set. */
  const upsertItem = useCallback((item, { prepend = true } = {}) => {
    setItems((current) => {
      if (current.some((i) => i && i.id === item.id)) return current.map((i) => (i && i.id === item.id ? { ...i, ...item } : i));
      return prepend ? [item].concat(current) : current;
    });
  }, []);

  return {
    items,
    setItems,
    status,
    error,
    refreshing,
    loadingMore,
    moreError,
    hasMore,
    refresh,
    reload,
    loadMore,
    updateItem,
    prependItem,
    upsertItem
  };
}
