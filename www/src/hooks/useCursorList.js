import { useCallback, useEffect, useRef, useState } from 'react';
import { createCursorPager } from '@trelliskit/shared';

/**
 * React state for a cursor-paged list (contract section 0.1). `fetchPage(cursor, signal)` must be
 * stable (useCallback); when it changes the list starts over. Returns the pager state plus
 * `loadMore`, `refresh`, and `update(fn)` for local edits.
 *
 * @param {(cursor: string|null, signal?: AbortSignal) => Promise<{ items: any[], nextCursor: string|null }>} fetchPage
 * @param {{ enabled?: boolean }} [options]
 */
export function useCursorList(fetchPage, { enabled = true } = {}) {
  const pagerRef = useRef(null);
  const [state, setState] = useState({ items: [], nextCursor: null, loading: enabled, error: null, done: false, loaded: false, extra: null });

  useEffect(() => {
    if (!enabled) return undefined;
    const pager = createCursorPager(({ cursor, signal }) => fetchPage(cursor, signal));
    pagerRef.current = pager;
    const unsubscribe = pager.subscribe(setState);
    pager.refresh();
    return () => {
      unsubscribe();
      pager.cancel();
      if (pagerRef.current === pager) pagerRef.current = null;
    };
  }, [fetchPage, enabled]);

  const loadMore = useCallback(() => (pagerRef.current ? pagerRef.current.loadMore() : Promise.resolve()), []);
  const refresh = useCallback(() => (pagerRef.current ? pagerRef.current.refresh() : Promise.resolve()), []);
  const update = useCallback((fn) => { if (pagerRef.current) pagerRef.current.update(fn); }, []);

  return { ...state, loadMore, refresh, update };
}

export default useCursorList;
