import { useSyncExternalStore } from 'react';

const subscribe = () => () => {};

/**
 * False during server rendering and while hydrating prerendered HTML, true afterwards (and
 * immediately on a plain client render). Use it to render browser-only state, such as the saved
 * theme, without a hydration mismatch.
 */
export function useHydrated() {
  return useSyncExternalStore(subscribe, () => true, () => false);
}

export default useHydrated;
