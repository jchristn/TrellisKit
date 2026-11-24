import { useCallback, useState } from 'react';
import { emptyFilters } from '../content/enumeration';

/** Filters per list screen, kept for the app session (docs/ENUMERATION_AND_METADATA.md section 3). */
const sessionFilters = new Map();

/** Forget every screen's filters (sign-out and tests). */
export function resetSessionFilters() {
  sessionFilters.clear();
}

/**
 * Filter state for one list screen, persisted for the session under `screenKey`.
 * @param {string} screenKey For example 'feed' or 'notifications'.
 * @param {string|null} [defaultOrdering]
 * @returns {[object, (next: object) => void, () => void]} Filters, set, and clear.
 */
export function useListFilters(screenKey, defaultOrdering = null) {
  const [filters, setState] = useState(() => sessionFilters.get(screenKey) || emptyFilters(defaultOrdering));
  const setFilters = useCallback(
    (next) => {
      sessionFilters.set(screenKey, next);
      setState(next);
    },
    [screenKey]
  );
  const clear = useCallback(() => setFilters({ ...emptyFilters(defaultOrdering), ordering: filters.ordering }), [setFilters, defaultOrdering, filters.ordering]);
  return [filters, setFilters, clear];
}
