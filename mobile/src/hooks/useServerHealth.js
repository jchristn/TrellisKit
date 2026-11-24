import { useCallback, useEffect, useRef, useState } from 'react';
import { getApiClient } from '../api/client';

/**
 * Load GET /api/v1/health and expose loading, data, error, and a retry function.
 * @param {object} [options]
 * @param {() => import('@trelliskit/shared').ApiClient} [options.getClient] Client factory (for tests).
 * @returns {{ loading: boolean, data: object|null, error: import('@trelliskit/shared').ApiError|Error|null, retry: () => void }}
 */
export function useServerHealth({ getClient = getApiClient } = {}) {
  const [state, setState] = useState({ loading: true, data: null, error: null });
  const [attempt, setAttempt] = useState(0);
  const getClientRef = useRef(getClient);

  useEffect(() => {
    const controller = new AbortController();
    let active = true;
    getClientRef.current()
      .health(controller.signal)
      .then((data) => {
        if (active) setState({ loading: false, data, error: null });
      })
      .catch((error) => {
        if (active) setState({ loading: false, data: null, error });
      });
    return () => {
      active = false;
      controller.abort();
    };
  }, [attempt]);

  const retry = useCallback(() => {
    setState({ loading: true, data: null, error: null });
    setAttempt((value) => value + 1);
  }, []);

  return { ...state, retry };
}
