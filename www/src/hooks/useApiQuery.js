import { useCallback, useEffect, useState } from 'react';

/**
 * Run an async API call with loading and error state. The fetcher receives an AbortSignal and
 * must be stable (wrap it in useCallback); it re-runs when it changes or when `reload` is called.
 * Previous data is kept when a later call fails so the page can show it with an error banner.
 *
 * @template T
 * @param {(signal: AbortSignal) => Promise<T>} fetcher
 * @param {{ enabled?: boolean }} [options]
 * @returns {{ data: T|null, error: Error|null, loading: boolean, loadedAt: Date|null, reload: () => void }}
 */
export function useApiQuery(fetcher, { enabled = true } = {}) {
  const [state, setState] = useState({ data: null, error: null, loading: enabled, loadedAt: null });
  const [nonce, setNonce] = useState(0);

  useEffect(() => {
    if (!enabled) return undefined;
    const controller = new AbortController();
    let active = true;
    setState((prev) => ({ ...prev, loading: true, error: null }));
    Promise.resolve()
      .then(() => fetcher(controller.signal))
      .then(
        (data) => { if (active) setState({ data, error: null, loading: false, loadedAt: new Date() }); },
        (error) => { if (active) setState((prev) => ({ ...prev, error, loading: false })); }
      );
    return () => {
      active = false;
      controller.abort();
    };
  }, [fetcher, enabled, nonce]);

  const reload = useCallback(() => setNonce((n) => n + 1), []);
  return { ...state, reload };
}

export default useApiQuery;
