import { normalizeEnumerationResult } from './enumeration.js';

/**
 * Continuation paging helpers (docs/ENUMERATION_AND_METADATA.md section 2). Lists return an
 * `EnumerationResult` (`objects`, `continuationToken`, `endOfResults`); tokens are opaque. Lists
 * sorted by a changing value can repeat an item across pages, so merging de-duplicates by `id`.
 */

/**
 * Append `incoming` to `existing`, skipping ids already present (the first copy wins, updated
 * in place with the newer one so counts stay fresh).
 * @template T
 * @param {T[]} existing
 * @param {T[]} incoming
 * @param {(item: T) => string} [getId]
 * @returns {T[]}
 */
export function mergeUniqueById(existing, incoming, getId = (item) => item && item.id) {
  const result = Array.isArray(existing) ? existing.slice() : [];
  const index = new Map(result.map((item, i) => [getId(item), i]));
  for (const item of Array.isArray(incoming) ? incoming : []) {
    const id = getId(item);
    if (id !== undefined && id !== null && index.has(id)) {
      result[index.get(id)] = item;
    } else {
      index.set(id, result.length);
      result.push(item);
    }
  }
  return result;
}

/**
 * A framework-free pager over a cursor route.
 *
 * `fetchPage({ cursor, signal })` (where `cursor` is the continuation token, null for the first
 * page) returns an `EnumerationResult` (older `CursorPage` shapes are accepted too). `loadMore()` fetches the next page
 * (ignored while a load is running or after the last page); `refresh()` starts over from the
 * first page (pull to refresh). `update(fn)` replaces items locally (optimistic edits). Listeners
 * get the new state after every change.
 *
 * @param {(args: { cursor: string|null, signal?: AbortSignal }) => Promise<{ items: any[], nextCursor: string|null }>} fetchPage
 * @param {{ getId?: (item: any) => string }} [options]
 */
export function createCursorPager(fetchPage, { getId } = {}) {
  let state = { items: [], nextCursor: null, loading: false, error: null, done: false, loaded: false, totalRecords: null, extra: null };
  let generation = 0;
  let controller = null;
  const listeners = new Set();

  const set = (changes) => {
    state = { ...state, ...changes };
    for (const listener of listeners) listener(state);
  };

  async function load(reset) {
    if (!reset && (state.loading || state.done)) return state;
    if (controller) controller.abort();
    controller = typeof AbortController === 'function' ? new AbortController() : null;
    const mine = ++generation;
    const cursor = reset ? null : state.nextCursor;
    set({ loading: true, error: null });
    try {
      const page = await fetchPage({ cursor, signal: controller ? controller.signal : undefined });
      if (mine !== generation) return state;
      const result = normalizeEnumerationResult(page);
      const nextCursor = result.endOfResults ? null : result.continuationToken;
      set({
        items: reset ? mergeUniqueById([], result.objects, getId) : mergeUniqueById(state.items, result.objects, getId),
        nextCursor,
        done: !nextCursor,
        loading: false,
        loaded: true,
        totalRecords: result.totalRecords,
        extra: result.extra
      });
    } catch (error) {
      if (mine !== generation) return state;
      set({ loading: false, error });
    }
    return state;
  }

  return {
    getState: () => state,
    subscribe(listener) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    loadMore: () => load(false),
    refresh: () => load(true),
    update(fn) {
      set({ items: fn(state.items) });
    },
    cancel() {
      generation += 1;
      if (controller) controller.abort();
      if (state.loading) set({ loading: false });
    }
  };
}
