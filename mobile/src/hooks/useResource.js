import { useCallback, useEffect, useRef, useState } from 'react';

/**
 * Load one object (a venue, an event, a lesson) by id: `status` is `loading`, `ready`, `gone`
 * (404), or `error`; `reload()` fetches again and `setData` replaces the object after an action.
 * @param {(signal?: AbortSignal) => Promise<any>} load
 * @param {string} key Reload when this changes (the id).
 */
export function useResource(load, key) {
  const [data, setData] = useState(null);
  const [status, setStatus] = useState('loading');
  const [error, setError] = useState(null);
  const loadRef = useRef(load);
  const generation = useRef(0);
  useEffect(() => {
    loadRef.current = load;
  });

  const reload = useCallback(async ({ quiet = false } = {}) => {
    const gen = (generation.current += 1);
    if (!quiet) setStatus('loading');
    try {
      const next = await loadRef.current();
      if (gen !== generation.current) return;
      setData(next);
      setError(null);
      setStatus('ready');
    } catch (e) {
      if (gen !== generation.current) return;
      setError(e);
      setStatus((s) => (quiet && s === 'ready' ? 'ready' : e && e.status === 404 ? 'gone' : 'error'));
    }
  }, []);

  useEffect(() => {
    reload();
  }, [reload, key]);

  return { data, setData, status, error, reload };
}
